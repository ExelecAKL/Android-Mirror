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
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern ulong SDL_GetWindowFlags(IntPtr w);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern bool SDL_GetWindowSize(IntPtr w,out int width,out int height);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern bool SDL_SetWindowSize(IntPtr w,int width,int height);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern bool SDL_RenderFillRect(IntPtr renderer,ref FloatRect rect);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_DestroyRenderer(IntPtr r);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_DestroyWindow(IntPtr w);
    [DllImport(Dll,CallingConvention=CallingConvention.Cdecl)] static extern void SDL_Quit();
    [StructLayout(LayoutKind.Sequential)] struct FloatRect { public float X,Y,W,H; }
    static int contentWidth=400,contentHeight=300,pendingWidth,pendingHeight;
    static bool Windowed(IntPtr window) { return (SDL_GetWindowFlags(window)&(0x40UL|0x80UL))==0; }
    static void ResizeForContent(IntPtr window,int oldWidth,int oldHeight) {
        int width,height;SDL_GetWindowSize(window,out width,out height);
        SDL_SetWindowSize(window,width*contentWidth/oldWidth,height*contentHeight/oldHeight);
    }
    static int Main(string[] args){
        SetDllDirectory(Path.GetFullPath("engine"));
        if(!SDL_Init(0x20))return 1;
        var w=SDL_CreateWindow("Android Mirror capture verification",400,300,0x20);var r=SDL_CreateRenderer(w,null);
        var hwnd=SDL_GetPointerProperty(SDL_GetWindowProperties(w),"SDL.window.win32.hwnd",IntPtr.Zero);
        File.WriteAllText(args[0],hwnd.ToInt64().ToString());
        var timer=Stopwatch.StartNew();var e=Marshal.AllocHGlobal(128);string lastCommand="",commandPath=args[0]+".command";
        try{while(timer.ElapsedMilliseconds<120000){
            while(SDL_PollEvent(e)) {
                int kind=Marshal.ReadInt32(e);if(kind==0x100)return 0;
                if(kind==0x20B&&pendingWidth>0&&Windowed(w)){ResizeForContent(w,pendingWidth,pendingHeight);pendingWidth=0;}
            }
            if(File.Exists(commandPath)) {
                string command;
                try {
                    using(var stream=new FileStream(commandPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    using(var reader=new StreamReader(stream))command=reader.ReadToEnd();
                } catch(IOException){command="";}
                var words=command.Split(' ');int nextWidth,nextHeight;
                if(command!=lastCommand&&words.Length==3&&Int32.TryParse(words[1],out nextWidth)&&Int32.TryParse(words[2],out nextHeight)) {
                    lastCommand=command;int oldWidth=contentWidth,oldHeight=contentHeight;contentWidth=nextWidth;contentHeight=nextHeight;
                    if(Windowed(w))ResizeForContent(w,oldWidth,oldHeight);else if(pendingWidth==0){pendingWidth=oldWidth;pendingHeight=oldHeight;}
                }
            }
            int width,height;SDL_GetWindowSize(w,out width,out height);
            SDL_SetRenderDrawColor(r,50,50,50,255);SDL_RenderClear(r);
            float scale=Math.Min((float)width/contentWidth,(float)height/contentHeight);
            var rect=new FloatRect{W=contentWidth*scale,H=contentHeight*scale};rect.X=(width-rect.W)/2;rect.Y=(height-rect.H)/2;
            SDL_SetRenderDrawColor(r,(byte)(40+timer.ElapsedMilliseconds/40%180),(byte)(30+timer.ElapsedMilliseconds/70%190),80,255);
            SDL_RenderFillRect(r,ref rect);SDL_RenderPresent(r);Thread.Sleep(16);
        }}
        finally{Marshal.FreeHGlobal(e);SDL_DestroyRenderer(r);SDL_DestroyWindow(w);SDL_Quit();}return 0;
    }
}
