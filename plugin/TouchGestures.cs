using System.Numerics;

namespace PthTouch;

public enum SwipeDirection { None, Left, Right, Up, Down }
public enum MagnifyPhase { None = 0, Began = 1, Changed = 2, Ended = 4, Cancelled = 8 }
public readonly record struct GestureMotion(bool Captured = false,
    SwipeDirection Swipe = SwipeDirection.None, float Magnification = 0,
    MagnifyPhase Magnify = MagnifyPhase.None);

// Classification uses physical travel, contact IDs and a locked mode until
// all fingers lift. A pinch never becomes a scroll midway through its life.
public sealed class TouchGestures
{
    private enum TwoFingerMode { Undecided, Scroll, Pinch }
    private TwoFingerMode twoMode;
    private int signature = -1;
    private Vector2 origin, previousCentroid;
    private float originDistance, previousDistance;
    private double lastTime;
    private bool advancedSession, blocked, fired;
    private readonly Vector2?[] starts = new Vector2?[16];

    public void Reset()
    {
        signature = -1;
        twoMode = TwoFingerMode.Undecided;
        origin = previousCentroid = Vector2.Zero;
        originDistance = previousDistance = 0;
        advancedSession = blocked = fired = false;
        Array.Clear(starts);
    }

    public MagnifyPhase Cancel()
    {
        bool active = twoMode == TwoFingerMode.Pinch;
        Reset();
        return active ? MagnifyPhase.Cancelled : MagnifyPhase.None;
    }

    public GestureMotion Update(ReadOnlySpan<Contact> contacts, double now,
        Vector2 mmPerUnit, bool threeFingerSwipes, bool pinchZoom)
    {
        if (contacts.Length == 0)
        {
            bool pinching = twoMode == TwoFingerMode.Pinch;
            Reset();
            return new(Magnify: pinching ? MagnifyPhase.Ended : MagnifyPhase.None);
        }
        if (blocked)
            return new(Captured: true);

        int ids = 0;
        Vector2 center = Vector2.Zero;
        foreach (var contact in contacts)
        {
            if (contact.Id >= starts.Length || (ids & (1 << contact.Id)) != 0 ||
                !float.IsFinite(contact.Position.X) || !float.IsFinite(contact.Position.Y))
            {
                var phase = Cancel();
                blocked = true;
                return new(true, Magnify: phase);
            }
            ids |= 1 << contact.Id;
            center += contact.Position * mmPerUnit;
        }
        center /= contacts.Length;
        bool changed = ids != signature;
        bool stale = signature != -1 && now - lastTime > .15;
        lastTime = now;

        // Contacts arrive in separate HID packets. A third finger may land
        // after a tentative two-finger pinch has already begun; cancel that
        // zoom and establish a fresh three-finger origin rather than blocking
        // the whole swipe until lift.
        bool promotePinch = contacts.Length == 3 && threeFingerSwipes &&
            twoMode == TwoFingerMode.Pinch;
        if (promotePinch)
        {
            twoMode = TwoFingerMode.Undecided;
            advancedSession = true;
            signature = ids;
            origin = previousCentroid = center;
            foreach (var c in contacts) starts[c.Id] = c.Position * mmPerUnit;
            return new(true, Magnify: MagnifyPhase.Cancelled);
        }

        if (contacts.Length > 3 || (advancedSession && contacts.Length != 3) ||
            (twoMode == TwoFingerMode.Pinch && (changed || stale)))
        {
            var phase = Cancel();
            blocked = true;
            return new(true, Magnify: phase);
        }
        if (contacts.Length == 3 && threeFingerSwipes)
        {
            advancedSession = true;
            if (changed || stale)
            {
                if (changed && signature != -1 && starts.Any(x => x is not null))
                {
                    blocked = true;
                    return new(true);
                }
                signature = ids;
                origin = previousCentroid = center;
                foreach (var c in contacts) starts[c.Id] = c.Position * mmPerUnit;
                return new(true);
            }
            if (fired)
                return new(true);
            Vector2 travel = center - origin;
            Vector2 step = center - previousCentroid;
            previousCentroid = center;
            if (step.Length() > 25)
            {
                blocked = true;
                return new(true);
            }
            float ax = MathF.Abs(travel.X), ay = MathF.Abs(travel.Y);
            if (MathF.Max(ax, ay) < 6 || MathF.Max(ax, ay) < MathF.Min(ax, ay) * 1.3f)
                return new(true);
            bool horizontal = ax > ay;
            float sign = MathF.Sign(horizontal ? travel.X : travel.Y);
            foreach (var c in contacts)
            {
                Vector2 delta = c.Position * mmPerUnit - starts[c.Id]!.Value;
                if ((horizontal ? delta.X : delta.Y) * sign < 3)
                    return new(true);
            }
            fired = true;
            return new(true, horizontal
                ? (travel.X < 0 ? SwipeDirection.Left : SwipeDirection.Right)
                : (travel.Y < 0 ? SwipeDirection.Up : SwipeDirection.Down));
        }
        if (contacts.Length != 2 || !pinchZoom)
        {
            signature = ids;
            return default;
        }
        float distance = ((contacts[0].Position - contacts[1].Position) * mmPerUnit).Length();
        if (changed || stale)
        {
            signature = ids;
            twoMode = TwoFingerMode.Undecided;
            origin = previousCentroid = center;
            originDistance = previousDistance = distance;
            return default;
        }
        if ((center - previousCentroid).Length() > 25 || MathF.Abs(distance - previousDistance) > 25)
        {
            var phase = Cancel();
            blocked = true;
            return new(true, Magnify: phase);
        }
        previousCentroid = center;
        if (twoMode == TwoFingerMode.Scroll)
            return default;
        if (twoMode == TwoFingerMode.Undecided)
        {
            float spread = MathF.Abs(distance - originDistance);
            float pan = (center - origin).Length();
            if (originDistance >= 3 && spread >= MathF.Max(1.0f, originDistance * .03f) && spread >= pan * 1.5f)
            {
                twoMode = TwoFingerMode.Pinch;
                previousDistance = distance;
                return new(true, Magnification: Math.Clamp(distance / originDistance - 1, -.2f, .2f),
                    Magnify: MagnifyPhase.Began);
            }
            if (pan >= 1)
                twoMode = TwoFingerMode.Scroll;
            previousDistance = distance;
            return default;
        }
        float amount = previousDistance >= 3 ? Math.Clamp(distance / previousDistance - 1, -.2f, .2f) : 0;
        previousDistance = distance;
        return new(true, Magnification: amount, Magnify: amount != 0 ? MagnifyPhase.Changed : MagnifyPhase.None);
    }
}
