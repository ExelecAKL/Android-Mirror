using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace AndroidMirror
{
    public sealed class EngineSetting
    {
        public string Name = "", Value = "";
        public bool Enabled;
        public EngineSetting Copy() { return new EngineSetting { Name = Name, Value = Value, Enabled = Enabled }; }
    }
    public sealed class EnvironmentSetting { public string Name = "", Value = ""; }
    public sealed class EngineFeature
    {
        public string Name, Syntax, Description, Category;
        public bool TakesValue, OptionalValue;
        public bool IsAction { get { return Name.StartsWith("--list-", StringComparison.Ordinal); } }
        public bool Managed { get { return EngineCatalog.ManagedNames.Contains(Name); } }
        public string Title { get { return System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(Name.TrimStart('-').Replace('-', ' ')); } }
    }
    public sealed class EngineCatalog
    {
        public static readonly string[] ManagedNames = { "--serial", "--select-usb", "--select-tcpip", "--tcpip", "--pause-on-exit", "--help", "--version", "--no-terminal-title" };
        public static readonly string[] EnvironmentNames = { "ADB", "ANDROID_SERIAL", "SCRCPY_ICON_DIR", "SCRCPY_SERVER_PATH" };
        public readonly List<EngineFeature> Features = new List<EngineFeature>();
        public string Reference = "";
        public static EngineCatalog Embedded()
        {
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("engine-help.txt"))
            using (var reader = new StreamReader(source)) return Parse(reader.ReadToEnd());
        }
        public static EngineCatalog Parse(string help)
        {
            var catalog = new EngineCatalog(); EngineFeature current = null;
            var description = new StringBuilder(); bool options = false;
            Action finish = () => { if (current != null) { current.Description = description.ToString().Trim(); catalog.Features.Add(current); current = null; description.Clear(); } };
            foreach (string line in help.Replace("\r", "").Split('\n'))
            {
                if (line == "Options:") { options = true; continue; }
                if (line == "Shortcuts:") { finish(); options = false; catalog.Reference = help.Substring(help.IndexOf("Shortcuts:", StringComparison.Ordinal)); break; }
                if (!options) continue;
                if (Regex.IsMatch(line, @"^    -[^ ]"))
                {
                    finish(); var match = Regex.Match(line, @"--[a-z0-9-]+");
                    if (!match.Success) continue; string tail = line.Substring(match.Index + match.Length);
                    current = new EngineFeature { Name = match.Value, Syntax = line.Trim(), TakesValue = tail.Contains("="), OptionalValue = tail.StartsWith("[", StringComparison.Ordinal), Category = CategoryFor(match.Value) };
                }
                else if (current != null) description.AppendLine(line.Length >= 8 ? line.Substring(8) : line);
            }
            finish();
            if (catalog.Features.Count == 0) throw new IOException("The installed engine did not provide an option catalogue.");
            return catalog;
        }
        public static string CategoryFor(string name)
        {
            if (name.Contains("shortcut")) return "Shortcuts";
            if (name.Contains("v4l2")) return "Video4Linux";
            if (name.Contains("tunnel") || name == "--port" || name == "--force-adb-forward") return "Tunnels";
            if (name == "--otg") return "OTG";
            if (name.Contains("keyboard") || name.Contains("key-repeat") || name.Contains("key-events") || name.Contains("prefer-text")) return "Keyboard";
            if (name.Contains("mouse")) return "Mouse";
            if (name.Contains("gamepad")) return "Gamepad";
            if (name.Contains("camera") || name == "--video-source") return "Camera";
            if (name.Contains("audio")) return "Audio";
            if (name.Contains("record") || name == "--time-limit") return "Recording";
            if (name.Contains("window") || name.Contains("render") || name.Contains("fullscreen") || name.Contains("always-on-top")) return "Window";
            if (name.Contains("control") || name.Contains("clipboard") || name.Contains("paste")) return "Control";
            if (name.Contains("display") || name.Contains("vd-")) return "Virtual display";
            if (name.Contains("video") || name.Contains("fps") || name.Contains("size") || name.Contains("crop") || name.Contains("orientation") || name.Contains("angle") || name.Contains("hwdec")) return "Video";
            if (name.Contains("port") || name.Contains("tunnel") || name.Contains("tcpip") || name.Contains("usb") || name.Contains("adb") || name.Contains("serial")) return "Connection";
            return "Device";
        }
    }
    public static class EngineArguments
    {
        public static string RecordingTarget(string value)
        {
            string directory = Path.GetDirectoryName(value);
            if (!Path.IsPathRooted(value)) directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Android Mirror", directory ?? "");
            string filename = Path.GetFileNameWithoutExtension(value) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + Path.GetExtension(value);
            return Path.GetFullPath(Path.Combine(directory, filename));
        }
        public static string Build(string serial, MirrorOptions options, string name)
        {
            var settings = options.Advanced.Where(s => s.Enabled).GroupBy(s => s.Name).Select(g => g.Last()).ToArray();
            foreach (var setting in settings)
                if (!Regex.IsMatch(setting.Name, @"^--[a-z0-9-]+$") || EngineCatalog.ManagedNames.Contains(setting.Name) || setting.Name.StartsWith("--list-", StringComparison.Ordinal)) throw new ArgumentException("This option is managed by the shell: " + setting.Name);
            bool otg = settings.Any(s => s.Name == "--otg");
            if (otg && !options.ControlEnabled) throw new ArgumentException("Enable PC control to use USB input-only mode.");
            var args = new List<string>();
            if (!String.IsNullOrEmpty(serial)) args.Add("--serial=" + MirrorProcess.Quote(serial));
            if (!settings.Any(s => s.Name == "--window-title")) args.Add("--window-title=" + MirrorProcess.Quote("Android Mirror" + (String.IsNullOrEmpty(name) ? "" : " - " + name)));
            args.Add("--pause-on-exit=false");
            if (!otg)
            {
                if (!options.AudioEnabled) args.Add("--no-audio");
                else if (!settings.Any(s => s.Name == "--audio-source")) args.Add("--audio-source=output");
                if (!options.ControlEnabled) { args.Add("--no-control"); args.Add("--no-clipboard-autosync"); }
            }
            foreach (var setting in settings)
            {
                if (setting.Name == "--no-audio" || setting.Name == "--no-control" || (setting.Name == "--no-clipboard-autosync" && !options.ControlEnabled)) continue;
                if (!options.AudioEnabled && setting.Name.StartsWith("--audio-", StringComparison.Ordinal)) continue;
                if (!options.ControlEnabled && new[] { "--keyboard", "--mouse", "--gamepad" }.Contains(setting.Name)) continue;
                if (setting.Value.IndexOf('\0') >= 0) throw new ArgumentException("Option values must not contain a null character.");
                args.Add(setting.Name + (setting.Value.Length > 0 ? "=" + MirrorProcess.Quote(setting.Value) : ""));
            }
            return String.Join(" ", args);
        }
    }
}
