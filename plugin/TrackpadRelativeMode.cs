using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace PthTouch;

[PluginName("PTH Touch Relative Mode")]
[SupportedPlatform(PluginPlatform.MacOS)]
public sealed class TrackpadRelativeMode : RelativeOutputMode
{
    private readonly object sync = new();
    private readonly TouchMotion touch = new();
    private double penUntil;
    private Vector2 scrollRemainder;
    private int previousTouchCount;
    private bool reportedUnsupportedPointer;
    private bool reanchorPen;
    private Vector2? lastPenPosition;
    private bool touchDragging;
    private bool upperPenButton;
    private bool magnifying;
    private readonly HoverDeadband hoverDeadband = new();
    private readonly PenButtonTap penButtonTap = new();

    // Public for deterministic output tests; the host uses the native default.
    public ITouchClicker Clicker { get; set; } = new MacTouchClicker();

    [Resolved]
    public override IRelativePointer? Pointer { get; set; }

    [BooleanProperty("Upper pen button tap = middle click", ""), DefaultPropertyValue(false)]
    public bool UpperButtonTapMiddleClick { get; set; }

    [Property("Touch scroll sensitivity"), DefaultPropertyValue(12f), Unit("px/mm")]
    public float ScrollSensitivity { get; set; } = 12f;

    [BooleanProperty("Three-finger system swipes", ""), DefaultPropertyValue(false)]
    public bool ThreeFingerSwipes { get; set; }

    [BooleanProperty("Pinch zoom (experimental)", ""), DefaultPropertyValue(false)]
    public bool PinchZoom { get; set; }

    [BooleanProperty("Natural scrolling", ""), DefaultPropertyValue(true)]
    public bool NaturalScrolling { get; set; } = true;

    [BooleanProperty("Tap to click", ""), DefaultPropertyValue(true)]
    public bool TapToClick { get; set; } = true;

    [BooleanProperty("Tap and drag", ""), DefaultPropertyValue(true)]
    public bool TapAndDrag { get; set; } = true;

    [BooleanProperty("Limit unstable pen hover", ""), DefaultPropertyValue(true)]
    public bool LimitUnstableHover { get; set; } = true;

    [Property("Hover jitter deadband"), DefaultPropertyValue(0f), Unit("mm")]
    public float HoverJitterDeadband { get; set; }

    [BooleanProperty("Reject hover spikes", ""), DefaultPropertyValue(false)]
    public bool RejectHoverSpikes { get; set; }

    public override void Read(IDeviceReport deviceReport)
    {
        lock (sync)
        {
            double now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (deviceReport is ITouchReport report)
            {
                HandleTouch(report, now);
                if (deviceReport is IAuxReport aux && aux.AuxButtons.Length > 0)
                    base.Read(new AuxiliaryOnlyReport(aux.Raw, aux.AuxButtons));
                return;
            }

            if (deviceReport is OutOfRangeReport)
            {
                penUntil = 0;
                reanchorPen = true;
                lastPenPosition = null;
                upperPenButton = false;
                penButtonTap.Reset();
                hoverDeadband.Reset();
            }
            else if (deviceReport is ITabletReport pen)
            {
                EndMagnification();
                if (touchDragging)
                {
                    Clicker.EndDrag();
                    touchDragging = false;
                    touch.Reset();
                }
                penUntil = now + 0.15;
                bool upper = pen.PenButtons.Length > 1 && pen.PenButtons[1];
                bool scrollButtonChanged = upper != upperPenButton &&
                    Elements.Any(element => element.GetType().FullName == "ScrollBinding.ScrollBindingFilter");
                upperPenButton = upper;
                bool farHover = LimitUnstableHover && deviceReport is IProximityReport { NearProximity: false } &&
                    pen.Pressure == 0 && !pen.PenButtons.Any(x => x);
                if (farHover || reanchorPen || scrollButtonChanged)
                    hoverDeadband.Reset();
                var digitizer = Tablet?.Properties.Specifications.Digitizer;
                Vector2 mmPerUnit = new(
                    (digitizer?.Width ?? 325.12f) / (digitizer?.MaxX ?? 65024),
                    (digitizer?.Height ?? 203.2f) / (digitizer?.MaxY ?? 40640));
                bool middleClick = penButtonTap.Update(pen.Position, mmPerUnit, upper,
                    pen.PenButtons.Length > 0 && pen.PenButtons[0], pen.Pressure, now,
                    UpperButtonTapMiddleClick && Elements.Any(element =>
                        element.GetType().FullName == "ScrollBinding.ScrollBindingFilter"));
                pen.Position = hoverDeadband.Filter(pen.Position, mmPerUnit,
                    Math.Clamp(HoverJitterDeadband, 0, 1),
                    !farHover && pen.Pressure == 0 && !pen.PenButtons.Any(x => x),
                    RejectHoverSpikes);
                Vector2 position = pen.Position;
                if (farHover)
                    pen.Position = lastPenPosition ?? position;

                // Keep pressure/button releases flowing even when coordinates
                // are unreliable. Only the cursor displacement is suppressed.
                if ((!farHover && reanchorPen) || lastPenPosition is null)
                {
                    // Re-establish the origin after ignoring far-range noise,
                    // without turning the pen's displacement into cursor motion.
                    TimeSpan savedReset = ResetTime;
                    ResetTime = TimeSpan.Zero;
                    base.Read(deviceReport);
                    ResetTime = savedReset;
                }
                else
                    base.Read(deviceReport);

                if (middleClick)
                    Clicker.Click(TapButton.Middle, now);
                if (!farHover)
                    lastPenPosition = position;
                // The optional Pen Scroll binding runs after transformation.
                // Its freeze starts/ends on the following packet, so reanchor
                // that next packet rather than the button-transition packet.
                reanchorPen = farHover || scrollButtonChanged;
                return;
            }

            // Pen pressure, clicks, bindings and relative positioning stay in
            // OTD's original pipeline. Touch never creates a pen report.
            base.Read(deviceReport);
        }
    }

