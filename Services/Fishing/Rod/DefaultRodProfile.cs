namespace Client.Services.Fishing;

// The standard rod — no target adjustment, no inversion, no action delay.
internal sealed class DefaultRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.Default;
}
