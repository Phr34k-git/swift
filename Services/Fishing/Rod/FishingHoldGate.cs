namespace Client.Services.Fishing;

internal enum FishingHoldAction
{
    None,
    Press,
    Release,
}

// Centralized hold/release gate for the fishing phase. Dedups unchanged state
// and enforces a minimum interval between flips (Requiem action delay).
internal sealed class FishingHoldGate
{
    private bool _held;
    private long _lastActionAt;

    public bool Held => _held;

    public FishingHoldAction Decide(bool desired, long nowMs, int delayMs)
    {
        if (desired == _held)
        {
            return FishingHoldAction.None;
        }

        if (delayMs > 0 && _lastActionAt != 0 && nowMs - _lastActionAt < delayMs)
        {
            return FishingHoldAction.None;
        }

        _held = desired;
        _lastActionAt = nowMs;
        return desired ? FishingHoldAction.Press : FishingHoldAction.Release;
    }

    public FishingHoldAction ForceRelease(long nowMs)
    {
        if (!_held)
        {
            return FishingHoldAction.None;
        }

        _held = false;
        _lastActionAt = nowMs;
        return FishingHoldAction.Release;
    }

    public void Reset()
    {
        _held = false;
        _lastActionAt = 0;
    }
}
