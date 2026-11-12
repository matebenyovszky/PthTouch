namespace PthTouch;

// Intuos V1 distance is the high six bits of byte9; the low two bits are
// X/Y coordinate fractions. These are sensor units, not millimetres.
public sealed class PenHoverHeightGate
{
    private bool high;
    public void Reset() => high = false;
    public bool Update(byte[] raw, float limit, uint pressure)
    {
        if (!float.IsFinite(limit) || limit <= 0 || pressure > 0 || raw.Length != 10 ||
            (raw[0] != 2 && raw[0] != 16) || (raw[1] & 0x20) == 0)
        { Reset(); return false; }
        int distance = raw[9] >> 2;
        limit = Math.Clamp(limit, 1, 63);
        if (distance > limit) high = true;
        else if (distance <= limit - 1) high = false;
        return high;
    }
}
