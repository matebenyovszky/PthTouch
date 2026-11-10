using System.Numerics;

namespace PthTouch;

// Position selects velocity inside a bounded virtual rectangle. Integration
// runs separately, so an unmoving pen at the edge keeps scrolling.
public sealed class PenJoystickScroll
{
    private bool active;
    private Vector2 origin, velocity, remainder;
    private double lastStep, lastReport;
    public bool Active => active;

    public void Reset()
    {
        active = false;
        velocity = remainder = Vector2.Zero;
    }

    public void Update(Vector2 positionMm, bool pressed, double now, Vector2 halfSizeMm,
        Vector2 maximumPixelsPerSecond, float deadzoneMm, float exponent, bool invert, float edgeBoost = 1)
    {
        if (!pressed || !double.IsFinite(now) || !float.IsFinite(positionMm.X) || !float.IsFinite(positionMm.Y))
        {
            Reset();
            return;
        }
        if (!active)
        {
            active = true;
            origin = positionMm;
            lastStep = now;
            remainder = Vector2.Zero;
        }
        lastReport = now;
        float Safe(float value, float fallback, float min, float max) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        halfSizeMm = new(Safe(halfSizeMm.X, 20, 1, 100), Safe(halfSizeMm.Y, 20, 1, 100));
        deadzoneMm = Safe(deadzoneMm, 1, 0, MathF.Min(halfSizeMm.X, halfSizeMm.Y) * .9f);
        exponent = Safe(exponent, 2, 1, 12);
        edgeBoost = Safe(edgeBoost, 1, 1, 10);
        Vector2 delta = positionMm - origin;
        float Axis(float offset, float halfSize, float maximum)
        {
            float fraction = Math.Clamp((MathF.Abs(offset) - deadzoneMm) / (halfSize - deadzoneMm), 0, 1);
            float edge = Math.Clamp((fraction - .65f) / .35f, 0, 1);
            edge = edge * edge * (3 - 2 * edge);
            float boost = 1 + (edgeBoost - 1) * edge;
            return MathF.Sign(offset) * MathF.Pow(fraction, exponent) *
                Safe(maximum, 1200, 0, 100000) * boost;
        }
        // Match the existing macOS pen-scroll direction: moving down/right
        // scrolls the document down/right unless explicitly reversed.
        float sign = invert ? 1 : -1;
        velocity = new Vector2(Axis(delta.X, halfSizeMm.X, maximumPixelsPerSecond.X),
            Axis(delta.Y, halfSizeMm.Y, maximumPixelsPerSecond.Y)) * sign;
    }

    public Vector2 Step(double now)
    {
        if (!active || !double.IsFinite(now)) return Vector2.Zero;
        double elapsed = Math.Clamp(now - lastStep, 0, .05);
        lastStep = now;
        // Stop on missing device reports instead of continuing after a lost
        // connection/sleep. The PTH-851 keeps reporting while stationary.
        if (now - lastReport > .5) { remainder = Vector2.Zero; return Vector2.Zero; }
        remainder += velocity * (float)elapsed;
        Vector2 whole = new(MathF.Truncate(remainder.X), MathF.Truncate(remainder.Y));
        remainder -= whole;
        return whole;
    }
}
