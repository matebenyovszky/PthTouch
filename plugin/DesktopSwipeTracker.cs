using System.Numerics;

namespace PthTouch;

public enum DesktopSwipePhase { None = 0, Began = 1, Changed = 2, Ended = 4, Cancelled = 8 }
public readonly record struct DesktopGesture(float Progress = 0, float Velocity = 0,
    DesktopSwipePhase Phase = DesktopSwipePhase.None);
public readonly record struct DesktopUpdate(bool Captured = false, DesktopGesture Gesture = default);

// Tracks one horizontal desktop transition, with direct progress from the
// centroid. Velocity affects only settling; it never delays finger tracking.
public sealed class DesktopSwipeTracker
{
    private int signature = -1;
    private Vector2 origin, previous;
    private readonly Vector2[] starts = new Vector2[16];
    private readonly Vector2[] lastPositions = new Vector2[16];
    private int lastPresent;
    private double previousTime;
    private double? missingSince;
    private bool active, blocked, vertical;
    private float progress, velocity;

    public void Reset()
    {
        signature = -1; active = blocked = vertical = false;
        missingSince = null; progress = velocity = 0;
    }
    public DesktopGesture Cancel()
    {
        var result = active ? new DesktopGesture(progress, 0, DesktopSwipePhase.Cancelled) : default;
        Reset();
        return result;
    }
    public DesktopGesture Finish(double now)
    {
        if (!active) { Reset(); return default; }
        float releaseVelocity = now - previousTime <= .08 ? velocity : 0;
        bool commit = MathF.Abs(progress) >= .2f ||
            (MathF.Abs(progress) >= .03f && releaseVelocity * MathF.Sign(progress) > .8f);
        var result = new DesktopGesture(progress, commit ? releaseVelocity : 0,
            commit ? DesktopSwipePhase.Ended : DesktopSwipePhase.Cancelled);
        Reset();
        return result;
    }
    public DesktopUpdate Update(ReadOnlySpan<Contact> contacts, double now,
        Vector2 mmPerUnit, float distanceMm)
    {
        if (contacts.Length == 0) return new(false, Finish(now));
        if (blocked) return new(true);
        int ids = 0;
        Vector2 center = Vector2.Zero;
        foreach (var c in contacts)
        {
            if (c.Id >= 16 || (ids & (1 << c.Id)) != 0 || !float.IsFinite(c.Position.X) || !float.IsFinite(c.Position.Y))
                return Block();
            ids |= 1 << c.Id;
            center += c.Position * mmPerUnit;
        }
        center /= contacts.Length;
        if (active && contacts.Length < 3)
            missingSince ??= now;
        if (active && missingSince is double missing && now - missing > .08)
        {
            var ended = Finish(now);
            blocked = true;
            return new(true, ended);
        }
        if (contacts.Length > 3) return Block();
        if (active && contacts.Length < 2) return new(true);
        if ((!active && contacts.Length != 3) || vertical) return default;
        if (signature == -1)
        {
            signature = lastPresent = ids;
            origin = previous = center; previousTime = now;
            foreach (var c in contacts) starts[c.Id] = lastPositions[c.Id] = c.Position * mmPerUnit;
            return default;
        }
        if ((!active && ids != signature) || (ids & ~signature) != 0) return Block();
        double dt = now - previousTime;
        if (dt < 0 || dt > .15) return Block();
        if (active)
        {
            // A reappearing finger has an old absolute coordinate. Average
            // only deltas from contacts present in both frames, so temporary
            // two-finger reports neither freeze nor jump the desktop.
            Vector2 delta = Vector2.Zero;
            int common = 0;
            foreach (var c in contacts)
                if ((lastPresent & (1 << c.Id)) != 0)
                {
                    delta += c.Position * mmPerUnit - lastPositions[c.Id];
                    common++;
                }
            if (common == 0) return Block();
            center = previous + delta / common;
        }
        if ((center - previous).Length() > 25) return Block();
        if (contacts.Length == 3) missingSince = null;
        lastPresent = ids;
        foreach (var c in contacts) lastPositions[c.Id] = c.Position * mmPerUnit;
        previous = center; previousTime = now;
        distanceMm = float.IsFinite(distanceMm) ? Math.Clamp(distanceMm, 20, 160) : 60;
        Vector2 travel = center - origin;
        float ax = MathF.Abs(travel.X), ay = MathF.Abs(travel.Y);
        if (!active)
        {
            if (ay >= 2 && ay > ax * 1.3f) { vertical = true; return default; }
            if (ax < 2 || ax < ay * 1.3f) return default;
            foreach (var c in contacts)
                if ((c.Position.X * mmPerUnit.X - starts[c.Id].X) * MathF.Sign(travel.X) < 1)
                    return default;
        }
        // Positive native progress reveals the Space to the right, so it
        // has the opposite sign to physical finger translation.
        float nextProgress = Math.Clamp(-travel.X / distanceMm, -.95f, .95f);
        if (dt > .0001)
            velocity = .7f * velocity + .3f * Math.Clamp((nextProgress - progress) / (float)dt, -8, 8);
        bool moved = MathF.Abs(nextProgress - progress) > .000001f;
        progress = nextProgress;
        if (active && !moved) return new(true);
        var phase = active ? DesktopSwipePhase.Changed : DesktopSwipePhase.Began;
        active = true;
        return new(true, new(progress, velocity, phase));
    }
    private DesktopUpdate Block()
    {
        bool hadSession = active || signature != -1;
        var cancelled = Cancel();
        blocked = true;
        return new(hadSession, cancelled);
    }
}
