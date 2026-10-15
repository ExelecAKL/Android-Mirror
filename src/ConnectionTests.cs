using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AndroidMirror;

static class ConnectionTests
{
    sealed class FakeSession : IMirrorSession
    {
        public event Action<int> Exited;
        public bool Ready { get { return true; } }
        public bool Stopped, Started;
        public void Start() { Started = true; }
        public void Stop() { Stopped = true; }
        public void Exit(int code) { if (Exited != null) Exited(code); }
    }
    sealed class Fixture : IDisposable
    {
        public readonly ConnectionController Controller;
        public readonly List<FakeSession> Sessions = new List<FakeSession>();
        public DateTime Time = new DateTime(2026, 1, 1);
        public Fixture()
        {
            Controller = new ConnectionController(serial => { var result = new FakeSession(); Sessions.Add(result); return result; }, action => action(), () => Time);
        }
        public void Devices(string value) { Controller.UpdateDevices(Device.Parse(value)); }
        public void Advance() { Time = Time.AddMinutes(1); Controller.Tick(); }
        public void Dispose() { Controller.Dispose(); }
    }
    static int count;
    static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action test) { test(); count++; Console.WriteLine("PASS " + name); }
    static int Main(string[] args)
    {
        try
        {
            if (args.Any(value => value.StartsWith("--serial="))) return FakeMirror(args);
            if (args.Length > 0 && args[0] == "pair") { string secret = Console.ReadLine(); Console.WriteLine(secret == "123456" && args.Length == 2 ? "Successfully paired using stdin" : "FAILED"); return 0; }
            if (args.Length > 0 && args[0] == "wait") { Thread.Sleep(30000); return 0; }
            if (args.Contains("--live-adb")) return LiveAdb();
            if (args.Contains("--live-updates")) return EngineUpdateTests.Live();
            if (args.Contains("--live-app-updates")) return AppUpdateTests.Live();
            if (args.Contains("--process-tests")) { MultiDeviceTests.ProcessTest(); ProcessTest(); Console.WriteLine("PASS isolated real child process lifecycle checks"); return 0; }
            Test("ADB device names and authorization parsing", () => {
                var devices = Device.Parse("one\tdevice usb:1 model:Pixel_9 transport_id:1\ntwo\tunauthorized\nthree\toffline\n");
                Assert(devices.Count == 3 && devices[0].Name == "Pixel 9" && devices[1].State == "unauthorized" && devices[2].State == "offline", "Incorrect snapshot parsing");
            });
            Test("First attachment connects only once", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice model:Phone\n"); f.Controller.Tick(); f.Devices("one\tdevice model:Phone\n");
                    Assert(f.Sessions.Count == 1 && f.Sessions[0].Started, "Duplicate mirror");
                }
            });
            Test("Cable loss and quick reattach wait for old process exit", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n"); var old = f.Sessions[0]; f.Devices("");
                    Assert(old.Stopped && f.Controller.Stopping, "Old process must close");
                    f.Devices("one\tdevice\n"); Assert(f.Sessions.Count == 1, "Overlapping mirror processes");
                    old.Exit(0); f.Advance();
                    Assert(f.Sessions.Count == 2 && f.Controller.Active == f.Sessions[1], "Reattachment did not resume");
                    old.Exit(2); Assert(f.Controller.Active == f.Sessions[1], "Stale callback replaced new session");
                }
            });
            Test("scrcpy disconnect exit code resumes without an empty snapshot", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n"); f.Sessions[0].Exit(2); f.Controller.Tick();
                    Assert(f.Sessions.Count == 1, "Retry must be delayed"); f.Advance();
                    Assert(f.Sessions.Count == 2, "Disconnect exit did not resume");
                }
            });
            Test("Stop stays stopped across unplug and replug", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n"); f.Controller.Stop(); f.Sessions[0].Exit(0);
                    f.Devices(""); f.Devices("one\tdevice\n"); f.Advance();
                    Assert(f.Sessions.Count == 1 && !f.Controller.Wanted, "Stopped session reopened");
                    f.Controller.Connect(); Assert(f.Sessions.Count == 2, "Connect did not resume");
                }
            });
            Test("Closing mirror does not immediately reopen it", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n"); f.Sessions[0].Exit(0); f.Advance();
                    Assert(f.Sessions.Count == 1 && !f.Controller.Wanted, "Clean exit reopened");
                }
            });
            Test("Reconnect disabled still detects without starting a mirror", () => {
                using (var f = new Fixture()) {
                    f.Controller.SetAutoReconnect(false); f.Devices("one\tdevice\n");
                    Assert(f.Sessions.Count == 0 && f.Controller.CanConnect, "Unexpected auto start");
                    f.Controller.Connect(); f.Devices(""); f.Sessions[0].Exit(2); f.Devices("one\tdevice\n"); f.Advance();
                    Assert(f.Sessions.Count == 1, "Auto reconnect ignored setting");
                }
            });
            Test("Unauthorized and offline phones never launch scrcpy", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tunauthorized\n"); f.Advance(); f.Devices("one\toffline\n"); f.Advance();
                    Assert(f.Sessions.Count == 0, "Unready phone launched");
                    f.Devices("one\tdevice\n"); Assert(f.Sessions.Count == 1, "Authorization did not trigger launch");
                }
            });
            Test("Several phones require a choice", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\ntwo\tdevice\n"); Assert(f.Sessions.Count == 0, "Arbitrary phone chosen");
                    f.Controller.Select("two"); Assert(f.Sessions.Count == 1 && f.Controller.Target == "two", "Choice did not launch");
                }
            });
            Test("Reconnect never switches to a different phone", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n"); f.Devices("two\tdevice\n"); f.Sessions[0].Exit(2); f.Advance();
                    Assert(f.Sessions.Count == 1 && f.Controller.Target == "one", "Reconnected to wrong phone");
                }
            });
            Test("Startup failures back off and stop after three attempts", () => {
                using (var f = new Fixture()) {
                    f.Devices("one\tdevice\n");
                    for (int i = 0; i < 3; i++) { f.Sessions[i].Exit(1); f.Advance(); }
                    f.Advance(); Assert(f.Sessions.Count == 3 && f.Controller.Failures == 3, "Unbounded retry loop");
                    f.Devices(""); f.Devices("one\tdevice\n");
                    Assert(f.Sessions.Count == 4, "Reattach did not clear failure limit");
                }
            });
            Test("Process start exception remains recoverable", () => {
                int attempts = 0;
                using (var controller = new ConnectionController(serial => { attempts++; throw new IOException("Test launch error"); }, action => action())) {
                    controller.UpdateDevices(Device.Parse("one\tdevice\n")); controller.Tick();
                    Assert(attempts == 1 && controller.Active == null && controller.Error == "Test launch error", "Start failure was not contained");
                    controller.Connect(); Assert(attempts == 2, "Manual retry was not attempted");
                }
            });
            Test("Windows argument quoting preserves spaces and trailing slashes", () => {
                Assert(MirrorProcess.Quote("phone name") == "\"phone name\"", "Spaces lost");
                Assert(MirrorProcess.Quote("phone\\") == "\"phone\\\\\"", "Trailing slash lost");
                Assert(MirrorProcess.Quote("phone\"id") == "\"phone\\\"id\"", "Quote escaped incorrectly");
            });
            MultiDeviceTests.Run(Test);
            WirelessTests.Run(Test);
            EngineFeatureTests.Run(Test);
            EngineUpdateTests.Run(Test);
            AppUpdateTests.Run(Test);
            Test("ADB stream handles fragmented frames, device changes, and server restart", () => ProtocolTest().GetAwaiter().GetResult());
            Test("Real child process drains logs and never overlaps on quick reattach", ProcessTest);
            Console.WriteLine(count + " tests passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    static int FakeMirror(string[] args)
    {
        Console.WriteLine("FAKE_MIRROR_READY " + args[0]);
        Console.Error.WriteLine("FAKE_MIRROR_DIAGNOSTICS");
        var elapsed = Stopwatch.StartNew();
        while (elapsed.ElapsedMilliseconds < 15000 && !File.Exists("release.txt")) Thread.Sleep(20);
        return 0;
    }
    static void Pump(ConcurrentQueue<Action> actions, ConnectionController controller, Func<bool> done)
    {
        var elapsed = Stopwatch.StartNew();
        while (!done() && elapsed.ElapsedMilliseconds < 6000)
        {
            Action action;
            while (actions.TryDequeue(out action)) action();
            controller.Tick(); Thread.Sleep(10);
        }
        Assert(done(), "Child process lifecycle did not complete");
    }
    static void ProcessTest()
    {
        string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".process-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, EngineLayout.RuntimeExecutable);
        File.Copy(System.Reflection.Assembly.GetExecutingAssembly().Location, executable);
        var actions = new ConcurrentQueue<Action>();
        var output = new ConcurrentQueue<string>();
        int starts = 0;
        using (var controller = new ConnectionController(serial => { starts++; return new MirrorProcess(directory, serial, line => output.Enqueue(line)); }, action => actions.Enqueue(action)))
        {
            try
            {
                controller.UpdateDevices(Device.Parse("one\tdevice\n"));
                Pump(actions, controller, () => output.Any(line => line.StartsWith("FAKE_MIRROR_READY")) && output.Contains("FAKE_MIRROR_DIAGNOSTICS"));
                controller.UpdateDevices(Device.Parse(""));
                controller.UpdateDevices(Device.Parse("one\tdevice\n"));
                Assert(starts == 1 && controller.Stopping, "Started before old child exited");
                Pump(actions, controller, () => starts == 2 && output.Count(line => line.StartsWith("FAKE_MIRROR_READY")) == 2);
                controller.Stop();
                File.WriteAllText(Path.Combine(directory, "release.txt"), "exit");
                Pump(actions, controller, () => controller.Active == null);
                Assert(starts == 2 && !controller.Wanted, "Manual stop reopened child");
                File.Delete(Path.Combine(directory, "release.txt"));
                controller.Connect();
                Pump(actions, controller, () => output.Count(line => line.StartsWith("FAKE_MIRROR_READY")) == 3);
                controller.Dispose();
                Assert(!Process.GetProcessesByName(Path.GetFileNameWithoutExtension(EngineLayout.RuntimeExecutable)).Any(process => { try { return String.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase); } catch { return false; } finally { process.Dispose(); } }), "Launcher shutdown left a child process running");
            }
            catch (Exception error) { throw new Exception("Child lifecycle failure: starts=" + starts + ", active=" + (controller.Active != null) + ", stopping=" + controller.Stopping + ", logs=" + String.Join(" | ", output), error); }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "release.txt"), "exit");
                controller.Stop();
            }
        }
        // Only remove the exact files created by this fixture; never recurse.
        File.Delete(executable); File.Delete(Path.Combine(directory, "release.txt")); Directory.Delete(directory);
    }
    static int LiveAdb()
    {
        string directory = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        using (var ready = new ManualResetEventSlim())
            using (var tracker = new AdbTracker(Path.Combine(directory, "engine")))
        {
            string error = "";
            tracker.StatusChanged += message => error = message;
            tracker.DevicesChanged += devices => { Console.WriteLine("Live ADB snapshot received: " + devices.Count + " device(s)."); ready.Set(); };
            Task run = tracker.Run();
            bool detected = ready.Wait(10000);
            tracker.Dispose(); run.Wait(2000);
            Assert(detected, "Live device detection did not initialize: " + error);
            Console.WriteLine("PASS real bundled ADB startup and event stream");
            return 0;
        }
    }
    static async Task Send(NetworkStream stream, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        // Separate writes exercise ReadExact instead of relying on packet boundaries.
        foreach (byte value in bytes) await stream.WriteAsync(new[] { value }, 0, 1);
    }
    static async Task Handshake(NetworkStream stream)
    {
        string command = await AdbTracker.ReadFrame(stream, CancellationToken.None);
        Assert(command == "host:track-devices-l", "Incorrect track request");
        await Send(stream, "OKAY");
    }
    static async Task Frame(NetworkStream stream, string payload)
    {
        await Send(stream, Encoding.UTF8.GetByteCount(payload).ToString("X4") + payload);
    }
    static async Task ProtocolTest()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var snapshots = new List<string>();
        var completed = new TaskCompletionSource<bool>();
        using (var tracker = new AdbTracker(".", port, false))
        {
            tracker.DevicesChanged += devices => {
                lock (snapshots) {
                    snapshots.Add(String.Join(",", devices.Select(d => d.State)));
                    if (snapshots.Count >= 4) completed.TrySetResult(true);
                }
            };
            Task run = tracker.Run();
            Task server = Task.Run(async () => {
                using (var first = await listener.AcceptTcpClientAsync())
                using (var stream = first.GetStream()) {
                    await Handshake(stream); await Frame(stream, "one\tunauthorized\n");
                    await Frame(stream, "one\tdevice model:Pixel_9\n"); await Frame(stream, "");
                }
                using (var second = await listener.AcceptTcpClientAsync())
                using (var stream = second.GetStream()) {
                    await Handshake(stream); await Frame(stream, "one\tdevice model:Pixel_9\n");
                    await completed.Task;
                }
            });
            try {
                if (await Task.WhenAny(completed.Task, Task.Delay(8000)) != completed.Task) throw new Exception("ADB tracker did not recover");
                await server;
                lock (snapshots) Assert(snapshots.Take(4).SequenceEqual(new[] { "unauthorized", "device", "", "device" }), "Lost an ADB state transition");
                tracker.Dispose();
                Assert(await Task.WhenAny(run, Task.Delay(2000)) == run, "Tracker did not shut down");
                await run;
            }
            finally { tracker.Dispose(); listener.Stop(); }
        }
    }
}
