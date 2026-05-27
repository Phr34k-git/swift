namespace Client.Services.Fishing;

// Strategy for one equipped rod. Implementations adjust the fish target
// (Pinion), transform the hold decision (Dreambreaker), and/or carry a
// fishing action delay (Requiem).
internal abstract class RodProfile
{
    public abstract RodKind Kind { get; }

    // Minimum milliseconds between hold/release flips during the fishing phase.
    public virtual int FishingActionDelayMs => 0;

    // Returns metrics with a possibly-shifted FishCenter. `note` may be null.
    public virtual ReelMetrics AdjustTarget(ReelMetrics metrics, NoteTarget? note) => metrics;

    // Returns the final hold intent given the controller's raw decision.
    public virtual bool TransformHold(bool desiredHold, double? progress) => desiredHold;

    // Clears any per-fishing-engagement state.
    public virtual void Reset()
    {
    }

    public static RodProfile For(RodKind kind) => kind switch
    {
        RodKind.BellonaWaraxe => new BellonaWaraxeRodProfile(),
        RodKind.MasterlineRod => new MasterlineRodProfile(),
        RodKind.Pinion => new PinionRodProfile(),
        RodKind.Tranquility => new TranquilityRodProfile(),
        RodKind.Dreambreaker => new DreambreakerRodProfile(),
        RodKind.Requiem => new RequiemRodProfile(),
        RodKind.SplitbranchTwig => new SplitbranchTwigRodProfile(),
        RodKind.MiguRod => new MiguRodProfile(),
        _ => new DefaultRodProfile(),
    };
}
