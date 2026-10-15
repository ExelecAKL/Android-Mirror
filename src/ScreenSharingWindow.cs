using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Forms = System.Windows.Forms;

namespace AndroidMirror
{
    // Windows capture sources stop receiving frames from an iconic HWND, and
    // SDL also stops presenting it. Keep the same visible, non-iconic HWND out
    // of the virtual desktop instead. No engine hooks or binary patches are used.
    public sealed class ScreenSharingWindow : IDisposable
    {
        const uint Foreground = 3, MinimizeStart = 0x16, LocationChanged = 0x800B;
        const uint NoActivate = 0x10, NoZOrder = 4, NoSize = 1, NoOwnerZOrder = 0x200;
        readonly uint processId;
        readonly Action<string> log;
        readonly Thread thread;
        readonly Func<uint> inputTick;
        volatile bool enabled, stopping, parked;
        uint threadId;
        IntPtr window;
        Rect savedBounds;
        Placement savedPlacement;
        bool haveBounds, wasMaximized, moving;
        bool minimizePending;
        int minimizeTick;
        int addedStyles;
        uint parkedInput;
        IntPtr savedMonitor;
        WinEvent callback;
        IntPtr foregroundHook, minimizeHook, locationHook;

        public ScreenSharingWindow(int processId, bool enabled = true, Action<string> log = null)
            : this(processId, enabled, log, LastInputTick) { }
        internal ScreenSharingWindow(int processId, bool enabled, Action<string> log, Func<uint> inputTick)
        {
            this.processId = (uint)processId; this.enabled = enabled; this.log = log; this.inputTick = inputTick;
            thread = new Thread(Run) { IsBackground = true, Name = "Android Mirror capture window" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
        }
        public bool Enabled { get { return enabled; } set { enabled = value; } }
        public bool IsParked { get { return parked; } }
        public IntPtr WindowHandle { get { return Interlocked.CompareExchange(ref window, IntPtr.Zero, IntPtr.Zero); } }

        void Run()
        {
            threadId = GetCurrentThreadId();
            try { SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            using (var context = new Forms.ApplicationContext())
            using (var timer = new Forms.Timer { Interval = 120 })
            {
                try
                {
                    callback = Event;
                    foregroundHook = SetWinEventHook(Foreground, Foreground, IntPtr.Zero, callback, processId, 0, 0);
                    minimizeHook = SetWinEventHook(MinimizeStart, MinimizeStart, IntPtr.Zero, callback, processId, 0, 0);
                    locationHook = SetWinEventHook(LocationChanged, LocationChanged, IntPtr.Zero, callback, processId, 0, 0);
                    timer.Tick += delegate { if (stopping) context.ExitThread(); else CheckWindow(); };
                    if (!stopping) { CheckWindow(); timer.Start(); Forms.Application.Run(context); }
                }
                catch (Exception error) { if (log != null) log("Background screen sharing is unavailable: " + error.Message); }
                finally
                {
                    timer.Stop();
                    if (foregroundHook != IntPtr.Zero) UnhookWinEvent(foregroundHook);
                    if (minimizeHook != IntPtr.Zero) UnhookWinEvent(minimizeHook);
                    if (locationHook != IntPtr.Zero) UnhookWinEvent(locationHook);
                    // A disabled watcher or an orderly shell exit must not strand
                    // a surviving window outside the desktop.
                    if (parked && OwnsWindow()) Restore();
                    callback = null;
                }
            }
        }
        bool OwnsWindow()
        {
            uint owner; return window != IntPtr.Zero && IsWindow(window) && GetWindowThreadProcessId(window, out owner) != 0 && owner == processId;
        }
        void CheckWindow()
        {
            if (!OwnsWindow())
            {
                window = IntPtr.Zero; parked = false; haveBounds = false; minimizePending = false; addedStyles = 0; savedMonitor = IntPtr.Zero;
                EnumWindows(delegate(IntPtr candidate, IntPtr unused) {
                    uint owner; GetWindowThreadProcessId(candidate, out owner);
                    if (owner != processId || !IsWindowVisible(candidate)) return true;
                    var name = new StringBuilder(64); GetClassName(candidate, name, name.Capacity);
                    if (name.ToString() != "SDL_app" && name.ToString() != "SDL_Window") return true;
                    window = candidate; return false;
                }, IntPtr.Zero);
            }
            if (!OwnsWindow()) return;
            if (!enabled) { if (parked) Restore(); else Remember(); return; }
            if (IsIconic(window))
            {
                // MINIMIZESTART can arrive before Windows finishes assigning
                // the foreground to the next app. Restoring during that event
                // can immediately activate this window again.
                if (!minimizePending) { minimizePending = true; minimizeTick = Environment.TickCount; }
                else
                {
                    int elapsed = unchecked(Environment.TickCount - minimizeTick);
                    if (elapsed >= 120 && (GetForegroundWindow() != window || elapsed >= 400)) Park();
                }
                return;
            }
            minimizePending = false;
            if (parked)
            {
                if (UserRequestedRestore()) { Restore(); return; }
                Rect current; if (GetWindowRect(window, out current) && Intersects(current, Desktop())) MoveOutside(current);
            }
            else Remember();
        }
        void Event(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint eventThread, uint time)
        {
            if (stopping || moving || hwnd != window || !OwnsWindow()) return;
            try
            {
                // A delayed activation from ShowWindow/SDL is not a taskbar or
                // Alt+Tab request. In particular, don't undo the minimize click.
                if (kind == Foreground && parked && UserRequestedRestore()) Restore();
                else if (enabled && kind == MinimizeStart) { minimizePending = true; minimizeTick = Environment.TickCount; }
                else if (kind == LocationChanged && objectId == 0 && !parked && !IsIconic(window)) Remember();
            }
            catch (Exception error) { if (log != null) log("Couldn’t update the background mirror window: " + error.Message); }
        }
        void Remember()
        {
            if (!OwnsWindow() || IsIconic(window) || parked) return;
            Rect bounds; var placement = new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
            if (!GetWindowRect(window, out bounds) || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top || !GetWindowPlacement(window, ref placement)) return;
            savedBounds = bounds; savedPlacement = placement; haveBounds = true; wasMaximized = IsZoomed(window);
            savedMonitor = MonitorFromRect(ref bounds, 2);
        }
        bool UserRequestedRestore() { return GetForegroundWindow() == window && inputTick() != parkedInput; }
        void Park()
        {
            if (!enabled || !OwnsWindow()) return;
            moving = true;
            try
            {
                if (!haveBounds)
                {
                    // Covers a minimize immediately after startup, before the
                    // first location notification reached this watcher.
                    var placement = new Placement { Length = Marshal.SizeOf(typeof(Placement)) };
                    if (!GetWindowPlacement(window, ref placement)) return;
                    savedPlacement = placement; wasMaximized = (placement.Flags & 2) != 0;
                    savedBounds = placement.Normal; haveBounds = savedBounds.Width > 0 && savedBounds.Height > 0;
                    if (!haveBounds) return;
                }
                if (!parked)
                {
                    int style = GetWindowLong(window, -20);
                    addedStyles = 0x08040000 & ~style; // NOACTIVATE + APPWINDOW
                    SetWindowLong(window, -20, style | 0x08040000);
                }
                // A second minimize click is also not a request to restore.
                parkedInput = inputTick();
                bool alreadyParked = parked;
                parked = true;
                if (wasMaximized && !alreadyParked)
                {
                    // A minimize from maximized carries RESTORETOMAXIMIZED.
                    // Clear it for the background phase so SDL receives a real
                    // windowed restore and can resize when the phone rotates.
                    var placement = savedPlacement; placement.Flags &= ~2; placement.Show = 4;
                    SetWindowPlacement(window, ref placement);
                }
                else ShowWindow(window, 4);
                Rect current; if (!GetWindowRect(window, out current)) current = savedBounds;
                MoveOutside(current);
            }
            finally { moving = false; }
        }
        void MoveOutside(Rect bounds)
        {
            var desktop = Desktop();
            int width = Math.Max(1, bounds.Width), height = Math.Max(1, bounds.Height);
            // Prefer an edge nearest the original monitor, keeping its DPI when
            // possible. Moving must never resize or rebuild SDL's client frame.
            Rect[] positions = {
                new Rect { Left = desktop.Right + 128, Top = savedBounds.Top },
                new Rect { Left = savedBounds.Left, Top = desktop.Bottom + 128 },
                new Rect { Left = desktop.Left - width - 128, Top = savedBounds.Top },
                new Rect { Left = savedBounds.Left, Top = desktop.Top - height - 128 }
            };
            Rect chosen = positions[0]; long best = Int64.MaxValue;
            foreach (var position in positions)
            {
                var candidate = position; candidate.Right = candidate.Left + width; candidate.Bottom = candidate.Top + height;
                if (candidate.Left < -30000 || candidate.Top < -30000 || candidate.Right > 30000 || candidate.Bottom > 30000) continue;
                long score = Math.Abs((long)candidate.Left - savedBounds.Left) + Math.Abs((long)candidate.Top - savedBounds.Top);
                if (savedMonitor != IntPtr.Zero && MonitorFromRect(ref candidate, 2) != savedMonitor) score += 1000000;
                if (score < best) { best = score; chosen = candidate; }
            }
            SetWindowPos(window, IntPtr.Zero, chosen.Left, chosen.Top, 0, 0, NoActivate | NoZOrder | NoSize | NoOwnerZOrder);
        }
        void Restore()
        {
            if (!parked || !OwnsWindow()) return;
            moving = true;
            try
            {
                Rect current; if (!GetWindowRect(window, out current)) return;
                if (wasMaximized)
                {
                    // Keep SDL's latest rotated windowed size for a later
                    // unmaximize, instead of resurrecting stale portrait bounds.
                    var placement = savedPlacement;
                    placement.Normal.Right = placement.Normal.Left + current.Width;
                    placement.Normal.Bottom = placement.Normal.Top + current.Height;
                    placement.Show = 3;
                    SetWindowPlacement(window, ref placement);
                }
                else
                {
                    var bounds = savedBounds;
                    bounds.Right = bounds.Left + current.Width; bounds.Bottom = bounds.Top + current.Height;
                    if (!Intersects(bounds, Desktop())) { bounds.Left = Desktop().Left + 40; bounds.Top = Desktop().Top + 40; }
                    // The engine owns size/aspect ratio. Only restore position.
                    SetWindowPos(window, IntPtr.Zero, bounds.Left, bounds.Top, 0, 0, NoActivate | NoZOrder | NoSize | NoOwnerZOrder);
                }
                SetWindowLong(window, -20, GetWindowLong(window, -20) & ~addedStyles); addedStyles = 0;
                parked = false; Remember();
            }
            finally { moving = false; }
        }
        public void Dispose()
        {
            stopping = true;
            uint id = threadId; if (id != 0) PostThreadMessage(id, 0x12, IntPtr.Zero, IntPtr.Zero);
        }
        static Rect Desktop() { int x = GetSystemMetrics(76), y = GetSystemMetrics(77); return new Rect { Left = x, Top = y, Right = x + GetSystemMetrics(78), Bottom = y + GetSystemMetrics(79) }; }
        static bool Intersects(Rect a, Rect b) { return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top; }
        static uint LastInputTick() { var input = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) }; return GetLastInputInfo(ref input) ? input.Tick : 0; }
        [StructLayout(LayoutKind.Sequential)] struct LastInput { public uint Size, Tick; }
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; public int Width { get { return Right - Left; } } public int Height { get { return Bottom - Top; } } }
        [StructLayout(LayoutKind.Sequential)] struct Placement { public int Length, Flags, Show; public Point Min, Max; public Rect Normal; }
        delegate bool EnumWindow(IntPtr hwnd, IntPtr data);
        delegate void WinEvent(IntPtr hook, uint kind, IntPtr hwnd, int objectId, int childId, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LastInput input);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Rect bounds, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int length);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect bounds);
        [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr hwnd, ref Placement placement);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    }
}
