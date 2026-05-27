namespace Client.Services.Fishing;

// Dreambreaker rod: inverts the hold decision once fishing progress reaches
// 40% (port of AHK FishingController.IsInverted).
internal sealed class DreambreakerRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.Dreambreaker;

    public override bool TransformHold(bool desiredHold, double? progress)
    {
        return progress is >= 40.0 ? !desiredHold : desiredHold;
    }
}
