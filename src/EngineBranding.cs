using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace AndroidMirror
{
    // Changes PE icon resources only. Engine code and server binaries are untouched.
    public static class EngineBranding
    {
        delegate bool EnumName(IntPtr module, IntPtr type, IntPtr name, IntPtr parameter);
        delegate bool EnumLanguage(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumName callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumLanguage callback, IntPtr parameter);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr BeginUpdateResource(string path, bool deleteExisting);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EndUpdateResource(IntPtr update, bool discard);
        sealed class OldIcon { public int Type, Id; public string Name; public ushort Language; }
        public static void Apply(string executable, string icon)
        {
            byte[] ico = File.ReadAllBytes(icon);
            if (ico.Length < 6 || BitConverter.ToUInt16(ico, 0) != 0 || BitConverter.ToUInt16(ico, 2) != 1) throw new IOException("Invalid app icon.");
            int count = BitConverter.ToUInt16(ico, 4); if (count < 1 || count > 256 || ico.Length < 6 + count * 16) throw new IOException("Invalid icon entries.");
            var old = new List<OldIcon>();
            IntPtr module = LoadLibraryEx(executable, IntPtr.Zero, 0x22); if (module == IntPtr.Zero) throw new Win32Exception();
            try {
                foreach (int kind in new[] { 3, 14 }) {
                    EnumName names = (m, t, n, parameter) => {
                        bool id = ((ulong)n.ToInt64() >> 16) == 0; string name = id ? null : Marshal.PtrToStringUni(n); int number = id ? n.ToInt32() : 0;
                        EnumLanguage languages = (lm, lt, ln, language, lp) => { old.Add(new OldIcon { Type = kind, Id = number, Name = name, Language = language }); return true; };
                        EnumResourceLanguages(m, t, n, languages, IntPtr.Zero); return true;
                    };
                    EnumResourceNames(module, new IntPtr(kind), names, IntPtr.Zero);
                }
            } finally { FreeLibrary(module); }
            IntPtr update = BeginUpdateResource(executable, false); if (update == IntPtr.Zero) throw new Win32Exception();
            bool completed = false;
            try {
                foreach (var item in old) {
                    IntPtr name = item.Name == null ? new IntPtr(item.Id) : Marshal.StringToHGlobalUni(item.Name);
                    try { if (!UpdateResource(update, new IntPtr(item.Type), name, item.Language, null, 0)) throw new Win32Exception(); }
                    finally { if (item.Name != null) Marshal.FreeHGlobal(name); }
                }
                byte[] group = new byte[6 + count * 14]; Array.Copy(ico, group, 6);
                for (int i = 0; i < count; i++) {
                    int entry = 6 + i * 16, size = BitConverter.ToInt32(ico, entry + 8), offset = BitConverter.ToInt32(ico, entry + 12);
                    if (size < 1 || offset < 0 || (long)offset + size > ico.Length) throw new IOException("Invalid icon image range.");
                    byte[] data = new byte[size]; Array.Copy(ico, offset, data, 0, size);
                    if (!UpdateResource(update, new IntPtr(3), new IntPtr(i + 1), 0, data, (uint)data.Length)) throw new Win32Exception();
                    Array.Copy(ico, entry, group, 6 + i * 14, 12); Array.Copy(BitConverter.GetBytes((ushort)(i + 1)), 0, group, 6 + i * 14 + 12, 2);
                }
                if (!UpdateResource(update, new IntPtr(14), new IntPtr(1), 0, group, (uint)group.Length)) throw new Win32Exception();
                if (!EndUpdateResource(update, false)) throw new Win32Exception(); completed = true;
            } finally { if (!completed) EndUpdateResource(update, true); }
        }
    }
}
#if BRANDING_TOOL
static class BrandEngineTool
{
    static int Main(string[] args) { try { AndroidMirror.EngineBranding.Apply(args[0], args[1]); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
}
#endif
