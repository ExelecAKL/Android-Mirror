using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;

// The visible engine entry is safe to open in Explorer; the shell uses the runtime directly.
static class EngineEntry
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(uint events, uint flags, string first, string second);
    static string Quote(string value) {
        var result = new StringBuilder("\""); int slashes = 0;
        foreach (char c in value) { if (c == '\\') { slashes++; continue; } result.Append('\\', slashes * (c == '"' ? 2 : 1) + (c == '"' ? 1 : 0)); result.Append(c); slashes = 0; }
        result.Append('\\', slashes * 2); return result.Append('"').ToString();
    }
    [STAThread]
    static int Main(string[] args) {
        try {
            if (args.Length == 1 && args[0] == "--refresh-icon") { string own = Assembly.GetExecutingAssembly().Location; SHChangeNotify(0x2000, 0x1005, own, null); SHChangeNotify(0x1000, 0x1005, Path.GetDirectoryName(own), null); SHChangeNotify(0x08000000, 0, null, null); return 0; }
            string root = Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
            string app = Path.Combine(root, "Android Mirror.exe"); if (!File.Exists(app)) return 1;
            bool check = args.Length >= 2 && (args[0] == "--self-check" || args[0] == "--render-preview");
            using (var child = Process.Start(new ProcessStartInfo(app, check ? String.Join(" ", args.Select(Quote)) : "") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true })) { if (check) { child.WaitForExit(); return child.ExitCode; } }
            return 0;
        } catch { return 1; }
    }
}
