using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AndroidMirror
{
    public sealed class Device
    {
        public string Serial, State, Model, PhysicalId = "";
        public bool IsWireless { get { return Serial.Contains(":") || Serial.Contains("._adb-tls-connect._tcp"); } }
        public string Key { get { return String.IsNullOrEmpty(PhysicalId) ? Serial : PhysicalId; } }
        public string Name { get { return String.IsNullOrEmpty(Model) ? Serial : Model.Replace('_', ' '); } }
        public override string ToString() { return Name + "  ·  " + (State == "device" ? "Connected" : State == "unauthorized" ? "Allow USB debugging" : State); }
        public static List<Device> Parse(string text)
        {
            var devices = new List<Device>();
            foreach (string line in text.Split('\n'))
            {
                string[] words = line.Trim().Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length < 2 || words[0] == "List" || words[0].StartsWith("*")) continue;
                var device = new Device { Serial = words[0], State = words[1], Model = "" };
                foreach (string word in words.Skip(2)) if (word.StartsWith("model:")) device.Model = word.Substring(6);
                devices.Add(device);
            }
            return devices;
        }
    }

    // ADB publishes a complete snapshot whenever a device connects, disconnects,
    // changes authorization, or goes offline. No repeated adb.exe polling.
    public sealed class AdbTracker : IDisposable
    {
        readonly string directory;
        readonly int port;
        readonly bool startServer;
        readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        TcpClient client;
        public event Action<List<Device>> DevicesChanged;
        public event Action<string> StatusChanged;
        public AdbTracker(string directory, int port = 5037, bool startServer = true)
        { this.directory = directory; this.port = port; this.startServer = startServer; }
        public Task Run() { return Task.Run((Func<Task>)Watch); }
        async Task Watch()
        {
            DateTime lastStart = DateTime.MinValue;
            while (!cancellation.IsCancellationRequested)
            {
                bool reconnect = false;
                try
                {
                    using (var connection = new TcpClient())
                    {
                        client = connection;
                        Task connect = connection.ConnectAsync("127.0.0.1", port);
                        if (await Task.WhenAny(connect, Task.Delay(2500, cancellation.Token)).ConfigureAwait(false) != connect)
                            throw new IOException("ADB connection timed out.");
                        await connect.ConfigureAwait(false);
                        using (NetworkStream stream = connection.GetStream())
                        {
                            const string command = "host:track-devices-l";
                            byte[] request = Encoding.ASCII.GetBytes(command.Length.ToString("X4") + command);
                            await stream.WriteAsync(request, 0, request.Length, cancellation.Token).ConfigureAwait(false);
                            string reply = Encoding.ASCII.GetString(await ReadExact(stream, 4, cancellation.Token).ConfigureAwait(false));
                            if (reply != "OKAY") throw new IOException(reply == "FAIL" ? await ReadFrame(stream, cancellation.Token).ConfigureAwait(false) : "Unexpected ADB response.");
                            if (StatusChanged != null) StatusChanged("");
                            while (!cancellation.IsCancellationRequested)
                            {
                                string snapshot = await ReadFrame(stream, cancellation.Token).ConfigureAwait(false);
                                if (DevicesChanged != null) DevicesChanged(Device.Parse(snapshot));
                            }
                        }
                    }
                }
                catch (Exception error)
                {
                    if (cancellation.IsCancellationRequested) break;
                    if (StatusChanged != null) StatusChanged(error.Message);
                    reconnect = true;
                }
                finally { client = null; }
                if (reconnect && !cancellation.IsCancellationRequested)
                {
                    if (startServer && DateTime.UtcNow - lastStart > TimeSpan.FromSeconds(8))
                    {
                        lastStart = DateTime.UtcNow;
                        try { await StartServer().ConfigureAwait(false); }
                        catch (Exception startError) { if (StatusChanged != null) StatusChanged(startError.Message); }
                    }
                    try { await Task.Delay(1000, cancellation.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        async Task StartServer()
        {
            var info = ProcessInfo("adb.exe", "start-server", directory);
            using (var process = new Process { StartInfo = info })
            {
                process.Start();
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                Task exited = Task.Run(() => process.WaitForExit());
                if (await Task.WhenAny(exited, Task.Delay(6000, cancellation.Token)).ConfigureAwait(false) != exited)
                {
                    try { if (!process.HasExited) process.Kill(); } catch { }
                    throw new IOException("ADB could not start. Check connection details below.");
                }
                await exited.ConfigureAwait(false);
                string details = await errors.ConfigureAwait(false);
                await output.ConfigureAwait(false);
                if (process.ExitCode != 0) throw new IOException(String.IsNullOrWhiteSpace(details) ? "ADB could not start." : details.Trim());
            }
        }
        public static ProcessStartInfo ProcessInfo(string name, string arguments, string directory)
        {
            var info = new ProcessStartInfo(Path.Combine(directory, name), arguments) {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.EnvironmentVariables["ADB"] = Path.Combine(directory, "adb.exe");
            info.EnvironmentVariables["ADB_SERVER_SOCKET"] = "tcp:localhost:5037";
            info.EnvironmentVariables["SCRCPY_SERVER_PATH"] = Path.Combine(directory, "scrcpy-server");
            string icons = Path.GetFullPath(Path.Combine(directory, "..", "assets", "icons"));
            if (Directory.Exists(icons)) info.EnvironmentVariables["SCRCPY_ICON_DIR"] = icons;
            return info;
        }
        public static async Task<byte[]> ReadExact(Stream stream, int length, CancellationToken token)
        {
            byte[] buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("ADB disconnected. Reconnecting to device detection…");
                offset += read;
            }
            return buffer;
        }
        public static async Task<string> ReadFrame(Stream stream, CancellationToken token)
        {
            string header = Encoding.ASCII.GetString(await ReadExact(stream, 4, token).ConfigureAwait(false));
            int length;
            if (!Int32.TryParse(header, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out length) || length > 65535)
                throw new IOException("Invalid ADB device-list response.");
            return Encoding.UTF8.GetString(await ReadExact(stream, length, token).ConfigureAwait(false));
        }
        public void Dispose()
        {
            cancellation.Cancel();
            var connection = client;
            if (connection != null) connection.Close();
        }
    }

    public interface IMirrorSession
    {
        event Action<int> Exited;
        bool Ready { get; }
        void Start();
        void Stop();
    }

    public sealed class MirrorProcess : IMirrorSession, IDisposable
    {
        readonly Process process;
        readonly Action<string> log;
        readonly bool headless;
        bool keepSharingWhenMinimized;
        ScreenSharingWindow sharingWindow;
        readonly Stopwatch lifetime = new Stopwatch();
        int released, disposing;
        public event Action<int> Exited;
        public MirrorProcess(string directory, string serial, Action<string> log, MirrorOptions options = null, string name = null, bool keepSharingWhenMinimized = true)
        {
            this.log = log;
            this.keepSharingWhenMinimized = keepSharingWhenMinimized;
            options = (options ?? new MirrorOptions()).Copy();
            foreach (var recording in options.Advanced.Where(s => s.Enabled && s.Name == "--record" && s.Value.Length > 0))
            {
                recording.Value = EngineArguments.RecordingTarget(recording.Value);
                Directory.CreateDirectory(Path.GetDirectoryName(recording.Value));
            }
            headless = options.Has("--otg") || options.Has("--no-window") || options.Has("--no-video") || options.Has("--no-video-playback") || options.Has("--no-playback");
            process = new Process { StartInfo = AdbTracker.ProcessInfo(EngineLayout.RuntimeExecutable, Arguments(serial, options ?? new MirrorOptions(), name), directory) };
            foreach (var setting in options.Environment) if (EngineCatalog.EnvironmentNames.Contains(setting.Name) && !String.IsNullOrWhiteSpace(setting.Value)) process.StartInfo.EnvironmentVariables[setting.Name] = setting.Value;
            process.OutputDataReceived += OnOutput;
            process.ErrorDataReceived += OnOutput;
            process.Exited += delegate { Task.Run(() => {
                try { process.WaitForExit(); int code = process.ExitCode; if (Exited != null) Exited(code); }
                catch (InvalidOperationException) { }
                finally { ReleaseProcess(); }
            }); };
        }
        void OnOutput(object sender, DataReceivedEventArgs e) { if (e.Data != null) log(e.Data); }
        public bool Ready { get { try { process.Refresh(); return !process.HasExited && (process.MainWindowHandle != IntPtr.Zero || (headless && lifetime.ElapsedMilliseconds > 1200)); } catch { return false; } } }
        public void Start()
        {
            process.Start();
            lifetime.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!headless) sharingWindow = new ScreenSharingWindow(process.Id, keepSharingWhenMinimized, log);
            process.EnableRaisingEvents = true;
        }
        public bool KeepSharingWhenMinimized
        {
            get { return keepSharingWhenMinimized; }
            set { keepSharingWhenMinimized = value; var watcher = sharingWindow; if (watcher != null) watcher.Enabled = value; }
        }
        public void Stop()
        {
            Task.Run(async () => {
                try
                {
                    if (process.HasExited) return;
                    process.CloseMainWindow();
                    var wait = Stopwatch.StartNew();
                    while (!process.HasExited && wait.ElapsedMilliseconds < 1800) await Task.Delay(40).ConfigureAwait(false);
                    if (!process.HasExited) process.Kill();
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposing, 1) != 0) return;
            // Shutdown must finish before the launcher exits; background tasks
            // alone are not enough because CLR thread-pool threads are background.
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                    if (!process.WaitForExit(1800)) { process.Kill(); process.WaitForExit(500); }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { ReleaseProcess(); }
        }
        void ReleaseProcess() { if (Interlocked.Exchange(ref released, 1) == 0) { if (sharingWindow != null) sharingWindow.Dispose(); process.Dispose(); } }
        public static string Arguments(string serial, MirrorOptions options, string name)
        {
            return EngineArguments.Build(serial, options, name);
        }
        // Windows CommandLineToArgvW escaping; no shell is involved.
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
                else { result.Append('\\', slashes); result.Append(c); }
                slashes = 0;
            }
            result.Append('\\', slashes * 2); result.Append('"');
            return result.ToString();
        }
    }

    // Called on the UI thread. Transport and process callbacks are marshalled here.
    public sealed class ConnectionController : IDisposable
    {
        readonly Func<string, IMirrorSession> create;
        readonly Action<Action> dispatch;
        readonly Func<DateTime> now;
        public event Action Changed;
        public List<Device> Devices = new List<Device>();
        public string Target = "", Error = "";
        public bool AutoReconnect = true, Wanted = true;
        public Func<Device, bool> AdditionalTransport;
        public IMirrorSession Active { get; private set; }
        public bool Stopping { get; private set; }
        public int Failures { get; private set; }
        DateTime retryAfter;
        bool disposed;
        bool transportLost;
        bool restartRequested;
        public ConnectionController(Func<string, IMirrorSession> create, Action<Action> dispatch, Func<DateTime> now = null)
        { this.create = create; this.dispatch = dispatch; this.now = now ?? (() => DateTime.UtcNow); }
        public Device Selected { get { return Devices.FirstOrDefault(d => d.Serial == Target); } }
        public bool CanConnect { get { return Selected != null && (Selected.State == "device" || (AdditionalTransport != null && AdditionalTransport(Selected))); } }
        public void UpdateDevices(List<Device> devices)
        {
            bool wasReady = CanConnect;
            Devices = devices;
            if (String.IsNullOrEmpty(Target) && devices.Count == 1) Target = devices[0].Serial;
            if (!wasReady && CanConnect) { Failures = 0; retryAfter = DateTime.MinValue; Error = ""; }
            if (Active != null && !CanConnect)
            {
                if (!AutoReconnect) Wanted = false;
                transportLost = true;
                if (!Stopping) { Stopping = true; Active.Stop(); }
                Failures = 0; retryAfter = DateTime.MinValue;
            }
            Tick();
        }
        public void Select(string serial)
        {
            if (Active != null || Target == serial) return;
            Target = serial; Failures = 0; Error = ""; retryAfter = DateTime.MinValue;
            Tick();
        }
        public void Connect() { Wanted = true; Failures = 0; Error = ""; retryAfter = DateTime.MinValue; Tick(); }
        public void Stop()
        {
            Wanted = false; Error = ""; Failures = 0;
            transportLost = false; restartRequested = false;
            if (Active != null && !Stopping) { Stopping = true; Active.Stop(); }
            Notify();
        }
        public void Restart()
        {
            if (disposed) return;
            Failures = 0; Error = ""; retryAfter = DateTime.MinValue;
            if (Active != null)
            {
                restartRequested = true;
                if (!Stopping) { Stopping = true; Active.Stop(); }
            }
            Tick();
        }
        public void ChangeTransport(string serial)
        {
            if (Target == serial) return;
            Target = serial; Restart();
        }
        public void SetAutoReconnect(bool enabled)
        {
            AutoReconnect = enabled;
            if (enabled && Active == null) Wanted = true;
            if (!enabled && Active == null) Wanted = false;
            Tick();
        }
        public void Tick()
        {
            if (disposed) return;
            if (Active != null && Active.Ready) Failures = 0;
            if (Wanted && Active == null && CanConnect && Failures < 3 && now() >= retryAfter)
            {
                IMirrorSession session = null;
                try
                {
                    session = create(Target); Active = session;
                    session.Exited += code => dispatch(() => Finished(session, code));
                    Error = ""; session.Start();
                }
                catch (Exception error)
                {
                    Active = null;
                    if (session != null) session.Stop();
                    Failed(error.Message);
                }
            }
            Notify();
        }
        void Finished(IMirrorSession session, int code)
        {
            if (disposed || !Object.ReferenceEquals(Active, session)) return;
            Active = null;
            if (Stopping)
            {
                Stopping = false;
                if (!transportLost && !restartRequested) Wanted = false;
                transportLost = false; restartRequested = false;
                retryAfter = now().AddMilliseconds(150);
            }
            else if (code == 0) { Wanted = false; Error = ""; Failures = 0; }
            else if (code == 2)
            {
                if (!AutoReconnect) Wanted = false;
                Failures = 0; Error = "";
                retryAfter = now().AddMilliseconds(700);
            }
            else Failed("Couldn’t start mirroring. Check connection details below.");
            Notify();
        }
        void Failed(string message)
        {
            Error = message; Failures++;
            retryAfter = now().AddSeconds(Math.Min(8, Failures * 2));
            if (!AutoReconnect) Wanted = false;
        }
        void Notify() { if (Changed != null) Changed(); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var remaining = Active;
            Stop();
            var resource = remaining as IDisposable;
            if (resource != null) resource.Dispose();
        }
    }
}
