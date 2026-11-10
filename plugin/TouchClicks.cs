using System.Numerics;
using System.Runtime.InteropServices;

namespace PthTouch;

public interface ITouchClicker
{
    void Click(TapButton button, double now);
    double DoubleClickInterval => 0.5;
    void BeginDrag(double now);
    void MoveDrag(Vector2 delta);
    void EndDrag();
}

public sealed class ClickSequence
{
    private double lastTime = double.NegativeInfinity;
    private Vector2 lastPosition;
    private TapButton lastButton;
    private int count;

    public void Reset() { lastTime = double.NegativeInfinity; lastButton = TapButton.None; count = 0; }

    public int Next(TapButton button, double now, Vector2 position, double doubleClickInterval)
    {
        count = button == TapButton.Left && lastButton == button &&
            now - lastTime <= doubleClickInterval && Vector2.Distance(position, lastPosition) <= 8
            ? Math.Min(count + 1, 3) : 1;
        lastTime = now;
        lastPosition = position;
        lastButton = button;
        return count;
    }
}

// Native mouse events have the same click count on down and up, so two taps
// are an actual macOS double click. All events use the existing host's access.
public sealed class MacTouchClicker : ITouchClicker
{
    private readonly ClickSequence sequence = new();
    private bool dragging;
    private double? doubleClickInterval;

    public double DoubleClickInterval => doubleClickInterval ??= ReadDoubleClickInterval();
    private const string Quartz = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public double X, Y; }

    [DllImport(Quartz)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(Quartz)] private static extern Point CGEventGetLocation(IntPtr ev);
    [DllImport(Quartz)] private static extern IntPtr CGEventCreateMouseEvent(IntPtr source, uint type, Point point, uint button);
    [DllImport(Quartz)] private static extern void CGEventSetIntegerValueField(IntPtr ev, uint field, long value);
    [DllImport(Quartz)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(Quartz)] private static extern ulong CGEventSourceFlagsState(int state);
    [DllImport(Quartz)] private static extern void CGEventPost(uint tap, IntPtr ev);
    [DllImport(Foundation)] private static extern void CFRelease(IntPtr obj);
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);

    public void Click(TapButton button, double now)
    {
        if (button == TapButton.None || !OperatingSystem.IsMacOS())
            return;
        IntPtr probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero)
            return;
        var position = CGEventGetLocation(probe);
        CFRelease(probe);
        bool right = button == TapButton.Right;
        uint mouseButton = right ? 1u : 0u;
        int clicks = sequence.Next(button, now, new Vector2((float)position.X, (float)position.Y),
            DoubleClickInterval);
        IntPtr down = CGEventCreateMouseEvent(IntPtr.Zero,
            right ? 3u : 1u,
            position, mouseButton);
        IntPtr up = CGEventCreateMouseEvent(IntPtr.Zero,
            right ? 4u : 2u,
            position, mouseButton);
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            if (down == IntPtr.Zero || up == IntPtr.Zero)
                return;
            foreach (IntPtr ev in new[] { down, up })
            {
                CGEventSetIntegerValueField(ev, 1, clicks); // kCGMouseEventClickState
                CGEventSetFlags(ev, CGEventSourceFlagsState(0));
                CGEventPost(0, ev); // kCGHIDEventTap
            }
        }
        finally
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
            objc_autoreleasePoolPop(pool);
        }
    }

    private static double ReadDoubleClickInterval()
    {
        if (!OperatingSystem.IsMacOS()) return 0.5;
        double interval = SendDouble(objc_getClass("NSEvent"), sel_registerName("doubleClickInterval"));
        return interval > 0 ? interval : 0.5;
    }

    private static Point CurrentPosition()
    {
        IntPtr probe = CGEventCreate(IntPtr.Zero);
        if (probe == IntPtr.Zero) return default;
        Point position = CGEventGetLocation(probe);
        CFRelease(probe);
        return position;
    }

    private static void PostDragEvent(uint type, Point position)
    {
        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr ev = CGEventCreateMouseEvent(IntPtr.Zero, type, position, 0);
        try
        {
            if (ev == IntPtr.Zero) return;
            // Count one prevents the second contact from maximizing a title
            // bar or selecting a word before the user begins a normal drag.
            CGEventSetIntegerValueField(ev, 1, 1);
            CGEventSetFlags(ev, CGEventSourceFlagsState(0));
            CGEventPost(0, ev);
        }
        finally
        {
            if (ev != IntPtr.Zero) CFRelease(ev);
            objc_autoreleasePoolPop(pool);
        }
    }

    public void BeginDrag(double now)
    {
        if (dragging || !OperatingSystem.IsMacOS()) return;
        sequence.Reset();
        PostDragEvent(1, CurrentPosition()); // left mouse down
        dragging = true;
    }

    public void MoveDrag(Vector2 delta)
    {
        if (!dragging || !OperatingSystem.IsMacOS()) return;
        Point p = CurrentPosition();
        p.X += delta.X;
        p.Y += delta.Y;
        PostDragEvent(6, p); // left mouse dragged
    }

    public void EndDrag()
    {
        if (!dragging || !OperatingSystem.IsMacOS()) return;
        PostDragEvent(2, CurrentPosition()); // left mouse up
        dragging = false;
    }
}
