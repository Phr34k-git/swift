using System;
using System.Globalization;
using System.Threading;
using Avalonia.Threading;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class AutoSovereignRechargeViewModel : ViewModelBase
{
    private readonly AutoSovereignRechargeRunner _runner = new();
    private readonly FishingViewModel _fishingViewModel;
    private readonly Timer _timer;
    private bool _enabled;
    private double _minimumPercent = 95.0;
    private double _maximumPercent = 99.0;
    private string _minimumPercentText = "95";
    private string _maximumPercentText = "99";
    private double? _currentPowerPercent;
    private string _statusText = "---";
    private int _stepRunning;
    private bool _sovereignPendingAfterFish;
    private bool _sovereignSawFishingPhase;
    private DateTimeOffset _nextReadAt = DateTimeOffset.MinValue;
    private DateTimeOffset _resumeAt = DateTimeOffset.MinValue;
    private DateTimeOffset _startLoopAt = DateTimeOffset.MinValue;
    private int _belowMinConfirmCount;
    private bool _lastMacroRunningState;
    private bool _pendingAwaitFishingLoop;
    private bool _pendingSawFishingSinceQueued;

    public AutoSovereignRechargeViewModel(FishingViewModel fishingViewModel)
    {
        _fishingViewModel = fishingViewModel;
        _lastMacroRunningState = fishingViewModel.IsRunning;
        AutoSovereignRechargeSettings.Enabled = _enabled;
        AutoSovereignRechargeSettings.MinimumPercent = _minimumPercent;
        AutoSovereignRechargeSettings.MaximumPercent = _maximumPercent;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
        _fishingViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(FishingViewModel.IsRunning))
            {
                return;
            }

            var isRunningNow = _fishingViewModel.IsRunning;
            if (isRunningNow == _lastMacroRunningState)
            {
                return;
            }

            _lastMacroRunningState = isRunningNow;
            if (!isRunningNow)
            {
                _runner.Reset();
                _currentPowerPercent = null;
                _belowMinConfirmCount = 0;
                AutoSovereignRechargeSettings.SequenceStartRequested = false;
                Dispatcher.UIThread.Post(() =>
                {
                    OnPropertyChanged(nameof(CurrentPowerText));
                    StatusText = Enabled ? CurrentPowerText : "---";
                });
                return;
            }

            // Keep sovereign fully idle during tracker startup to avoid any
            // startup interaction with Predict/Responsive/Hybrid.
            _sovereignPendingAfterFish = false;
            _sovereignSawFishingPhase = false;
            _belowMinConfirmCount = 0;
            _pendingAwaitFishingLoop = false;
            _pendingSawFishingSinceQueued = false;
            _resumeAt = DateTimeOffset.UtcNow.AddSeconds(4);
            _nextReadAt = _resumeAt;
            if (_enabled)
            {
                // Match Aquarium startup behavior: request an immediate startup
                // sequence launch when macro starts.
                _sovereignPendingAfterFish = true;
                AutoSovereignRechargeSettings.PendingRequested = true;
                AutoSovereignRechargeSettings.SequenceStartRequested = true;
                AutoSovereignRechargeSettings.PauseFishingRequested = true;
            }
        };
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (!SetProperty(ref _enabled, value))
            {
                return;
            }

            AutoSovereignRechargeSettings.Enabled = value;
            if (!value)
            {
                _runner.Reset();
                _currentPowerPercent = null;
                AutoSovereignRechargeSettings.PauseFishingRequested = false;
                AutoSovereignRechargeSettings.PendingRequested = false;
                AutoSovereignRechargeSettings.SequenceStartRequested = false;
                OnPropertyChanged(nameof(CurrentPowerText));
                StatusText = "---";
                _sovereignPendingAfterFish = false;
                _sovereignSawFishingPhase = false;
                _belowMinConfirmCount = 0;
                _pendingAwaitFishingLoop = false;
                _pendingSawFishingSinceQueued = false;
                _startLoopAt = DateTimeOffset.MinValue;
                _resumeAt = DateTimeOffset.UtcNow.AddMilliseconds(1200);
            }
            else
            {
                _belowMinConfirmCount = 0;
                _startLoopAt = DateTimeOffset.MinValue;
                _resumeAt = DateTimeOffset.UtcNow.AddMilliseconds(1200);
                _nextReadAt = DateTimeOffset.MinValue;
                StatusText = CurrentPowerText;
                if (_fishingViewModel.IsRunning)
                {
                    _sovereignPendingAfterFish = true;
                    AutoSovereignRechargeSettings.PendingRequested = true;
                    AutoSovereignRechargeSettings.SequenceStartRequested = true;
                    AutoSovereignRechargeSettings.PauseFishingRequested = true;
                }
            }
        }
    }

    public double MinimumPercent
    {
        get => _minimumPercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (!SetProperty(ref _minimumPercent, normalized))
            {
                return;
            }

            if (_maximumPercent < normalized)
            {
                MaximumPercent = normalized;
            }

            AutoSovereignRechargeSettings.MinimumPercent = _minimumPercent;
            MinimumPercentText = _minimumPercent.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    public double MaximumPercent
    {
        get => _maximumPercent;
        set
        {
            var normalized = Math.Clamp(value, 0, 100);
            if (normalized < _minimumPercent)
            {
                normalized = _minimumPercent;
            }

            if (!SetProperty(ref _maximumPercent, normalized))
            {
                return;
            }

            AutoSovereignRechargeSettings.MaximumPercent = _maximumPercent;
            MaximumPercentText = _maximumPercent.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    public string MinimumPercentText
    {
        get => _minimumPercentText;
        set
        {
            if (!SetProperty(ref _minimumPercentText, value))
            {
                return;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                MinimumPercent = parsed;
            }
        }
    }

    public string MaximumPercentText
    {
        get => _maximumPercentText;
        set
        {
            if (!SetProperty(ref _maximumPercentText, value))
            {
                return;
            }

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                MaximumPercent = parsed;
            }
        }
    }

    public string CurrentPowerText => _currentPowerPercent is null
        ? "---"
        : $"{_currentPowerPercent.Value:0.0}%";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private void Tick()
    {
        if (!Enabled)
        {
            Dispatcher.UIThread.Post(() => StatusText = "---");
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var livePower = _runner.ReadCurrentPowerPercent();
        Dispatcher.UIThread.Post(() =>
        {
            if (livePower is { } value)
            {
                _currentPowerPercent = value;
            }

            OnPropertyChanged(nameof(CurrentPowerText));
            StatusText = CurrentPowerText;
        });

        var isRunning = _fishingViewModel.IsRunning;
        var isFishingNow = isRunning && string.Equals(_fishingViewModel.Phase, "FISHING", StringComparison.OrdinalIgnoreCase);
        if (AutoSovereignRechargeSettings.SequenceStartRequested)
        {
            // Orchestrated sovereign sequencing should begin quickly; do not wait
            // through long passive resume delays.
            _resumeAt = DateTimeOffset.MinValue;
        }

        if (!AutoSovereignRechargeSettings.SequenceStartRequested && now < _resumeAt)
        {
            return;
        }

        var isFishing = isFishingNow;
        var safeBoundary = isRunning && IsSafePostFishBoundary();
        if (!isRunning)
        {
            _sovereignPendingAfterFish = false;
            _sovereignSawFishingPhase = false;
            _belowMinConfirmCount = 0;
            _pendingAwaitFishingLoop = false;
            _pendingSawFishingSinceQueued = false;
            _startLoopAt = DateTimeOffset.MinValue;
            AutoSovereignRechargeSettings.PauseFishingRequested = false;
            AutoSovereignRechargeSettings.PendingRequested = false;
            AutoSovereignRechargeSettings.SequenceStartRequested = false;
            Dispatcher.UIThread.Post(() => StatusText = CurrentPowerText);
            return;
        }

        if (isFishing)
        {
            _sovereignSawFishingPhase = true;
            if (_pendingAwaitFishingLoop && AutoSovereignRechargeSettings.PendingRequested)
            {
                _pendingSawFishingSinceQueued = true;
            }

            if (!AutoSovereignRechargeSettings.RuntimeBusy &&
                livePower is { } duringFishPower &&
                duringFishPower < MinimumPercent)
            {
                _belowMinConfirmCount = Math.Min(_belowMinConfirmCount + 1, 10);
                QueuePending(requireFishingLoop: true);
            }
            else if (!AutoSovereignRechargeSettings.RuntimeBusy)
            {
                _belowMinConfirmCount = 0;
            }

            return;
        }

        var sequenceRequested = AutoSovereignRechargeSettings.PendingRequested ||
            AutoSovereignRechargeSettings.SequenceStartRequested ||
            AutoSovereignRechargeSettings.RuntimeBusy;
        if (!sequenceRequested)
        {
            _startLoopAt = DateTimeOffset.MinValue;
            AutoSovereignRechargeSettings.PauseFishingRequested = false;
            Dispatcher.UIThread.Post(() => StatusText = CurrentPowerText);
            return;
        }

        if (!AutoSovereignRechargeSettings.RuntimeBusy)
        {
            var externalPending = AutoSovereignRechargeSettings.PendingRequested;
            if (externalPending)
            {
                _sovereignPendingAfterFish = true;
                if (_sovereignSawFishingPhase && !_pendingAwaitFishingLoop)
                {
                    _pendingAwaitFishingLoop = true;
                    _pendingSawFishingSinceQueued = false;
                }
            }

            if (AutoSovereignRechargeSettings.SequenceStartRequested)
            {
                // Orchestrator has already selected a safe post-fish boundary.
                // Start immediately on requested boundary.
                AutoSovereignRechargeSettings.PauseFishingRequested = true;
                AutoSovereignRechargeSettings.PendingRequested = true;
                _startLoopAt = DateTimeOffset.MinValue;
                AppLog.Info("Sovereign", "Starting sequence immediately from SequenceStartRequested.");
            }
            else
            {
                var loopReady = !_pendingAwaitFishingLoop || _pendingSawFishingSinceQueued;
                var boundaryQueued = _sovereignSawFishingPhase && _sovereignPendingAfterFish && loopReady;
                var readyByBoundary = safeBoundary && boundaryQueued && externalPending;
                var launchArmed = _startLoopAt != DateTimeOffset.MinValue;

                // As soon as sovereign is due/queued after a fish cycle, block casting
                // so tracker cannot cast between queue and sequence start.
                if (boundaryQueued || launchArmed)
                {
                    AutoSovereignRechargeSettings.PauseFishingRequested = true;
                    AutoSovereignRechargeSettings.PendingRequested = true;
                }

                if (!readyByBoundary && !launchArmed)
                {
                    _startLoopAt = DateTimeOffset.MinValue;
                    AutoSovereignRechargeSettings.PauseFishingRequested = false;
                    AutoSovereignRechargeSettings.PendingRequested = externalPending;
                    Dispatcher.UIThread.Post(() => StatusText = CurrentPowerText);
                    return;
                }

                // Pause and start immediately once sovereign is approved.
                AutoSovereignRechargeSettings.PauseFishingRequested = true;
                AutoSovereignRechargeSettings.PendingRequested = true;
                _startLoopAt = DateTimeOffset.MinValue;
                AppLog.Info("Sovereign", $"Starting sequence at boundary. safeBoundary={safeBoundary}, loopReady={loopReady}, pending={externalPending}");
            }
        }

        if (Interlocked.Exchange(ref _stepRunning, 1) == 1)
        {
            return;
        }

        try
        {
            // Pause tracker only while sovereign is actively stepping at boundary.
            AutoSovereignRechargeSettings.PauseFishingRequested = true;
            var result = _runner.Step(MinimumPercent, MaximumPercent);
            Dispatcher.UIThread.Post(() =>
            {
                _currentPowerPercent = result.CurrentPowerPercent;
                OnPropertyChanged(nameof(CurrentPowerText));
                OnPropertyChanged(nameof(StatusText));
            });

            if (!AutoSovereignRechargeSettings.RuntimeBusy)
            {
                _sovereignPendingAfterFish = false;
                _sovereignSawFishingPhase = false;
                _belowMinConfirmCount = 0;
                _pendingAwaitFishingLoop = false;
                _pendingSawFishingSinceQueued = false;
                _startLoopAt = DateTimeOffset.MinValue;
                AutoSovereignRechargeSettings.PauseFishingRequested = false;
                AutoSovereignRechargeSettings.PendingRequested = false;
                AutoSovereignRechargeSettings.SequenceStartRequested = false;
                Dispatcher.UIThread.Post(() => StatusText = CurrentPowerText);
            }
        }
        catch (Exception)
        {
            _belowMinConfirmCount = 0;
            _pendingAwaitFishingLoop = false;
            _pendingSawFishingSinceQueued = false;
            AutoSovereignRechargeSettings.PauseFishingRequested = false;
            AutoSovereignRechargeSettings.PendingRequested = false;
            AutoSovereignRechargeSettings.SequenceStartRequested = false;
            Dispatcher.UIThread.Post(() =>
            {
                _currentPowerPercent = null;
                OnPropertyChanged(nameof(CurrentPowerText));
                OnPropertyChanged(nameof(StatusText));
            });
        }
        finally
        {
            Volatile.Write(ref _stepRunning, 0);
        }
    }

    private bool IsSafePostFishBoundary()
    {
        // Safe window after fishing and before recast. We require both the
        // CASTING phase AND the tracker's shared post-catch settle gate to
        // have opened — the phase transitions to CASTING the same tick the
        // completion threshold is hit, which is still inside the game's
        // post-catch input lockout. The gate enforces the settle delay so
        // sovereign input does not land in a dead input frame.
        return string.Equals(_fishingViewModel.Phase, "CASTING", StringComparison.OrdinalIgnoreCase) &&
            _fishingViewModel.IsAutomationGateOpen;
    }

    private void QueuePending(bool requireFishingLoop)
    {
        _sovereignPendingAfterFish = true;
        AutoSovereignRechargeSettings.PendingRequested = true;
        if (requireFishingLoop)
        {
            if (!_pendingAwaitFishingLoop)
            {
                _pendingAwaitFishingLoop = true;
                _pendingSawFishingSinceQueued = false;
            }

            // If pending is raised while currently in/after fishing phase, count that
            // cycle as the required loop so boundary execution can proceed on exit.
            if (_sovereignSawFishingPhase)
            {
                _pendingSawFishingSinceQueued = true;
            }
        }
    }
}
