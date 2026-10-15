using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using AndroidMirror;

namespace AndroidMirrorSetup
{
    public static class Setup
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        static string ReadResource(string name) { using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) using (var reader = new StreamReader(stream)) return reader.ReadToEnd(); }
        static T Find<T>(Window window, string name) where T : class { return (T)window.FindName(name); }
        static void Brush(Window window, string name, string value) { window.Resources[name] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)); }
        static void Theme(Window window, bool dark)
        {
            if (SystemParameters.HighContrast) {
                foreach (string name in new[] { "Canvas", "Surface", "PopupSurface", "DialogFooter", "Input", "Disabled" }) window.Resources[name] = SystemColors.WindowBrush;
                foreach (string name in new[] { "Text", "Muted", "Stroke", "DisabledText" }) window.Resources[name] = SystemColors.WindowTextBrush;
                window.Resources["Accent"] = SystemColors.HighlightBrush; window.Resources["AccentText"] = SystemColors.HighlightTextBrush;
            } else {
                Brush(window, "Canvas", dark ? "#292929" : "#FFFFFF"); Brush(window, "Surface", dark ? "#292929" : "#FFFFFF"); Brush(window, "PopupSurface", dark ? "#292929" : "#FFFFFF");
                Brush(window, "DialogFooter", dark ? "#202020" : "#F3F3F3"); Brush(window, "Text", dark ? "#F2F2F2" : "#1B1B1B"); Brush(window, "Muted", dark ? "#C1C1C1" : "#606060");
                Brush(window, "Input", dark ? "#363636" : "#FAFAFA"); Brush(window, "Stroke", dark ? "#484848" : "#DEDEDE"); Brush(window, "Hover", dark ? "#454545" : "#E7F6EE");
                Brush(window, "Disabled", dark ? "#393939" : "#E8E8E8"); Brush(window, "DisabledText", dark ? "#929292" : "#777777"); Brush(window, "Accent", "#32DE84");
            }
            window.SourceInitialized += delegate { try { var handle = new WindowInteropHelper(window).Handle; int enabled = dark ? 1 : 0, round = 2; DwmSetWindowAttribute(handle, 20, ref enabled, 4); DwmSetWindowAttribute(handle, 33, ref round, 4); } catch (DllNotFoundException) { } };
        }
        [STAThread]
        public static void Main(string[] args)
        {
#if UNINSTALLER
            bool uninstall = true;
#else
            bool uninstall = false;
#endif
            bool preview = args.Length >= 2 && args[0] == "--render-preview";
            string mode = preview && args.Length > 2 ? args[2] : "";
            bool dark = true;
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) dark = key != null && Convert.ToInt32(key.GetValue("AppsUseLightTheme", 1)) == 0; } catch { }
            if (preview) dark = !mode.StartsWith("light", StringComparison.Ordinal);
            XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var layout = XDocument.Parse(ReadResource("SetupWindow.xaml"));
            var styles = XDocument.Parse(ReadResource("MainWindow.xaml")).Root.Element(p + "Window.Resources");
            layout.Root.AddFirst(new XElement(styles));
            string xaml = layout.ToString();
            if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || preview) xaml = xaml.Replace("0:0:0.12", "0:0:0");
            var window = (Window)XamlReader.Parse(xaml);
            Theme(window, dark);
            Find<Image>(window, "SetupBrand").Source = SvgLogo.LoadEmbedded();
            using (var artwork = Assembly.GetExecutingAssembly().GetManifestResourceStream("app-artwork.png")) window.Icon = BitmapFrame.Create(artwork, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            string target = uninstall ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') : Package.InstallPath;
            Find<TextBlock>(window, "SetupPath").Text = target;
            var heading = Find<TextBlock>(window, "SetupHeading"); var status = Find<TextBlock>(window, "SetupStatus"); var progress = Find<ProgressBar>(window, "SetupProgress");
            var desktop = Find<CheckBox>(window, "DesktopShortcut"); var action = Find<Button>(window, "SetupAction"); var cancel = Find<Button>(window, "SetupCancel");
            bool complete = false, busy = false;
            if (uninstall) { window.Title = "Uninstall Android Mirror"; heading.Text = "Remove Android Mirror?"; status.Text = "Your saved preferences will be kept."; action.Content = "Uninstall"; desktop.Visibility = Visibility.Collapsed; }
            window.Closing += (sender, e) => { if (busy) e.Cancel = true; };
            window.PreviewKeyDown += (sender, e) => { if (e.Key == Key.Escape && !busy) { window.Close(); e.Handled = true; } };
            cancel.Click += delegate { window.Close(); };
            action.Click += async delegate {
                if (preview) return;
                if (complete) { window.Close(); return; }
                busy = true; action.IsEnabled = cancel.IsEnabled = desktop.IsEnabled = false; progress.Visibility = Visibility.Visible; progress.Value = 5;
                heading.Text = uninstall ? "Removing Android Mirror" : "Installing Android Mirror";
                bool addDesktop = desktop.IsChecked == true;
                try {
                    Action<string> report = text => window.Dispatcher.BeginInvoke(new Action(() => { status.Text = text; progress.Value = text.Contains("shortcuts") ? 90 : text.Contains("installed") || text.Contains("removed") ? 100 : 25; }));
                    await Task.Run(() => { if (uninstall) Package.Uninstall(target, true, report); else Package.Install(target, true, addDesktop, report); });
                    complete = true; heading.Text = uninstall ? "Android Mirror was removed" : "You’re all set"; status.Text = uninstall ? "Your saved preferences were kept." : "Open Android Mirror from the Windows Start menu.";
                    progress.Value = 100; action.Content = "Done"; cancel.Visibility = Visibility.Collapsed; desktop.Visibility = Visibility.Collapsed;
                } catch (Exception error) { heading.Text = "Setup couldn’t finish"; status.Text = error.Message; action.Content = "Try again"; progress.Visibility = Visibility.Collapsed; }
                finally { busy = false; action.IsEnabled = cancel.IsEnabled = desktop.IsEnabled = true; }
            };
            if (preview) {
                if (mode.Contains("installing")) { heading.Text = "Installing Android Mirror"; status.Text = "Preparing the mirroring components…"; progress.Visibility = Visibility.Visible; progress.Value = 62; action.IsEnabled = cancel.IsEnabled = desktop.IsEnabled = false; }
                if (mode.Contains("complete")) { heading.Text = "You’re all set"; status.Text = "Open Android Mirror from the Windows Start menu."; progress.Visibility = Visibility.Visible; progress.Value = 100; action.Content = "Done"; cancel.Visibility = desktop.Visibility = Visibility.Collapsed; }
                window.Loaded += delegate { window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => { window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(args[1])) encoder.Save(file); window.Close(); })); };
            }
            new Application().Run(window);
        }
    }
}
