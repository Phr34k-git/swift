namespace Client.Services.Fishing;

// IsAutomationGateOpen is the shared post-catch settle signal. It opens once
// a fish has been caught AND the post-catch input lockout has elapsed AND no
// per-tracker safety flag (perfect-cast holding, cast bar charging) is active.
// Add-ons that drive input outside the tracker (Auto Totem, Auto Aquarium,
// Auto Sovereign Recharge) must wait for this to avoid firing into the game's
// post-catch input lockout.
public sealed record FishingTrackerStatus(
    bool IsRunning,
    string Phase,
    string Message,
    double? ProgressPercent,
    bool IsHolding,
    FishingReelStats ReelStats = default,
    bool IsAutomationGateOpen = false,
    ReelSnapshotData? ReelSnapshot = null);

// Latest reel mini-game positions, sourced from ReelMetrics in the tracker
// when a minigame is active. Coordinates are container-normalized [0..1].
public sealed record ReelSnapshotData(double FishCenter, double PlayerbarCenter, double PlayerbarWidth);
