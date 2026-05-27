using System;

namespace Client.Services.Fishing;

public interface IFishingTracker : IDisposable
{
    FishingTrackerMode Mode { get; }

    FishingTrackerStatus Status { get; }

    FishingCastingMode CastingMode { get; set; }

    void Start();

    void Stop();

    void Suspend(string message);

    void Resume();
}
