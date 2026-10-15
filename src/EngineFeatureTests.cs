using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AndroidMirror;

static class EngineFeatureTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string, Action> test)
    {
        test("Every documented long option is represented in Advanced", () => {
            var catalog = EngineCatalog.Embedded();
            Assert(catalog.Features.Count == 109 && catalog.Features.Select(f => f.Name).Distinct().Count() == 109, "An engine option was lost or duplicated");
            Assert(catalog.Features.Single(f => f.Name == "--new-display").OptionalValue && catalog.Features.Single(f => f.Name == "--record").TakesValue && !catalog.Features.Single(f => f.Name == "--fullscreen").TakesValue, "Optional and required values confused");
            Assert(catalog.Reference.Contains("MOD+f") && catalog.Reference.Contains("Drag & drop APK") && catalog.Reference.Contains("SCRCPY_SERVER_PATH"), "Shortcut, drag/drop or environment reference missing");
        });
        test("Advanced values stay separate from managed selectors and audio defaults", () => {
            var options = new MirrorOptions(); options.Advanced.Add(new EngineSetting { Name = "--audio-source", Value = "mic", Enabled = true }); options.Advanced.Add(new EngineSetting { Name = "--window-title", Value = "My tablet", Enabled = true });
            string arguments = EngineArguments.Build("device", options, "Phone");
            Assert(Regex.Matches(arguments, "--audio-source=").Count == 1 && !arguments.Contains("audio-source=output") && Regex.Matches(arguments, "--window-title=").Count == 1 && arguments.Contains("My tablet"), "Default option duplicated an override");
            options.AudioEnabled = false; options.ControlEnabled = false; options.Advanced.Add(new EngineSetting { Name = "--keyboard", Value = "uhid", Enabled = true });
            arguments = EngineArguments.Build("device", options, "Phone"); Assert(arguments.Contains("--no-audio") && !arguments.Contains("--audio-source") && !arguments.Contains("--keyboard") && arguments.Contains("--no-control"), "Advanced options bypassed audio/control switches");
        });
        test("Selectors, action-only options and malformed names cannot escape the shell", () => {
            foreach (string name in new[] { "--serial", "--select-usb", "--pause-on-exit", "--list-apps", "--record --otg" }) {
                var options = new MirrorOptions(); options.Advanced.Add(new EngineSetting { Name = name, Enabled = true }); bool rejected = false;
                try { EngineArguments.Build("one", options, "Phone"); } catch (ArgumentException) { rejected = true; } Assert(rejected, "A managed or malformed option was accepted");
            }
        });
        test("Advanced options are copied per device and reset to clean defaults", () => {
            var options = new MirrorOptions(); options.Advanced.Add(new EngineSetting { Name = "--max-fps", Value = "60", Enabled = true }); options.Environment.Add(new EnvironmentSetting { Name = "SCRCPY_ICON_DIR", Value = "custom" });
            var copy = options.Copy(); copy.Advanced[0].Value = "30"; copy.Environment[0].Value = "other";
            Assert(options.Advanced[0].Value == "60" && options.Environment[0].Value == "custom" && !new MirrorOptions().Advanced.Any(), "Device options share mutable state");
        });
        test("Recording sessions get distinct paths without overwriting chosen files", () => {
            string first = EngineArguments.RecordingTarget(Path.Combine(Path.GetTempPath(), "My recording.mp4")), second = EngineArguments.RecordingTarget(Path.Combine(Path.GetTempPath(), "My recording.mp4"));
            Assert(first != second && Path.GetExtension(first) == ".mp4" && Path.GetFileName(first).StartsWith("My recording-") && !File.Exists(first), "Recording path was not safely unique");
        });
        test("OTG can use an unapproved USB transport without enabling wireless control", () => {
            var prefs = new AppPreferences { AutoReconnect = false }; using (var controller = new MultiDeviceController(prefs, p => null, action => action())) {
                controller.UpdateDevices(Device.Parse("USB\tunauthorized\n")); var slot = controller.Slots.Single();
                var options = new MirrorOptions(); options.Advanced.Add(new EngineSetting { Name = "--otg", Enabled = true }); controller.ApplyOptions("USB", options);
                Assert(slot.Controller.CanConnect, "OTG incorrectly requires ADB authorization");
                controller.SetConnectionMode("adaptive");
                controller.UpdateDevices(new System.Collections.Generic.List<Device> { new Device { Serial = "USB", State = "unauthorized" }, new Device { Serial = "192.168.1.5:38001", State = "device", PhysicalId = "USB" } });
                Assert(slot.Device != null && !slot.Device.IsWireless, "OTG selected an authorized Wi-Fi transport instead of USB");
                controller.SetConnectionMode("wireless"); Assert(!slot.Controller.CanConnect, "Wireless-only mode allowed USB input-only mode");
                string arguments = EngineArguments.Build("", options, "USB input"); Assert(arguments.Contains("--otg") && !arguments.Contains("--serial") && !arguments.Contains("--audio-source"), "Standalone OTG included ADB selectors or audio options");
            }
        });
    }
}
