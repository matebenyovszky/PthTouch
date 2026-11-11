using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PthTouch;

// Undo and AI opening do not depend on Accessibility text inspection.
// Only eraser deletion checks selection; no text is transferred or logged.
public sealed class MacPenActions
{
    private const string AX = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    // The headless daemon need not have loaded AppKit before using these classes.
    private static readonly Lazy<IntPtr> AppKit = new(() =>
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
    private static readonly Lazy<IntPtr> CoreFoundation = new(() => NativeLibrary.Load(CF));
    private readonly object gate = new();
    private int generation;
    public static string[] Targets => ["ChatGPT/Codex", "ChatGPT", "Claude", "ChatGPT in browser", "Claude in browser"];
    public static (string? Bundle, string? Url) ResolveTarget(string target) => target switch
    {
        "ChatGPT/Codex" => ("com.openai.codex", null),
        "ChatGPT" => ("com.openai.chat", null),
        "Claude" => ("com.anthropic.claudefordesktop", null),
        "ChatGPT in browser" => (null, "https://chatgpt.com/"),
        "Claude in browser" => (null, "https://claude.ai/new"),
        _ => default
    };
    [DllImport(AX)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXIsProcessTrusted();
    [DllImport(AX)] private static extern IntPtr AXUIElementCreateSystemWide();
    [DllImport(AX)] private static extern int AXUIElementCopyAttributeValue(IntPtr node, IntPtr attribute, out IntPtr value);
    [DllImport(AX)] private static extern int AXUIElementSetAttributeValue(IntPtr node, IntPtr attribute, IntPtr value);
    [DllImport(AX)] private static extern int AXUIElementIsAttributeSettable(IntPtr node, IntPtr attribute, [MarshalAs(UnmanagedType.I1)] out bool settable);
    [DllImport(AX)] private static extern int AXUIElementSetMessagingTimeout(IntPtr node, float timeout);
    [DllImport(AX)] private static extern int AXUIElementGetPid(IntPtr node, out int pid);
    [DllImport(AX)] private static extern IntPtr AXUIElementCreateApplication(int pid);
    [DllImport(AX)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool AXValueGetValue(IntPtr value, uint type, out Range range);
    [DllImport(AX)] private static extern nuint AXValueGetTypeID();
    [StructLayout(LayoutKind.Sequential)] private struct Range { public nint Location, Length; }
    [DllImport(CF)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
    [DllImport(CF)] private static extern nint CFStringGetLength(IntPtr value);
    [DllImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint size, uint encoding);
    [DllImport(CF)] private static extern nuint CFGetTypeID(IntPtr value);
    [DllImport(CF)] private static extern nuint CFStringGetTypeID();
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

    private static IntPtr Attribute(IntPtr node, string name) => Attribute(node, name, out _);
    private static IntPtr Attribute(IntPtr node, string name, out int error)
    {
        if (node == IntPtr.Zero) { error = -25202; return IntPtr.Zero; }
        IntPtr key = CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
        try { error = AXUIElementCopyAttributeValue(node, key, out var result); return error == 0 ? result : IntPtr.Zero; }
        finally { CFRelease(key); }
    }
    private static string? Text(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID()) return null;
        nint length = CFStringGetLength(value);
        if (length < 0 || length > 250000) return null;
        var bytes = new byte[(int)length * 4 + 1];
        if (!CFStringGetCString(value, bytes, bytes.Length, 0x08000100)) return null;
        return System.Text.Encoding.UTF8.GetString(bytes, 0, Array.IndexOf(bytes, (byte)0));
    }
    private static string? AttributeText(IntPtr node, string name)
    {
        var value = Attribute(node, name);
        try { return Text(value); }
        finally { if (value != IntPtr.Zero) CFRelease(value); }
    }
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
    private static int Pid(IntPtr node) => node != IntPtr.Zero && AXUIElementGetPid(node, out int pid) == 0 ? pid : 0;
    private static IntPtr Focus(int pid)
    {
        // The focused UI element on the system-wide object is not available in
        // every app. Query the known foreground application's focus first.
        var app = AXUIElementCreateApplication(pid);
        var system = AXUIElementCreateSystemWide();
        try
        {
            AXUIElementSetMessagingTimeout(app, .10f);
            AXUIElementSetMessagingTimeout(system, .10f);
            var node = Attribute(app, "AXFocusedUIElement", out int appError);
            int systemError = 0;
            if (node == IntPtr.Zero) node = Attribute(system, "AXFocusedUIElement", out systemError);
            if (Pid(node) != pid)
            {
                Diagnostic($"App focus unresolved: app AX error={appError}, system AX error={systemError}, element present={node != IntPtr.Zero}.");
                if (node != IntPtr.Zero) CFRelease(node);
                return IntPtr.Zero;
            }
            AXUIElementSetMessagingTimeout(node, .10f);
            return node;
        }
        finally { CFRelease(app); CFRelease(system); }
    }
    private static bool HasSelection(IntPtr node)
    {
        if (AttributeText(node, "AXSubrole") == "AXSecureTextField") return false;
        var range = Attribute(node, "AXSelectedTextRange");
        try
        {
            if (range != IntPtr.Zero && CFGetTypeID(range) == AXValueGetTypeID() &&
                AXValueGetValue(range, 4, out var selected)) return selected.Location >= 0 && selected.Length > 0;
        }
        finally { if (range != IntPtr.Zero) CFRelease(range); }
        // Web/rich-text views may expose only selected text, not its range.
        var text = Attribute(node, "AXSelectedText");
        try { return text != IntPtr.Zero && CFGetTypeID(text) == CFStringGetTypeID() && CFStringGetLength(text) > 0; }
        finally { if (text != IntPtr.Zero) CFRelease(text); }
    }
    private static bool Editable(IntPtr node)
    {
        var attribute = CFStringCreateWithCString(IntPtr.Zero, "AXValue", 0x08000100);
        try { return AXUIElementIsAttributeSettable(node, attribute, out bool editable) == 0 && editable; }
        finally { CFRelease(attribute); }
    }
    private static bool RequestAccessibility(int pid)
    {
        // Documented Electron integration, scoped to the foreground application:
        // https://www.electronjs.org/docs/latest/tutorial/accessibility
        // This does not grant or change macOS Accessibility permissions.
        var app = AXUIElementCreateApplication(pid);
        var attribute = CFStringCreateWithCString(IntPtr.Zero, "AXManualAccessibility", 0x08000100);
        try
        {
            AXUIElementSetMessagingTimeout(app, .10f);
            var enabled = Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreFoundation.Value, "kCFBooleanTrue"));
            int result = AXUIElementSetAttributeValue(app, attribute, enabled);
            Diagnostic($"App Accessibility tree request: AX result={result}.");
            return result == 0;
        }
        finally { CFRelease(attribute); CFRelease(app); }
    }
    private EraserDeleteResult TryDelete(int ticket, int sourcePid)
    {
        lock (gate)
        {
            if (ticket != generation || FrontmostPid() != sourcePid) return EraserDeleteResult.Cancelled;
            if (!AXIsProcessTrusted()) return EraserDeleteResult.NoAccess;
            var focused = Focus(sourcePid);
            try
            {
                if (focused == IntPtr.Zero) return EraserDeleteResult.NoFocus;
                if (!HasSelection(focused)) return EraserDeleteResult.NoSelection;
                if (!Editable(focused)) return EraserDeleteResult.NotEditable;
                if (FrontmostPid() != sourcePid) return EraserDeleteResult.Cancelled;
                return Shortcut(51, false) ? EraserDeleteResult.Deleted : EraserDeleteResult.EventUnavailable;
            }
            finally { if (focused != IntPtr.Zero) CFRelease(focused); }
        }
    }
    private static void Diagnostic(string message) =>
        OpenTabletDriver.Plugin.Log.Write("PTH Gestures", message, OpenTabletDriver.Plugin.LogLevel.Debug);
    private static bool Shortcut(ushort key, bool command)
    {
        var down = CGEventCreateKeyboardEvent(IntPtr.Zero, key, true);
        var up = CGEventCreateKeyboardEvent(IntPtr.Zero, key, false);
        try
        {
            if (down == IntPtr.Zero || up == IntPtr.Zero) return false;
            CGEventSetFlags(down, command ? 1UL << 20 : 0); CGEventSetFlags(up, command ? 1UL << 20 : 0);
            CGEventPost(0, down); CGEventPost(0, up); return true;
        }
        finally { if (down != IntPtr.Zero) CFRelease(down); if (up != IntPtr.Zero) CFRelease(up); }
    }
    public void Cancel() { lock (gate) generation++; }
    public bool Perform(PenGestureAction action, string target)
    {
        if (!OperatingSystem.IsMacOS() || action == PenGestureAction.None) return false;
        _ = AppKit.Value;
        var destination = ResolveTarget(target);
        if (action == PenGestureAction.OpenAI && destination == default) return false;
        int sourcePid = FrontmostPid();
        if (sourcePid == 0) { Diagnostic("No foreground application; action skipped."); return false; }
        int ticket; lock (gate) ticket = ++generation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60); // Allow the target app to process mouse-up.
                if (action == PenGestureAction.DeleteSelection)
                {
                    var result = await EraserDeleteRetry.RunAsync(
                        () => TryDelete(ticket, sourcePid),
                        () => { lock (gate) return ticket == generation && FrontmostPid() == sourcePid && RequestAccessibility(sourcePid); },
                        () => Task.Delay(100));
                    Diagnostic($"Eraser delete result: {result}.");
                    return;
                }
                lock (gate)
                {
                    if (ticket != generation) return;
                    if (FrontmostPid() != sourcePid) { Diagnostic("Foreground app changed; action cancelled."); return; }
                    if (action == PenGestureAction.Undo)
                    {
                        Diagnostic(Shortcut(6, true) ? "Undo sent." : "Could not construct Undo event.");
                        return;
                    }
                    using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false } };
                    if (destination.Bundle is string bundle) { process.StartInfo.ArgumentList.Add("-b"); process.StartInfo.ArgumentList.Add(bundle); }
                    else process.StartInfo.ArgumentList.Add(destination.Url!);
                    process.Start();
                    Diagnostic("AI target open requested.");
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
