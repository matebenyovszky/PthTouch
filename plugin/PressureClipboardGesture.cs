using System.Numerics;

namespace PthTouch;

public enum PressureClipboardAction { None, Copy, Paste }

// One physical tip contact is one session. Pressure peaks request commands;
// release returns the command so the host can send mouse-up before Cmd+C/V.
public sealed class PressureClipboardGesture
{
    private bool active, blocked, dragged, peak, confirmed, peakAfterDrag;
    private bool copyArmed, pasteArmed;
    private Vector2 origin, copySpot;
    private float baseline, peakRelease;
    private double started, previousTime, peakStarted, previousPeak = double.NegativeInfinity;
    private int peaks;
    private float maximumTravel;

    public void Reset()
    {
        active = blocked = dragged = peak = confirmed = copyArmed = pasteArmed = false;
        peaks = 0; maximumTravel = 0; previousPeak = double.NegativeInfinity;
    }

    public PressureClipboardAction Update(Vector2 positionMm, float pressure, bool cancelled,
        double now, bool copyEnabled, bool pasteEnabled, float extraPressure = .18f,
        float dragDistanceMm = 1.5f, double doublePressInterval = .5)
    {
        if (!copyEnabled && !pasteEnabled) { Reset(); return default; }
        if (!float.IsFinite(pressure) || !float.IsFinite(positionMm.X) ||
            !float.IsFinite(positionMm.Y) || !double.IsFinite(now))
        {
            Reset(); blocked = true; return default;
        }
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
            active = true; origin = copySpot = positionMm;
            baseline = pressure; started = previousTime = now;
            return default;
        }
        double dt = now - previousTime;
        if (dt < 0 || dt > .25)
        {
            Reset(); blocked = pressure > 0; return default;
        }
        previousTime = now;
        maximumTravel = MathF.Max(maximumTravel, Vector2.Distance(positionMm, origin));
        dragDistanceMm = float.IsFinite(dragDistanceMm) ? Math.Clamp(dragDistanceMm, .5f, 10) : 1.5f;
        if (!dragged && maximumTravel >= dragDistanceMm)
        {
            dragged = true; pasteArmed = false; peaks = 0;
            previousPeak = double.NegativeInfinity;
        }
        if (copyArmed && Vector2.Distance(positionMm, copySpot) > .8f)
            copyArmed = false; // Further dragging means this was not its end.
        if (pressure == 0)
        {
            var result = dragged && copyArmed && copyEnabled ? PressureClipboardAction.Copy :
                !dragged && maximumTravel <= .8f && pasteArmed && pasteEnabled
                    ? PressureClipboardAction.Paste : PressureClipboardAction.None;
            Reset();
            return result;
        }
        extraPressure = float.IsFinite(extraPressure) ? Math.Clamp(extraPressure, .05f, .5f) : .18f;
        // Adapt to the user's light pressure and remaining sensor headroom.
        float rise = MathF.Max(.05f, MathF.Min(extraPressure, (1 - baseline) * .65f));
        float threshold = MathF.Max(.35f, baseline + rise);
        if (!peak && now - started >= .08 && pressure >= threshold)
        {
            peak = true; confirmed = false; peakStarted = now;
            peakAfterDrag = dragged;
            peakRelease = baseline + rise * .35f;
        }
        if (peak)
        {
            if (pressure <= peakRelease)
                peak = confirmed = false;
            else if (!confirmed && now - peakStarted >= .04)
            {
                confirmed = true;
                if (peakAfterDrag && dragged && copyEnabled)
                {
                    copyArmed = true; copySpot = positionMm;
                }
                else if (!dragged && pasteEnabled && maximumTravel <= .8f)
                {
                    doublePressInterval = double.IsFinite(doublePressInterval)
                        ? Math.Clamp(doublePressInterval, .15, 1) : .5;
                    peaks = peakStarted - previousPeak <= doublePressInterval ? peaks + 1 : 1;
                    previousPeak = peakStarted;
                    if (peaks >= 2) pasteArmed = true;
                }
            }
        }
        else if (dt > 0)
            baseline += (pressure - baseline) * (1 - MathF.Exp(-(float)dt / .15f));
        return default;
    }
}
