using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AndroidMirror;

static class Redirect
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            string directory = AppDomain.CurrentDomain.BaseDirectory;
            string executable = Path.Combine(directory, "Android Mirror.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("Keep this launcher beside Android Mirror.exe and its engine folder.");
            var info = new ProcessStartInfo(executable, String.Join(" ", args.Select(MirrorProcess.Quote))) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true };
            using (var child = Process.Start(info))
            {
                if (args.Contains("--self-check") || args.Contains("--render-preview")) { child.WaitForExit(); return child.ExitCode; }
            }
            return 0;
        }
        catch (Exception error) { MessageBox.Show(error.Message, "Android Mirror", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
    }
}
