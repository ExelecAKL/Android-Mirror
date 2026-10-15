using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AndroidMirror;

static class WirelessTests
{
    sealed class Commands : IAdbCommands
    {
        public List<string> Arguments = new List<string>(), Inputs = new List<string>();
        public string Services = "", Identity = "PHONE123", PairResponse = "Successfully paired to 192.168.1.5:37001";
        public Task<AdbReply> Run(string arguments, string input, int timeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Arguments.Add(arguments); Inputs.Add(input);
            string text = arguments == "mdns services" ? Services : arguments.StartsWith("pair ") ? PairResponse : arguments.Contains("getprop") ? Identity : "connected to 192.168.1.5:38001";
            return Task.FromResult(new AdbReply { Text = text });
        }
    }
    sealed class Session : IMirrorSession
    {
        public bool Stopped;
        public event Action<int> Exited;
        public bool Ready { get { return true; } }
        public void Start() { }
        public void Stop() { Stopped = true; }
        public void Exit() { if (Exited != null) Exited(0); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("USB is the default and transport modes filter correctly", () => {
            var prefs = new AppPreferences { AutoReconnect = false }; Assert(prefs.ConnectionMode == "usb", "Wireless enabled by default");
            using (var controller = new MultiDeviceController(prefs, p => new Session(), a => a())) {
                controller.UpdateDevices(Device.Parse("USB123\tdevice\n192.168.1.5:38001\tdevice\n")); Assert(controller.Devices.Count == 1 && !controller.Devices[0].IsWireless, "USB-only accepted Wi-Fi");
                controller.SetConnectionMode("wireless"); Assert(controller.Devices.Count == 1 && controller.Devices[0].IsWireless, "Wireless-only accepted USB");
                controller.SetConnectionMode("adaptive"); Assert(controller.Devices.Count == 2, "Adaptive omitted a transport");
            }
        });
        test("Adaptive deduplicates physical devices and waits before transport handover", () => {
            var prefs = new AppPreferences { ConnectionMode = "adaptive" }; var sessions = new List<Session>(); DateTime time = DateTime.UtcNow;
            using (var controller = new MultiDeviceController(prefs, p => { var session = new Session(); sessions.Add(session); return session; }, a => a(), () => time)) {
                var usb = new Device { Serial = "PHONE123", State = "device", Model = "Phone" };
                var wifi = new Device { Serial = "192.168.1.5:38001", PhysicalId = "PHONE123", State = "device", Model = "Phone" };
                controller.UpdateDevices(new List<Device> { wifi, usb }); Assert(controller.Devices.Count == 1 && !controller.Devices[0].IsWireless && sessions.Count == 1, "USB was not preferred");
                controller.UpdateDevices(new List<Device> { wifi }); Assert(sessions.Count == 1 && sessions[0].Stopped, "Handover overlapped mirrors");
                sessions[0].Exit(); time = time.AddSeconds(2); controller.Tick(); Assert(sessions.Count == 2 && controller.Slots.Single().Controller.Target == wifi.Serial, "Wi-Fi fallback failed");
                controller.UpdateDevices(new List<Device> { wifi, usb }); Assert(sessions[1].Stopped && sessions.Count == 2, "USB return overlapped sessions");
            }
        });
        test("Manual pairing keeps secrets in stdin and uses the separate connection port", () => {
            var commands = new Commands { Services = "adb-PHONE123-test _adb-tls-connect._tcp 192.168.1.5:38001\n" };
            var result = new WirelessPairing(commands).PairCode("192.168.1.5:37001", "123456", CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.Connected && result.Endpoint == "192.168.1.5:38001" && commands.Arguments[0] == "pair \"192.168.1.5:37001\"" && commands.Inputs[0] == "123456" && !commands.Arguments.Any(a => a.Contains("123456")), "Pairing secret or ports incorrect");
        });
        test("QR pairing matches its own nonce before pairing", () => {
            var credentials = new QrCredentials(); var second = new QrCredentials();
            Assert(credentials.ServiceName != second.ServiceName && credentials.Password != second.Password && credentials.Payload.StartsWith("WIFI:T:ADB;S:studio-") && credentials.Payload.EndsWith(";;"), "QR secret reuse or format invalid");
            var commands = new Commands { Services = "other _adb-tls-pairing._tcp 192.168.1.99:37001\n" + credentials.ServiceName + " _adb-tls-pairing._tcp 192.168.1.5:37001\nadb-PHONE123-test _adb-tls-connect._tcp 192.168.1.5:38001\n" };
            var result = new WirelessPairing(commands).PairQr(credentials, CancellationToken.None).GetAwaiter().GetResult();
            Assert(result.Connected && commands.Inputs.Contains(credentials.Password) && !commands.Arguments.Any(a => a.Contains("192.168.1.99")), "Paired an unrelated network service");
        });
        test("A cancelled QR session cannot pair an unrelated advertiser", () => {
            var commands = new Commands { Services = "other _adb-tls-pairing._tcp 192.168.1.99:37001\n" };
            using (var cancellation = new CancellationTokenSource(100)) {
                bool cancelled = false; try { new WirelessPairing(commands).PairQr(new QrCredentials(), cancellation.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && !commands.Arguments.Any(a => a.StartsWith("pair ")), "Cancellation paired an unrelated device");
            }
        });
        test("Invalid endpoints and pairing codes are rejected before launching ADB", () => {
            var commands = new Commands(); var pairing = new WirelessPairing(commands); int rejected = 0;
            foreach (string endpoint in new[] { "host & command:5555", "192.168.1.5:0", "192.168.1.5:65536", "192.168.1.5:-1" }) try { WirelessPairing.NormalizeEndpoint(endpoint); } catch (ArgumentException) { rejected++; }
            try { pairing.PairCode("192.168.1.5:37001", "12345x", CancellationToken.None).GetAwaiter().GetResult(); } catch (ArgumentException) { rejected++; }
            Assert(rejected == 5 && commands.Arguments.Count == 0 && WirelessPairing.Endpoint("::1", "5555") == "[::1]:5555", "Invalid endpoint reached ADB");
        });
        test("Wireless identity links Wi-Fi to its USB profile", () => {
            var commands = new Commands(); var cache = new Dictionary<string, string>(); var pairing = new WirelessPairing(commands);
            var devices = pairing.Identify(Device.Parse("192.168.1.5:38001\tdevice model:Phone\n"), cache, CancellationToken.None).GetAwaiter().GetResult();
            pairing.Identify(devices, cache, CancellationToken.None).GetAwaiter().GetResult();
            Assert(devices[0].Key == "PHONE123" && commands.Arguments.Count == 1, "Identity lookup was not cached");
        });
        test("Wireless ADB commands run without a terminal window", () => {
            var info = AdbTracker.ProcessInfo("adb.exe", "mdns services", "."); Assert(info.CreateNoWindow && !info.UseShellExecute && info.RedirectStandardOutput && info.RedirectStandardError, "ADB could open a terminal");
        });
        test("Real hidden ADB command redirects stdin and terminates a timed-out child", () => {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".wireless-command-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            string executable = Path.Combine(directory, "adb.exe"); File.Copy(Assembly.GetExecutingAssembly().Location, executable);
            try {
                var runner = new AdbCommands(directory);
                var reply = runner.Run("pair 192.168.1.5:37001", "123456", 5000, CancellationToken.None).GetAwaiter().GetResult();
                Assert(reply.Code == 0 && reply.Text.Contains("using stdin"), "Pairing stdin was not redirected");
                bool timedOut = false; try { runner.Run("wait", null, 200, CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { timedOut = true; }
                Assert(timedOut, "Unresponsive ADB command did not time out");
                using (var cancelled = new CancellationTokenSource(200)) {
                    bool stopped = false; try { runner.Run("wait", null, 5000, cancelled.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { stopped = true; }
                    Assert(stopped, "Cancelled ADB child remained running");
                }
            } finally { File.Delete(executable); Directory.Delete(directory); }
        });
        test("Connection mode and paired endpoint metadata persist without secrets", () => {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".wireless-preferences-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); string file = Path.Combine(directory, "settings.xml");
            try {
                var prefs = new AppPreferences { ConnectionMode = "wireless" }; prefs.WirelessPeers.Add(new WirelessPeer { Endpoint = "192.168.1.5:38001", ServiceName = "adb-PHONE123-test", PhysicalId = "PHONE123" }); prefs.Save(file);
                var restored = AppPreferences.Load(file, ""); Assert(restored.ConnectionMode == "wireless" && restored.WirelessPeers.Single().PhysicalId == "PHONE123" && !File.ReadAllText(file).Contains("Password"), "Wireless settings lost or contained secrets");
            } finally { File.Delete(file); Directory.Delete(directory); }
        });
    }
}
