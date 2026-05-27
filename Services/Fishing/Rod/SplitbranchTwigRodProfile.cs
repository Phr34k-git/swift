namespace Client.Services.Fishing;

// Splitbranch Twig currently uses default tracking behavior.
// Special interaction is handled in Tracking1FishingTracker.
internal sealed class SplitbranchTwigRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.SplitbranchTwig;
}

