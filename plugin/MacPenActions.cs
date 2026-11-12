using System.Runtime.InteropServices;

namespace PthTouch;

// Native keyboard shortcuts. Eraser Delete deliberately has
// ordinary keyboard behavior, without reading app fields or selection.
public sealed class MacPenActions
{
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    // The headless daemon need not have loaded AppKit before using these classes.
    private static readonly Lazy<IntPtr> AppKit = new(() =>
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
    private readonly object gate = new();
    private int generation;
    public static SwipeShortcut ShortcutFor(PenGestureAction action) => action switch
    {
        PenGestureAction.Delete => new(51, 0),
        PenGestureAction.DesktopLeft => SwipeShortcut.For(SwipeDirection.Left),
        PenGestureAction.DesktopRight => SwipeShortcut.For(SwipeDirection.Right),
        _ => default
    };
    [DllImport(CF)] private static extern void CFRelease(IntPtr value);
    [DllImport(CG)] private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort key, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport(CG)] private static extern void CGEventSetFlags(IntPtr ev, ulong flags);
    [DllImport(CG)] private static extern void CGEventPost(uint tap, IntPtr ev);
    [DllImport(ObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(ObjC)] private static extern void objc_autoreleasePoolPop(IntPtr pool);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendInteger(IntPtr obj, IntPtr sel);

    private static int FrontmostPid()
    {
        var pool = objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(objc_getClass("NSWorkspace"), sel_registerName("sharedWorkspace"));
            var app = Send(workspace, sel_registerName("frontmostApplication"));
            return (int)SendInteger(app, sel_registerName("processIdentifier"));
        }
        finally { objc_autoreleasePoolPop(pool); }
    }
    private static void Diagnostic(string message) =>
        OpenTabletDriver.Plugin.Log.Write("PTH Gestures", message, OpenTabletDriver.Plugin.LogLevel.Debug);
    private static bool Shortcut(ushort key, ulong flags)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, key, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, key, false);
        try
        {
            if (down == IntPtr.Zero || up == IntPtr.Zero) return false;
            CGEventSetFlags(down, flags); CGEventSetFlags(up, flags);
            CGEventPost(0, down); CGEventPost(0, up); return true;
        }
        finally { if (down != IntPtr.Zero) CFRelease(down); if (up != IntPtr.Zero) CFRelease(up); }
    }
    public void Cancel() { lock (gate) generation++; }
    public bool Perform(PenGestureAction action)
    {
        if (!OperatingSystem.IsMacOS() || action == PenGestureAction.None) return false;
        _ = AppKit.Value;
        int sourcePid = FrontmostPid();
        if (sourcePid == 0) { Diagnostic("No foreground application; action skipped."); return false; }
        int ticket; lock (gate) ticket = ++generation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60); // Allow the target app to process mouse-up.
                lock (gate)
                {
                    if (ticket != generation) return;
                    if (FrontmostPid() != sourcePid) { Diagnostic("Foreground app changed; action cancelled."); return; }
                    var shortcut = ShortcutFor(action);
                    Diagnostic(Shortcut(shortcut.Key, shortcut.Flags) ? $"{action} shortcut sent." : $"Could not construct {action} event.");
                }
            }
            catch (Exception ex)
            {
                OpenTabletDriver.Plugin.Log.Write("PTH Gestures", $"Pen action unavailable ({ex.GetType().Name}).", OpenTabletDriver.Plugin.LogLevel.Warning);
            }
        });
        return true;
    }
}
