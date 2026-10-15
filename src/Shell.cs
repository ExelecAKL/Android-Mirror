using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        readonly string directory = AppDomain.CurrentDomain.BaseDirectory;
        readonly string engineDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine");
        readonly Window window;
        readonly MultiDeviceController controller;
        readonly AppPreferences preferences;
        readonly DispatcherTimer timer;
        readonly Queue<string> logs = new Queue<string>();
        readonly Dictionary<string, DeviceRow> rows = new Dictionary<string, DeviceRow>();
        AdbTracker tracker;
        string trackerError = "", lastSettings = "", settingsSerial = "", rowSignature = "", settingsSignature = "";
        bool updating, closed, settingsOpen;
        readonly bool preview, motionPreview, selfCheck;
        readonly string settingsFile;
        string animatedState = "", themeMode = "system";
        bool pulseActive, exiting, backgroundTipShown;
        TrayIntegration tray;
        RegisteredWaitHandle activationWait, quitWait;
        bool MotionEnabled { get { return (!preview || motionPreview) && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast; } }
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        T Control<T>(string name) where T : class { return (T)window.FindName(name); }
        void OnUI(Action action)
        {
            if (!closed && !window.Dispatcher.HasShutdownStarted) window.Dispatcher.BeginInvoke(new Action(() => { if (!closed) action(); }));
        }
        sealed class DeviceRow
        {
            public Border Root;
            public CheckBox Selected;
            public TextBlock Name, Status, Summary;
            public Button Action;
        }
        sealed class PreviewMirror : IMirrorSession
        {
            public event Action<int> Exited;
            public bool Ready { get { return true; } }
            public void Start() { }
            public void Stop() { if (Exited != null) Exited(0); }
        }
        public Shell(bool preview, bool motionPreview = false, EventWaitHandle activation = null, bool selfCheck = false, EventWaitHandle quitSignal = null)
        {
            this.preview = preview; this.motionPreview = motionPreview; this.selfCheck = selfCheck;
            string data = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android Mirror");
            settingsFile = System.IO.Path.Combine(data, "settings.xml");
            string legacy = System.IO.Path.Combine(data, "settings.txt");
            if (!File.Exists(legacy)) legacy = System.IO.Path.Combine(directory, ".scrcpy-connect", "settings.txt");
            if (!File.Exists(legacy)) legacy = System.IO.Path.Combine(directory, "archive", "preferences", ".scrcpy-connect", "settings.txt");
            preferences = preview ? new AppPreferences() : AppPreferences.Load(settingsFile, legacy);
            themeMode = new[] { "system", "dark", "light" }.Contains(preferences.Theme) ? preferences.Theme : "system";
            using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml"))
            using (var reader = new StreamReader(source))
            {
                string xaml = reader.ReadToEnd();
                if (!MotionEnabled) xaml = xaml.Replace("0:0:0.12", "0:0:0").Replace("PopupAnimation=\"Fade\"", "PopupAnimation=\"None\"");
                window = (Window)XamlReader.Parse(xaml);
            }
            using (var artwork = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-artwork.png")) window.Icon = BitmapFrame.Create(artwork, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Control<Image>("BrandIcon").Source = SvgLogo.LoadEmbedded();
            controller = new MultiDeviceController(preferences, profile => preview ? (IMirrorSession)new PreviewMirror() : new MirrorProcess(engineDirectory, String.IsNullOrEmpty(profile.TransportSerial) ? profile.Serial : profile.TransportSerial, line => AddLog(profile.Name + ": " + line), profile.Options.Copy(), profile.Name), OnUI);
            if (selfCheck)
            {
                preferences.SelectionInitialized = true;
                foreach (var slot in controller.Slots) slot.Controller.Wanted = false;
                window.ShowActivated = false; window.ShowInTaskbar = false;
            }
            controller.Changed += Render;
            Control<Button>("SettingsButton").Click += delegate { if (wirelessOpen) OpenWireless(false); else OpenSettings(!settingsOpen); };
            window.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape && (settingsOpen || wirelessOpen)) { if (wirelessOpen) OpenWireless(false); else OpenSettings(false); e.Handled = true; } };
            Control<Button>("PrimaryButton").Click += delegate {
                if (!String.IsNullOrEmpty(trackerError) && controller.ActiveCount == 0) StartTracker();
                else if (controller.ActiveCount > 0 || controller.Waiting) controller.StopAll();
                else controller.ConnectSelected();
                SaveSettings();
            };
            Control<CheckBox>("AutoReconnect").Checked += delegate { if (!updating) { controller.SetAutoReconnect(true); SaveSettings(); } };
            Control<CheckBox>("AutoReconnect").Unchecked += delegate { if (!updating) { controller.SetAutoReconnect(false); SaveSettings(); } };
            Control<CheckBox>("AudioToggle").Checked += delegate { ChangeDeviceOptions(); };
            Control<CheckBox>("AudioToggle").Unchecked += delegate { ChangeDeviceOptions(); };
            Control<CheckBox>("ControlToggle").Checked += delegate { ChangeDeviceOptions(); };
            Control<CheckBox>("ControlToggle").Unchecked += delegate { ChangeDeviceOptions(); };
            Control<CheckBox>("StartupToggle").Checked += delegate { ChangeStartup(true); };
            Control<CheckBox>("StartupToggle").Unchecked += delegate { ChangeStartup(false); };
            Control<ComboBox>("SettingsDevice").SelectionChanged += delegate {
                if (updating) return;
                var profile = Control<ComboBox>("SettingsDevice").SelectedItem as DeviceProfile;
                settingsSerial = profile == null ? "" : profile.Serial; RenderSettings();
            };
            Control<ComboBox>("ThemePicker").SelectionChanged += delegate {
                if (updating) return;
                themeMode = new[] { "system", "light", "dark" }[Math.Max(0, Control<ComboBox>("ThemePicker").SelectedIndex)];
                preferences.Theme = themeMode; RefreshAppearance(); SaveSettings();
            };
            Control<Expander>("Help").Expanded += delegate { RefreshLog(); AnimateIn(Control<TextBox>("Diagnostics"), 160, 0); };
            window.SourceInitialized += delegate { RefreshAppearance(); };
            window.Closing += (sender, e) => {
                if (preview || exiting) return;
                e.Cancel = true; SaveSettings(); window.Hide();
                if (!backgroundTipShown) { backgroundTipShown = true; if (tray != null && !selfCheck) tray.NotifyBackground(); }
            };
            window.Closed += delegate {
                SaveSettings(); closed = true; timer.Stop();
                SystemEvents.UserPreferenceChanged -= OnPreferenceChanged;
                if (activationWait != null) activationWait.Unregister(null);
                if (quitWait != null) quitWait.Unregister(null);
                wirelessLifetime.Cancel(); CancelPairing();
                if (tracker != null) tracker.Dispose();
                controller.Dispose(); if (tray != null) tray.Dispose();
            };
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            timer.Tick += delegate { controller.Tick(); MaybeReconnectWireless(); };
            if (!preview)
            {
                tray = new TrayIntegration(Assembly.GetExecutingAssembly().Location, "ScrcpyConnect-" + StableId(directory));
                tray.OpenRequested += () => OnUI(RestoreWindow);
                tray.SettingsRequested += () => OnUI(() => { RestoreWindow(); OpenSettings(true); });
                tray.QuitRequested += () => OnUI(Quit);
                tray.MirrorRequested += () => OnUI(() => { if (controller.ActiveCount > 0 || controller.Waiting) controller.StopAll(); else controller.ConnectSelected(); });
                tray.Error += AddLog;
                SystemEvents.UserPreferenceChanged += OnPreferenceChanged;
                if (activation != null) activationWait = ThreadPool.RegisterWaitForSingleObject(activation, (state, timeout) => OnUI(RestoreWindow), null, Timeout.Infinite, false);
                if (quitSignal != null) quitWait = ThreadPool.RegisterWaitForSingleObject(quitSignal, (state, timeout) => OnUI(Quit), null, Timeout.Infinite, false);
                Application.Current.SessionEnding += delegate { exiting = true; };
            }
            InitializeWireless(); RefreshAppearance();
        }
        void RestoreWindow()
        {
            window.Show(); window.WindowState = WindowState.Normal;
            if (!selfCheck) { window.Activate(); SetForegroundWindow(new WindowInteropHelper(window).Handle); }
            Control<Grid>("AppContent").RenderTransform = new TranslateTransform(); AnimateIn(Control<Grid>("AppContent"), 200, 4);
        }
        void Quit() { exiting = true; window.Close(); Application.Current.Shutdown(); }
        void OnPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { OnUI(RefreshAppearance); }
        void RefreshAppearance() { Appearance.Apply(window, themeMode, preview); Render(); }
        void SaveSettings()
        {
            if (preview || selfCheck) return;
            preferences.Theme = themeMode;
            string signature = preferences.AutoReconnect + "|" + preferences.SelectionInitialized + "|" + themeMode + "|" + preferences.ConnectionMode + "|" + String.Join("|", preferences.WirelessPeers.Select(p => p.Endpoint + ":" + p.ServiceName + ":" + p.PhysicalId)) + "|" + String.Join("|", preferences.Profiles.Select(p => p.Serial + ":" + p.Name + ":" + p.Selected + ":" + p.Options.AudioEnabled + ":" + p.Options.ControlEnabled));
            if (signature == lastSettings) return;
            try { preferences.Save(settingsFile); lastSettings = signature; }
            catch (Exception error) { AddLog("Couldn’t save settings: " + error.Message); }
        }
        void StartTracker()
        {
            if (tracker != null) tracker.Dispose();
            var next = new AdbTracker(engineDirectory); tracker = next; trackerError = "";
            next.DevicesChanged += async devices => {
                int revision = Interlocked.Increment(ref snapshotRevision);
                try
                {
                    if (preferences.ConnectionMode != "usb") devices = await wireless.Identify(devices, wirelessIdentities, wirelessLifetime.Token).ConfigureAwait(false);
                    OnUI(() => {
                        if (tracker != next || revision != snapshotRevision) return;
                        var previous = controller.Devices.Where(d => d.State == "device").Select(d => d.Key).ToArray();
                        foreach (var device in devices.Where(d => d.IsWireless && d.PhysicalId.Length > 0))
                            foreach (var peer in preferences.WirelessPeers.Where(p => p.Endpoint == device.Serial || (p.ServiceName.Length > 0 && device.Serial.StartsWith(p.ServiceName + ".", StringComparison.Ordinal)))) peer.PhysicalId = device.PhysicalId;
                        trackerError = ""; controller.UpdateDevices(devices); SaveSettings();
                        if (tray != null && !selfCheck) foreach (var device in controller.Devices.Where(d => d.State == "device" && !previous.Contains(d.Key))) tray.NotifyConnected(device);
                    });
                }
                catch (OperationCanceledException) { }
                catch (Exception error) { OnUI(() => { if (tracker == next) { trackerError = error.Message; Render(); } }); }
            };
            next.StatusChanged += error => OnUI(() => { if (tracker != next) return; trackerError = error; if (!String.IsNullOrEmpty(error)) AddLog("ADB: " + error); Render(); });
            next.Run();
        }
        void AddLog(string line)
        {
            lock (logs) { logs.Enqueue(line); while (logs.Count > 120) logs.Dequeue(); }
            OnUI(() => { if (Control<Expander>("Help").IsExpanded) RefreshLog(); });
        }
        void RefreshLog()
        {
            string message; lock (logs) message = String.Join(Environment.NewLine, logs);
            Control<TextBox>("Diagnostics").Text = String.IsNullOrEmpty(message) ? "Listening for Android device changes. No errors reported." : message;
            Control<TextBox>("Diagnostics").ScrollToEnd();
        }
        static void Resource(FrameworkElement element, DependencyProperty property, string key) { element.SetResourceReference(property, key); }
        TextBlock Text(string text, int size, bool muted = false)
        {
            var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            Resource(label, TextBlock.ForegroundProperty, muted ? "Muted" : "Text"); return label;
        }
        DeviceRow MakeRow(DeviceSlot slot)
        {
            string serial = slot.Profile.Serial;
            var row = new DeviceRow();
            row.Root = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 8) };
            Resource(row.Root, Border.BackgroundProperty, "Surface"); Resource(row.Root, Border.BorderBrushProperty, "Stroke");
            var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Selected = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Select this device to mirror" };
            AutomationProperties.SetName(row.Selected, "Select " + slot.Profile.Name);
            row.Selected.Checked += delegate { if (!updating) { controller.Select(serial, true); SaveSettings(); } };
            row.Selected.Unchecked += delegate { if (!updating) { controller.Select(serial, false); SaveSettings(); } };
            var copy = new StackPanel(); Grid.SetColumn(copy, 1);
            row.Name = Text("", 14); row.Name.FontWeight = FontWeights.SemiBold; row.Name.ToolTip = serial;
            row.Status = Text("", 12, true); row.Status.Margin = new Thickness(0, 4, 0, 0);
            row.Summary = Text("", 12, true); row.Summary.Margin = new Thickness(0, 4, 0, 0);
            copy.Children.Add(row.Name); copy.Children.Add(row.Status); copy.Children.Add(row.Summary);
            row.Action = new Button { Padding = new Thickness(12, 6, 12, 6), MinHeight = 34, MinWidth = 62, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(row.Action, 2);
            row.Action.Click += delegate { if (slot.Controller.Active != null || (slot.Controller.Wanted && !slot.Connected)) controller.Stop(serial); else controller.Connect(serial); SaveSettings(); };
            grid.Children.Add(row.Selected); grid.Children.Add(copy); grid.Children.Add(row.Action); row.Root.Child = grid; return row;
        }
        void RenderRows()
        {
            var visible = controller.VisibleSlots.ToArray(); string signature = String.Join("|", visible.Select(s => s.Profile.Serial));
            if (signature != rowSignature)
            {
                rowSignature = signature; rows.Clear(); Control<StackPanel>("DeviceRows").Children.Clear();
                foreach (var slot in visible) { var row = MakeRow(slot); rows.Add(slot.Profile.Serial, row); Control<StackPanel>("DeviceRows").Children.Add(row.Root); AnimateIn(row.Root, 180, 0); }
            }
            updating = true;
            foreach (var slot in visible)
            {
                var row = rows[slot.Profile.Serial]; row.Name.Text = slot.Profile.Name; row.Status.Text = slot.Status; row.Summary.Text = slot.Summary; row.Selected.IsChecked = slot.Profile.Selected;
                row.Action.Content = slot.Controller.Stopping ? "Wait…" : slot.Controller.Active != null || (slot.Controller.Wanted && !slot.Connected) ? "Stop" : "Mirror";
                row.Action.IsEnabled = !slot.Controller.Stopping && (slot.Connected || slot.Controller.Wanted);
                AutomationProperties.SetName(row.Action, row.Action.Content + " " + slot.Profile.Name);
            }
            updating = false;
        }
        void RenderSettings()
        {
            if (closed) return;
            updating = true;
            var profiles = controller.Slots.OrderByDescending(s => s.Device != null).ThenBy(s => s.Profile.Name).Select(s => s.Profile).ToArray();
            string signature = String.Join("|", profiles.Select(p => p.Serial + ":" + p.Name));
            var combo = Control<ComboBox>("SettingsDevice");
            if (settingsSignature != signature) { settingsSignature = signature; combo.ItemsSource = profiles; }
            var selected = profiles.FirstOrDefault(p => p.Serial == settingsSerial) ?? profiles.FirstOrDefault();
            combo.SelectedItem = selected; settingsSerial = selected == null ? "" : selected.Serial; combo.IsEnabled = selected != null;
            Control<CheckBox>("AudioToggle").IsEnabled = selected != null; Control<CheckBox>("ControlToggle").IsEnabled = selected != null;
            Control<CheckBox>("AudioToggle").IsChecked = selected != null && selected.Options.AudioEnabled;
            Control<CheckBox>("ControlToggle").IsChecked = selected != null && selected.Options.ControlEnabled;
            Control<CheckBox>("AutoReconnect").IsChecked = preferences.AutoReconnect;
            Control<CheckBox>("StartupToggle").IsChecked = tray != null && tray.StartupEnabled;
            Control<CheckBox>("StartupToggle").IsEnabled = !preview && !selfCheck;
            Control<ComboBox>("ThemePicker").SelectedIndex = themeMode == "light" ? 1 : themeMode == "dark" ? 2 : 0;
            Control<ComboBox>("ConnectionModePicker").SelectedIndex = preferences.ConnectionMode == "usb" ? 0 : preferences.ConnectionMode == "wireless" ? 1 : 2;
            Control<Button>("WirelessSetupButton").IsEnabled = preferences.ConnectionMode != "usb";
            Control<TextBlock>("DeviceSettingsHint").Text = selected == null ? "Connect a device to configure it." : "Saved for this device. Changes apply immediately and may briefly reopen its mirror. Audio routing needs Android 11 or later.";
            updating = false;
        }
        void OpenSettings(bool open)
        {
            if (wirelessOpen) { wirelessOpen = false; CancelPairing(); Control<ScrollViewer>("WirelessView").Visibility = Visibility.Collapsed; }
            settingsOpen = open; RenderSettings();
            Control<ScrollViewer>("HomeView").Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            Control<ScrollViewer>("SettingsView").Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("SettingsGlyph").Text = open ? "\uE72B" : "\uE713";
            Control<Button>("SettingsButton").ToolTip = open ? "Back to devices" : "Settings";
            AutomationProperties.SetName(Control<Button>("SettingsButton"), open ? "Back to devices" : "Settings");
            var view = Control<ScrollViewer>(open ? "SettingsView" : "HomeView"); view.RenderTransform = new TranslateTransform(); AnimateIn(view, 200, open ? 8 : 4);
        }
        void ChangeDeviceOptions()
        {
            if (updating || String.IsNullOrEmpty(settingsSerial)) return;
            controller.SetOptions(settingsSerial, Control<CheckBox>("AudioToggle").IsChecked == true, Control<CheckBox>("ControlToggle").IsChecked == true); SaveSettings();
        }
        void ChangeStartup(bool enabled) { if (!updating && tray != null && !selfCheck) { tray.StartupEnabled = enabled; RenderSettings(); } }
        void Render()
        {
            if (closed) return;
            RenderRows(); RenderSettings();
            int active = controller.ActiveCount, selected = controller.VisibleSlots.Count(s => s.Profile.Selected);
            string title, description, tag, color = "#777777";
            bool ready = controller.Slots.Any(s => s.Controller.Active != null && s.Controller.Active.Ready);
            if (active > 0)
            {
                title = ready ? (active == 1 ? "Your device is mirrored" : active + " devices mirrored") : "Opening your devices";
                description = String.Join(", ", controller.Slots.Where(s => s.Controller.Active != null).Select(s => s.Profile.Name));
                tag = ready ? "Mirroring" : "Connecting"; color = Appearance.IsDark ? "#32DE84" : "#13783F";
            }
            else if (!String.IsNullOrEmpty(trackerError)) { title = "Device detection is reconnecting"; description = "We’ll keep trying. Connection help has the details."; tag = "Reconnecting"; color = Appearance.IsDark ? "#F6C85F" : "#926000"; }
            else if (controller.Devices.Any(d => d.State == "unauthorized") && !controller.Devices.Any(d => d.State == "device")) { title = "Allow the connection"; description = "Unlock your device and allow USB debugging."; tag = "Permission needed"; color = Appearance.IsDark ? "#F6C85F" : "#926000"; }
            else if (controller.Waiting) { title = "Waiting to reconnect"; description = "Plug your selected devices back in. Each mirror will reopen with its own settings."; tag = "Waiting"; }
            else if (controller.Devices.Count > 0) { title = "Choose your devices"; description = "Select one or several devices below. Use Settings for each device’s audio and controls."; tag = "Devices detected"; }
            else { title = "Looking for your devices"; description = preferences.ConnectionMode == "usb" ? "Plug in an Android device and enable USB debugging." : preferences.ConnectionMode == "wireless" ? "Pair a device over Wi-Fi in Settings. Enable Wireless debugging on the device." : "Plug in an Android device, or pair over Wi-Fi in Settings."; tag = "Listening"; }
            Control<TextBlock>("StateTitle").Text = title; Control<TextBlock>("StateDescription").Text = description; Control<TextBlock>("StatusTag").Text = tag;
            Control<Ellipse>("StatusDot").Fill = SystemParameters.HighContrast ? SystemColors.HighlightBrush : (Brush)new BrushConverter().ConvertFromString(color);
            var primary = Control<Button>("PrimaryButton");
            primary.Content = !String.IsNullOrEmpty(trackerError) && active == 0 ? "Retry detection" : active > 0 || controller.Waiting ? "Stop all mirrors" : "Mirror selected";
            primary.IsEnabled = !String.IsNullOrEmpty(trackerError) || active > 0 || controller.Waiting || controller.CanConnect;
            AutomationProperties.SetName(primary, primary.Content.ToString());
            Control<TextBlock>("SelectionHint").Text = selected == 0 ? "Select the devices you want to mirror." : selected + " selected · one window per device · separate audio and control settings";
            AnimateState(title, tag);
            if (tray != null) tray.Update(active > 1 ? active + " mirrors" : tag, active > 0, controller.CanConnect, preferences.AutoReconnect, controller.Waiting || controller.Slots.Any(s => s.Controller.Stopping || (s.Controller.Active != null && !s.Controller.Active.Ready)), Appearance.IsDark, themeMode);
        }
        void AnimateIn(FrameworkElement element, int milliseconds, double distance)
        {
            if (!MotionEnabled) return;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            var translate = element.RenderTransform as TranslateTransform;
            if (distance > 0 && translate != null) translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, TimeSpan.FromMilliseconds(milliseconds)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        void AnimateState(string title, string tag)
        {
            bool shouldPulse = MotionEnabled && new[] { "Connecting", "Reconnecting", "Waiting", "Listening" }.Contains(tag);
            if (shouldPulse != pulseActive)
            {
                pulseActive = shouldPulse;
                Control<Ellipse>("StatusDot").BeginAnimation(UIElement.OpacityProperty, shouldPulse ? new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } } : null);
            }
            if (animatedState == title) return; animatedState = title;
            AnimateIn(Control<StackPanel>("StateCopy"), 200, 4);
        }
        public void Show(string renderPath, string renderState, bool background = false)
        {
            if (renderState.StartsWith("dark-")) { themeMode = "dark"; renderState = renderState.Substring(5); }
            else if (renderState.StartsWith("light-")) { themeMode = "light"; renderState = renderState.Substring(6); }
            if (!background) window.Show(); RefreshAppearance();
            Control<Grid>("AppContent").RenderTransform = new TranslateTransform(); AnimateIn(Control<Grid>("AppContent"), 280, 8);
            if (!preview)
            {
                var missing = new[] { "adb.exe", "scrcpy.exe", "scrcpy-server" }.Where(name => !File.Exists(System.IO.Path.Combine(engineDirectory, name))).ToArray();
                if (missing.Length > 0) { trackerError = "Missing: " + String.Join(", ", missing); AddLog(trackerError + ". Keep the engine folder beside Android Mirror."); Render(); }
                else { StartTracker(); timer.Start(); }
            }
            else
            {
                preferences.AutoReconnect = false;
                preferences.ConnectionMode = "adaptive";
                if (new[] { "multiple", "settings", "mirroring", "motion", "wireless", "manual" }.Contains(renderState))
                {
                    controller.UpdateDevices(Device.Parse("phone-a\tdevice model:Pixel_9\ntablet-b\tdevice model:Galaxy_Tab\n"));
                    controller.SetOptions("phone-a", true, false); controller.SetOptions("tablet-b", false, true);
                    controller.Select("phone-a", true); controller.Select("tablet-b", true);
                    if (renderState == "mirroring") controller.ConnectSelected();
                    if (renderState == "settings" || renderState == "motion") { settingsSerial = "tablet-b"; OpenSettings(true); }
                    if (renderState == "wireless" || renderState == "manual") { OpenSettings(true); OpenWireless(true); if (renderState == "manual") ShowPairingTab(false); }
                }
                else if (renderState == "permission") controller.UpdateDevices(Device.Parse("phone-a\tunauthorized model:Android_phone\n"));
                else if (renderState == "ready") controller.UpdateDevices(Device.Parse("phone-a\tdevice model:Android_phone\n"));
                else if (renderState == "reconnect") { controller.UpdateDevices(Device.Parse("preview-device\tdevice model:Android_phone\n")); controller.SetAutoReconnect(true); controller.Select("preview-device", true); controller.UpdateDevices(new List<Device>()); }
                else if (renderState == "error") trackerError = "Preview detection error";
                Render();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                    window.UpdateLayout();
                    if (renderState == "tray") using (var previewTray = new TrayIntegration(Assembly.GetExecutingAssembly().Location, "Preview", false)) { previewTray.Update("Listening", false, false, true, false, Appearance.IsDark, themeMode); previewTray.SaveMenuPreview(renderPath + ".tray.png"); }
                    if (renderState == "glass") { Appearance.Apply(window, themeMode, false); File.WriteAllText(renderPath + ".glass.txt", "Material: " + Appearance.Material); }
                    if (motionPreview)
                    {
                        var toggle = Control<CheckBox>("AudioToggle"); toggle.IsChecked = true; toggle.IsChecked = false;
                        var thumb = (Ellipse)toggle.Template.FindName("SwitchThumb", toggle);
                        File.WriteAllLines(renderPath + ".motion.txt", new[] { "Settings transition: " + Control<ScrollViewer>("SettingsView").HasAnimatedProperties, "Toggle motion: " + thumb.RenderTransform.HasAnimatedProperties, "Vector logo: " + (Control<Image>("BrandIcon").Source is DrawingImage) });
                    }
                    var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(renderPath)) encoder.Save(file);
                    window.Close();
                }));
            }
        }
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, request) => {
                if (new AssemblyName(request.Name).Name != "QRCoder") return null;
                using (var source = Assembly.GetExecutingAssembly().GetManifestResourceStream("QRCoder.dll"))
                using (var copy = new MemoryStream()) { source.CopyTo(copy); return Assembly.Load(copy.ToArray()); }
            };
            bool preview = args.Length >= 2 && args[0] == "--render-preview";
            bool selfCheck = args.Length >= 2 && args[0] == "--self-check";
            bool ownsMutex;
            string userIdentity = StableId(Environment.UserDomainName + "\\" + Environment.UserName);
            using (var mutex = new Mutex(true, "Local\\AndroidMirrorUser-" + userIdentity, out ownsMutex))
            using (var activation = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\AndroidMirrorShow-" + userIdentity))
            using (var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ScrcpyConnectQuit-" + StableId(AppDomain.CurrentDomain.BaseDirectory)))
            {
                if (!ownsMutex && !preview)
                {
                    activation.Set();
                    return;
                }
                var app = new Application { ShutdownMode = preview ? ShutdownMode.OnLastWindowClose : ShutdownMode.OnExplicitShutdown };
                try
                {
                    var shell = new Shell(preview, preview && args.Length >= 3 && args[2].EndsWith("motion"), activation, selfCheck, quitSignal);
                    shell.Show(preview ? args[1] : null, args.Length >= 3 ? args[2] : "idle", selfCheck || (!preview && args.Contains("--background")));
                    if (selfCheck) shell.VerifyShell(args[1], activation);
                    app.Run();
                }
                catch (Exception error)
                {
                    if (preview || selfCheck) File.WriteAllText(args[1] + ".error.txt", error.ToString());
                    else MessageBox.Show(error.Message, "Android Mirror", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.ExitCode = 1;
                }
                if (ownsMutex) mutex.ReleaseMutex();
            }
        }
        static string StableId(string path)
        {
            uint hash = 2166136261;
            foreach (char c in path.ToUpperInvariant()) { hash ^= c; hash *= 16777619; }
            return hash.ToString("X8");
        }
        void VerifyShell(string output, EventWaitHandle activation)
        {
            var report = new List<string>();
            int phase = 0;
            var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            check.Tick += delegate {
                try
                {
                    if (phase == 0)
                    {
                        if (window.IsVisible || tray == null) throw new Exception("Background startup failed.");
                        report.Add("PASS background startup with native tray");
                        RestoreWindow();
                        if (!window.IsVisible) throw new Exception("Tray restore failed.");
                        report.Add("PASS restore window");
                        window.Close();
                        if (closed || window.IsVisible || !timer.IsEnabled) throw new Exception("Close-to-tray stopped device detection.");
                        report.Add("PASS close-to-tray keeps detection running");
                        activation.Set(); phase++;
                    }
                    else
                    {
                        if (!window.IsVisible) throw new Exception("Single-instance activation did not restore the hidden window.");
                        report.Add("PASS single-instance activation from tray");
                        themeMode = "light"; RefreshAppearance();
                        if (Appearance.IsDark) throw new Exception("Light theme failed.");
                        themeMode = "dark"; RefreshAppearance();
                        if (!Appearance.IsDark) throw new Exception("Dark theme failed.");
                        report.Add("PASS light and dark theme switching");
                        OpenSettings(true);
                        if (Control<ScrollViewer>("SettingsView").Visibility != Visibility.Visible || Control<ScrollViewer>("HomeView").Visibility != Visibility.Collapsed) throw new Exception("Settings navigation failed.");
                        OpenWireless(true); window.UpdateLayout();
                        if (!(Control<Image>("PairingQrImage").Source is DrawingImage)) throw new Exception("QR code rendering failed.");
                        ShowPairingTab(false);
                        if (Control<StackPanel>("ManualPairPanel").Visibility != Visibility.Visible) throw new Exception("Manual pairing navigation failed.");
                        OpenWireless(false); OpenSettings(false);
                        report.Add("PASS settings, vector QR rendering, and manual wireless pairing screens");
                        if (!(Control<Image>("BrandIcon").Source is DrawingImage)) throw new Exception("SVG logo missing.");
                        report.Add("PASS embedded SVG app logo");
                        report.Add("Native material: " + Appearance.Material);
                        report.Add("Notification delivery: Windows NotifyIcon.ShowBalloonTip (suppressed during checks)");
                        File.WriteAllLines(output, report);
                        check.Stop(); Quit();
                    }
                }
                catch (Exception error)
                {
                    report.Add("FAIL " + error);
                    File.WriteAllLines(output, report); Environment.ExitCode = 1;
                    check.Stop(); Quit();
                }
            };
            check.Start();
        }
    }
}
