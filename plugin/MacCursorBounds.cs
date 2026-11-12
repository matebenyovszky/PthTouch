using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace PthTouch;

// No event tap or extra permission: only pointer position and active display
// geometry. Each mode owns its callback and unregisters it when replaced.
public sealed class MacCursorBounds : ICursorBounds, IDisposable
{
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    [StructLayout(LayoutKind.Sequential)] private struct Point { public double X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public double X, Y, Width, Height; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DisplayChanged(uint display, uint flags, IntPtr context);
    [DllImport(CG)] private static extern int CGGetActiveDisplayList(uint capacity, [Out] uint[]? displays, out uint count);
    [DllImport(CG)] private static extern Rect CGDisplayBounds(uint display);
    [DllImport(CG)] private static extern int CGDisplayRegisterReconfigurationCallback(DisplayChanged callback, IntPtr context);
    [DllImport(CG)] private static extern int CGDisplayRemoveReconfigurationCallback(DisplayChanged callback, IntPtr context);
    [DllImport(CG)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(CG)] private static extern Point CGEventGetLocation(IntPtr ev);
    [DllImport(CG)] private static extern void CGEventSetLocation(IntPtr ev, Point point);
    [DllImport(CG)] private static extern void CGEventSetDoubleValueField(IntPtr ev, uint field, double value);
    [DllImport(CF)] private static extern void CFRelease(IntPtr obj);
    private readonly object sync = new();
    private readonly DisplayChanged changed;
    private readonly bool registered;
    private bool disposed;
    private long generation, cachedGeneration = -1;
    private double nextPoll;
    private CursorDisplay[] screens = [];

    public MacCursorBounds()
    {
        changed = (_, flags, _) => { if ((flags & 1) == 0) Interlocked.Increment(ref generation); };
        if (OperatingSystem.IsMacOS()) registered = CGDisplayRegisterReconfigurationCallback(changed, IntPtr.Zero) == 0;
    }
    private CursorDisplay[] Screens()
    {
        if (!OperatingSystem.IsMacOS()) return [];
        lock (sync)
        {
            if (disposed) return [];
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            long currentGeneration = Volatile.Read(ref generation);
            if (currentGeneration == cachedGeneration && now < nextPoll) return screens;
            // Polling is also a fallback for unavailable/delayed notifications.
            nextPoll = now + .25;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (CGGetActiveDisplayList(0, null, out uint count) != 0 || count == 0 || count > 256) break;
                var ids = new uint[count];
                if (CGGetActiveDisplayList(count, ids, out uint actual) != 0 || actual > count) continue;
                var next = new List<CursorDisplay>();
                foreach (uint id in ids.Take((int)actual))
                {
                    var rect = CGDisplayBounds(id);
                    var display = new CursorDisplay(rect.X, rect.Y, rect.Width, rect.Height);
                    if (display.Valid) next.Add(display);
                }
                if (next.Count > 0) screens = next.ToArray();
                break;
            }
            cachedGeneration = currentGeneration;
            return screens;
        }
    }
    public Vector2 LimitDelta(Vector2 delta)
    {
        if (!OperatingSystem.IsMacOS()) return delta;
        var ev = CGEventCreate(IntPtr.Zero);
        if (ev == IntPtr.Zero) return delta;
        try
        {
            var position = CGEventGetLocation(ev);
            return CursorBounds.Delta(new(position.X, position.Y), delta, Screens());
        }
        finally { CFRelease(ev); }
    }
    // Called by the stock pointer's final position hook: read current once,
    // clamp the actual event target, and keep delta fields consistent with it.
    public static void WriteRelativeEvent(IntPtr ev, float x, float y, ICursorBounds? bounds)
    {
        var probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return;
        Point current;
        try { current = CGEventGetLocation(probe); }
        finally { CFRelease(probe); }
        var target = new CursorPoint(current.X + x, current.Y + y);
        if (bounds is not null) target = bounds.LimitPoint(target);
        CGEventSetLocation(ev, new Point { X = target.X, Y = target.Y });
        CGEventSetDoubleValueField(ev, 4, target.X - current.X);
        CGEventSetDoubleValueField(ev, 5, target.Y - current.Y);
    }
    public CursorPoint LimitPoint(CursorPoint point) => CursorBounds.Clamp(point, Screens());
    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) return;
            disposed = true;
            if (registered) CGDisplayRemoveReconfigurationCallback(changed, IntPtr.Zero);
        }
        GC.KeepAlive(changed);
    }
}
