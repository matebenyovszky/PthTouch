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
    private bool suppressNextPenDelta;
    private Vector2? lastPenPosition;
    private bool touchDragging;
    private bool upperPenButton;
    private bool magnifying;
    private DesktopGesture? desktopGesture;
    public bool InteractiveDesktopSupported { get; set; } = MacTouchClicker.SupportsInteractiveDesktop;
    private readonly HoverDeadband hoverDeadband = new();
    private readonly PenButtonTap penButtonTap = new();
    private readonly HoverDeadband scrollDeadband = new();
    private readonly PenJoystickScroll joystick = new();
    private System.Threading.Timer? joystickTimer;
    private Vector2? joystickAnchor;
    private bool disposed;
    private readonly PointerAcceleration penAcceleration = new(), touchAcceleration = new();
    public bool EnableJoystickTimer { get; set; } = true;

    // Public for deterministic output tests; the host uses the native default.
    public ITouchClicker Clicker { get; set; } = new MacTouchClicker();

    [Resolved]
    public override IRelativePointer? Pointer { get; set; }

    [BooleanProperty("Upper pen button tap = middle click", ""), DefaultPropertyValue(false)]
    public bool UpperButtonTapMiddleClick { get; set; }

    [BooleanProperty("Filter all pen movement", ""), DefaultPropertyValue(false)]
    public bool FilterAllPenMovement { get; set; }

    [BooleanProperty("Filter pen-scroll noise", ""), DefaultPropertyValue(false)]
    public bool FilterPenScrollNoise { get; set; }

    [BooleanProperty("Joystick pen scrolling", ""), DefaultPropertyValue(false)]
    public bool PenJoystickScrolling { get; set; }

    [Property("Joystick horizontal reach"), DefaultPropertyValue(20f), Unit("mm")]
    public float JoystickHalfWidthMm { get; set; } = 20;

    [Property("Joystick vertical reach"), DefaultPropertyValue(20f), Unit("mm")]
    public float JoystickHalfHeightMm { get; set; } = 20;

    [Property("Joystick maximum horizontal speed"), DefaultPropertyValue(600f), Unit("px/s")]
    public float JoystickMaxSpeedX { get; set; } = 600;

    [Property("Joystick maximum vertical speed"), DefaultPropertyValue(1200f), Unit("px/s")]
    public float JoystickMaxSpeedY { get; set; } = 1200;

    [Property("Joystick centre deadzone"), DefaultPropertyValue(1f), Unit("mm")]
    public float JoystickDeadzoneMm { get; set; } = 1;

    [Property("Joystick acceleration curve"), DefaultPropertyValue(2f)]
    public float JoystickExponent { get; set; } = 2;

    [Property("Joystick outer-edge speed boost"), DefaultPropertyValue(1f)]
    public float JoystickEdgeBoost { get; set; } = 1;

    [BooleanProperty("Velocity-based pen sensitivity", ""), DefaultPropertyValue(false)]
    public bool PenVelocityAcceleration { get; set; }

    [BooleanProperty("Velocity-based touch sensitivity", ""), DefaultPropertyValue(false)]
    public bool TouchVelocityAcceleration { get; set; }

    [Property("Slow movement sensitivity multiplier"), DefaultPropertyValue(.9f)]
    public float AccelerationSlowGain { get; set; } = .9f;

    [Property("Fast movement sensitivity multiplier"), DefaultPropertyValue(1.35f)]
    public float AccelerationFastGain { get; set; } = 1.35f;

    [Property("Full acceleration speed"), DefaultPropertyValue(150f), Unit("mm/s")]
    public float AccelerationTransitionSpeed { get; set; } = 150;

    [BooleanProperty("Reverse joystick scrolling", ""), DefaultPropertyValue(false)]
    public bool JoystickInvertScroll { get; set; }

    [Property("Touch scroll sensitivity"), DefaultPropertyValue(12f), Unit("px/mm")]
    public float ScrollSensitivity { get; set; } = 12f;

    [BooleanProperty("Three-finger system swipes", ""), DefaultPropertyValue(false)]
    public bool ThreeFingerSwipes { get; set; }

    [BooleanProperty("Interactive desktop swipes (experimental)", ""), DefaultPropertyValue(false)]
    public bool InteractiveDesktopSwipes { get; set; }

    [Property("Desktop swipe travel"), DefaultPropertyValue(60f), Unit("mm")]
    public float DesktopSwipeDistanceMm { get; set; } = 60;

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
            if (disposed) return;
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
                scrollDeadband.Reset();
                StopJoystick();
                penAcceleration.Reset();
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
                bool upperChanged = upper != upperPenButton;
                bool hasScrollFilter = Elements.Any(element => element.GetType().FullName == "ScrollBinding.ScrollBindingFilter");
                bool scrollButtonChanged = upperChanged && (hasScrollFilter || PenJoystickScrolling);
                if (upperChanged) scrollDeadband.Reset();
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
                    UpperButtonTapMiddleClick && (hasScrollFilter || PenJoystickScrolling));
                if (!FilterAllPenMovement && FilterPenScrollNoise && upper && (hasScrollFilter || PenJoystickScrolling))
                    pen.Position = scrollDeadband.Filter(pen.Position, mmPerUnit,
                        Math.Clamp(HoverJitterDeadband, 0, 1), true, RejectHoverSpikes);
                pen.Position = hoverDeadband.Filter(pen.Position, mmPerUnit,
                    Math.Clamp(HoverJitterDeadband, 0, 1),
                    !farHover && (FilterAllPenMovement || (pen.Pressure == 0 && !pen.PenButtons.Any(x => x))),
                    RejectHoverSpikes);
                Vector2 position = pen.Position;
                if (farHover)
                    pen.Position = lastPenPosition ?? position;

                if (PenJoystickScrolling)
                {
                    bool valid = deviceReport is not IProximityReport { NearProximity: false };
                    if (upper && valid)
                    {
                        joystickAnchor ??= lastPenPosition ?? position;
                        bool wasActive = joystick.Active;
                        joystick.Update(position * mmPerUnit, true, now,
                            new(JoystickHalfWidthMm, JoystickHalfHeightMm),
                            new(JoystickMaxSpeedX, JoystickMaxSpeedY), JoystickDeadzoneMm,
                            JoystickExponent, JoystickInvertScroll, JoystickEdgeBoost);
                        if (EnableJoystickTimer)
                        {
                            joystickTimer ??= new System.Threading.Timer(EmitJoystickScroll, null,
                                System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                            if (!wasActive) joystickTimer.Change(8, 8);
                        }
                        pen.Position = joystickAnchor.Value;
                        pen.Pressure = 0;
                        position = pen.Position;
                    }
                    else
                        StopJoystick();
                    // Own joystick handling consumes this button so an existing
                    // Drag Scroll binding cannot produce duplicate wheel events.
                    if (pen.PenButtons.Length > 1) pen.PenButtons[1] = false;
                }

                // Keep pressure/button releases flowing even when coordinates
                // are unreliable. Only the cursor displacement is suppressed.
                if ((!farHover && reanchorPen) || lastPenPosition is null || (PenJoystickScrolling && upperChanged))
                {
                    // Re-establish the origin after ignoring far-range noise,
                    // without turning the pen's displacement into cursor motion.
                    suppressNextPenDelta = true;
                    base.Read(deviceReport);
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
                reanchorPen = farHover || (!PenJoystickScrolling && scrollButtonChanged);
                return;
            }

            // Pen pressure, clicks, bindings and relative positioning stay in
            // OTD's original pipeline. Touch never creates a pen report.
            base.Read(deviceReport);
        }
    }

    protected override IAbsolutePositionReport Transform(IAbsolutePositionReport report)
    {
        // Let the base mode advance its origin, then discard just this delta.
        // Never temporarily change ResetTime: the host can configure it while
        // the new mode is already receiving reports during SetSettings.
        var transformed = base.Transform(report);
        if (suppressNextPenDelta)
        {
            transformed.Position = Vector2.Zero;
            suppressNextPenDelta = false;
            penAcceleration.Reset();
        }
        else
        {
            bool eligible = !upperPenButton && report is ITabletReport pen &&
                pen.Pressure == 0 && !pen.PenButtons.Any(x => x);
            transformed.Position = penAcceleration.Apply(transformed.Position,
                PhysicalDelta(transformed.Position), Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
                PenVelocityAcceleration && eligible, AccelerationSlowGain,
                AccelerationFastGain, AccelerationTransitionSpeed);
        }
        return transformed;
    }

    private Vector2 PhysicalDelta(Vector2 pixels) => new(
        Sensitivity.X > 0 ? pixels.X / Sensitivity.X : 0,
        Sensitivity.Y > 0 ? pixels.Y / Sensitivity.Y : 0);

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
            TapAndDrag && TapToClick, Clicker.DoubleClickInterval, ThreeFingerSwipes, PinchZoom,
            InteractiveDesktopSwipes && InteractiveDesktopSupported, DesktopSwipeDistanceMm);

        if (action.Magnify != MagnifyPhase.None)
        {
            Clicker.Magnify(action.Magnification, action.Magnify);
            magnifying = action.Magnify is MagnifyPhase.Began or MagnifyPhase.Changed;
        }
        if (action.Desktop.Phase != DesktopSwipePhase.None)
        {
            Clicker.DesktopSwipe(action.Desktop);
            desktopGesture = action.Desktop.Phase is DesktopSwipePhase.Began or DesktopSwipePhase.Changed
                ? action.Desktop : null;
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

        Vector2 cursor = touchAcceleration.Apply(action.Cursor, PhysicalDelta(action.Cursor), now,
            TouchVelocityAcceleration && count == 1 && now >= penUntil &&
            !touchDragging && action.Drag == DragChange.None,
            AccelerationSlowGain, AccelerationFastGain, AccelerationTransitionSpeed);
        if (cursor != Vector2.Zero)
        {
            Vector2 delta = Vector2.Transform(cursor,
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

    private void StopJoystick()
    {
        bool wasActive = joystick.Active;
        joystick.Reset();
        joystickAnchor = null;
        if (wasActive) joystickTimer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
    }

    private void EmitJoystickScroll(object? state)
    {
        lock (sync)
        {
            if (disposed || Pointer is not IMouseScrollHandler wheel) return;
            var delta = joystick.Step(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            if (delta.X != 0) wheel.ScrollHorizontally((int)delta.X);
            if (delta.Y != 0) wheel.ScrollVertically((int)delta.Y);
            if (delta != Vector2.Zero) (Pointer as ISynchronousPointer)?.Flush();
        }
    }

    private void EndMagnification()
    {
        bool hadGesture = magnifying || desktopGesture is not null;
        if (magnifying) Clicker.Magnify(0, MagnifyPhase.Cancelled);
        if (desktopGesture is DesktopGesture desktop)
            Clicker.DesktopSwipe(desktop with { Phase = DesktopSwipePhase.Cancelled, Velocity = 0 });
        magnifying = false;
        desktopGesture = null;
        if (hadGesture) touch.Reset();
    }

    protected override void Dispose(bool disposing)
    {
        lock (sync)
        {
            disposed = true;
            joystick.Reset();
            joystickTimer?.Dispose();
            joystickTimer = null;
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
