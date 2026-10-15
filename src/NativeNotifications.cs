using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AndroidMirror
{
    public static class NativeNotifications
    {
        public const string AppId = WindowsAppIdentity.AppId;
        public static void Register(string executable)
        {
            WindowsAppIdentity.EnsureShortcut(executable);
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
            { key.SetValue("DisplayName", "Android Mirror"); key.SetValue("IconUri", Path.Combine(Path.GetDirectoryName(executable), "assets", "icons", "Android Mirror.ico")); key.SetValue("IconBackgroundColor", "0"); }
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId));
        }
        public static void Show(Forms.NotifyIcon icon, string title, string message)
        {
            // Keep Windows' blue information symbol in the body. The app logo
            // belongs to the header, resolved through the registered shortcut.
            icon.BalloonTipTitle = title; icon.BalloonTipText = message; icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            if (icon.Visible) icon.ShowBalloonTip(5000);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    }
}
