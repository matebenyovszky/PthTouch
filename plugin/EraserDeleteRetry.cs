namespace PthTouch;

public enum EraserDeleteResult { Deleted, NoFocus, NoSelection, NotEditable, NoAccess, Cancelled, EventUnavailable }

// Electron may expose its renderer tree only after AXManualAccessibility is
// requested. Retry that one condition; never turn an empty/read-only selection
// into an unconditional Delete. Native callbacks recheck foreground/generation.
public static class EraserDeleteRetry
{
    public static async Task<EraserDeleteResult> RunAsync(Func<EraserDeleteResult> attempt,
        Func<bool> requestAccessibility, Func<Task> delay)
    {
        var result = attempt();
        if (result != EraserDeleteResult.NoFocus || !requestAccessibility()) return result;
        for (int i = 0; i < 3; i++)
        {
            await delay();
            result = attempt();
            if (result != EraserDeleteResult.NoFocus) break;
        }
        return result;
    }
}
