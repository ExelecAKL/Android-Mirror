using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace AndroidMirror
{
    // Windows resolves a desktop notification's name and small icon from its
    // Start menu shortcut. Registry metadata alone does not establish that link.
    public static class WindowsAppIdentity
    {
        public const string AppId = "Exelec.AndroidMirror.Desktop";
        static readonly PropertyKey AppIdKey = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);
        public static string UserShortcut { get { return ShortcutPath(Environment.SpecialFolder.Programs); } }
        static string ShortcutPath(Environment.SpecialFolder folder)
        { return Path.Combine(Environment.GetFolderPath(folder), "Android Mirror", "Android Mirror.lnk"); }

        public static void EnsureShortcut(string executable)
        {
            executable = Path.GetFullPath(executable);
            string common = ShortcutPath(Environment.SpecialFolder.CommonPrograms);
            // An older per-user link shadows the all-users link, so repair it too.
            if (MatchesShortcut(common, executable) && !File.Exists(UserShortcut)) return;
            if (!MatchesShortcut(UserShortcut, executable)) CreateShortcut(UserShortcut, executable, Path.GetDirectoryName(executable));
        }
        public static void CreateShortcut(string destination, string executable, string workingDirectory)
        {
            bool replacing = File.Exists(destination);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
            object instance = new ShellLink();
            try
            {
                var link = (IShellLink)instance;
                link.SetPath(Path.GetFullPath(executable)); link.SetWorkingDirectory(workingDirectory);
                link.SetDescription("Mirror and manage Android devices."); link.SetIconLocation(executable, 0);
                var store = (IPropertyStore)instance;
                var key = AppIdKey;
                using (var value = new PropVariant(AppId)) { store.SetValue(ref key, value); store.Commit(); }
                ((IPersistFile)instance).Save(destination, true);
            }
            finally { Marshal.FinalReleaseComObject(instance); }
            SHChangeNotify(replacing ? 0x00002000u : 0x00000002u, 0x5, destination, IntPtr.Zero);
        }
        public sealed class ShortcutInfo
        {
            public string Target, IconPath, AppId;
            public int IconIndex;
        }
        public static ShortcutInfo ReadShortcut(string path)
        {
            object instance = new ShellLink();
            try
            {
                ((IPersistFile)instance).Load(path, 0);
                var link = (IShellLink)instance;
                var target = new StringBuilder(32768); var icon = new StringBuilder(32768); int index;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4);
                link.GetIconLocation(icon, icon.Capacity, out index);
                var key = AppIdKey;
                using (var value = new PropVariant())
                {
                    ((IPropertyStore)instance).GetValue(ref key, value);
                    return new ShortcutInfo { Target = target.ToString(), IconPath = icon.ToString(), IconIndex = index, AppId = value.Text };
                }
            }
            finally { Marshal.FinalReleaseComObject(instance); }
        }
        public static bool MatchesShortcut(string path, string executable)
        {
            if (!File.Exists(path)) return false;
            try
            {
                var link = ReadShortcut(path);
                return SamePath(link.Target, executable) && SamePath(link.IconPath, executable) && link.IconIndex == 0 && link.AppId == AppId;
            }
            catch (COMException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        public static void RemoveShortcutIfOwned(string path, string executable)
        {
            if (!File.Exists(path) || !SamePath(ReadShortcut(path).Target, executable)) return;
            File.Delete(path);
            string parent = Path.GetDirectoryName(path);
            if (Directory.Exists(parent) && Directory.GetFileSystemEntries(parent).Length == 0) Directory.Delete(parent);
            SHChangeNotify(0x00000004, 0x5, path, IntPtr.Zero);
        }
        static bool SamePath(string first, string second)
        { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink { }
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int size, IntPtr findData, uint flags);
            void GetIDList(out IntPtr id); void SetIDList(IntPtr id);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string value);
            void GetHotkey(out short value); void SetHotkey(short value);
            void GetShowCmd(out int value); void SetShowCmd(int value);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder value, int size, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string value, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string value, uint reserved);
            void Resolve(IntPtr window, uint flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string value);
        }
        [StructLayout(LayoutKind.Sequential)] struct PropertyKey
        {
            public Guid Format; public uint Id;
            public PropertyKey(Guid format, uint id) { Format = format; Id = id; }
        }
        // PROPVARIANT is 24 bytes on x64 and 16 bytes on x86.
        [StructLayout(LayoutKind.Sequential)] sealed class PropVariant : IDisposable
        {
            ushort type, reserved1, reserved2, reserved3;
            IntPtr pointer, padding;
            public PropVariant() { }
            public PropVariant(string value) { type = 31; pointer = Marshal.StringToCoTaskMemUni(value); }
            public string Text { get { return type == 31 && pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pointer) : ""; } }
            public void Dispose() { PropVariantClear(this); }
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            void GetCount(out uint count); void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, [Out] PropVariant value);
            void SetValue(ref PropertyKey key, [In] PropVariant value); void Commit();
        }
        [DllImport("ole32.dll")] static extern int PropVariantClear([In, Out] PropVariant value);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(uint change, uint flags, string first, IntPtr second);
    }
}
