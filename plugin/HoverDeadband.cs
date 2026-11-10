using System.Numerics;

namespace PthTouch;

// A spatial threshold only: no time window, averaging or queued movement.
public sealed class HoverDeadband
{
    private Vector2? anchor;

    public void Reset() => anchor = null;

    public Vector2 Filter(Vector2 position, Vector2 mmPerUnit, float thresholdMm, bool hovering)
    {
        if (!hovering || thresholdMm <= 0 || anchor is null ||
            ((position - anchor.Value) * mmPerUnit).LengthSquared() > thresholdMm * thresholdMm)
            anchor = position;
        return anchor.Value;
    }
}