    private void HandleTouch(ITouchReport report, double now)
    {
        Span<Contact> contacts = stackalloc Contact[16];
        int count = 0;
        foreach (TouchPoint? point in report.Touches)
            if (point is not null && count < contacts.Length)
                contacts[count++] = new Contact(point.TouchID, point.Position);

        if (count != 2 || previousTouchCount != 2 || now < penUntil)
            scrollRemainder = Vector2.Zero;
        previousTouchCount = count;

        var specs = Tablet?.Properties.Specifications.Touch;
        Vector2 mmPerUnit = new(
            (specs?.Width ?? 325.12f) / (specs?.MaxX ?? 4095),
            (specs?.Height ?? 203.2f) / (specs?.MaxY ?? 4095));
        Motion action = touch.Update(contacts[..count], now, mmPerUnit,
            Sensitivity, Math.Clamp(ScrollSensitivity, 0.1f, 100f), now < penUntil,
            TapAndDrag && TapToClick, Clicker.DoubleClickInterval, ThreeFingerSwipes, PinchZoom);

        if (action.Magnify != MagnifyPhase.None)
        {
            Clicker.Magnify(action.Magnification, action.Magnify);
            magnifying = action.Magnify is MagnifyPhase.Began or MagnifyPhase.Changed;
        }
        if (action.Swipe != SwipeDirection.None)
            Clicker.Swipe(action.Swipe);

        if (Pointer is null)
            return;

        if (action.Drag == DragChange.End && touchDragging)
        {
            Clicker.EndDrag();
            touchDragging = false;
        }
        if (action.Drag == DragChange.Start)
        {
            Clicker.BeginDrag(now);
            touchDragging = true;
        }

        if (TapToClick && action.Tap != TapButton.None)
            Clicker.Click(action.Tap, now);

        if (action.Cursor != Vector2.Zero)
        {
            Vector2 delta = Vector2.Transform(action.Cursor,
                Matrix3x2.CreateRotation(-Rotation * MathF.PI / 180));
            if (touchDragging)
                Clicker.MoveDrag(delta);
            else
            {
                if (Pointer is IPressureHandler pressure)
                    pressure.SetPressure(0);
                Pointer.SetPosition(delta);
                (Pointer as ISynchronousPointer)?.Flush();
            }
        }

        if (action.Scroll == Vector2.Zero)
            return;

        if (Pointer is not IMouseScrollHandler wheel)
        {
            if (!reportedUnsupportedPointer)
                Log.Write("PTH Touch", "The current pointer cannot send scroll events.", LogLevel.Error);
            reportedUnsupportedPointer = true;
            return;
        }

        scrollRemainder += action.Scroll;
        int x = (int)scrollRemainder.X;
        int y = (int)scrollRemainder.Y;
        scrollRemainder -= new Vector2(x, y);
        int sign = NaturalScrolling ? -1 : 1;
        if (x != 0)
            wheel.ScrollHorizontally(sign * x);
        if (y != 0)
            wheel.ScrollVertically(sign * y);
        if (x != 0 || y != 0)
            (Pointer as ISynchronousPointer)?.Flush();
    }

    private void EndMagnification()
    {
        if (!magnifying) return;
        Clicker.Magnify(0, MagnifyPhase.Cancelled);
        magnifying = false;
        touch.Reset();
    }

    protected override void Dispose(bool disposing)
    {
        lock (sync)
        {
            EndMagnification();
            if (touchDragging)
                Clicker.EndDrag();
            touchDragging = false;
            base.Dispose(disposing);
        }
    }

    private sealed class AuxiliaryOnlyReport(byte[] raw, bool[] buttons) : IAuxReport
    {
        public byte[] Raw { get; set; } = raw;
        public bool[] AuxButtons { get; set; } = buttons;
    }
}
