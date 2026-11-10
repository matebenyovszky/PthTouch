using System.Numerics;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

// A test double for the optional plugin's ordering: it freezes coordinates
// before transform, while the button binding changes state after transform.
namespace ScrollBinding;

public sealed class ScrollBindingFilter : IPositionedPipelineElement<IDeviceReport>
{
    private bool pressing;
    public List<Vector2> SeenPositions = [];
    private Vector2? anchor;
    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport> Emit;

    public void Consume(IDeviceReport report)
    {
        bool nextPressing = pressing;
        if (report is ITabletReport pen)
        {
            SeenPositions.Add(pen.Position);
            nextPressing = pen.PenButtons.Length > 1 && pen.PenButtons[1];
            if (pressing)
            {
                anchor ??= pen.Position;
                pen.Position = anchor.Value;
            }
        }
        Emit?.Invoke(report);
        pressing = nextPressing;
        if (!pressing) anchor = null;
    }
}
