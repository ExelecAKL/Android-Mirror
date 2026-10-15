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
            Assert(Hash(Path.Combine(target, "engine", "scrcpy.exe")) == Hash(Path.Combine(package, "engine", "scrcpy.exe")), "Original scrcpy engine was changed.");
            Assert(Hash(Path.Combine(target, "scrcpy.exe")) != Hash(Path.Combine(target, "engine", "scrcpy.exe")), "Entry point was not redirected.");
            Assert(Hash(Path.Combine(target, "scrcpy.exe")) == Hash(Path.Combine(target, "scrcpy-connect.exe")), "Legacy entry point differs.");
            string report = Path.Combine(target, "installed-layout-check.txt");
            var start = new ProcessStartInfo(Path.Combine(target, "Android Mirror.exe"), "--self-check \"" + report + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = target };
            using (var app = Process.Start(start))
            {
                if (!app.WaitForExit(12000)) { app.Kill(); throw new Exception("Installed app startup check timed out."); }
                Assert(app.ExitCode == 0 && File.Exists(report) && File.ReadAllText(report).Contains("PASS close-to-tray"), "The installed app layout did not start correctly.");
            }
            File.Delete(report);
            Console.WriteLine("PASS full install preserves the original engine and redirects both launchers");
            string shortcut = Path.Combine(target, "Android Mirror.lnk");
            Package.Shortcut(shortcut, Path.Combine(target, "Android Mirror.exe"), target);
            Assert(File.Exists(shortcut), "Windows shortcut missing."); File.Delete(shortcut);
            Console.WriteLine("PASS native Windows shortcut creation");
            string userFile = Path.Combine(target, "keep-user-file.txt"); File.WriteAllText(userFile, "keep");
            Package.Install(target, false, false, message => { });
            Assert(File.ReadAllText(userFile) == "keep", "Update removed an unrelated file.");
            Console.WriteLine("PASS reinstall keeps unrelated files");
            Package.Uninstall(target, false, message => { });
            Assert(File.ReadAllText(userFile) == "keep", "Uninstall removed an unrelated file.");
            Assert(!File.Exists(Path.Combine(target, "Android Mirror.exe")) && !Directory.Exists(Path.Combine(target, "engine")) && !Directory.Exists(Path.Combine(target, "assets")) && !Directory.Exists(Path.Combine(target, "licenses")) && !Directory.Exists(Path.Combine(target, "docs")), "Owned installation files remain.");
            File.Delete(userFile); Directory.Delete(target);
            Console.WriteLine("PASS uninstall removes owned files and keeps user files");
            Console.WriteLine("5 installer checks passed. Windows installation settings were not changed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
