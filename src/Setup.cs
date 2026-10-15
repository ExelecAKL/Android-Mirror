using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AndroidMirrorSetup
{
    public static class Package
    {
        public static readonly string InstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android Mirror");
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AndroidMirror";
        const string Manifest = "install-manifest.txt";
        public static string SafePath(string root, string relative)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0) throw new IOException("Invalid package path.");
            string destination = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new IOException("A package path leaves the installation folder.");
            string parent = Path.GetDirectoryName(destination);
            string rootPath = prefix.TrimEnd(Path.DirectorySeparatorChar);
            while (parent != null && (String.Equals(parent, rootPath, StringComparison.OrdinalIgnoreCase) || parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new IOException("Installation paths must not pass through filesystem links.");
                if (String.Equals(parent, rootPath, StringComparison.OrdinalIgnoreCase)) break;
                parent = Path.GetDirectoryName(parent);
            }
            return destination;
        }
        static void CheckTarget(string target)
        {
            if (!String.Equals(Path.GetFullPath(target).TrimEnd('\\'), Path.GetFullPath(InstallPath).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new IOException("The installation target must be the Android Mirror folder in Program Files.");
            if (Directory.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new IOException("The installation folder must not be a filesystem link.");
        }
        static List<string> Extract(string destination)
        {
            Directory.CreateDirectory(destination);
            var files = new List<string>();
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (payload == null) throw new IOException("The installation payload is missing.");
                using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
                foreach (var entry in archive.Entries)
                {
                    if (String.IsNullOrEmpty(entry.Name)) continue;
                    string path = SafePath(destination, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var source = entry.Open()) using (var output = File.Create(path)) source.CopyTo(output);
                    files.Add(entry.FullName);
                }
            }
            foreach (string required in new[] { "Android Mirror.exe", "scrcpy.exe", "engine/scrcpy.exe", "engine/adb.exe", "engine/scrcpy-server", "Uninstall Android Mirror.exe", "LICENSE.txt" })
                if (!files.Contains(required)) throw new IOException("The package is incomplete: " + required);
            return files;
        }
        public static void Install(string target, bool windowsIntegration, bool desktop, Action<string> progress)
        {
            if (windowsIntegration) { CheckTarget(target); StopApp(target); }
            string staging = Path.Combine(Path.GetTempPath(), "AndroidMirrorSetup-" + Guid.NewGuid().ToString("N"));
            List<string> files = null;
            try
            {
                progress("Preparing the mirroring components…");
                files = Extract(staging);
                Directory.CreateDirectory(target);
                foreach (string relative in files.OrderBy(value => value.StartsWith("engine/") ? 0 : 1))
                {
                    string destination = SafePath(target, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(SafePath(staging, relative), destination, true);
                }
                File.WriteAllLines(Path.Combine(target, Manifest), files.Concat(new[] { Manifest }));
                if (windowsIntegration)
                {
                    progress("Adding Windows shortcuts…");
                    string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Android Mirror");
                    Directory.CreateDirectory(programs);
                    Shortcut(Path.Combine(programs, "Android Mirror.lnk"), Path.Combine(target, "Android Mirror.exe"), target);
                    if (desktop) Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Android Mirror.lnk"), Path.Combine(target, "Android Mirror.exe"), target);
                    using (var key = Registry.LocalMachine.CreateSubKey(UninstallKey))
                    {
                        key.SetValue("DisplayName", "Android Mirror"); key.SetValue("DisplayVersion", "1.1.0");
                        key.SetValue("Publisher", "Android Mirror"); key.SetValue("InstallLocation", target);
                        key.SetValue("DisplayIcon", Path.Combine(target, "Android Mirror.exe"));
                        key.SetValue("UninstallString", Quote(Path.Combine(target, "Uninstall Android Mirror.exe")));
                        key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                        key.SetValue("EstimatedSize", (int)(files.Sum(file => new FileInfo(SafePath(target, file)).Length) / 1024));
                    }
                }
                progress("Android Mirror is installed.");
            }
            finally { if (files != null) RemoveFiles(staging, files, false); }
        }
        public static void Shortcut(string destination, string executable, string workingDirectory)
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try
            {
                dynamic shortcut = shell.CreateShortcut(destination);
                try { shortcut.TargetPath = executable; shortcut.WorkingDirectory = workingDirectory; shortcut.IconLocation = executable + ",0"; shortcut.Description = "Mirror and manage Android devices."; shortcut.Save(); }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
        }
        public static void Uninstall(string target, bool windowsIntegration, Action<string> progress)
        {
            if (windowsIntegration) { CheckTarget(target); StopApp(target); }
            string manifest = Path.Combine(target, Manifest);
            if (!File.Exists(manifest)) throw new IOException("The installation manifest is missing. No files were removed.");
            string[] files = File.ReadAllLines(manifest);
            foreach (string relative in files) SafePath(target, relative);
            if (windowsIntegration)
                using (var key = Registry.LocalMachine.OpenSubKey(UninstallKey))
                { if (key != null && !String.Equals(key.GetValue("InstallLocation") as string, target, StringComparison.OrdinalIgnoreCase)) throw new IOException("The Windows installation entry belongs to another location."); }
            progress("Removing Android Mirror…");
            RemoveFiles(target, files, windowsIntegration);
            if (windowsIntegration)
            {
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);
                string programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Android Mirror");
                File.Delete(Path.Combine(programs, "Android Mirror.lnk"));
                if (Directory.Exists(programs) && !Directory.EnumerateFileSystemEntries(programs).Any()) Directory.Delete(programs);
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Android Mirror.lnk"));
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) if (key != null) key.DeleteValue("ScrcpyConnect-" + StableId(Path.GetFullPath(target).TrimEnd('\\') + "\\"), false);
                string ownExecutable = Assembly.GetExecutingAssembly().Location;
                if (String.Equals(Path.GetDirectoryName(ownExecutable), target, StringComparison.OrdinalIgnoreCase))
                {
                    string safeTarget = target.Replace("'", "''");
                    string script = "Wait-Process -Id " + Process.GetCurrentProcess().Id + " -ErrorAction SilentlyContinue; Remove-Item -LiteralPath '" + ownExecutable.Replace("'", "''") + "' -Force -ErrorAction SilentlyContinue; if ([IO.Directory]::Exists('" + safeTarget + "') -and [IO.Directory]::GetFileSystemEntries('" + safeTarget + "').Length -eq 0) { [IO.Directory]::Delete('" + safeTarget + "') }";
                    var info = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script))) { UseShellExecute = false, CreateNoWindow = true };
                    Process.Start(info).Dispose();
                }
            }
            progress("Android Mirror was removed. Your preferences were kept.");
        }
        static void RemoveFiles(string target, IEnumerable<string> files, bool skipSelf)
        {
            string own = Assembly.GetExecutingAssembly().Location;
            foreach (string relative in files)
            {
                string path = SafePath(target, relative);
                if (skipSelf && String.Equals(path, own, StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(path);
            }
            // Remove only empty known directories; never recursively delete a target.
            string root = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
            var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in files)
            {
                string parent = Path.GetDirectoryName(SafePath(target, relative));
                while (parent != null && !String.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                {
                    parents.Add(parent); parent = Path.GetDirectoryName(parent);
                }
            }
            foreach (string parent in parents.OrderByDescending(value => value.Length))
                if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
            if (Directory.Exists(target) && !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target);
        }
        static void StopApp(string target)
        {
            try { using (var signal = EventWaitHandle.OpenExisting("Local\\ScrcpyConnectQuit-" + StableId(Path.GetFullPath(target).TrimEnd('\\') + "\\"))) signal.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            var time = Stopwatch.StartNew();
            while (time.ElapsedMilliseconds < 5000)
            {
                bool running = false;
                foreach (var process in Process.GetProcessesByName("Android Mirror"))
                {
                    try { if (String.Equals(Path.GetDirectoryName(process.MainModule.FileName), target, StringComparison.OrdinalIgnoreCase)) running = true; }
                    catch { }
                    finally { process.Dispose(); }
                }
                if (!running) return;
                Thread.Sleep(100);
            }
            throw new IOException("Quit Android Mirror from its tray menu, then try again.");
        }
        static string StableId(string value) { uint hash = 2166136261; foreach (char c in value.ToUpperInvariant()) { hash ^= c; hash *= 16777619; } return hash.ToString("X8"); }
        static string Quote(string value) { return "\"" + value + "\""; }
    }

    public static class Setup
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
#if UNINSTALLER
            bool uninstall = true;
#else
            bool uninstall = false;
#endif
            string target = uninstall ? AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\') : Package.InstallPath;
            var form = new Form { Text = uninstall ? "Uninstall Android Mirror" : "Android Mirror Setup", Width = 550, Height = 350, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 9), AutoScaleMode = AutoScaleMode.Dpi };
            form.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            var picture = new PictureBox { Image = form.Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Left = 24, Top = 24, Width = 40, Height = 40 };
            var title = new Label { Text = "Android Mirror", Left = 80, Top = 20, Width = 420, Height = 32, Font = new Font("Segoe UI Semibold", 18) };
            var subtitle = new Label { Text = "Mirror and manage your Android devices.", Left = 82, Top = 56, Width = 420, Height = 24 };
            var description = new Label { Text = uninstall ? "Remove the app from this PC. Your preferences will be kept." : "Install for all users, with a Start menu shortcut and Windows uninstall support.", Left = 24, Top = 96, Width = 490, Height = 40 };
            var path = new TextBox { Text = target, Left = 24, Top = 140, Width = 490, ReadOnly = true };
            var desktop = new CheckBox { Text = "Add a desktop shortcut", Left = 24, Top = 176, Width = 350, Visible = !uninstall };
            var status = new Label { Text = "Ready", Left = 24, Top = 210, Width = 490, Height = 24 };
            var progress = new ProgressBar { Left = 24, Top = 238, Width = 490, Height = 6, Visible = false, Style = ProgressBarStyle.Marquee };
            var action = new Button { Text = uninstall ? "Uninstall" : "Install", Left = 414, Top = 266, Width = 100, Height = 32 };
            var cancel = new Button { Text = "Cancel", Left = 304, Top = 266, Width = 100, Height = 32 };
            bool complete = false, busy = false;
            cancel.Click += delegate { form.Close(); };
            form.FormClosing += (sender, e) => { if (busy) e.Cancel = true; };
            action.Click += async delegate {
                if (complete) { form.Close(); return; }
                busy = true; action.Enabled = cancel.Enabled = desktop.Enabled = false; progress.Visible = true;
                bool addDesktop = desktop.Checked;
                try
                {
                    Action<string> report = text => form.BeginInvoke(new Action(() => status.Text = text));
                    await Task.Run(() => { if (uninstall) Package.Uninstall(target, true, report); else Package.Install(target, true, addDesktop, report); });
                    complete = true; action.Text = "Finish"; cancel.Visible = false;
                    if (!uninstall) description.Text = "Installed. Open Android Mirror from the Windows Start menu.";
                }
                catch (Exception error) { MessageBox.Show(form, error.Message, "Android Mirror Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); status.Text = "Please try again."; }
                finally { busy = false; action.Enabled = cancel.Enabled = desktop.Enabled = true; progress.Visible = false; }
            };
            form.Controls.AddRange(new Control[] { picture, title, subtitle, description, path, desktop, status, progress, action, cancel });
            form.AcceptButton = action; form.CancelButton = cancel;
            Application.Run(form);
        }
    }
}
