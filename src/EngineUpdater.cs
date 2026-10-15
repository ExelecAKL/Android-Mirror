using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AndroidMirror;

static class EngineUpdater
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string tag = args.FirstOrDefault(a => a.StartsWith("--tag=")), package = args.FirstOrDefault(a => a.StartsWith("--package=")), parent = args.FirstOrDefault(a => a.StartsWith("--parent="));
        if (tag == null || package == null || parent == null) { MessageBox.Show("Open Android Mirror Settings to update its mirroring engine.", "Android Mirror", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        string root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
        int parentId;
        if (!Int32.TryParse(parent.Substring(9), out parentId)) return;
        try { using (var app = Process.GetProcessById(parentId)) if (!String.Equals(app.MainModule.FileName, Path.Combine(root, "Android Mirror.exe"), StringComparison.OrdinalIgnoreCase)) return; }
        catch { return; }
        string stage = EngineUpdateFiles.SafePath(root, "updates/stage-" + Guid.NewGuid().ToString("N"));
        var form = new Form { Text = "Android Mirror — Engine update", Width = 480, Height = 220, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10) };
        form.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
        var title = new Label { Text = "Updating the mirroring engine", Left = 24, Top = 24, Width = 420, Height = 32, Font = new Font("Segoe UI Semibold", 15) };
        var status = new Label { Text = "Verifying the official release…", Left = 24, Top = 72, Width = 420, Height = 44 };
        var progress = new ProgressBar { Left = 24, Top = 126, Width = 420, Height = 8, Style = ProgressBarStyle.Marquee };
        form.Controls.AddRange(new Control[] { title, status, progress }); bool busy = true;
        form.FormClosing += (sender, e) => { if (busy) e.Cancel = true; };
        form.Shown += async delegate {
            var files = new System.Collections.Generic.List<string>();
            try {
                using (var client = new EngineReleaseClient()) {
                    var release = await client.Get(tag.Substring(6), CancellationToken.None);
                    string archivePath = Path.GetFullPath(package.Substring(10));
                    if (new FileInfo(archivePath).Length != release.Size || EngineReleaseClient.Hash(archivePath) != release.Sha256) throw new IOException("The downloaded engine package failed verification. No update was installed.");
                    await Task.Run(() => {
                        files = EngineUpdateFiles.Extract(archivePath, stage, release.Tag);
                        string enginePath = Path.Combine(stage, EngineLayout.RuntimeExecutable), originalHash = EngineReleaseClient.Hash(enginePath);
                        var start = new ProcessStartInfo(enginePath, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = stage };
                        using (var check = Process.Start(start)) {
                            var output = check.StandardOutput.ReadToEndAsync(); var errors = check.StandardError.ReadToEndAsync();
                            if (!check.WaitForExit(10000)) { check.Kill(); throw new IOException("The new engine could not start."); }
                            string version = output.GetAwaiter().GetResult(); errors.GetAwaiter().GetResult();
                            if (check.ExitCode != 0 || !version.StartsWith("scrcpy " + release.Version + " ", StringComparison.Ordinal)) throw new IOException("The engine version did not match the release.");
                        }
                        string iconPath = EngineUpdateFiles.SafePath(root, "assets/icons/Android Mirror.ico");
                        EngineBranding.Apply(enginePath, iconPath);
                        EngineUpdateFiles.Apply(root, stage, files, release.Version, text => form.BeginInvoke(new Action(() => status.Text = text)), originalExecutableHash: originalHash, brandIconHash: EngineReleaseClient.Hash(iconPath));
                    });
                }
                Environment.ExitCode = 0;
            }
            catch (Exception error) { Environment.ExitCode = 1; MessageBox.Show(form, error.Message + "\n\nThe previous engine was kept or restored. Close other apps using these files before trying again.", "Android Mirror update", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { try { EngineUpdateFiles.RemoveStage(stage, files); } catch { } busy = false; form.Close(); }
        };
        Application.Run(form);
    }
}
