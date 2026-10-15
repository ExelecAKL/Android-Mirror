using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using AndroidMirrorSetup;

static class SetupTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)); }
    [STAThread]
    static int Main()
    {
        string target = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".setup-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            bool rejected = false;
            try { Package.SafePath(target, "../escape.txt"); } catch (IOException) { rejected = true; }
            Assert(rejected, "Package path traversal was accepted.");
            Console.WriteLine("PASS package paths stay inside the installation folder");
            Package.Install(target, false, false, message => { });
            string package = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
            Assert(File.Exists(Path.Combine(target, "Android Mirror.exe")), "App missing.");
            Assert(File.Exists(Path.Combine(target, "tools", "Engine Updater.exe")) && File.Exists(Path.Combine(target, "engine", "engine-manifest.json")) && File.Exists(Path.Combine(target, "assets", "icons", "scrcpy.png")), "Update helper, inventory, or mirror icon missing.");
            Assert(Hash(Path.Combine(target, "engine", "Android Mirror Runtime.bin")) == Hash(Path.Combine(package, "engine", "Android Mirror Runtime.bin")) && File.Exists(Path.Combine(target, "engine", "Android Mirror Engine.exe")), "Engine runtime or UI entry missing.");
            Assert(!File.Exists(Path.Combine(target, "scrcpy.exe")) && !File.Exists(Path.Combine(target, "scrcpy-connect.exe")), "Duplicate app launchers remain.");
            string report = Path.Combine(target, "installed-layout-check.txt");
            var start = new ProcessStartInfo(Path.Combine(target, "Android Mirror.exe"), "--self-check \"" + report + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = target };
            using (var app = Process.Start(start))
            {
                if (!app.WaitForExit(12000)) { app.Kill(); throw new Exception("Installed app startup check timed out."); }
                Assert(app.ExitCode == 0 && File.Exists(report) && File.ReadAllText(report).Contains("PASS close-to-tray"), "The installed app layout did not start correctly.");
            }
            File.Delete(report);
            start.FileName = Path.Combine(target, "engine", "Android Mirror Engine.exe");
            using (var entry = Process.Start(start)) { if (!entry.WaitForExit(15000)) { entry.Kill(); throw new Exception("Engine UI redirection timed out."); } Assert(entry.ExitCode == 0 && File.Exists(report) && File.ReadAllText(report).Contains("PASS close-to-tray"), "Opening the engine entry did not redirect to the UI."); }
            File.Delete(report);
            Console.WriteLine("PASS full install preserves the branded engine and provides one app launcher");
            string shortcut = Path.Combine(target, "Android Mirror.lnk");
            Package.Shortcut(shortcut, Path.Combine(target, "Android Mirror.exe"), target);
            Assert(File.Exists(shortcut), "Windows shortcut missing."); File.Delete(shortcut);
            Console.WriteLine("PASS native Windows shortcut creation");
            string userFile = Path.Combine(target, "keep-user-file.txt"); File.WriteAllText(userFile, "keep");
            foreach (string name in new[] { "scrcpy.exe", "scrcpy-connect.exe", "engine/scrcpy.exe" }) File.WriteAllText(Package.SafePath(target, name), "retired launcher");
            File.AppendAllLines(Path.Combine(target, "install-manifest.txt"), new[] { "scrcpy.exe", "scrcpy-connect.exe", "engine/scrcpy.exe" });
            Package.Install(target, false, false, message => { });
            Assert(File.ReadAllText(userFile) == "keep", "Update removed an unrelated file.");
            Assert(!File.Exists(Path.Combine(target, "scrcpy.exe")) && !File.Exists(Path.Combine(target, "scrcpy-connect.exe")) && !File.Exists(Path.Combine(target, "engine", "scrcpy.exe")), "Upgrade left duplicate launchers or the former engine name.");
            Console.WriteLine("PASS reinstall keeps unrelated files");
            string runtime = Path.Combine(target, "engine", "Android Mirror Runtime.bin"), inventory = Path.Combine(target, "engine", "engine-manifest.json");
            File.AppendAllText(runtime, "custom engine fixture"); File.WriteAllText(inventory, "custom engine metadata");
            string retained = Hash(runtime);
            Package.Install(target, false, false, message => { }, true);
            Assert(Hash(runtime) == retained && File.ReadAllText(inventory) == "custom engine metadata" && File.Exists(Path.Combine(target, "tools", "App Updater.exe")), "App upgrade replaced a custom or newer engine, or omitted the app updater.");
            Console.WriteLine("PASS app upgrade retains the installed engine and update helpers");
            string appPath = Path.Combine(target, "Android Mirror.exe"), readme = Path.Combine(target, "README.md");
            File.WriteAllText(appPath, "previous app fixture"); File.WriteAllText(readme, "previous README fixture");
            string receipt = File.ReadAllText(Path.Combine(target, "install-manifest.txt")); bool rolledBack = false;
            using (var locked = new FileStream(readme, FileMode.Open, FileAccess.Read, FileShare.Read))
                try { Package.Install(target, false, false, message => { }, true); } catch (IOException) { rolledBack = true; }
            Assert(rolledBack && File.ReadAllText(appPath) == "previous app fixture" && File.ReadAllText(readme) == "previous README fixture" && File.ReadAllText(Path.Combine(target, "install-manifest.txt")) == receipt && Hash(runtime) == retained, "A failed app upgrade did not roll back app files and preserve its engine.");
            Console.WriteLine("PASS interrupted app file replacement restores the previous installation");
            Package.Uninstall(target, false, message => { });
            Assert(File.ReadAllText(userFile) == "keep", "Uninstall removed an unrelated file.");
            Assert(!File.Exists(Path.Combine(target, "Android Mirror.exe")) && !Directory.Exists(Path.Combine(target, "engine")) && !Directory.Exists(Path.Combine(target, "assets")) && !Directory.Exists(Path.Combine(target, "licenses")) && !Directory.Exists(Path.Combine(target, "docs")) && !Directory.Exists(Path.Combine(target, "tools")), "Owned installation files remain.");
            File.Delete(userFile); Directory.Delete(target);
            Console.WriteLine("PASS uninstall removes owned files and keeps user files");
            Console.WriteLine("7 installer checks passed. Windows installation settings were not changed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
