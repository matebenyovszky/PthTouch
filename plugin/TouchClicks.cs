using System.Numerics;
using System.Runtime.InteropServices;

namespace PthTouch;

public readonly record struct SwipeShortcut(ushort Key, ulong Flags)
{
    public static SwipeShortcut For(SwipeDirection direction) => new(direction switch
    {
        SwipeDirection.Left => 124, SwipeDirection.Right => 123,
        SwipeDirection.Up => 126, SwipeDirection.Down => 125, _ => 0
    }, (1UL << 18) | (1UL << 23)); // Control + function-key classification for arrows
}

public interface ITouchClicker
{
    bool PenAction(PenGestureAction action, string target) => false;
    void CancelPenActions() { }
    bool BeginDictation(bool macParakeet) => false;
    void EndDictation() { }
    bool ClipboardShortcut(PressureClipboardAction action) => false;
    void Click(TapButton button, double now);
    double DoubleClickInterval => 0.5;
    void BeginDrag(double now);
    void MoveDrag(Vector2 delta);
    void EndDrag();
    void Swipe(SwipeDirection direction) { }
    void Magnify(float amount, MagnifyPhase phase) { }
    void DesktopSwipe(DesktopGesture gesture) { }
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
    private readonly MacPenActions penActions = new();
    public bool PenAction(PenGestureAction action, string target) =>
        penActions.Perform(action, target);
    public void CancelPenActions() => penActions.Cancel();
    private readonly ClickSequence sequence = new();
    private bool dragging;
    private bool dictationFnHeld;
    private double? doubleClickInterval;

