namespace Client.Services.Fishing;

// Requiem rod: auto-applies a 160ms minimum interval between hold/release
// flips (the value that works at the tracker's 20ms tick rate).
internal sealed class RequiemRodProfile : RodProfile
{
    public override RodKind Kind => RodKind.Requiem;

    public override int FishingActionDelayMs => 160;
}
