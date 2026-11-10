using System.Numerics;

namespace PthTouch;

public readonly record struct Contact(byte Id, Vector2 Position);
public enum TapButton { None, Left, Right, Middle }
public enum DragChange { None, Start, End }
public readonly record struct Motion(Vector2 Cursor, Vector2 Scroll,
    TapButton Tap = TapButton.None, DragChange Drag = DragChange.None,
    SwipeDirection Swipe = SwipeDirection.None, float Magnification = 0,
    MagnifyPhase Magnify = MagnifyPhase.None);

// A contact snapshot is supplied by OTD's Wacom parser. No timers or delayed
// scroll actions are used: every output comes from an actual coordinate delta.
public sealed class TouchMotion
{
    private readonly TouchGestures gestures = new();
    private Vector2? lastPosition;
    private int lastSignature = -1;
    private double lastTime;
    private bool multipleContacts;
    private readonly Vector2?[] tapOrigins = new Vector2?[16];
    private bool gestureActive, tapEligible, ignoreUntilLift, scrolling;
    private double gestureStart;
    private int maximumContacts;
    private Vector2? scrollStart;
    private double lastSingleTap = double.NegativeInfinity;
    private bool dragCandidate, dragging;
    private Vector2 dragPending;

    public void Reset()
    {
        gestures.Reset();
        lastPosition = null;
        lastSignature = -1;
        multipleContacts = false;
        gestureActive = false;
        tapEligible = false;
        ignoreUntilLift = false;
        scrolling = false;
        scrollStart = null;
        maximumContacts = 0;
        lastSingleTap = double.NegativeInfinity;
        dragCandidate = dragging = false;
        dragPending = Vector2.Zero;
        Array.Clear(tapOrigins);
    }

