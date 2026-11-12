using System.Numerics;

namespace PthTouch;

public readonly record struct CursorPoint(double X, double Y);
public readonly record struct CursorDisplay(double X, double Y, double Width, double Height)
{
    public bool Valid => double.IsFinite(X) && double.IsFinite(Y) && Width > 0 && Height > 0 &&
        double.IsFinite(X + Width) && double.IsFinite(Y + Height);
    public bool Contains(CursorPoint p) => Valid && p.X >= X && p.Y >= Y && p.X < X + Width && p.Y < Y + Height;
}

public interface ICursorBounds
{
    Vector2 LimitDelta(Vector2 delta);
    CursorPoint LimitPoint(CursorPoint point);
}

// Project onto the union of individual displays, not its enclosing rectangle:
// offsets, negative origins and gaps are meaningful in Quartz global coordinates.
public static class CursorBounds
{
    public static CursorPoint Clamp(CursorPoint target, ReadOnlySpan<CursorDisplay> displays)
    {
        foreach (var display in displays) if (display.Contains(target)) return target;
        bool finite = double.IsFinite(target.X) && double.IsFinite(target.Y);
        CursorPoint best = finite ? target : default;
        double nearest = double.PositiveInfinity;
        bool found = false;
        foreach (var display in displays)
        {
            if (!display.Valid) continue;
            if (!finite) return new(display.X, display.Y);
            var candidate = new CursorPoint(
                Math.Clamp(target.X, display.X, Math.Max(display.X, display.X + display.Width - 1)),
                Math.Clamp(target.Y, display.Y, Math.Max(display.Y, display.Y + display.Height - 1)));
            double dx = candidate.X - target.X, dy = candidate.Y - target.Y;
            double distance = dx * dx + dy * dy;
            if (!found || distance < nearest) { best = candidate; nearest = distance; found = true; }
        }
        return best;
    }
    public static Vector2 Delta(CursorPoint current, Vector2 delta, ReadOnlySpan<CursorDisplay> displays)
    {
        if (!double.IsFinite(current.X) || !double.IsFinite(current.Y) ||
            !float.IsFinite(delta.X) || !float.IsFinite(delta.Y)) return Vector2.Zero;
        var target = Clamp(new(current.X + delta.X, current.Y + delta.Y), displays);
        return new((float)(target.X - current.X), (float)(target.Y - current.Y));
    }
}
