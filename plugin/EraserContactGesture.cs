namespace PthTouch;

// One Delete after a real eraser contact, including stationary taps. No text,
// selection or app-focus inspection; hover alone never arms this gesture.
public sealed class EraserContactGesture
{
    private bool active, blocked;
    private double started, previous;
    public void Reset() { active = blocked = false; }
    public bool Update(float pressure, bool eraser, bool buttons, double now, bool enabled)
    {
        if (!enabled || !eraser || !float.IsFinite(pressure) || !double.IsFinite(now))
        { Reset(); return false; }
        bool down = pressure >= .01f;
        if (buttons) { Reset(); blocked = down; return false; }
        if (blocked) { if (!down) Reset(); return false; }
        if (active && (now < previous || now - previous > .25))
        { Reset(); blocked = down; return false; }
        if (!active)
        {
            if (down) { active = true; started = previous = now; }
            return false;
        }
        previous = now;
        if (down) return false;
        bool fire = now - started >= .03;
        Reset(); return fire;
    }
}
