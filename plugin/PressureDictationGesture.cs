namespace PthTouch;

// A held upper button plus a sustained strong tip press toggles dictation once.
// Releasing the button is required before another activation.
public sealed class PressureDictationGesture
{
    private double? since;
    private double previous;
    private bool fired;
    public bool Captured => since is not null || fired;
    public void Reset() { since = null; fired = false; previous = 0; }
    public bool Update(float pressure, bool upper, bool other, bool enabled, double now, float threshold)
    {
        if (!enabled || !upper || other || !float.IsFinite(pressure) || !double.IsFinite(now))
        { Reset(); return false; }
        if (now < previous || now - previous > .25) since = null;
        previous = now;
        threshold = float.IsFinite(threshold) ? Math.Clamp(threshold, .2f, .95f) : .6f;
        if (fired) return false;
        if (pressure < threshold) { since = null; return false; }
        since ??= now;
        if (now - since < .04) return false;
        fired = true;
        return true;
    }
}
