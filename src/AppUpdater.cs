using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace AndroidMirror
{
    // This helper stays at the interactive user's integrity level. Only setup
    // elevates; launching the updated app here avoids restarting it as admin.
    static class AppUpdater
    {
        [STAThread] static int Main(string[] args)
        {
            bool setupStarted = false;
            string app = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "Android Mirror.exe"));
            try
            {
                string root = Path.GetDirectoryName(app);
                if (args.Length != 1 || !AppReleaseClient.IsInstalled(root)) throw new IOException("App updates require a Program Files installation.");
                EngineRelease release;
                using (var client = new AppReleaseClient()) release = client.Get(CancellationToken.None, args[0]).GetAwaiter().GetResult();
                string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android Mirror", "updates");
                string installer = Path.Combine(cache, "Android-Mirror-" + release.Version + "-Setup.exe");
                int result;
                using (var locked = new FileStream(installer, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (locked.Length != release.Size || EngineReleaseClient.Hash(installer) != release.Sha256) throw new IOException("The Android Mirror installer checksum did not match.");
                    using (var setup = Process.Start(new ProcessStartInfo(installer, "--update") { UseShellExecute = true, Verb = "runas", WorkingDirectory = cache }))
                    { setupStarted = true; setup.WaitForExit(); result = setup.ExitCode; }
                }
                // The installer exits only after the transaction completes or
                // is cancelled. Its close event stops only this installation.
                Process.Start(new ProcessStartInfo(app, result == 0 ? "" : "--update-failed") { UseShellExecute = false, WorkingDirectory = root }).Dispose();
                return result;
            }
            catch (System.ComponentModel.Win32Exception error) { return error.NativeErrorCode == 1223 ? 1223 : 1; }
            catch { if (setupStarted && File.Exists(app)) try { Process.Start(new ProcessStartInfo(app, "--update-failed") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(app) }).Dispose(); } catch { } return 1; }
        }
    }
}
