using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;
using SystemColors = System.Windows.SystemColors;
using Pen = System.Drawing.Pen;

namespace AndroidMirror
{
    public static class Appearance
    {
        public static bool IsDark, GlassEnabled;
        public static string Material = "Solid";
        const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        [StructLayout(LayoutKind.Sequential)] struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)] struct Accent { public int State, Flags; public uint Tint; public int Animation; }
        [StructLayout(LayoutKind.Sequential)] struct Composition { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref Composition data);
        static int Preference(string name, int fallback)
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(Personalize)) return Convert.ToInt32(key == null ? fallback : key.GetValue(name, fallback)); }
            catch { return fallback; }
        }
        static SolidColorBrush Brush(string hex) { return (SolidColorBrush)new BrushConverter().ConvertFromString(hex); }
        public static void Apply(Window window, string mode, bool preview)
        {
            IsDark = mode == "dark" || (mode == "system" && Preference("AppsUseLightTheme", 1) == 0);
            bool contrast = SystemParameters.HighContrast;
            string canvas = IsDark ? "#202020" : "#F3F3F3";
            GlassEnabled = !preview && !contrast && !SystemParameters.IsRemoteSession && Preference("EnableTransparency", 1) != 0;
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            Material = "Solid";
            if (hwnd != IntPtr.Zero)
            {
                try
                {
                    int dark = IsDark ? 1 : 0, rounded = 2, backdrop = GlassEnabled ? 3 : 1;
                    DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
                    DwmSetWindowAttribute(hwnd, 33, ref rounded, 4);
                    bool modern = Environment.OSVersion.Version.Build >= 22621;
                    if (modern)
                    {
                        GlassEnabled = GlassEnabled && DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4) == 0;
                        if (GlassEnabled) Material = "Desktop acrylic";
                        else { backdrop = 1; DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4); }
                    }
                    else
                    {
                        var accent = new Accent { State = GlassEnabled ? 4 : 0, Flags = 2, Tint = IsDark ? 0xCC202020u : 0xCCF3F3F3u };
                        var data = new Composition { Attribute = 19, Size = Marshal.SizeOf(typeof(Accent)), Data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Accent))) };
                        try { Marshal.StructureToPtr(accent, data.Data, false); GlassEnabled = SetWindowCompositionAttribute(hwnd, ref data) != 0 && GlassEnabled; }
                        finally { Marshal.FreeHGlobal(data.Data); }
                        if (GlassEnabled) Material = "Acrylic blur";
                    }
                    var margins = new Margins { Left = GlassEnabled ? -1 : 0 };
                    DwmExtendFrameIntoClientArea(hwnd, ref margins);
                    var source = HwndSource.FromHwnd(hwnd);
                    if (source != null && source.CompositionTarget != null)
                        source.CompositionTarget.BackgroundColor = GlassEnabled ? Colors.Transparent : (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(canvas);
                }
                catch { GlassEnabled = false; Material = "Solid"; }
            }
            else GlassEnabled = false;
            var resources = window.Resources;
            resources["Canvas"] = contrast ? SystemColors.WindowBrush : GlassEnabled ? System.Windows.Media.Brushes.Transparent : Brush(canvas);
            resources["Surface"] = contrast ? SystemColors.WindowBrush : Brush(IsDark ? (GlassEnabled ? "#B8323232" : "#2B2B2B") : (GlassEnabled ? "#CFFFFFFF" : "#FFFFFF"));
            resources["PopupSurface"] = contrast ? SystemColors.WindowBrush : Brush(IsDark ? "#2B2B2B" : "#FFFFFF");
            resources["DialogFooter"] = contrast ? SystemColors.WindowBrush : Brush(IsDark ? "#202020" : "#F3F3F3");
            resources["Text"] = contrast ? SystemColors.WindowTextBrush : Brush(IsDark ? "#F4F4F4" : "#1B1B1B");
            resources["Muted"] = contrast ? SystemColors.WindowTextBrush : Brush(IsDark ? "#C0C0C0" : "#606060");
            resources["Stroke"] = contrast ? SystemColors.WindowTextBrush : Brush(IsDark ? "#505050" : "#DEDEDE");
            resources["Input"] = contrast ? SystemColors.WindowBrush : Brush(IsDark ? "#363636" : "#FAFAFA");
            resources["Hover"] = contrast ? SystemColors.HighlightBrush : Brush(IsDark ? "#454545" : "#E7F6EE");
            resources["Disabled"] = contrast ? SystemColors.WindowBrush : Brush(IsDark ? "#3A3A3A" : "#E8E8E8");
            resources["DisabledText"] = contrast ? SystemColors.GrayTextBrush : Brush(IsDark ? "#A0A0A0" : "#777777");
            resources["Accent"] = contrast ? SystemColors.HighlightBrush : Brush("#32DE84");
            resources["AccentText"] = contrast ? SystemColors.HighlightTextBrush : Brush("#103622");
        }
    }

    // Uses the real Windows notification area and OS notification delivery.
    public sealed class TrayIntegration : IDisposable
    {
        readonly Forms.NotifyIcon icon;
        readonly Forms.ContextMenuStrip menu;
        readonly Forms.ToolStripMenuItem status, mirror;
        readonly Forms.Timer pulseTimer = new Forms.Timer { Interval = 400 };
        readonly Forms.Timer fadeTimer = new Forms.Timer { Interval = 16 };
        readonly Icon[] frames = new Icon[6];
        readonly Icon idleIcon, connectedIcon;
        readonly Icon brandIcon;
        readonly string executable, startupName;
        DateTime fadeStart;
        int frame;
        bool dark;
        public event Action OpenRequested, QuitRequested, MirrorRequested, SettingsRequested;
        public event Action<string> Error;
        void Notify(string title, string text) { if (!NativeNotifications.Show(icon, brandIcon, title, text)) icon.ShowBalloonTip(5000, title, text, Forms.ToolTipIcon.None); }
        public void NotifyUpdate(string version) { Notify("Engine based update", "Mirroring engine " + version + " is available. Open Android Mirror to review what changed."); }
        public void NotifyAppUpdate(string version) { Notify("Android Mirror update", "Android Mirror " + version + " is available. Open the app to update."); }
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        public TrayIntegration(string executable, string startupName, bool visible = true, bool registerIdentity = true)
        {
            this.executable = executable; this.startupName = startupName;
            if (visible && registerIdentity) try { NativeNotifications.Register(executable); } catch (Exception) { }
            using (var artwork = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            using (var source = new Icon(artwork)) brandIcon = (Icon)source.Clone();
            idleIcon = (Icon)brandIcon.Clone();
            connectedIcon = MakeIcon(brandIcon, DrawingColor.FromArgb(50, 222, 132));
            for (int i = 0; i < frames.Length; i++) frames[i] = MakeIcon(brandIcon, DrawingColor.FromArgb(70 + i * 32, 50, 222, 132));
            menu = new Forms.ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true, Font = new Font("Segoe UI", 9), Padding = new Forms.Padding(4), AllowTransparency = true };
            status = Item("Android Mirror", null); status.Enabled = false;
            menu.Items.Add(new Forms.ToolStripSeparator());
            Item("Open Android Mirror", () => { if (OpenRequested != null) OpenRequested(); });
            mirror = Item("Connect", () => { if (MirrorRequested != null) MirrorRequested(); });
            Item("Settings", () => { if (SettingsRequested != null) SettingsRequested(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            Item("Quit Android Mirror", () => { if (QuitRequested != null) QuitRequested(); });
            menu.Opening += delegate {
                menu.Opacity = SystemParameters.MenuAnimation && !SystemParameters.HighContrast ? 0.2 : 1;
            };
            menu.Opened += delegate { fadeStart = DateTime.UtcNow; if (menu.Opacity < 1) fadeTimer.Start(); };
            menu.Closed += delegate { fadeTimer.Stop(); menu.Opacity = 1; };
            fadeTimer.Tick += delegate {
                double t = Math.Min(1, (DateTime.UtcNow - fadeStart).TotalMilliseconds / 120);
                menu.Opacity = 0.2 + 0.8 * (1 - Math.Pow(1 - t, 3));
                if (t >= 1) fadeTimer.Stop();
            };
            icon = new Forms.NotifyIcon { Text = "Android Mirror", Icon = idleIcon, ContextMenuStrip = menu, Visible = visible };
            icon.MouseClick += (sender, e) => { if (e.Button == Forms.MouseButtons.Left && OpenRequested != null) OpenRequested(); };
            icon.BalloonTipClicked += delegate { if (OpenRequested != null) OpenRequested(); };
            pulseTimer.Tick += delegate { frame = (frame + 1) % 10; icon.Icon = frames[frame < 6 ? frame : 10 - frame]; };
        }
        Forms.ToolStripMenuItem Item(string label, Action click)
        {
            var item = new Forms.ToolStripMenuItem(label) { Padding = new Forms.Padding(8, 6, 8, 6) };
            if (click != null) item.Click += delegate { click(); };
            menu.Items.Add(item); return item;
        }
        public void Update(string label, bool active, bool canConnect, bool auto, bool busy, bool isDark, string mode)
        {
            status.Text = "Android Mirror · " + label;
            string tooltip = "Android Mirror · " + label;
            icon.Text = tooltip.Substring(0, Math.Min(63, tooltip.Length));
            mirror.Text = active ? "Stop all mirrors" : busy ? "Stop waiting" : "Mirror selected";
            mirror.Enabled = active || canConnect || busy;
            if (menu.Renderer == null || dark != isDark || !(menu.Renderer is MenuRenderer))
            {
                dark = isDark;
                menu.Renderer = new MenuRenderer(dark);
                menu.ForeColor = dark ? DrawingColor.WhiteSmoke : DrawingColor.FromArgb(27, 27, 27);
            }
            busy = busy && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
            if (busy) pulseTimer.Start();
            else { pulseTimer.Stop(); icon.Icon = active ? connectedIcon : idleIcon; }
        }
        public void NotifyConnected(Device device)
        {
            Notify(device.Name + " connected", "Your device is connected to Android Mirror.");
        }
        public void NotifyBackground()
        { Notify("Android Mirror is running in the tray", "Device detection continues in the background. Click the tray icon to reopen the window."); }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        string StartupCommand { get { return MirrorProcess.Quote(executable) + " --background"; } }
        bool StartsWithWindows()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string command = key == null ? null : key.GetValue(startupName) as string;
                    string directory = Path.GetDirectoryName(executable);
                    return new[] { "Android Mirror.exe", "scrcpy-connect.exe" }.Any(name => String.Equals(command, MirrorProcess.Quote(Path.Combine(directory, name)) + " --background", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch { return false; }
        }
        public bool StartupEnabled { get { return StartsWithWindows(); } set { if (value != StartsWithWindows()) ToggleStartup(); } }
        void ToggleStartup()
        {
            try
            {
                bool enabled = StartsWithWindows();
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                { if (enabled) key.DeleteValue(startupName, false); else key.SetValue(startupName, StartupCommand); }
            }
            catch (Exception error) { if (Error != null) Error("Couldn’t change startup preference: " + error.Message); }
        }
        static Icon MakeIcon(Icon artwork, DrawingColor dot)
        {
            using (var bitmap = new Bitmap(32, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawIcon(artwork, new Rectangle(0, 0, 32, 32));
                using (var border = new SolidBrush(DrawingColor.FromArgb(32, 32, 32))) graphics.FillEllipse(border, 21, 21, 11, 11);
                using (var fill = new SolidBrush(dot)) graphics.FillEllipse(fill, 23, 23, 7, 7);
                IntPtr handle = bitmap.GetHicon();
                try { using (var original = Icon.FromHandle(handle)) return (Icon)original.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        public void SaveMenuPreview(string path)
        {
            menu.CreateControl(); menu.PerformLayout();
            using (var bitmap = new Bitmap(menu.PreferredSize.Width, menu.PreferredSize.Height)) { menu.Size = menu.PreferredSize; menu.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, bitmap.Size)); bitmap.Save(path); }
        }
        public void Dispose()
        {
            pulseTimer.Stop(); fadeTimer.Stop(); pulseTimer.Dispose(); fadeTimer.Dispose();
            icon.Visible = false; icon.Dispose(); menu.Dispose();
            idleIcon.Dispose(); connectedIcon.Dispose(); foreach (var value in frames) value.Dispose();
            brandIcon.Dispose();
        }
        sealed class MenuRenderer : Forms.ToolStripProfessionalRenderer
        {
            readonly bool dark;
            public MenuRenderer(bool dark) : base(new MenuColors(dark)) { this.dark = dark; RoundedEdges = true; }
            protected override void OnRenderImageMargin(Forms.ToolStripRenderEventArgs e)
            {
                using (var fill = new SolidBrush(SystemParameters.HighContrast ? System.Drawing.SystemColors.Menu : dark ? DrawingColor.FromArgb(32, 32, 32) : DrawingColor.FromArgb(249, 249, 249)))
                    e.Graphics.FillRectangle(fill, e.AffectedBounds);
            }
            protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
            {
                Rectangle bounds = e.ImageRectangle;
                using (var pen = new Pen(SystemParameters.HighContrast ? System.Drawing.SystemColors.MenuText : dark ? DrawingColor.WhiteSmoke : DrawingColor.FromArgb(27, 27, 27), 1.7f))
                {
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(pen, new[] { new PointF(bounds.Left + 3, bounds.Top + 8), new PointF(bounds.Left + 6, bounds.Top + 11), new PointF(bounds.Left + 12, bounds.Top + 4) });
                }
            }
            protected override void OnRenderArrow(Forms.ToolStripArrowRenderEventArgs e)
            { e.ArrowColor = dark ? DrawingColor.WhiteSmoke : DrawingColor.FromArgb(27, 27, 27); base.OnRenderArrow(e); }
            protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = SystemParameters.HighContrast ? System.Drawing.SystemColors.MenuText : !e.Item.Enabled ? (dark ? DrawingColor.Silver : DrawingColor.DimGray) : dark ? DrawingColor.WhiteSmoke : DrawingColor.FromArgb(27, 27, 27);
                base.OnRenderItemText(e);
            }
        }
        sealed class MenuColors : Forms.ProfessionalColorTable
        {
            readonly bool dark;
            public MenuColors(bool dark) { this.dark = dark; UseSystemColors = SystemParameters.HighContrast; }
            public override DrawingColor ToolStripDropDownBackground { get { return SystemParameters.HighContrast ? System.Drawing.SystemColors.Menu : dark ? DrawingColor.FromArgb(32, 32, 32) : DrawingColor.FromArgb(249, 249, 249); } }
            public override DrawingColor MenuBorder { get { return dark ? DrawingColor.FromArgb(64, 64, 64) : DrawingColor.FromArgb(212, 212, 212); } }
            public override DrawingColor MenuItemSelected { get { return SystemParameters.HighContrast ? System.Drawing.SystemColors.Highlight : dark ? DrawingColor.FromArgb(54, 54, 54) : DrawingColor.FromArgb(232, 232, 232); } }
            public override DrawingColor MenuItemSelectedGradientBegin { get { return MenuItemSelected; } }
            public override DrawingColor MenuItemSelectedGradientEnd { get { return MenuItemSelected; } }
            public override DrawingColor MenuItemBorder { get { return MenuItemSelected; } }
            public override DrawingColor SeparatorDark { get { return MenuBorder; } }
            public override DrawingColor SeparatorLight { get { return ToolStripDropDownBackground; } }
            public override DrawingColor CheckBackground { get { return MenuItemSelected; } }
            public override DrawingColor CheckSelectedBackground { get { return MenuItemSelected; } }
            public override DrawingColor CheckPressedBackground { get { return MenuItemSelected; } }
            public override DrawingColor ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override DrawingColor ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override DrawingColor ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        }
    }
}