    public Motion Update(ReadOnlySpan<Contact> contacts, double now,
        Vector2 mmPerUnit, Vector2 cursorPixelsPerMm, float scrollPixelsPerMm,
        bool penNearby = false, bool tapAndDrag = true, double doubleClickInterval = 0.5,
        bool threeFingerSwipes = false, bool pinchZoom = false)
    {
        if (penNearby)
        {
            bool endDrag = dragging;
            MagnifyPhase phase = gestures.Cancel();
            Reset();
            ignoreUntilLift = contacts.Length > 0;
            return new Motion(Vector2.Zero, Vector2.Zero, Drag: endDrag ? DragChange.End : DragChange.None, Magnify: phase);
        }
        if (contacts.Length == 0)
        {
            // Idle mask/status packets must not disarm the second contact.
            if (!gestureActive && !ignoreUntilLift)
                return default;
            bool endDrag = dragging;
            var endGesture = gestures.Update(contacts, now, mmPerUnit, threeFingerSwipes, pinchZoom);
            TapButton tap = !endDrag && !ignoreUntilLift && gestureActive && tapEligible &&
                now - gestureStart <= 0.30
                ? maximumContacts switch { 1 => TapButton.Left, 2 => TapButton.Right, _ => TapButton.None }
                : TapButton.None;
            Reset();
            if (tap == TapButton.Left)
                lastSingleTap = now;
            return new Motion(Vector2.Zero, Vector2.Zero, tap, endDrag ? DragChange.End : DragChange.None,
                Magnify: endGesture.Magnify);
        }
        if (ignoreUntilLift)
            return default;

        if (!gestureActive)
        {
            gestureActive = true;
            tapEligible = true;
            gestureStart = now;
            dragCandidate = tapAndDrag && contacts.Length == 1 && now - lastSingleTap <= doubleClickInterval;
            lastSingleTap = double.NegativeInfinity;
        }
        if (dragging && contacts.Length != 1)
        {
            Reset();
            ignoreUntilLift = true;
            return new Motion(Vector2.Zero, Vector2.Zero, Drag: DragChange.End);
        }
        if (contacts.Length != 1)
            dragCandidate = false;
        maximumContacts = Math.Max(maximumContacts, contacts.Length);
        foreach (var contact in contacts)
        {
            if (contact.Id >= tapOrigins.Length || !float.IsFinite(contact.Position.X) ||
                !float.IsFinite(contact.Position.Y))
            {
                bool endDrag = dragging;
                var phase = gestures.Cancel();
                Reset();
                ignoreUntilLift = true;
                return new Motion(Vector2.Zero, Vector2.Zero, Drag: endDrag ? DragChange.End : DragChange.None,
                    Magnify: phase);
            }
            tapOrigins[contact.Id] ??= contact.Position;
            if (((contact.Position - tapOrigins[contact.Id]!.Value) * mmPerUnit).Length() > 1.0f)
                tapEligible = false;
        }

        var advanced = gestures.Update(contacts, now, mmPerUnit, threeFingerSwipes, pinchZoom);
        if (advanced.Captured)
        {
            // Pending two-finger touches can still be taps; captured pinch,
            // swipe and palm sessions cannot turn into clicks or scrolling.
            tapEligible = false;
            dragCandidate = false;
            multipleContacts = contacts.Length >= 2 || multipleContacts;
            lastPosition = null;
            lastSignature = -1;
            return new Motion(Vector2.Zero, Vector2.Zero, Swipe: advanced.Swipe,
                Magnification: advanced.Magnification, Magnify: advanced.Magnify);
        }

        if (contacts.Length >= 2)
            multipleContacts = true;

        // Palm/disabled three-finger input is ignored. After scrolling, require a full
        // lift before the remaining single finger can move the cursor.
        if (contacts.Length > 2 || (contacts.Length == 1 && multipleContacts))
        {
            if (contacts.Length > 2)
                tapEligible = false;
            lastPosition = null;
            lastSignature = -1;
            return default;
        }

        int signature = 0;
        Vector2 position = Vector2.Zero;
        foreach (var contact in contacts)
        {
            if (!float.IsFinite(contact.Position.X) || !float.IsFinite(contact.Position.Y))
            {
                Reset();
                return default;
            }
            signature |= 1 << contact.Id;
            position += contact.Position;
        }
        position /= contacts.Length;

        Vector2? previous = lastPosition;
        bool sameContacts = signature == lastSignature;
        bool changed = !sameContacts || now - lastTime > 0.15;
        lastPosition = position;
        lastSignature = signature;
        lastTime = now;
        if (changed || previous is null)
        {
            if (contacts.Length == 2)
                scrollStart = position;
            if (contacts.Length == 1 && dragCandidate && !dragging &&
                sameContacts && previous is not null && now - gestureStart >= 0.15)
            {
                dragging = true;
                tapEligible = false;
                return new Motion(Vector2.Zero, Vector2.Zero, Drag: DragChange.Start);
            }
            return default;
        }

        Vector2 deltaMm = (position - previous.Value) * mmPerUnit;
        if (MathF.Abs(deltaMm.X) > 25 || MathF.Abs(deltaMm.Y) > 25)
        {
            tapEligible = false;
            return default;
        }

        if (contacts.Length == 1 && dragCandidate && !dragging)
        {
            dragPending += deltaMm * cursorPixelsPerMm;
            if (tapEligible && now - gestureStart < 0.15)
                return default;
            dragging = true;
            tapEligible = false;
            return new Motion(dragPending, Vector2.Zero, Drag: DragChange.Start);
        }

        // A small activation distance separates a two-finger tap from scroll
        // jitter. Once scrolling starts, it can never turn into a right click.
        if (contacts.Length == 2)
        {
            scrollStart ??= position;
            if (!scrolling && ((position - scrollStart.Value) * mmPerUnit).Length() < 1.0f)
                return default;
            scrolling = true;
            tapEligible = false;
        }

        return contacts.Length == 1
            ? new Motion(deltaMm * cursorPixelsPerMm, Vector2.Zero)
            : new Motion(Vector2.Zero, deltaMm * scrollPixelsPerMm);
    }
}