    public double DoubleClickInterval => doubleClickInterval ??= ReadDoubleClickInterval();
    private const string Quartz = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public double X, Y; }

    [DllImport(Quartz)] private static extern IntPtr CGEventCreate(IntPtr source);
    [DllImport(Quartz)] private static extern Point CGEventGetLocation(IntPtr ev);
    [DllImport(Quartz)] private static extern void CGEventSetType(IntPtr ev, uint type);
    [DllImport(Quartz)] private static extern void CGEventSetLocation(IntPtr ev, Point position);
    [DllImport(Quartz)] private static extern void CGEventSetDoubleValueField(IntPtr ev, uint field, double value);
    [DllImport(Quartz)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
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

    public bool BeginDictation(bool macParakeet)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        if (macParakeet)
        {
            if (dictationFnHeld) return true;
            dictationFnHeld = SendFn(true);
            return dictationFnHeld;
        }
        IntPtr down = CGEventCreateKeyboardEvent(IntPtr.Zero, 2, true);
        IntPtr up = CGEventCreateKeyboardEvent(IntPtr.Zero, 2, false);
        try
        {
            if (down == IntPtr.Zero || up == IntPtr.Zero) return false;
            CGEventSetFlags(down, 1UL << 23); CGEventSetFlags(up, 1UL << 23); // macOS Fn+D toggle
            CGEventPost(0, down); CGEventPost(0, up);
            return true;
        }
        finally
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
        }
    }

    public void EndDictation()
    {
        if (!dictationFnHeld) return;
        SendFn(false);
        dictationFnHeld = false;
    }

    private static bool SendFn(bool down)
    {
        IntPtr ev = CGEventCreateKeyboardEvent(IntPtr.Zero, 63, down);
        if (ev == IntPtr.Zero) return false;
        try
        {
            CGEventSetType(ev, 12); // flagsChanged
            CGEventSetFlags(ev, down ? 1UL << 23 : 0);
            CGEventPost(0, ev);
            return true;
        }
        finally { CFRelease(ev); }
    }

    public bool ClipboardShortcut(PressureClipboardAction action)
    {
        if (!OperatingSystem.IsMacOS() || action == PressureClipboardAction.None) return false;
        ushort key = action == PressureClipboardAction.Copy ? (ushort)8 : (ushort)9; // C / V
        IntPtr down = CGEventCreateKeyboardEvent(IntPtr.Zero, key, true);
        IntPtr up = CGEventCreateKeyboardEvent(IntPtr.Zero, key, false);
        try
        {
            if (down == IntPtr.Zero || up == IntPtr.Zero) return false;
            foreach (var ev in new[] { down, up })
            {
                CGEventSetFlags(ev, 1UL << 20); // Command only
                CGEventPost(0, ev);
            }
            return true;
        }
        finally
        {
            if (down != IntPtr.Zero) CFRelease(down);
            if (up != IntPtr.Zero) CFRelease(up);
        }
    }

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
        bool middle = button == TapButton.Middle;
        uint mouseButton = middle ? 2u : right ? 1u : 0u;
        int clicks = sequence.Next(button, now, new Vector2((float)position.X, (float)position.Y),
            DoubleClickInterval);
        IntPtr down = CGEventCreateMouseEvent(IntPtr.Zero,
            middle ? 25u : right ? 3u : 1u,
            position, mouseButton);
        IntPtr up = CGEventCreateMouseEvent(IntPtr.Zero,
            middle ? 26u : right ? 4u : 2u,
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

    public void Swipe(SwipeDirection direction)
    {
        if (!OperatingSystem.IsMacOS() || direction == SwipeDirection.None) return;
        // Match trackpad content motion: fingers left reveal the Space to
        // the right. Keyboard shortcuts give discrete, reliable transitions.
        var shortcut = SwipeShortcut.For(direction);
        IntPtr pool = objc_autoreleasePoolPush();
        try
        {
            foreach (bool down in new[] { true, false })
            {
                IntPtr ev = CGEventCreateKeyboardEvent(IntPtr.Zero, shortcut.Key, down);
                if (ev == IntPtr.Zero) continue;
                try
                {
                    // Arrow-key symbolic shortcuts include the function-key bit.
                    // Omitting it produces valid key events that macOS ignores
                    // for Control+Arrow Mission Control / Space actions.
                    CGEventSetFlags(ev, shortcut.Flags);
                    CGEventPost(0, ev);
                }
                finally { CFRelease(ev); }
            }
        }
        finally { objc_autoreleasePoolPop(pool); }
    }

    public static bool SupportsInteractiveDesktop => OperatingSystem.IsMacOSVersionAtLeast(13) &&
        !OperatingSystem.IsMacOSVersionAtLeast(27);

    public void DesktopSwipe(DesktopGesture gesture)
    {
        if (!SupportsInteractiveDesktop || gesture.Phase == DesktopSwipePhase.None ||
            !float.IsFinite(gesture.Progress) || !float.IsFinite(gesture.Velocity)) return;
        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr ev = CGEventCreate(IntPtr.Zero);
        IntPtr companion = CGEventCreate(IntPtr.Zero);
        try
        {
            if (ev == IntPtr.Zero || companion == IntPtr.Zero) return;
            // Native pre-macOS-27 DockControl protocol. Field definitions
            // cross-checked against InstantSpaceSwitcher (MIT) and IOHIDFamily.
            CGEventSetType(ev, 30);
            CGEventSetIntegerValueField(ev, 110, 23); // Dock swipe HID subtype
            CGEventSetIntegerValueField(ev, 123, 1); // horizontal motion
            CGEventSetIntegerValueField(ev, 132, (long)gesture.Phase);
            CGEventSetDoubleValueField(ev, 124, gesture.Progress);
            CGEventSetDoubleValueField(ev, 129,
                gesture.Phase == DesktopSwipePhase.Ended ? gesture.Velocity : 0);
            CGEventSetDoubleValueField(ev, 130, 0);
            CGEventSetLocation(ev, CurrentPosition());
            CGEventSetFlags(ev, CGEventSourceFlagsState(0));
            CGEventPost(1, ev); // session event tap used by native Dock gestures
            CGEventSetType(companion, 29); // companion generic gesture envelope
            CGEventSetLocation(companion, CurrentPosition());
            CGEventSetFlags(companion, CGEventSourceFlagsState(0));
            CGEventPost(1, companion);
        }
        finally
        {
            if (ev != IntPtr.Zero) CFRelease(ev);
            if (companion != IntPtr.Zero) CFRelease(companion);
            objc_autoreleasePoolPop(pool);
        }
    }

    public void Magnify(float amount, MagnifyPhase phase)
    {
        if (!OperatingSystem.IsMacOS() || phase == MagnifyPhase.None || !float.IsFinite(amount)) return;
        // macOS has no documented global magnify-event constructor. These
        // protocol fields are opt-in and tested by converting a constructed
        // CGEvent to NSEvent on macOS 26, without posting a test UI event.
        if (phase == MagnifyPhase.Began)
        {
            PostMagnification(0, MagnifyPhase.Began);
            if (amount != 0) PostMagnification(amount, MagnifyPhase.Changed);
        }
        else
            PostMagnification(amount, phase);
    }

    private static void PostMagnification(float amount, MagnifyPhase phase)
    {
        IntPtr pool = objc_autoreleasePoolPush();
        IntPtr ev = CGEventCreate(IntPtr.Zero);
        try
        {
            if (ev == IntPtr.Zero) return;
            CGEventSetType(ev, 29); // generic gesture envelope
            CGEventSetIntegerValueField(ev, 110, 8); // zoom subtype
            CGEventSetIntegerValueField(ev, 132, (long)phase);
            CGEventSetDoubleValueField(ev, 113, amount);
            CGEventSetLocation(ev, CurrentPosition());
            CGEventSetFlags(ev, CGEventSourceFlagsState(0));
            CGEventPost(0, ev);
        }
        finally
        {
            if (ev != IntPtr.Zero) CFRelease(ev);
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
