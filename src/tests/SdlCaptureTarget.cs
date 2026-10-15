using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
class SdlCaptureTarget
{
    const string Dll="SDL3.dll";
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool SetDllDirectory(string dir);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] static extern bool SDL_Init(uint flags);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr SDL_CreateWindow(string title,int w,int h,ulong flags);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr SDL_CreateRenderer(IntPtr window,string driver);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern uint SDL_GetWindowProperties(IntPtr window);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern IntPtr SDL_GetPointerProperty(uint props,string name,IntPtr fallback);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] [return:MarshalAs(UnmanagedType.I1)] static extern bool SDL_PollEvent(IntPtr e);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_SetRenderDrawColor(IntPtr r,byte red,byte green,byte blue,byte alpha);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_RenderClear(IntPtr r);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_RenderPresent(IntPtr r);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_DestroyRenderer(IntPtr r);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_DestroyWindow(IntPtr w);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_Quit();
    static int Main(string[] args){
        SetDllDirectory(Path.GetFullPath("engine"));
        if(!SDL_Init(0x20))return 1;
        var w=SDL_CreateWindow("Android Mirror capture verification",400,300,0x20);var r=SDL_CreateRenderer(w,null);
        var hwnd=SDL_GetPointerProperty(SDL_GetWindowProperties(w),"SDL.window.win32.hwnd",IntPtr.Zero);
        File.WriteAllText(args[0],hwnd.ToInt64().ToString());
        var timer=Stopwatch.StartNew();var e=Marshal.AllocHGlobal(128);
        try{while(timer.ElapsedMilliseconds<45000){while(SDL_PollEvent(e)){if(Marshal.ReadInt32(e)==0x100)return 0;} SDL_SetRenderDrawColor(r,(byte)(40+timer.ElapsedMilliseconds/40%180),(byte)(30+timer.ElapsedMilliseconds/70%190),80,255);SDL_RenderClear(r);SDL_RenderPresent(r);Thread.Sleep(16);}}
        finally{Marshal.FreeHGlobal(e);SDL_DestroyRenderer(r);SDL_DestroyWindow(w);SDL_Quit();}return 0;
    }
}
