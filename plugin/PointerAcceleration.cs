using System.Numerics;

namespace PthTouch;

// Smooth only the speed estimate, never the coordinates. Every input delta is
// emitted immediately with a bounded gain; no delayed motion is replayed.
public sealed class PointerAcceleration
{
    private double? previousTime;
    private float speed;

    public void Reset() { previousTime = null; speed = 0; }

    public Vector2 Apply(Vector2 pixels, Vector2 millimetres, double now, bool enabled,
        float slowGain, float fastGain, float transitionSpeed)
    {
        if (!enabled || !double.IsFinite(now) || !float.IsFinite(millimetres.X) || !float.IsFinite(millimetres.Y))
        {
            Reset();
            return pixels;
        }
        float Safe(float value, float fallback, float min, float max) =>
            float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        slowGain = Safe(slowGain, .9f, .2f, 2);
        fastGain = Safe(fastGain, MathF.Max(1.35f, slowGain), slowGain, 5);
        transitionSpeed = Safe(transitionSpeed, 150, 10, 1000);
        double dt = previousTime is double previous ? now - previous : 0;
        previousTime = now;
        if (dt <= 0 || dt > .15)
        {
            speed = 0;
            return pixels * slowGain;
        }
        float instant = Math.Clamp(millimetres.Length() / MathF.Max((float)dt, .0001f), 0, 10000);
        // An 8 ms time constant keeps gain changes gentle at any report rate.
        float weight = 1 - MathF.Exp(-(float)dt / .008f);
        speed += weight * (instant - speed);
        float t = Math.Clamp(speed / transitionSpeed, 0, 1);
        t = t * t * (3 - 2 * t);
        return pixels * (slowGain + (fastGain - slowGain) * t);
    }
}
