using System.Numerics;

namespace PthTouch;

public sealed class EraserSelectionGesture
{
    private Vector2 origin;
    private bool active, blocked;
    private float travel;
    private double previous;
    public void Reset() { active = blocked = false; travel = 0; }
    public bool Update(Vector2 mm, float pressure, bool eraser, bool buttons, double now,
        bool enabled, float distanceMm)
    {
        if (!enabled || !eraser || buttons || !double.IsFinite(now) ||
            !float.IsFinite(mm.X) || !float.IsFinite(mm.Y) || !float.IsFinite(pressure))
        { Reset(); return false; }
        if (blocked) { if (pressure <= 0) Reset(); return false; }
        if (!active)
        {
            if (pressure <= 0) return false;
            active = true; origin = mm; travel = 0; previous = now;
            return false;
        }
        if (now < previous || now - previous > .25)
        { Reset(); blocked = pressure > 0; return false; }
        previous = now;
        if (pressure <= 0)
        {
            bool result = travel >= (float.IsFinite(distanceMm) ? Math.Clamp(distanceMm, .5f, 10) : 1.5f);
            Reset(); return result;
        }
        travel = MathF.Max(travel, Vector2.Distance(mm, origin));
        return false;
    }
}
