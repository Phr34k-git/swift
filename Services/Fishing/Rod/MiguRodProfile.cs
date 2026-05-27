namespace Client.Services.Fishing;

// Behaves like default rod; special behavior is handled in Tracking1 for
// counterAttack-triggered Shift pulses.
internal sealed class MiguRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.MiguRod;
}
