using System.Numerics;

namespace PthTouch;

public enum PenGestureAction { None, Delete, DesktopRight, DesktopLeft }
public readonly record struct FlickUpdate(PenGestureAction Action = PenGestureAction.None, bool JustArmed = false);

public sealed class PressureFlickGesture
{
    private double? highSince, motionSince;
    private double armedAt, previousTime;
    private Vector2 origin, originTilt, previousPosition, motionOrigin;
    private bool armed, blocked;
    private bool hasPrevious;
    public bool Armed => armed;
    public void Reset() { highSince = motionSince = null; armed = blocked = hasPrevious = false; }
    public FlickUpdate Update(Vector2 mm, Vector2? tilt, float pressure, bool cancel, double now,
        bool enabled, float pressureThreshold = .65f,
        float distanceMm = 6, float speedMmPerSecond = 100, float tiltChange = .12f, bool reverse = false)
    {
        if (!enabled || cancel || tilt is not Vector2 angle ||
            !float.IsFinite(mm.X) || !float.IsFinite(mm.Y) || !float.IsFinite(angle.X) ||
            !float.IsFinite(angle.Y) || !double.IsFinite(now) || !float.IsFinite(pressure))
        { Reset(); return default; }
        if (blocked) { if (pressure <= .1f) Reset(); return default; }
        if (hasPrevious && (now < previousTime || now - previousTime > .25))
        { Reset(); blocked = pressure > .1f; return default; }
        double stepSeconds = hasPrevious ? now - previousTime : 0;
        previousTime = now; hasPrevious = true;
        pressureThreshold = float.IsFinite(pressureThreshold) ? Math.Clamp(pressureThreshold, .3f, .95f) : .65f;
        if (!armed)
        {
            if (pressure < pressureThreshold) { highSince = null; return default; }
            highSince ??= now;
            if (now - highSince < .04) return default;
            armed = true; armedAt = now; origin = previousPosition = mm; originTilt = angle;
            motionSince = null;
            return new(JustArmed: true);
        }
        // Squeezing in preparation must not consume the response window.
        // Keep it fresh while pressure is held and motion has not started.
        if (pressure >= pressureThreshold && motionSince is null) armedAt = now;
        if (now - armedAt > .65)
        { Reset(); blocked = pressure > .1f; return default; }
        float minimumSpeed = float.IsFinite(speedMmPerSecond) ? Math.Clamp(speedMmPerSecond, 30, 1000) : 100;
        Vector2 frame = mm - previousPosition;
        bool fastStart = MathF.Abs(frame.X) / MathF.Max((float)stepSeconds, .002f) >= minimumSpeed;
        if (motionSince is null && Vector2.Distance(mm, origin) > 1 && fastStart)
        { motionSince = now; motionOrigin = previousPosition; }
        previousPosition = mm;
        if (motionSince is not double motionStart) return default;
        if (now - motionStart > .18)
        { Reset(); blocked = pressure > .1f; return default; }
        Vector2 delta = mm - motionOrigin;
        float travel = MathF.Abs(delta.X);
        float minimum = float.IsFinite(distanceMm) ? Math.Clamp(distanceMm, 2, 30) : 6;
        float speed = travel / MathF.Max((float)(now - motionStart), .005f);
        float minimumTilt = float.IsFinite(tiltChange) ? Math.Clamp(tiltChange, .02f, 1) : .12f;
        if (travel < minimum || travel < MathF.Abs(delta.Y) * 1.5f || speed < minimumSpeed ||
            Vector2.Distance(angle, originTilt) < minimumTilt) return default;
        var action = (delta.X > 0) != reverse ? PenGestureAction.DesktopRight : PenGestureAction.DesktopLeft;
        Reset(); blocked = true;
        return new(action);
    }
}
