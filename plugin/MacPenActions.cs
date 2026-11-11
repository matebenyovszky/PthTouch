using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PthTouch;

// Best-effort Accessibility selection/composer support. Never reads whole field
// values or clipboard contents, never sends a prompt, and never logs user text.
public sealed class MacPenActions
{
    private const string AX = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string CG = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    // The headless daemon need not have loaded AppKit before using these classes.
    private static readonly Lazy<IntPtr> AppKit = new(() =>
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"));
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
    [DllImport(AX)] private static extern int AXUIElementSetMessagingTimeout(IntPtr node, float timeout);
    [DllImport(AX)] private static extern int AXUIElementGetPid(IntPtr node, out int pid);
    [DllImport(AX)] private static extern int AXUIElementIsAttributeSettable(IntPtr node, IntPtr attribute, [MarshalAs(UnmanagedType.I1)] out bool value);
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
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPid(IntPtr obj, IntPtr sel, int pid);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendInteger(IntPtr obj, IntPtr sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr obj, IntPtr sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SetString(IntPtr obj, IntPtr sel, IntPtr text, IntPtr type);

    private static IntPtr Attribute(IntPtr node, string name)
    {
        if (node == IntPtr.Zero) return IntPtr.Zero;
        IntPtr key = CFStringCreateWithCString(IntPtr.Zero, name, 0x08000100);
        try { return AXUIElementCopyAttributeValue(node, key, out var result) == 0 ? result : IntPtr.Zero; }
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
    private static IntPtr Focus()
    {
        if (!AXIsProcessTrusted()) return IntPtr.Zero;
        var system = AXUIElementCreateSystemWide();
        try
        {
            AXUIElementSetMessagingTimeout(system, .03f);
            var node = Attribute(system, "AXFocusedUIElement");
            if (node != IntPtr.Zero) AXUIElementSetMessagingTimeout(node, .03f);
            return node;
        }
        finally { CFRelease(system); }
    }
    private static int Pid(IntPtr node) => node != IntPtr.Zero && AXUIElementGetPid(node, out int pid) == 0 ? pid : 0;
    private static bool Secure(IntPtr node) => AttributeText(node, "AXSubrole") == "AXSecureTextField";
    private static string? Selected(IntPtr node)
    {
        if (node == IntPtr.Zero || Secure(node)) return null;
        string? text = AttributeText(node, "AXSelectedText");
        return string.IsNullOrEmpty(text) ? null : text;
    }
    public string? CaptureSelection()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var node = Focus();
        try { return Selected(node); }
        finally { if (node != IntPtr.Zero) CFRelease(node); }
    }
    private static string? Bundle(int pid)
    {
        var app = SendPid(objc_getClass("NSRunningApplication"), sel_registerName("runningApplicationWithProcessIdentifier:"), pid);
        return app == IntPtr.Zero ? null : Text(Send(app, sel_registerName("bundleIdentifier")));
    }
    // A focused, editable text area is a deliberately narrow composer fallback.
    // Browsers additionally require an ancestor web area's expected URL host.
    private static bool Composer(IntPtr node, string? url)
    {
        if (Secure(node) || AttributeText(node, "AXRole") != "AXTextArea") return false;
        string label = string.Join(" ", new[] { "AXTitle", "AXDescription", "AXPlaceholderValue", "AXHelp", "AXIdentifier" }
            .Select(name => AttributeText(node, name)));
        if (!ComposerLabel(label)) return false;
        var key = CFStringCreateWithCString(IntPtr.Zero, "AXValue", 0x08000100);
        try { if (AXUIElementIsAttributeSettable(node, key, out bool writable) != 0 || !writable) return false; }
        finally { CFRelease(key); }
        if (url is null) return true;
        string host = new Uri(url).Host;
        var ancestor = Attribute(node, "AXParent");
        try
        {
            for (int depth = 0; ancestor != IntPtr.Zero && depth < 16; depth++)
            {
                if (AttributeText(ancestor, "AXRole") == "AXWebArea")
                {
                    var raw = Attribute(ancestor, "AXURL");
                    try
                    {
                        // AXURL is a CFURL, not a CFString.
                        var text = raw == IntPtr.Zero || CFGetTypeID(raw) != CFURLGetTypeID()
                            ? null : Text(CFURLGetString(raw));
                        return Uri.TryCreate(text, UriKind.Absolute, out var page) &&
                            page.Scheme == "https" && page.Host.Equals(host, StringComparison.OrdinalIgnoreCase);
                    }
                    finally { if (raw != IntPtr.Zero) CFRelease(raw); }
                }
                var parent = Attribute(ancestor, "AXParent");
                CFRelease(ancestor); ancestor = parent;
            }
            return false;
        }
        finally { if (ancestor != IntPtr.Zero) CFRelease(ancestor); }
    }
    [DllImport(CF)] private static extern IntPtr CFURLGetString(IntPtr url);
    [DllImport(CF)] private static extern nuint CFURLGetTypeID();
    public static bool ComposerLabel(string label)
    {
        label = label.ToLowerInvariant();
        return new[] { "message", "prompt", "ask anything", "reply", "üzenet", "kérdezz" }.Any(label.Contains);
    }
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
    public bool Perform(PenGestureAction action, string target, string? selection, bool paste)
    {
        if (!OperatingSystem.IsMacOS() || action == PenGestureAction.None) return false;
        _ = AppKit.Value;
        var destination = ResolveTarget(target);
        if (action == PenGestureAction.OpenAI && destination == default) return false;
        var source = Focus(); int sourcePid;
        try { sourcePid = Pid(source); }
        finally { if (source != IntPtr.Zero) CFRelease(source); }
        if (sourcePid == 0) return false;
        int ticket; lock (gate) ticket = ++generation;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(60); // Allow the target app to process mouse-up.
                lock (gate)
                {
                    if (ticket != generation) return;
                    var focused = Focus();
                    try
                    {
                        if (Pid(focused) != sourcePid) return;
                        if (action == PenGestureAction.DeleteSelection)
                        {
                            if (Selected(focused) is not null) Shortcut(51, false); // Backspace, only with an actual selection.
                            return;
                        }
                        if (action == PenGestureAction.Undo) { Shortcut(6, true); return; }
                    }
                    finally { if (focused != IntPtr.Zero) CFRelease(focused); }
                }
                nint clipboardVersion = -1;
                lock (gate)
                {
                    if (ticket != generation) return;
                    var pool = objc_autoreleasePoolPush();
                    try
                    {
                        if (paste && !string.IsNullOrEmpty(selection))
                        {
                            var board = Send(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
                            var text = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), selection);
                            var type = SendString(objc_getClass("NSString"), sel_registerName("stringWithUTF8String:"), "public.utf8-plain-text");
                            SendInteger(board, sel_registerName("clearContents"));
                            if (SetString(board, sel_registerName("setString:forType:"), text, type))
                                clipboardVersion = SendInteger(board, sel_registerName("changeCount"));
                        }
                        using var process = new Process { StartInfo = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false } };
                        if (destination.Bundle is string bundle) { process.StartInfo.ArgumentList.Add("-b"); process.StartInfo.ArgumentList.Add(bundle); }
                        else process.StartInfo.ArgumentList.Add(destination.Url!);
                        process.Start();
                    }
                    finally { objc_autoreleasePoolPop(pool); }
                }
                if (clipboardVersion < 0) return;
                int acquiredPid = 0;
                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(120);
                    lock (gate)
                    {
                        if (ticket != generation) return;
                        var pool = objc_autoreleasePoolPush();
                        var node = Focus();
                        try
                        {
                            var board = Send(objc_getClass("NSPasteboard"), sel_registerName("generalPasteboard"));
                            if (SendInteger(board, sel_registerName("changeCount")) != clipboardVersion) return;
                            int pid = Pid(node);
                            bool matches = destination.Bundle is string bundle
                                ? Bundle(pid) == bundle
                                : Composer(node, destination.Url);
                            if (!matches) { if (acquiredPid != 0) return; continue; }
                            if (acquiredPid != 0 && pid != acquiredPid) return;
                            acquiredPid = pid;
                            if (!Composer(node, destination.Url)) continue;
                            Shortcut(9, true); // Paste only; no Return/Send.
                            return;
                        }
                        finally { if (node != IntPtr.Zero) CFRelease(node); objc_autoreleasePoolPop(pool); }
                    }
                }
                // Unsupported/unfocused composer: selection stays on the clipboard for manual paste.
            }
            catch (Exception ex)
            {
                OpenTabletDriver.Plugin.Log.Write("PTH Gestures", $"Pen action unavailable ({ex.GetType().Name}).", OpenTabletDriver.Plugin.LogLevel.Warning);
            }
        });
        return true;
    }
}
