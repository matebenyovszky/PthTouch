using System.Numerics;

namespace PthTouch;

// A click is deferred until release so a scrolling hold never opens a link.
public sealed class PenButtonTap
{
    private bool pressed, eligible;
    private Vector2 origin;
    private double started;

    public void Reset() { pressed = eligible = false; }

    public bool Update(Vector2 position, Vector2 mmPerUnit, bool button,
        bool otherButton, uint pressure, double now, bool enabled)
    {
        if (!enabled || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            Reset();
            return false;
        }
        if (button && !pressed)
        {
            origin = position;
            started = now;
            eligible = pressure == 0 && !otherButton;
        }
        if (pressed || button)
            eligible &= pressure == 0 && !otherButton && now >= started && now - started <= .5 &&
                ((position - origin) * mmPerUnit).LengthSquared() <= .3f * .3f;
        bool click = pressed && !button && eligible;
        pressed = button;
        if (!button) eligible = false;
        return click;
    }
}
