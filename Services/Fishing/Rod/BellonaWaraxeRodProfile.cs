namespace Client.Services.Fishing;

// Dedicated Bellona profile hook. Behavior currently matches Default; this
// gives Bellona its own extension point for future rod-specific mechanics.
internal sealed class BellonaWaraxeRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.BellonaWaraxe;
}
