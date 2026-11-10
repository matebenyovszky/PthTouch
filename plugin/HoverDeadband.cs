using System.Numerics;

namespace PthTouch;

// Spatial hysteresis, optionally preceded by a three-report spike filter.
public sealed class HoverDeadband
{
    private Vector2? anchor;
    private Vector2 previous, older;
    private bool hasHistory;

    public void Reset()
    {
        anchor = null;
        hasHistory = false;
    }

    public Vector2 Filter(Vector2 position, Vector2 mmPerUnit, float thresholdMm, bool hovering,
        bool rejectSpikes = false)
    {
        if (!hovering || thresholdMm <= 0)
        {
            Reset();
            anchor = position;
            return position;
        }
        if (rejectSpikes)
        {
            Vector2 raw = position;
            // Large deliberate motion bypasses the window immediately. Reset
            // its history too, so stopping does not replay stale positions.
            if (!hasHistory || anchor is null ||
                ((position - anchor.Value) * mmPerUnit).LengthSquared() >
                    16 * thresholdMm * thresholdMm)
                previous = older = position;
            else
                position = new(Median(position.X, previous.X, older.X),
                    Median(position.Y, previous.Y, older.Y));
            older = previous;
            previous = raw;
            hasHistory = true;
        }
        else
            hasHistory = false;
        if (!hovering || thresholdMm <= 0 || anchor is null ||
            ((position - anchor.Value) * mmPerUnit).LengthSquared() > thresholdMm * thresholdMm)
            anchor = position;
        return anchor.Value;
    }

    private static float Median(float a, float b, float c) =>
        MathF.Max(MathF.Min(a, b), MathF.Min(MathF.Max(a, b), c));
}
