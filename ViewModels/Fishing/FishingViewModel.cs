using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Client.Controls;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class FishingViewModel : ViewModelBase
{
    private const string AquariumGateOwner = "AUTO_AQUARIUM";
    private const int AquariumGatePriority = 300;
    // Acquire the input gate this far ahead of the scheduled cycle. Required
    // when the previous catch happens shortly before the cycle becomes due:
    // without pre-acquisition, the tracker's post-catch hold expires before
    // we engage, the tracker spam-casts during the gap, and the resulting
    // in-flight cast lands during the upcoming aquarium suspend — surfacing
    // as the "ignored reel after SUSPENDED" symptom on resume.
    private const int CycleDueSoonMs = 3000;
    private readonly Dictionary<FishingTrackerMode, IFishingTracker> _trackers = new();
    private readonly AquariumSequenceRunner _aquariumRunner = new();
    private readonly HotbarRodReader _rodReader = new();
    private readonly Timer _statusTimer;
    private readonly Timer _aquariumTimer;
    private readonly Timer _rodTimer;
    private FishingTrackerOption _selectedTracker;
    private FishingCastingMode _selectedCastingMode = FishingCastingMode.Normal;
    private FishingTrackerStatus _status = new(false, "OFF", "Ready.", null, false);
    private bool _autoAquariumEnabled;
    private bool _aquariumPending;
    private bool _aquariumActive;
    private bool _aquariumTicking;
    private bool _aquariumGateHeld;
    private bool _macroRequestedRunning;
    private bool _wasInMinigame;
    private DateTimeOffset? _nextAquariumAt;
    private double _autoAquariumCycleDelayMinutes = 65;
    private string _aquariumStatusText = "Auto Aquarium off.";
    private string _equippedRodText = "---";
    private string _rodHeaderText = "Rod - Unequipped";
    private IReadOnlyList<ColoredTextLineViewModel> _equippedRodLines = ColoredEnchantText.BuildLines("---");
    private string _masterlineRodNamesText = "---";
    private bool _isMasterlineEquipped;

    public FishingViewModel()
    {
        _selectedTracker = TrackerOptions[0];
        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => StopAsync(), _ => IsRunning);
        NavigateToAutoTotemCommand = new RelayCommand(_ => { NavigateToAutoTotemAction?.Invoke(); return Task.CompletedTask; });
        NavigateToAutoAquariumCommand = new RelayCommand(_ => { NavigateToAutoAquariumAction?.Invoke(); return Task.CompletedTask; });
        _statusTimer = new Timer(_ => RefreshStatus(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        _aquariumTimer = new Timer(_ => AquariumTick(), null, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20));
        _rodTimer = new Timer(_ => RefreshEquippedRod(), null, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2));
    }

    public Action? NavigateToAutoTotemAction { get; set; }

    public Action? NavigateToAutoAquariumAction { get; set; }

    public IReadOnlyList<FishingTrackerOption> TrackerOptions { get; } =
    [
        new(FishingTrackerMode.Tracking1, "Predict", "- Tryhard Rod"),
        new(FishingTrackerMode.Tracking2, "Spam", ""),
        new(FishingTrackerMode.Tracking3, "Hybrid", ""),
    ];

    public IReadOnlyList<FishingCastingMode> CastingModes { get; } =
    [
        FishingCastingMode.Normal,
        FishingCastingMode.Perfect,
    ];

    public FishingTrackerOption SelectedTracker
    {
        get => _selectedTracker;
        set
        {
            if (!SetProperty(ref _selectedTracker, value))
            {
                return;
            }

            if (IsRunning)
            {
                StopCurrent();
            }

            OnPropertyChanged(nameof(SelectedMode));
            RefreshStatus();
        }
    }

    public FishingTrackerMode SelectedMode => SelectedTracker.Mode;

    public FishingCastingMode SelectedCastingMode
    {
        get => _selectedCastingMode;
        set
        {
            if (!SetProperty(ref _selectedCastingMode, value))
            {
                return;
            }

            foreach (var tracker in _trackers.Values)
            {
                tracker.CastingMode = value;
            }

            if (IsRunning)
            {
                StopCurrent();
            }

            RefreshStatus();
        }
    }

    public bool IsRunning => _macroRequestedRunning || _status.IsRunning || _aquariumPending || _aquariumActive;

    public bool IsAquariumSequenceRunning => _aquariumPending || _aquariumActive;

    public bool AutoAquariumEnabled
    {
        get => _autoAquariumEnabled;
        set
        {
            if (!SetProperty(ref _autoAquariumEnabled, value))
            {
                return;
            }

            AppLog.Fishing("FlowVM", $"AutoAquariumEnabled set to {value}");

            if (!value)
            {
                AppLog.Fishing("FlowVM", $"AutoAquariumEnabled=false | tearing down | {AquariumState()}");
                _aquariumPending = false;
                _aquariumActive = false;
                ReleaseAquariumGate();
                _nextAquariumAt = null;
                _aquariumRunner.Reset();
                ResumeCurrent();
            }

            UpdateAquariumStatusText();
        }
    }

    public double AutoAquariumCycleDelayMinutes
    {
        get => _autoAquariumCycleDelayMinutes;
        set
        {
            var normalized = Math.Clamp(value, 0, 1440);
            if (SetProperty(ref _autoAquariumCycleDelayMinutes, normalized))
            {
                UpdateAquariumStatusText();
            }
        }
    }

    public string AquariumStatusText
    {
        get => _aquariumStatusText;
        private set => SetProperty(ref _aquariumStatusText, value);
    }

    public string EquippedRodText
    {
        get => _equippedRodText;
        private set => SetProperty(ref _equippedRodText, value);
    }

    public IReadOnlyList<ColoredTextLineViewModel> EquippedRodLines
    {
        get => _equippedRodLines;
        private set => SetProperty(ref _equippedRodLines, value);
    }

    public string RodHeaderText
    {
        get => _rodHeaderText;
        private set => SetProperty(ref _rodHeaderText, value);
    }

    public string MasterlineRodNamesText
    {
        get => _masterlineRodNamesText;
        private set => SetProperty(ref _masterlineRodNamesText, value);
    }

    public bool IsMasterlineEquipped
    {
        get => _isMasterlineEquipped;
        private set => SetProperty(ref _isMasterlineEquipped, value);
    }

    public string Phase => IsRunning ? _status.Phase : "---";

    // Shared post-catch settle signal sourced from the active tracker. External
    // add-ons (Auto Aquarium, Auto Sovereign Recharge, etc.) must gate their
    // actions on this so they do not fire into the game's post-catch input
    // lockout window.
    public bool IsAutomationGateOpen => _status.IsAutomationGateOpen;

    public string StatusMessage => _status.Message;

    public string ProgressText => _status.ProgressPercent is { } value ? $"{value:0.0}%" : "---";

    public string HoldingText => IsRunning ? (_status.IsHolding ? "Holding" : "Released") : "---";

    /// <summary>Latest reel mini-game snapshot from the active tracker. Null when no minigame is on-screen.</summary>
    public ReelSnapshotData? CurrentReelSnapshot => _status.ReelSnapshot;

    public IReadOnlyList<AppKeyValueItem> StatusItems =>
    [
        new AppKeyValueItem("Phase", Phase),
        new AppKeyValueItem("Progress", ProgressText),
        new AppKeyValueItem("Input", HoldingText),
        new AppKeyValueItem(RodHeaderText, EquippedRodText, ColoredLines: EquippedRodLines),
    ];

    public IReadOnlyList<AppKeyValueItem> StatsItems =>
    [
        new AppKeyValueItem("Caught", _status.ReelStats.Caught.ToString()),
        new AppKeyValueItem("Lost", _status.ReelStats.Lost.ToString()),
        new AppKeyValueItem("Success Rate", $"{_status.ReelStats.SuccessRatePercent:0.0}%"),
        new AppKeyValueItem("Fish skipped", "---"),
    ];

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand NavigateToAutoTotemCommand { get; }

    public RelayCommand NavigateToAutoAquariumCommand { get; }

    public Task StartAsync()
    {
        AppLog.Fishing("FlowVM", $"StartAsync ENTER | mode={SelectedMode} castingMode={SelectedCastingMode} aquariumEnabled={AutoAquariumEnabled} interval={AutoAquariumCycleDelayMinutes:0.###}min | {AquariumState()}");
        _macroRequestedRunning = true;
        _wasInMinigame = false;
        StartCurrentTracker();
        AppLog.Fishing("FlowVM", "StartAsync tracker started");
        if (AutoAquariumEnabled)
        {
            AppLog.Fishing("FlowVM", "StartAsync auto-queueing aquarium (aquarium enabled at start)");
            QueueAquariumRun();
        }

        RefreshStatus();
        OnPropertyChanged(nameof(IsRunning));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        AppLog.Fishing("FlowVM", $"StartAsync EXIT | {AquariumState()} | {StatusSnapshot(_status)}");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        AppLog.Fishing("FlowVM", $"StopAsync ENTER | {AquariumState()} | {StatusSnapshot(_status)}");
        _macroRequestedRunning = false;
        _aquariumPending = false;
        _aquariumActive = false;
        ReleaseAquariumGate();
        AutomationInputGate.Reset();
        AppLog.Fishing("FlowVM", "StopAsync forced AutomationInputGate.Reset()");
        _nextAquariumAt = null;
        _aquariumRunner.Reset();
        StopCurrent();
        UpdateAquariumStatusText();
        RefreshStatus();
        OnPropertyChanged(nameof(IsRunning));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        AppLog.Fishing("FlowVM", $"StopAsync EXIT | {AquariumState()}");
        return Task.CompletedTask;
    }

    private IFishingTracker GetTracker(FishingTrackerMode mode)
    {
        if (_trackers.TryGetValue(mode, out var tracker))
        {
            return tracker;
        }

        tracker = mode switch
        {
            FishingTrackerMode.Tracking1 => new Tracking1FishingTracker(FishingTrackerMode.Tracking1),
            FishingTrackerMode.Tracking2 => new Tracking1FishingTracker(FishingTrackerMode.Tracking2),
            FishingTrackerMode.Tracking3 => new Tracking1FishingTracker(FishingTrackerMode.Tracking3),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
        tracker.CastingMode = SelectedCastingMode;
        _trackers[mode] = tracker;
        return tracker;
    }

    private void StopCurrent()
    {
        if (_trackers.TryGetValue(SelectedMode, out var tracker))
        {
            tracker.Stop();
        }
    }

    private void SuspendCurrent(string message)
    {
        if (_trackers.TryGetValue(SelectedMode, out var tracker))
        {
            tracker.Suspend(message);
        }
    }

    private void ResumeCurrent()
    {
        if (_trackers.TryGetValue(SelectedMode, out var tracker))
        {
            tracker.Resume();
        }
    }

    private void StartCurrentTracker()
    {
        var tracker = GetTracker(SelectedMode);
        tracker.CastingMode = SelectedCastingMode;
        tracker.Start();
    }

    private void QueueAquariumRun()
    {
        AppLog.Fishing("Aquarium", $"QueueAquariumRun ENTER | {AquariumState()} | {StatusSnapshot(_status)}");
        _aquariumPending = true;
        _aquariumActive = false;
        _aquariumRunner.Reset();
        AppLog.Fishing("Aquarium", "QueueAquariumRun runner reset; pending=true active=false");
        if (!_aquariumGateHeld && AutomationInputGate.TryEnter(AquariumGateOwner, AquariumGatePriority))
        {
            _aquariumGateHeld = true;
            AppLog.Fishing("Aquarium", "QueueAquariumRun acquired input gate (was unheld)");
        }
        else
        {
            AppLog.Fishing("Aquarium", $"QueueAquariumRun gate-state unchanged | held={_aquariumGateHeld}");
        }
        SuspendCurrent("Auto Aquarium queued.");
        AppLog.Fishing("Aquarium", "QueueAquariumRun called SuspendCurrent(\"Auto Aquarium queued.\")");
        UpdateAquariumStatusText();
        AppLog.Fishing("Aquarium", $"QueueAquariumRun EXIT | {AquariumState()}");
    }

    private void AquariumTick()
    {
        if (_aquariumTicking)
        {
            return;
        }

        if (!_macroRequestedRunning || !AutoAquariumEnabled || !_aquariumPending)
        {
            return;
        }

        AppLog.Fishing("AquariumTick", $"ENTER | {AquariumState()} | {StatusSnapshot(_status)}");
        _aquariumTicking = true;
        try
        {
            if (!_aquariumGateHeld && !AutomationInputGate.TryEnter(AquariumGateOwner, AquariumGatePriority))
            {
                AppLog.Fishing("AquariumTick", "DECISION=wait-other-addon | gate held by other; calling SuspendCurrent");
                SuspendCurrent("Auto Aquarium waiting for other add-on.");
                UpdateAquariumStatusText();
                return;
            }

            _aquariumGateHeld = true;
            _aquariumActive = true;
            SuspendCurrent("Auto Aquarium running.");
            AppLog.Fishing("AquariumTick", "Suspended tracker; stepping aquarium runner");
            var result = _aquariumRunner.Step(TimeSpan.FromMilliseconds(250));
            AppLog.Fishing("AquariumTick", $"Runner step result: completed={result.Completed}");
            if (result.Completed)
            {
                _aquariumPending = false;
                _aquariumActive = false;
                ReleaseAquariumGate();
                _nextAquariumAt = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(AutoAquariumCycleDelayMinutes);
                _wasInMinigame = false;
                AppLog.Fishing("AquariumTick", $"DECISION=complete | next aquarium at {_nextAquariumAt:O} | calling ResumeCurrent");
                ResumeCurrent();
            }
        }
        catch (Exception ex)
        {
            AppLog.FishingError("AquariumTick", "Runner step threw", ex);
            _aquariumPending = false;
            _aquariumActive = false;
            ReleaseAquariumGate();
            _nextAquariumAt = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(AutoAquariumCycleDelayMinutes);
            _aquariumRunner.Reset();
            if (_macroRequestedRunning)
            {
                AppLog.Fishing("AquariumTick", "Exception path: calling ResumeCurrent");
                ResumeCurrent();
            }

            AquariumStatusText = $"Auto Aquarium skipped: {ex.Message}";
            return;
        }
        finally
        {
            _aquariumTicking = false;
            AppLog.Fishing("AquariumTick", $"EXIT | {AquariumState()}");
        }

        UpdateAquariumStatusText();
    }

    private void ReleaseAquariumGate()
    {
        if (!_aquariumGateHeld)
        {
            AppLog.Fishing("Aquarium", "ReleaseAquariumGate noop (not held)");
            return;
        }

        AutomationInputGate.Exit(AquariumGateOwner);
        _aquariumGateHeld = false;
        AppLog.Fishing("Aquarium", "ReleaseAquariumGate exited gate");
    }

    private string AquariumState()
    {
        var due = _nextAquariumAt is { } d && DateTimeOffset.UtcNow >= d;
        return $"pending={_aquariumPending} active={_aquariumActive} ticking={_aquariumTicking} gateHeld={_aquariumGateHeld} wasInMinigame={_wasInMinigame} nextAt={_nextAquariumAt?.ToString("O") ?? "null"} due={due} macroRunning={_macroRequestedRunning} enabled={_autoAquariumEnabled}";
    }

    private static string StatusSnapshot(FishingTrackerStatus s)
    {
        var reel = s.ReelSnapshot is null
            ? "null"
            : $"fish={s.ReelSnapshot.FishCenter:0.000}/bar={s.ReelSnapshot.PlayerbarCenter:0.000}/w={s.ReelSnapshot.PlayerbarWidth:0.000}";
        return $"phase={s.Phase} msg=\"{s.Message}\" holding={s.IsHolding} gateOpen={s.IsAutomationGateOpen} progress={(s.ProgressPercent?.ToString("0.0") ?? "null")} reel={reel}";
    }

    private void RefreshStatus()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(RefreshStatus);
            return;
        }

        var status = _trackers.TryGetValue(SelectedMode, out var tracker)
            ? tracker.Status
            : new FishingTrackerStatus(false, "OFF", $"{SelectedTracker.Title} ready.", null, false);

        // The aquarium scheduler's decision depends on wall-clock time
        // (msUntilDue), not just on the status record. If the tracker is
        // stuck publishing an identical Status every tick (e.g. parked in
        // wait->ADDON-blocked while we hold the gate), dedup would skip the
        // scheduler forever and the cycle-due transition would be missed.
        // Always run it; dedup only the UI notifications below.
        UpdateAquariumSchedule(status);

        if (_status == status)
        {
            return;
        }

        var phaseChanged = !string.Equals(_status.Phase, status.Phase, StringComparison.Ordinal);
        if (phaseChanged)
        {
            AppLog.Fishing("Status", $"PHASE-CHANGE {_status.Phase} -> {status.Phase} | new={StatusSnapshot(status)} | {AquariumState()}");
        }
        else
        {
            AppLog.Fishing("Status", $"STATUS-DELTA phase={status.Phase} | {StatusSnapshot(status)}");
        }

        _status = status;
        UpdateMasterlineRodNamesFromStatus(status.Message);
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(HoldingText));
        OnPropertyChanged(nameof(StatusItems));
        OnPropertyChanged(nameof(StatsItems));
        StartCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }

    internal void ApplyStatusForTests(FishingTrackerStatus status)
    {
        _status = status;
        UpdateMasterlineRodNamesFromStatus(status.Message);
        OnPropertyChanged(nameof(StatsItems));
    }

    private void RefreshEquippedRod()
    {
        try
        {
            var snapshot = _rodReader.GetSnapshot();
            SetRodState(snapshot.RodName, snapshot.IsEquipped ? "Equipped" : "Unequipped");
            var masterlineNames = _rodReader.GetMasterlineOverlayRodNames();
            SetMasterlineRodNames(masterlineNames);
        }
        catch
        {
            SetRodState("---", "Unequipped");
            SetMasterlineRodNames(Array.Empty<string>());
        }
    }

    private void SetRodState(string rodText, string equippedState)
    {
        var isMasterline = rodText.Contains("masterline", StringComparison.OrdinalIgnoreCase);
        SetEquippedRodText(rodText);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsMasterlineEquipped = isMasterline;
                RodHeaderText = $"Rod - {equippedState}";
                OnPropertyChanged(nameof(StatusItems));
            });
            return;
        }

        IsMasterlineEquipped = isMasterline;
        RodHeaderText = $"Rod - {equippedState}";
        OnPropertyChanged(nameof(StatusItems));
    }

    private void SetEquippedRodText(string value)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetEquippedRodText(value));
            return;
        }

        EquippedRodText = value;
        EquippedRodLines = ColoredEnchantText.BuildLines(value);
        OnPropertyChanged(nameof(StatusItems));
    }

    private void UpdateAquariumSchedule(FishingTrackerStatus status)
    {
        AppLog.Fishing("Schedule", $"ENTER | {AquariumState()} | {StatusSnapshot(status)}");

        if (!_macroRequestedRunning || !AutoAquariumEnabled || _aquariumPending || _aquariumActive)
        {
            AppLog.Fishing("Schedule", $"DECISION=skip-precondition | macroRunning={_macroRequestedRunning} enabled={AutoAquariumEnabled} pending={_aquariumPending} active={_aquariumActive}");
            return;
        }

        if (string.Equals(status.Phase, "FISHING", StringComparison.OrdinalIgnoreCase))
        {
            var wasFlipped = !_wasInMinigame;
            _wasInMinigame = true;
            AppLog.Fishing("Schedule", $"DECISION=mark-in-minigame | wasInMinigame: false->true={wasFlipped}");
            return;
        }

        if (_nextAquariumAt is not { } dueAt)
        {
            AppLog.Fishing("Schedule", "DECISION=not-scheduled | nextAt=null");
            UpdateAquariumStatusText();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var msUntilDue = (dueAt - now).TotalMilliseconds;
        var isDue = msUntilDue <= 0;
        var isDueSoon = msUntilDue <= CycleDueSoonMs;

        if (!isDueSoon)
        {
            AppLog.Fishing("Schedule", $"DECISION=not-due | nextAt={dueAt:O} remainingSec={msUntilDue / 1000.0:0.000}");
            UpdateAquariumStatusText();
            return;
        }

        if (!_wasInMinigame)
        {
            AppLog.Fishing("Schedule", $"DECISION=wait-for-minigame | due/due-soon but no FISHING phase seen yet | msUntilDue={msUntilDue:0}");
            UpdateAquariumStatusText();
            return;
        }

        // Pre-acquire the shared input gate up to CycleDueSoonMs ahead of the
        // strict due time. The tracker's ShouldBlockCastingForPendingSequence
        // sees the held gate and sets the post-catch recast hold accordingly.
        // Without this pre-acquisition, a catch landing in the last ~3 s
        // before the cycle becomes due lets the tracker spam-cast through
        // the gap; the resulting in-flight cast lands during the upcoming
        // aquarium suspend and the next reel is mid-minigame on resume.
        if (!_aquariumGateHeld)
        {
            if (!AutomationInputGate.TryEnter(AquariumGateOwner, AquariumGatePriority))
            {
                AppLog.Fishing("Schedule", $"DECISION=wait-other-addon-holds-gate | TryEnter failed | msUntilDue={msUntilDue:0}");
                UpdateAquariumStatusText();
                return;
            }

            _aquariumGateHeld = true;
            AppLog.Fishing("Schedule", $"GATE-ACQUIRED-EARLY | acquired AUTO_AQUARIUM input gate | msUntilDue={msUntilDue:0}");
        }
        else
        {
            AppLog.Fishing("Schedule", $"GATE-ALREADY-HELD | msUntilDue={msUntilDue:0}");
        }

        // Don't actually queue until the cycle is strictly due. The eager
        // gate acquisition above is enough to make the tracker hold the
        // recast; we just need to wait out the remaining lead time before
        // suspending and running the sequence.
        if (!isDue)
        {
            AppLog.Fishing("Schedule", $"DECISION=hold-until-strict-due | msUntilDue={msUntilDue:0} | gate held, tracker will block recast");
            UpdateAquariumStatusText();
            return;
        }

        // Wait for the tracker's post-catch settle signal. With the gate now
        // held, the tracker is also holding the recast, so this will open at
        // the next clean catch boundary.
        if (!status.IsAutomationGateOpen)
        {
            AppLog.Fishing("Schedule", $"DECISION=wait-gate-not-open | phase={status.Phase} gateOpen=false");
            UpdateAquariumStatusText();
            return;
        }

        // Accept CASTING (post-catch recast hold), ADDON (any add-on
        // pending), or TOTEM (Auto Totem queued and waiting for our gate —
        // tracker has already stopped fishing, so it is safe and we MUST
        // run, otherwise we deadlock totem-vs-aquarium-vs-gate).
        var phaseOk =
            string.Equals(status.Phase, "CASTING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Phase, "ADDON", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status.Phase, "TOTEM", StringComparison.OrdinalIgnoreCase);
        if (!phaseOk)
        {
            AppLog.Fishing("Schedule", $"DECISION=wait-phase-not-safe | phase={status.Phase}");
            UpdateAquariumStatusText();
            return;
        }

        if (status.IsHolding)
        {
            AppLog.Fishing("Schedule", $"DECISION=wait-tracker-holding | phase={status.Phase} holding=true");
            UpdateAquariumStatusText();
            return;
        }

        AppLog.Fishing("Schedule", $"DECISION=queue | BOUNDARY-REACHED dueAt={dueAt:O} phase={status.Phase} gateOpen=true holding=false");
        _wasInMinigame = false;
        QueueAquariumRun();
    }

    private void UpdateAquariumStatusText()
    {
        if (!AutoAquariumEnabled)
        {
            AquariumStatusText = "Auto Aquarium off.";
            return;
        }

        if (_aquariumPending || _aquariumActive)
        {
            AquariumStatusText = "Auto Aquarium running.";
            return;
        }

        if (_nextAquariumAt is { } next)
        {
            var remaining = next - DateTimeOffset.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
            }

            AquariumStatusText = $"Next aquarium in {remaining.TotalMinutes:0.0} min.";
            return;
        }

        AquariumStatusText = $"Auto Aquarium ready. Cycle delay {AutoAquariumCycleDelayMinutes:0.##} min.";
    }

    private void UpdateMasterlineRodNamesFromStatus(string message)
    {
        const string marker = "Masterline rods:";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return;
        }

        var names = message[(markerIndex + marker.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(names))
        {
            return;
        }

        MasterlineRodNamesText = names;
    }

    private void SetMasterlineRodNames(IReadOnlyList<string> names)
    {
        var text = names.Count == 0 ? "---" : string.Join(" | ", names);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => MasterlineRodNamesText = text);
            return;
        }

        MasterlineRodNamesText = text;
    }
}

public sealed record FishingTrackerOption(FishingTrackerMode Mode, string Title, string Description)
{
    public Thickness DescriptionMargin => string.Equals(Description, "♕", StringComparison.Ordinal)
        ? new Thickness(0, -1, 0, 0)
        : default;

    public override string ToString()
    {
        return Title;
    }
}
