namespace Client.Services.Fishing;

// Tranquility rod: the rhythm minigame is handled by TranquilityController and
// a dedicated branch in the tracker. This profile only carries the Kind so the
// tracker can detect it; it applies no target adjustment or hold transform.
internal sealed class TranquilityRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.Tranquility;
}
