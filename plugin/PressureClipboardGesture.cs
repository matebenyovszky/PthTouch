using System.Numerics;

namespace PthTouch;

public enum PressureClipboardAction { None, Copy, Paste }

// Track normal moving pressure, then freeze that reference at the drag end.
// A partial relaxation relative to the actual peak rearms a second squeeze.
public sealed class PressureClipboardGesture
{
    private bool active, blocked, dragged, peak, confirmed, peakAfterDrag;
    private bool copyArmed, pasteArmed;
    private Vector2 origin, copySpot, motionAnchor;
    private float baseline, peakMaximum, maximumTravel;
    private double started, previousTime, peakStarted, peakMaximumTime;
    private double previousPeak = double.NegativeInfinity;
    private int peaks;
    public bool LockPointer { get; private set; }

    public void Reset()
    {
        active = blocked = dragged = peak = confirmed = copyArmed = pasteArmed = LockPointer = false;
        peaks = 0; maximumTravel = 0; previousPeak = double.NegativeInfinity;
    }

    public PressureClipboardAction Update(Vector2 positionMm, float pressure, bool cancelled,
        double now, bool copyEnabled, bool pasteEnabled, float extraPressure = .08f,
        float dragDistanceMm = 1.5f, double doublePressInterval = .5)
    {
        if (!copyEnabled && !pasteEnabled) { Reset(); return default; }
        if (!float.IsFinite(pressure) || !float.IsFinite(positionMm.X) ||
            !float.IsFinite(positionMm.Y) || !double.IsFinite(now))
        { Reset(); blocked = true; return default; }
        pressure = Math.Clamp(pressure, 0, 1);
        if (cancelled) { Reset(); blocked = pressure > 0; return default; }
        if (blocked)
        {
            if (pressure == 0) Reset();
            return default;
        }
        if (!active)
        {
            if (pressure == 0) return default;
            active = true; origin = copySpot = motionAnchor = positionMm;
            baseline = pressure; started = previousTime = now;
            return default;
        }
        double dt = now - previousTime;
        if (dt < 0 || dt > .25) { Reset(); blocked = pressure > 0; return default; }
        previousTime = now;
        // Coordinates can shift as the tip lifts. They no longer belong to
        // the held selection, and must not invalidate a completed gesture.
        if (pressure == 0)
        {
            var result = dragged && copyArmed && copyEnabled ? PressureClipboardAction.Copy :
                !dragged && maximumTravel <= .8f && pasteArmed && pasteEnabled
                    ? PressureClipboardAction.Paste : PressureClipboardAction.None;
            Reset(); return result;
        }
        maximumTravel = MathF.Max(maximumTravel, Vector2.Distance(positionMm, origin));
        dragDistanceMm = float.IsFinite(dragDistanceMm) ? Math.Clamp(dragDistanceMm, .5f, 10) : 1.5f;
        bool moving = Vector2.Distance(positionMm, motionAnchor) > .25f;
        if (moving) motionAnchor = positionMm;
        if (!dragged && maximumTravel >= dragDistanceMm)
        {
            dragged = true; pasteArmed = false; peaks = 0;
            peak = confirmed = false; baseline = pressure;
            previousPeak = double.NegativeInfinity;
        }
        if (copyArmed && Vector2.Distance(positionMm, copySpot) > .8f) copyArmed = false;
        extraPressure = float.IsFinite(extraPressure) ? Math.Clamp(extraPressure, .05f, .5f) : .08f;
        if (!peak)
        {
            if (dragged && moving) baseline = pressure;
            else if (now - started < .08 || pressure < baseline)
                baseline += (pressure - baseline) * (1 - MathF.Exp(-(float)dt / .08f));
            // Upward stationary pressure is intentionally not learned away.
            float rise = MathF.Max(.04f, MathF.Min(extraPressure, (1 - baseline) * .65f));
            if (now - started >= .08 && pressure >= MathF.Max(.25f, baseline + rise))
            {
                peak = true; confirmed = false; peakStarted = peakMaximumTime = now;
                peakMaximum = pressure; peakAfterDrag = dragged;
            }
        }
        if (peak)
        {
            if (pressure > peakMaximum + .005f)
            {
                peakMaximum = pressure; peakMaximumTime = now;
                if (confirmed && !dragged && peaks == 1) previousPeak = peakMaximumTime;
            }
            float relaxation = MathF.Max(.06f, extraPressure);
            if (pressure <= peakMaximum - relaxation)
            {
                peak = confirmed = false;
                baseline = pressure; // A partial valley is enough to rearm.
            }
            else if (!confirmed && now - peakStarted >= .04)
            {
                confirmed = true;
                if (peakAfterDrag && dragged && copyEnabled)
                { copyArmed = true; copySpot = positionMm; }
                else if (!dragged && pasteEnabled && maximumTravel <= .8f)
                {
                    doublePressInterval = double.IsFinite(doublePressInterval)
                        ? Math.Clamp(doublePressInterval, .15, 1) : .5;
                    peaks = peakStarted - previousPeak <= doublePressInterval ? peaks + 1 : 1;
                    previousPeak = peakMaximumTime;
                    if (peaks >= 2) pasteArmed = true;
                }
            }
        }
        LockPointer = pasteEnabled && !dragged && maximumTravel <= .8f && (peak || peaks > 0);
        return default;
    }
}
