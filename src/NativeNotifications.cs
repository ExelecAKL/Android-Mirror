using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AndroidMirror
{
    public static class NativeNotifications
    {
        public const string AppId = "Exelec.AndroidMirror.Desktop";
        public static void Register(string executable)
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            string icon = Path.Combine(Path.GetDirectoryName(executable), "assets", "icons", "Android Mirror.ico");
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
            { key.SetValue("DisplayName", "Android Mirror"); key.SetValue("IconUri", icon); key.SetValue("IconBackgroundColor", "0"); }
        }
        public static bool Show(Forms.NotifyIcon icon, Icon artwork, string title, string message)
        {
            // .NET Framework's NotifyIcon exposes only stock balloon icons.
            // Reuse its own HWND/id for NIIF_USER, preserving native callbacks.
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo id = typeof(Forms.NotifyIcon).GetField("id", flags) ?? typeof(Forms.NotifyIcon).GetField("_id", flags);
            FieldInfo field = typeof(Forms.NotifyIcon).GetField("window", flags) ?? typeof(Forms.NotifyIcon).GetField("_window", flags);
            var window = field == null ? null : field.GetValue(icon) as Forms.NativeWindow;
            if (id == null || window == null || window.Handle == IntPtr.Zero || !icon.Visible) return false;
            var data = new NotificationData { Size = Marshal.SizeOf(typeof(NotificationData)), Window = window.Handle, Id = Convert.ToUInt32(id.GetValue(icon)), Flags = 0x12,
                Icon = artwork.Handle, BalloonIcon = artwork.Handle, Tip = "", Info = Limit(message, 255), Title = Limit(title, 63), InfoFlags = 0xA4, Timeout = 5000 };
            return Shell_NotifyIcon(1, ref data);
        }
        static string Limit(string value, int length) { value = value ?? ""; return value.Length > length ? value.Substring(0, length) : value; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct NotificationData
        {
            public int Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
            public uint Timeout;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
            public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIcon(uint message, ref NotificationData data);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
    }
}
