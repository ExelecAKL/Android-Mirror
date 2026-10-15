using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AndroidMirror;

static class MultiDeviceTests
{
    sealed class Session : IMirrorSession
    {
        public string Serial;
        public MirrorOptions Options;
        public int Stops;
        public event Action<int> Exited;
        public bool Ready { get { return true; } }
        public void Start() { }
        public void Stop() { Stops++; }
        public void Exit(int code) { if (Exited != null) Exited(code); }
    }
    sealed class Fixture : IDisposable
    {
        public AppPreferences Preferences = new AppPreferences();
        public MultiDeviceController Controller;
        public List<Session> Sessions = new List<Session>();
        public DateTime Time = new DateTime(2026, 1, 1);
        public Fixture()
        {
            Controller = new MultiDeviceController(Preferences, p => { var session = new Session { Serial = p.Serial, Options = p.Options.Copy() }; Sessions.Add(session); return session; }, action => action(), () => Time);
        }
        public DeviceSlot Slot(string serial) { return Controller.Slots.First(s => s.Profile.Serial == serial); }
        public void Devices(string snapshot) { Controller.UpdateDevices(Device.Parse(snapshot)); }
        public void Advance() { Time = Time.AddMinutes(1); Controller.Tick(); }
        public void Dispose() { Controller.Dispose(); }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Removing a device stops only its mirror and clears its saved profile", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\ntablet\tdevice\n"); f.Controller.Select("phone", true); f.Controller.Select("tablet", true);
                f.Preferences.WirelessPeers.Add(new WirelessPeer { PhysicalId = "phone", Endpoint = "192.168.1.5:38001" });
                var phone = f.Sessions.Single(s => s.Serial == "phone"); var tablet = f.Sessions.Single(s => s.Serial == "tablet");
                f.Controller.Remove("phone");
                Assert(phone.Stops > 0 && tablet.Stops == 0 && f.Preferences.Profiles.All(p => p.Serial != "phone") && f.Preferences.WirelessPeers.Count == 0, "Remove interrupted another device or left saved settings");
                f.Devices("phone\tdevice\ntablet\tdevice\n"); Assert(f.Controller.Slots.All(s => s.Profile.Serial != "phone"), "Removed device immediately reappeared");
            }
        });
        test("A removed device returns with defaults after disconnecting and reconnecting", () => {
            using (var f = new Fixture()) {
                f.Controller.SetAutoReconnect(false); f.Devices("phone\tdevice\n"); f.Controller.SetOptions("phone", false, false); f.Controller.Remove("phone");
                f.Devices(""); f.Devices("phone\tdevice\n");
                Assert(f.Slot("phone").Profile.Options.AudioEnabled && f.Slot("phone").Profile.Options.ControlEnabled && !f.Slot("phone").Profile.Selected, "Removed settings returned or the device started without being selected");
            }
        });
        test("Several devices are selectable without an arbitrary automatic choice", () => {
            using (var f = new Fixture()) { f.Devices("phone\tdevice model:Phone\ntablet\tdevice model:Tablet\n"); Assert(f.Sessions.Count == 0, "A device was chosen automatically"); f.Controller.Select("phone", true); f.Controller.Select("tablet", true); Assert(f.Controller.ActiveCount == 2 && f.Sessions.Count == 2, "Both devices did not mirror"); }
        });
        test("Each device starts with independent audio and control flags", () => {
            using (var f = new Fixture()) {
                f.Controller.SetAutoReconnect(false); f.Devices("phone\tdevice\ntablet\tdevice\n");
                f.Controller.SetOptions("phone", true, false); f.Controller.SetOptions("tablet", false, true);
                f.Controller.Select("phone", true); f.Controller.Select("tablet", true); f.Controller.ConnectSelected();
                var phone = f.Sessions.Single(s => s.Serial == "phone"); var tablet = f.Sessions.Single(s => s.Serial == "tablet");
                Assert(phone.Options.AudioEnabled && !phone.Options.ControlEnabled && !tablet.Options.AudioEnabled && tablet.Options.ControlEnabled, "Device options leaked across sessions");
            }
        });
        test("Changing options restarts only the affected mirror and coalesces rapid changes", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\ntablet\tdevice\n"); f.Controller.Select("phone", true); f.Controller.Select("tablet", true);
                var phone = f.Sessions.Single(s => s.Serial == "phone"); var tablet = f.Sessions.Single(s => s.Serial == "tablet");
                f.Controller.SetOptions("phone", false, true); f.Controller.SetOptions("phone", false, false);
                Assert(phone.Stops == 1 && tablet.Stops == 0 && f.Sessions.Count == 2, "Mirrors overlapped or the other device stopped");
                phone.Exit(0); f.Advance();
                var replacement = f.Sessions.Last(); Assert(f.Sessions.Count == 3 && !replacement.Options.AudioEnabled && !replacement.Options.ControlEnabled && Object.ReferenceEquals(f.Slot("tablet").Controller.Active, tablet), "Updated options were lost");
            }
        });
        test("A cable disconnect reconnects only that device with its saved settings", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\ntablet\tdevice\n"); f.Controller.SetOptions("phone", true, false); f.Controller.Select("phone", true); f.Controller.Select("tablet", true);
                var phone = f.Sessions.Single(s => s.Serial == "phone"); var tablet = f.Sessions.Single(s => s.Serial == "tablet");
                f.Devices("tablet\tdevice\n"); phone.Exit(2); f.Advance(); Assert(f.Sessions.Count == 2 && tablet.Stops == 0, "Other mirror interrupted");
                f.Devices("phone\tdevice\ntablet\tdevice\n"); Assert(f.Sessions.Count == 3 && f.Sessions.Last().Serial == "phone" && !f.Sessions.Last().Options.ControlEnabled, "Wrong device or settings on reconnect");
            }
        });
        test("Deselect and Stop all cancel pending device restarts", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\n"); var first = f.Sessions[0]; f.Controller.SetOptions("phone", false, false); f.Controller.StopAll(); first.Exit(0); f.Advance(); Assert(f.Sessions.Count == 1, "Stop all failed to cancel restart");
                f.Controller.Connect("phone"); var second = f.Sessions.Last(); f.Controller.Select("phone", false); second.Exit(0); f.Advance(); Assert(f.Sessions.Count == 2 && !f.Slot("phone").Profile.Selected, "Deselected device reopened");
            }
        });
        test("Disabled auto reconnect survives a cable loss while options are applying", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\n"); var first = f.Sessions[0]; f.Controller.SetOptions("phone", false, false); f.Controller.SetAutoReconnect(false); f.Devices(""); first.Exit(0); f.Devices("phone\tdevice\n"); f.Advance(); Assert(f.Sessions.Count == 1, "Reconnected despite disabled preference");
            }
        });
        test("A normal mirror-window close pauses only its own device", () => {
            using (var f = new Fixture()) {
                f.Devices("phone\tdevice\ntablet\tdevice\n"); f.Controller.Select("phone", true); f.Controller.Select("tablet", true);
                f.Sessions.First(s => s.Serial == "phone").Exit(0); f.Advance(); Assert(f.Controller.ActiveCount == 1 && f.Sessions.Count == 2 && f.Slot("tablet").Controller.Active != null, "Closing one mirror changed the other");
            }
        });
        test("Audio off keeps capture disabled and view-only disables control and clipboard sync", () => {
            foreach (bool audio in new[] { false, true }) foreach (bool control in new[] { false, true }) {
                string args = MirrorProcess.Arguments("device", new MirrorOptions { AudioEnabled = audio, ControlEnabled = control }, "Tablet");
                Assert(args.Contains("--no-audio") == !audio && args.Contains("--audio-source=output") == audio, "Audio route flags incorrect");
                Assert(args.Contains("--no-control") == !control && args.Contains("--no-clipboard-autosync") == !control, "Read-only flags incorrect");
            }
        });
        test("Profiles persist independently and migrate the former settings file", () => {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".profiles-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            string xml = Path.Combine(root, "settings.xml"), legacy = Path.Combine(root, "settings.txt");
            try {
                File.WriteAllLines(legacy, new[] { "true", "phone", "dark" }); var prefs = AppPreferences.Load(xml, legacy);
                prefs.Profiles[0].Options.ControlEnabled = false; prefs.Profiles.Add(new DeviceProfile { Serial = "tablet", Name = "Tablet & keyboard", Selected = true, Options = new MirrorOptions { AudioEnabled = false } }); prefs.Save(xml);
                var restored = AppPreferences.Load(xml, legacy);
                Assert(restored.SelectionInitialized && restored.Theme == "dark" && restored.Profiles.Count == 2 && !restored.Profiles[0].Options.ControlEnabled && !restored.Profiles[1].Options.AudioEnabled && restored.Profiles[1].Name == "Tablet & keyboard", "Profile persistence or migration failed");
            } finally { foreach (string name in new[] { xml, legacy, xml + ".tmp" }) if (File.Exists(name)) File.Delete(name); Directory.Delete(root); }
        });
        test("Two real mirror child processes run independently", ProcessTest);
    }
    public static void ProcessTest()
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".multi-process-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string executable = Path.Combine(root, EngineLayout.RuntimeExecutable); File.Copy(Assembly.GetExecutingAssembly().Location, executable);
        var actions = new ConcurrentQueue<Action>(); var output = new ConcurrentQueue<string>();
        var preferences = new AppPreferences { AutoReconnect = false };
        using (var controller = new MultiDeviceController(preferences, profile => new MirrorProcess(root, profile.Serial, line => output.Enqueue(profile.Serial + ":" + line), profile.Options.Copy(), profile.Name), action => actions.Enqueue(action)))
        {
            Action<Func<bool>> pump = complete => {
                var elapsed = Stopwatch.StartNew(); while (!complete() && elapsed.ElapsedMilliseconds < 8000) { Action action; while (actions.TryDequeue(out action)) action(); controller.Tick(); System.Threading.Thread.Sleep(10); } Assert(complete(), "Parallel child lifecycle did not complete");
            };
            try {
                controller.UpdateDevices(Device.Parse("phone\tdevice\ntablet\tdevice\n")); controller.SetOptions("phone", true, false); controller.SetOptions("tablet", false, true); controller.Select("phone", true); controller.Select("tablet", true); controller.ConnectSelected();
                pump(() => output.Count(line => line.Contains("FAKE_MIRROR_READY")) == 2);
                Assert(controller.ActiveCount == 2, "Both child processes were not active");
                controller.SetOptions("phone", false, false);
                pump(() => output.Count(line => line.StartsWith("phone:FAKE_MIRROR_READY")) == 2);
                Assert(output.Count(line => line.StartsWith("tablet:FAKE_MIRROR_READY")) == 1, "Unchanged child restarted");
                controller.StopAll(); File.WriteAllText(Path.Combine(root, "release.txt"), "exit"); pump(() => controller.ActiveCount == 0);
            }
            finally { File.WriteAllText(Path.Combine(root, "release.txt"), "exit"); controller.Dispose(); }
        }
        File.Delete(executable); File.Delete(Path.Combine(root, "release.txt")); Directory.Delete(root);
    }
}
