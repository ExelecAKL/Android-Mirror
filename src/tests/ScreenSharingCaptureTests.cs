using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

class CaptureProbe
{
    static IDirect3DDevice captureDevice;
    static int bufferWidth,bufferHeight,inputRevision=1,commandSequence;
    [ComImport, Guid("3628e81b-3cac-4c60-b7f4-23ce0e0c3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IItemInterop { IntPtr CreateForWindow(IntPtr hwnd, ref Guid iid); IntPtr CreateForMonitor(IntPtr monitor, ref Guid iid); }
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISurfaceAccess { void GetInterface(ref Guid iid, out IntPtr result); }
    [DllImport("combase.dll")] static extern int WindowsCreateString([MarshalAs(UnmanagedType.LPWStr)] string value, int length, out IntPtr result);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, out IItemInterop result);
    [DllImport("d3d11.dll")] static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll")] static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi, out IntPtr device);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd); [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr hwnd, bool altTab); [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow(); [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid); [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId(); [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Desc { public uint Width, Height, MipLevels, ArraySize, Format, Count, Quality, Usage, Bind, Cpu, Misc; }
    [StructLayout(LayoutKind.Sequential)] struct Mapped { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDesc(IntPtr self, out Desc desc);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture(IntPtr self, ref Desc desc, IntPtr initial, out IntPtr texture);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResource(IntPtr self, IntPtr dest, IntPtr source);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Map(IntPtr self, IntPtr resource, uint subresource, uint type, uint flags, out Mapped mapped);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Unmap(IntPtr self, IntPtr resource, uint subresource);
    static T Method<T>(IntPtr obj, int slot) where T : class { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), slot * IntPtr.Size), typeof(T)) as T; }
    static void Check(int hr) { Marshal.ThrowExceptionForHR(hr); }
    [MTAThread] static int Main(string[] args)
    {
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero, staging = IntPtr.Zero;
        try {
            IntPtr hwnd = new IntPtr(Int64.Parse(args[0]));
            Console.WriteLine("Supported: " + GraphicsCaptureSession.IsSupported());
            IntPtr hstring; const string name = "Windows.Graphics.Capture.GraphicsCaptureItem";
            Check(WindowsCreateString(name, name.Length, out hstring));
            Guid interopId = typeof(IItemInterop).GUID; IItemInterop interop;
            Check(RoGetActivationFactory(hstring, ref interopId, out interop)); WindowsDeleteString(hstring);
            Guid itemId = new Guid("79c3f95b-31f7-4ec2-a464-632ef5d30760");
            IntPtr itemPtr = interop.CreateForWindow(hwnd, ref itemId);
            var item = (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(itemPtr); Marshal.Release(itemPtr);
            int level; Check(D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, out level, out context));
            Guid dxgiId = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c"); IntPtr dxgi, inspectable;
            Check(Marshal.QueryInterface(device, ref dxgiId, out dxgi));
            Check(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out inspectable)); Marshal.Release(dxgi);
            var directDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable); Marshal.Release(inspectable);
            captureDevice=directDevice;bufferWidth=item.Size.Width;bufferHeight=item.Size.Height;
            using (var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(directDevice, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, item.Size))
            using (var session = pool.CreateCaptureSession(item)) {
                session.IsCursorCaptureEnabled = false; session.StartCapture();
                Rect original; GetWindowRect(hwnd, out original);
                if(args.Length>1) {
                    var constructor=typeof(AndroidMirror.ScreenSharingWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(int),typeof(bool),typeof(Action<string>),typeof(Func<uint>)},null);
                    using(var watcher=constructor==null?new AndroidMirror.ScreenSharingWindow(Int32.Parse(args[1]),true,Console.WriteLine):(AndroidMirror.ScreenSharingWindow)constructor.Invoke(new object[]{Int32.Parse(args[1]),true,(Action<string>)Console.WriteLine,(Func<uint>)(()=>unchecked((uint)Interlocked.CompareExchange(ref inputRevision,0,0)))})) {
                        Wait(()=>watcher.WindowHandle==hwnd,"watcher attachment");
                        Require(Sample(pool,device,context,ref staging,"before minimize",1500)>10,"Live visible frames");
                        ShowWindow(hwnd,6);Wait(()=>watcher.IsParked&&!IsIconic(hwnd),"background minimize");
                        Require(Sample(pool,device,context,ref staging,"background sharing",2800)>25,"Background picture must change");
                        Rect parked;GetWindowRect(hwnd,out parked);Console.WriteLine("Parked rect: "+parked.Left+","+parked.Top+","+parked.Right+","+parked.Bottom); Require(OutsideDesktop(hwnd)&&watcher.IsParked,"Window must stay outside desktop without bouncing open");
                        Rotate(args[2],300,400);Wait(()=>ClientSize(hwnd,300,400),"portrait rotation while parked");
                        Require(Sample(pool,device,context,ref staging,"rotated background sharing",1300)>10,"Rotated background picture must change");
                        Require(watcher.IsParked&&OutsideDesktop(hwnd),"Rotation must not reopen the mirror");
                        Interlocked.Increment(ref inputRevision);ShowWindow(hwnd,6);Wait(()=>!IsIconic(hwnd),"repeat minimize");
                        UserActivate(hwnd);Wait(()=>!watcher.IsParked,"taskbar activation restore");
                        Thread.Sleep(350);Require(ClientSize(hwnd,300,400),"Restoring must keep the engine's latest portrait dimensions, with no stale landscape letterbox");
                        Rect restored;GetWindowRect(hwnd,out restored);Console.WriteLine("Original: "+original.Left+","+original.Top+","+original.Right+","+original.Bottom+"; restored: "+restored.Left+","+restored.Top+","+restored.Right+","+restored.Bottom);Require(Math.Abs(restored.Left-original.Left)<8&&Math.Abs(restored.Top-original.Top)<8,"Original position restored");
                        Require(Sample(pool,device,context,ref staging,"after restore",1600)>10,"Restored picture must change");
                        Rotate(args[2],400,300);Wait(()=>ClientSize(hwnd,400,300),"landscape rotation after restore");
                        ShowWindow(hwnd,6);Wait(()=>watcher.IsParked&&!IsIconic(hwnd),"second background minimize");
                        Rotate(args[2],300,400);Wait(()=>ClientSize(hwnd,300,400),"second portrait rotation");
                        Rotate(args[2],400,300);Wait(()=>ClientSize(hwnd,400,300),"second landscape rotation");
                        Activate(hwnd);Thread.Sleep(600);Require(watcher.IsParked&&OutsideDesktop(hwnd),"A programmatic foreground event without new user input must not undo minimize");
                        UserActivate(hwnd);Wait(()=>!watcher.IsParked,"explicit user activation after stale foreground");
                        Require(ClientSize(hwnd,400,300),"Repeated rotations must preserve the current landscape dimensions");
                        ShowWindow(hwnd,3);Thread.Sleep(400);ShowWindow(hwnd,6);Wait(()=>watcher.IsParked&&!IsIconic(hwnd),"maximized background minimize");
                        Require(Sample(pool,device,context,ref staging,"maximized background sharing",1600)>10,"Maximized background picture must change");
                        Rotate(args[2],300,400);Wait(()=>ClientSize(hwnd,300,400),"portrait rotation from a parked maximized window");
                        UserActivate(hwnd);Wait(()=>!watcher.IsParked&&IsZoomed(hwnd),"maximized restore");
                        ShowWindow(hwnd,9);Wait(()=>ClientSize(hwnd,300,400),"unmaximize retains the latest portrait dimensions");
                        Rotate(args[2],400,300);Wait(()=>ClientSize(hwnd,400,300),"landscape after unmaximize");
                        ShowWindow(hwnd,6);Wait(()=>watcher.IsParked&&!IsIconic(hwnd),"minimize before disabling");
                        watcher.Enabled=false;Wait(()=>!watcher.IsParked,"disabled setting restores window");
                        ShowWindow(hwnd,6);Thread.Sleep(500);Require(IsIconic(hwnd),"Disabled watcher must allow real minimize");
                        ShowWindow(hwnd,4);watcher.Enabled=true;Thread.Sleep(250);ShowWindow(hwnd,6);Wait(()=>watcher.IsParked&&!IsIconic(hwnd),"reenabled watcher");
                    }
                    Wait(()=>{Rect rect;GetWindowRect(hwnd,out rect);return rect.Right>0&&!IsIconic(hwnd);},"watcher cleanup restores window");
                    Console.WriteLine("PASS screen sharing window lifecycle");return 0;
                }
                Sample(pool, device, context, ref staging, "visible", 2200);
                ShowWindow(hwnd, 6);
                Sample(pool, device, context, ref staging, "minimized", 2200);
                ShowWindow(hwnd, 4);
                SetWindowPos(hwnd, IntPtr.Zero, -24000, -24000, original.Right-original.Left, original.Bottom-original.Top, 0x14);
                Sample(pool, device, context, ref staging, "offscreen", 3500);
                SetWindowPos(hwnd, IntPtr.Zero, original.Left, original.Top, original.Right-original.Left, original.Bottom-original.Top, 0x14);
                Sample(pool, device, context, ref staging, "restored", 1800);
            }
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if(staging!=IntPtr.Zero)Marshal.Release(staging);if(context!=IntPtr.Zero)Marshal.Release(context);if(device!=IntPtr.Zero)Marshal.Release(device); }
    }
    static void UserActivate(IntPtr hwnd){Interlocked.Increment(ref inputRevision);Activate(hwnd);}
    static void Rotate(string path,int width,int height){File.WriteAllText(path+".command",(++commandSequence)+" "+width+" "+height);}
    static bool ClientSize(IntPtr hwnd,int width,int height){Rect rect;GetClientRect(hwnd,out rect);return Math.Abs(rect.Right-width)<=2&&Math.Abs(rect.Bottom-height)<=2;}
    static bool OutsideDesktop(IntPtr hwnd){Rect rect;GetWindowRect(hwnd,out rect);int x=GetSystemMetrics(76),y=GetSystemMetrics(77);return rect.Right<=x||rect.Bottom<=y||rect.Left>=x+GetSystemMetrics(78)||rect.Top>=y+GetSystemMetrics(79);}
    static void Activate(IntPtr hwnd){uint pid;uint target=GetWindowThreadProcessId(hwnd,out pid),foreground=GetWindowThreadProcessId(GetForegroundWindow(),out pid),current=GetCurrentThreadId();bool a=AttachThreadInput(current,foreground,true),b=AttachThreadInput(current,target,true);try{Console.WriteLine("Activate: "+SetForegroundWindow(hwnd)+"; input: "+a+","+b);}finally{if(b)AttachThreadInput(current,target,false);if(a)AttachThreadInput(current,foreground,false);}} static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    static void Wait(Func<bool> condition,string message){var w=Stopwatch.StartNew();while(!condition()){if(w.ElapsedMilliseconds>3500)throw new Exception("Timed out: "+message);Thread.Sleep(25);}Console.WriteLine("PASS "+message);}
    static int Sample(Direct3D11CaptureFramePool pool, IntPtr device, IntPtr context, ref IntPtr staging, string name, int duration)
    {
        var watch=Stopwatch.StartNew(); var hashes=new HashSet<int>(); int frames=0;
        while(watch.ElapsedMilliseconds<duration) {
            var next=pool.TryGetNextFrame();
            if(next!=null&&(next.ContentSize.Width!=bufferWidth||next.ContentSize.Height!=bufferHeight)) {
                var size=next.ContentSize;next.Dispose();
                if(size.Width>0&&size.Height>0){pool.Recreate(captureDevice,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,size);bufferWidth=size.Width;bufferHeight=size.Height;}
                continue;
            }
            using(var frame=next) {
                if(frame==null){Thread.Sleep(15);continue;}
                Guid id=new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c"); IntPtr texture;
                ((ISurfaceAccess)frame.Surface).GetInterface(ref id, out texture);
                try {
                    Desc desc;Method<GetDesc>(texture,10)(texture,out desc);
                    if(staging!=IntPtr.Zero){Desc old;Method<GetDesc>(staging,10)(staging,out old);if(old.Width!=desc.Width||old.Height!=desc.Height){Marshal.Release(staging);staging=IntPtr.Zero;}}
                    if(staging==IntPtr.Zero){desc.Usage=3;desc.Bind=0;desc.Cpu=0x20000;desc.Misc=0;Check(Method<CreateTexture>(device,5)(device,ref desc,IntPtr.Zero,out staging));}
                    Method<CopyResource>(context,47)(context,staging,texture);
                    Mapped mapped;Check(Method<Map>(context,14)(context,staging,0,1,0,out mapped));
                    int hash=17;for(int y=30;y<Math.Min(desc.Height,250);y+=30)for(int x=20;x<Math.Min(desc.Width,250);x+=25)hash=unchecked(hash*31+Marshal.ReadInt32(mapped.Data,y*(int)mapped.RowPitch+x*4));
                    Method<Unmap>(context,15)(context,staging,0);hashes.Add(hash);frames++;
                } finally { Marshal.Release(texture); }
            }
        }
        Console.WriteLine(name+": frames="+frames+", unique pixels="+hashes.Count);
        return hashes.Count;
    }
}




