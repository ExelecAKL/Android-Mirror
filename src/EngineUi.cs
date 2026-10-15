using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        EngineCatalog engineCatalog = EngineCatalog.Embedded();
        MirrorOptions advancedDraft;
        string advancedTarget = "";
        bool advancedOpen, advancedLoading;
        MirrorProcess otgSession;
        sealed class AdvancedTarget
        {
            public string Serial = "", Name;
            public MirrorOptions Options;
            public override string ToString() { return Name; }
        }
        void InitializeEngineUi()
        {
            Control<Button>("AdvancedButton").Click += delegate { OpenAdvanced(true); };
            Control<Button>("ApplyAdvancedButton").Click += delegate { ApplyAdvanced(); };
            Control<Button>("ResetAdvancedButton").Click += delegate { advancedDraft = new MirrorOptions(); RenderFeatureList(); };
            Control<TextBox>("FeatureSearch").TextChanged += delegate { if (advancedOpen) RenderFeatureList(); };
            Control<ComboBox>("FeatureCategory").ItemsSource = new[] { "All features", "Connection", "Video", "Audio", "Control", "Keyboard", "Mouse", "Gamepad", "Device", "Window", "Recording", "Virtual display", "Tunnels", "OTG", "Camera", "Video4Linux", "Shortcuts" };
            Control<ComboBox>("FeatureCategory").SelectedIndex = 0;
            Control<ComboBox>("FeatureCategory").SelectionChanged += delegate { if (advancedOpen) RenderFeatureList(); };
            Control<ComboBox>("AdvancedDevicePicker").SelectionChanged += delegate {
                if (advancedLoading) return;
                var target = Control<ComboBox>("AdvancedDevicePicker").SelectedItem as AdvancedTarget;
                if (target == null) return;
                advancedTarget = target.Serial; advancedDraft = target.Options.Copy(); RenderFeatureList();
            };
            Control<Button>("RunOtgButton").Click += delegate { RunOtg(); };
        }
        void OpenAdvanced(bool open)
        {
            advancedOpen = open;
            if (!open) { Control<ScrollViewer>("AdvancedView").Visibility = Visibility.Collapsed; OpenSettings(true); return; }
            if (wirelessOpen) { CancelPairing(); wirelessOpen = false; Control<ScrollViewer>("WirelessView").Visibility = Visibility.Collapsed; }
            settingsOpen = true; Control<ScrollViewer>("HomeView").Visibility = Visibility.Collapsed; Control<ScrollViewer>("SettingsView").Visibility = Visibility.Collapsed;
            Control<ScrollViewer>("AdvancedView").Visibility = Visibility.Visible;
            var targets = new List<AdvancedTarget> { new AdvancedTarget { Name = "Defaults for new devices", Options = preferences.DefaultOptions } };
            targets.AddRange(controller.Slots.OrderByDescending(s => s.Device != null).ThenBy(s => s.Profile.Name).Select(s => new AdvancedTarget { Name = s.Profile.ToString(), Serial = s.Profile.Serial, Options = s.Profile.Options }));
            var target = targets.FirstOrDefault(t => t.Serial == settingsSerial) ?? targets[0];
            advancedLoading = true; Control<ComboBox>("AdvancedDevicePicker").ItemsSource = targets; Control<ComboBox>("AdvancedDevicePicker").SelectedItem = target; advancedLoading = false;
            advancedTarget = target.Serial; advancedDraft = target.Options.Copy();
            Control<TextBox>("FeatureReference").Text = Friendly(engineCatalog.Reference);
            RenderFeatureList();
            var view = Control<ScrollViewer>("AdvancedView"); view.RenderTransform = new TranslateTransform(); AnimateIn(view, 200, 8);
            if (!preview && !selfCheck) RefreshEngineCatalog();
        }
        static string Friendly(string value) { return value.Replace("scrcpy", "Android Mirror"); }
        async void RefreshEngineCatalog()
        {
            try {
                var result = await new AdbCommands(engineDirectory, EngineLayout.RuntimeExecutable).Run("--help", null, 6000, wirelessLifetime.Token);
                engineCatalog = EngineCatalog.Parse(result.Text);
                if (advancedOpen) { Control<TextBox>("FeatureReference").Text = Friendly(engineCatalog.Reference); RenderFeatureList(); }
            } catch (OperationCanceledException) { } catch (Exception error) { if (!closed) Control<TextBlock>("AdvancedStatus").Text = error.Message; }
        }
        EngineSetting Setting(string name)
        {
            var value = advancedDraft.Advanced.FirstOrDefault(s => s.Name == name);
            if (value == null) { value = new EngineSetting { Name = name }; advancedDraft.Advanced.Add(value); }
            return value;
        }
        Border FeatureCard()
        {
            var card = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10) };
            Resource(card, Border.BackgroundProperty, "Surface"); Resource(card, Border.BorderBrushProperty, "Stroke"); return card;
        }
        Border FeatureDescription(string description)
        {
            var body = new StackPanel(); var paragraph = new StringBuilder();
            Action flush = () => { if (paragraph.Length == 0) return; var text = Text(paragraph.ToString().Trim(), 12, true); text.LineHeight = 18; text.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(text); paragraph.Clear(); };
            foreach (string raw in description.Replace("\r", "").Split('\n')) {
                string line = raw.Trim();
                if (line.Length == 0) { flush(); continue; }
                Uri link;
                if (line.StartsWith("<https://", StringComparison.Ordinal) && Uri.TryCreate(line.Trim('<', '>'), UriKind.Absolute, out link)) {
                    flush(); var text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
                    var hyperlink = new Hyperlink(new Run(link.Host.EndsWith("android.com", StringComparison.OrdinalIgnoreCase) ? "Android documentation ↗" : "Open documentation ↗")) { NavigateUri = link };
                    hyperlink.SetResourceReference(Hyperlink.ForegroundProperty, "Accent");
                    hyperlink.RequestNavigate += (sender, e) => { try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch (Exception error) { AddLog(error.Message); } e.Handled = true; };
                    text.Inlines.Add(hyperlink); body.Children.Add(text); continue;
                }
                if (line.StartsWith("- ") || line.StartsWith("\"") || line.StartsWith("'") || (paragraph.Length > 0 && paragraph[paragraph.Length - 1] == '.' && Char.IsUpper(line[0]))) flush();
                if (paragraph.Length > 0) paragraph.Append(' '); paragraph.Append(line);
            }
            flush();
            var inset = new Border { Child = body, Padding = new Thickness(12, 12, 12, 4), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 8, 0, 4) };
            Resource(inset, Border.BackgroundProperty, "Input"); return inset;
        }
        void RenderFeatureList()
        {
            if (advancedDraft == null) return;
            var list = Control<StackPanel>("FeatureRows"); list.Children.Clear();
            string search = Control<TextBox>("FeatureSearch").Text.Trim(), category = Control<ComboBox>("FeatureCategory").SelectedItem as string;
            var features = engineCatalog.Features.Where(f => (category == null || category == "All features" || f.Category == category) && (search.Length == 0 || (f.Name + " " + f.Title + " " + f.Description).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).OrderBy(f => f.Category).ThenBy(f => f.Title).ToArray();
            Control<TextBlock>("FeatureCount").Text = features.Length + " of " + engineCatalog.Features.Count + " options · changes apply only when saved";
            Control<Expander>("ShortcutReference").Visibility = category == "All features" || category == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
            if (category == "Shortcuts") Control<Expander>("ShortcutReference").IsExpanded = true;
            if (category == "Video4Linux") list.Children.Add(Text("Video4Linux virtual webcam output is a Linux-only feature. This Windows engine supports camera mirroring and recording, but does not provide a V4L2 device.", 13, true));
            foreach (var feature in features)
            {
                var card = FeatureCard(); var content = new StackPanel(); card.Child = content;
                var title = Text(feature.Title, 14); title.FontWeight = FontWeights.SemiBold; content.Children.Add(title);
                var syntax = Text(feature.Syntax, 11, true); syntax.FontFamily = new FontFamily("Consolas"); syntax.Margin = new Thickness(0, 4, 0, 8); content.Children.Add(syntax);
                string description = Friendly(feature.Description);
                var details = new Expander { Header = "About this option", Margin = new Thickness(0, 0, 0, 8), FontSize = 12 };
                Resource(details, System.Windows.Controls.Control.ForegroundProperty, "Muted"); details.Content = FeatureDescription(description); content.Children.Add(details);
                if (feature.Managed)
                {
                    string hint = feature.Name == "--serial" || feature.Name.StartsWith("--select-") ? "Use the device picker and Connection mode in Settings." : feature.Name == "--tcpip" ? "Use Wireless setup, including legacy USB setup." : feature.Name == "--help" || feature.Name == "--version" ? "Options are listed here; keyboard shortcuts and environment settings are below. The engine version is in Updates." : "Handled by the app so sessions stay in the UI without a terminal.";
                    content.Children.Add(Text(hint, 12, true)); list.Children.Add(card); continue;
                }
                if (feature.IsAction)
                {
                    var run = new Button { Content = "Show on this device", Margin = new Thickness(0, 4, 0, 0) }; run.Click += async delegate { await QueryDevice(feature); }; content.Children.Add(run); list.Children.Add(card); continue;
                }
                bool audio = feature.Name == "--no-audio", control = feature.Name == "--no-control";
                var setting = audio || control ? null : Setting(feature.Name);
                var enabled = new CheckBox { Content = feature.TakesValue ? "Override default" : "Enabled", IsChecked = audio ? !advancedDraft.AudioEnabled : control ? !advancedDraft.ControlEnabled : setting.Enabled, Margin = new Thickness(0, 2, 0, 6) };
                content.Children.Add(enabled);
                FrameworkElement editor = null;
                if (feature.TakesValue)
                {
                    string[] choices; if (Choices.TryGetValue(feature.Name, out choices)) {
                        var combo = new ComboBox { ItemsSource = new[] { "" }.Concat(choices).ToArray(), IsEditable = true, MinHeight = 36, Text = setting.Value, Margin = new Thickness(0, 4, 0, 0) };
                        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((sender, e) => { setting.Value = combo.Text; })); editor = combo;
                    } else { var input = new TextBox { Text = setting.Value, Margin = new Thickness(0, 4, 0, 0) }; input.TextChanged += delegate { setting.Value = input.Text; }; editor = input; }
                    editor.IsEnabled = enabled.IsChecked == true; content.Children.Add(editor);
                    if (feature.Name == "--record") {
                        var browse = new Button { Content = "Choose recording file…", Margin = new Thickness(0, 8, 0, 0) };
                        browse.Click += delegate { var dialog = new SaveFileDialog { Filter = "MP4 video|*.mp4|Matroska video|*.mkv|Audio|*.m4a;*.opus;*.flac;*.wav|All files|*.*", AddExtension = true, DefaultExt = ".mp4" }; if (dialog.ShowDialog(window) == true) { setting.Value = dialog.FileName; setting.Enabled = true; RenderFeatureList(); } }; content.Children.Add(browse);
                    }
                    if (feature.OptionalValue) content.Children.Add(Text("Leave the value empty to use this option’s automatic value.", 11, true));
                }
                Action setEnabled = () => { bool value = enabled.IsChecked == true; if (audio) advancedDraft.AudioEnabled = !value; else if (control) advancedDraft.ControlEnabled = !value; else setting.Enabled = value; if (editor != null) editor.IsEnabled = value; };
                enabled.Checked += delegate { setEnabled(); }; enabled.Unchecked += delegate { setEnabled(); };
                list.Children.Add(card);
            }
            var environment = Control<StackPanel>("EngineEnvironmentRows"); environment.Children.Clear();
            foreach (string name in EngineCatalog.EnvironmentNames)
            {
                var item = advancedDraft.Environment.FirstOrDefault(e => e.Name == name);
                if (item == null) { item = new EnvironmentSetting { Name = name }; advancedDraft.Environment.Add(item); }
                environment.Children.Add(Text(name, 12, true)); var input = new TextBox { Text = item.Value, Margin = new Thickness(0, 4, 0, 10) }; input.TextChanged += delegate { item.Value = input.Text; }; environment.Children.Add(input);
            }
        }
        async void ApplyAdvanced()
        {
            try
            {
                foreach (var setting in advancedDraft.Advanced.Where(s => s.Enabled))
                {
                    var feature = engineCatalog.Features.FirstOrDefault(f => f.Name == setting.Name);
                    if (feature == null) throw new ArgumentException("The installed engine does not support " + setting.Name + ". Disable it before applying.");
                    if (feature.TakesValue && !feature.OptionalValue && String.IsNullOrWhiteSpace(setting.Value)) throw new ArgumentException("Enter a value for " + feature.Title + ".");
                }
                EngineArguments.Build("", advancedDraft, "");
                if (advancedDraft.Has("--kill-adb-on-close") && !await ConfirmDialog("Stop ADB when a mirror closes?", "This stops the shared ADB server and can disconnect other apps using your devices.", "Enable option")) return;
                if (advancedTarget.Length == 0) preferences.DefaultOptions = advancedDraft.Copy();
                else controller.ApplyOptions(advancedTarget, advancedDraft);
                var target = Control<ComboBox>("AdvancedDevicePicker").SelectedItem as AdvancedTarget; if (target != null) target.Options = advancedDraft.Copy();
                SaveSettings(); Control<TextBlock>("AdvancedStatus").Text = "Saved. Only the affected active session reopens. New devices inherit the saved defaults.";
            }
            catch (Exception error) { Control<TextBlock>("AdvancedStatus").Text = error.Message; }
        }
        async Task QueryDevice(EngineFeature feature)
        {
            if (preview || selfCheck) { Control<TextBox>("EngineQueryOutput").Text = "Device information appears here."; return; }
            var slot = controller.Slots.FirstOrDefault(s => s.Profile.Serial == advancedTarget && s.Device != null);
            if (slot == null) { Control<TextBlock>("AdvancedStatus").Text = "Choose a connected device before requesting its capabilities."; return; }
            try {
                Control<TextBlock>("AdvancedStatus").Text = "Reading device capabilities…";
                var reply = await new AdbCommands(engineDirectory, EngineLayout.RuntimeExecutable).Run("--serial=" + MirrorProcess.Quote(slot.Device.Serial) + " " + feature.Name + " --pause-on-exit=false", null, 20000, wirelessLifetime.Token);
                if (closed) return;
                Control<TextBox>("EngineQueryOutput").Text = Friendly(String.Join("\n", reply.Text.Split('\n').Where(line => !line.StartsWith("scrcpy ", StringComparison.Ordinal)))); Control<Expander>("DeviceQueryExpander").IsExpanded = true;
                Control<TextBlock>("AdvancedStatus").Text = reply.Code == 0 ? "Device capabilities loaded below." : "The device could not provide this list. Details are below.";
            } catch (OperationCanceledException) { } catch (Exception error) { if (!closed) Control<TextBlock>("AdvancedStatus").Text = error.Message; }
        }
        void RunOtg()
        {
            if (otgSession != null) { otgSession.Stop(); Control<Button>("RunOtgButton").Content = "Stopping…"; return; }
            if (preview || selfCheck) return;
            if (preferences.ConnectionMode == "wireless") { Control<TextBlock>("AdvancedStatus").Text = "USB input-only mode requires USB. Choose USB only or Adaptive in Settings."; return; }
            if (controller.Slots.Any(s => s.Controller.Active != null && s.Device != null && !s.Device.IsWireless)) { Control<TextBlock>("AdvancedStatus").Text = "Stop active USB mirrors before starting standalone USB input-only mode."; return; }
            try {
                var options = advancedDraft.Copy(); options.ControlEnabled = true; options.AudioEnabled = false;
                options.Advanced = options.Advanced.Where(s => new[] { "--keyboard", "--mouse", "--gamepad", "--mouse-bind", "--shortcut-mod" }.Contains(s.Name)).ToList();
                options.Advanced.Add(new EngineSetting { Name = "--otg", Enabled = true });
                var session = new MirrorProcess(engineDirectory, Control<TextBox>("OtgSerial").Text.Trim(), AddLog, options, "USB input"); otgSession = session;
                session.Exited += code => OnUI(() => { if (otgSession == session) { otgSession = null; Control<Button>("RunOtgButton").Content = "Start USB input only"; Control<TextBlock>("AdvancedStatus").Text = code == 0 ? "USB input session stopped." : "USB input could not start. Check Connection help for details."; Render(); } });
                session.Start(); Control<Button>("RunOtgButton").Content = "Stop USB input"; Control<TextBlock>("AdvancedStatus").Text = "USB input session started. No USB debugging is needed. No video or audio is captured."; Render();
            } catch (Exception error) { if (otgSession != null) otgSession.Dispose(); otgSession = null; Control<TextBlock>("AdvancedStatus").Text = error.Message; }
        }
        static readonly Dictionary<string, string[]> Choices = new Dictionary<string, string[]> {
            { "--shortcut-mod", new[] { "lalt", "ralt", "lctrl", "rctrl", "lsuper", "rsuper", "lalt,lsuper", "lctrl+lalt" } },
            { "--video-codec", new[] { "h264", "h265", "av1" } }, { "--audio-codec", new[] { "opus", "aac", "flac", "raw" } },
            { "--video-source", new[] { "display", "camera" } }, { "--audio-source", new[] { "output", "playback", "mic", "mic-unprocessed", "mic-camcorder", "mic-voice-recognition", "mic-voice-communication", "voice-call", "voice-call-uplink", "voice-call-downlink", "voice-performance" } },
            { "--keyboard", new[] { "disabled", "sdk", "uhid", "aoa" } }, { "--mouse", new[] { "disabled", "sdk", "uhid", "aoa" } }, { "--gamepad", new[] { "disabled", "uhid", "aoa" } },
            { "--hwdec", new[] { "auto", "disabled", "d3d11va" } }, { "--camera-facing", new[] { "front", "back", "external" } }, { "--display-ime-policy", new[] { "local", "fallback", "hide" } },
            { "--record-format", new[] { "mp4", "mkv", "m4a", "mka", "opus", "aac", "flac", "wav" } }, { "--render-fit", new[] { "letterbox", "stretched", "unscaled" } },
            { "--render-driver", new[] { "direct3d", "opengl", "opengles2", "opengles", "software" } }
        };
    }
}
