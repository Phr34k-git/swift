using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia.Media;
using Client.Services;

namespace Client.Services.Fishing;

public sealed class Tracking1FishingTracker : IFishingTracker
{
    private const string AutoTotemGateOwner = "AUTO_TOTEM";
    private const int AutoTotemGatePriority = 200;
    private const int AutoTotemAwaitFishCycleTimeoutMs = 30000;
    private const int AutoTotemRetryDelayMs = 180000;
    private const int AutoTotemPostCatchSettleMs = 600;
    private const int PostCatchRecastHoldMs = 800;
    // Always hold for at least this long after a catch before issuing the
    // next cast. Without this floor, the tracker's first post-completion
    // tick presses LMB before any add-on scheduler (Auto Aquarium / Totem)
    // can engage the gate, so a cast goes out into the water during the
    // ~50-100 ms scheduler reaction window. The cast then results in a
    // new reel mid-aquarium-suspend, which is the actual root cause of the
    // "ignored reel after SUSPENDED" symptom. 400 ms is ~4-8x the worst-
    // case scheduler reaction latency (RefreshStatus 50 ms cadence +
    // AquariumTimer 20 ms cadence) and well under perceived input latency.
    private const int MinPostCatchHoldMs = 400;
    private const int PerfectPostFishHoldDelayMs = 1000;
    private const int PerfectPowerDetectBurstMs = 12;
    private const int PerfectPowerDetectStepMs = 2;
    private const int PerfectReleaseMicroPollMs = 14;
    private const int PerfectReleaseMicroPollStepMs = 2;
    private const int AutoTotemShortActionDelayMs = 250;
    private const int AutoTotemMediumActionDelayMs = 350;
    private const int AutoTotemLongActionDelayMs = 500;
    private readonly FishingTrackerMode _mode;
    private const int ReelInputSettleMs = 125;
    private readonly Tracking1Settings _settings = new();
    private readonly Tracking2Settings _perfectCastingSettings = new();
    private readonly Tracking2Settings _startupAssistSettings = new();
    private readonly Tracking2Settings _tracking2Settings = new();
    private readonly Tracking3Settings _tracking3Settings = new();
    private readonly RobloxMemory _memory;
    private readonly FishingRuntimeContext _context;
    private readonly Tracking1Controller _controller = new();
    private readonly Tracking1Controller _bellonaRightController = new();
    private readonly Tracking2Controller _startupAssistController = new();
    private readonly Tracking2Controller _tracking2Controller = new();
    private readonly Tracking2Controller _bellonaRightTracking2Controller = new();
    private readonly Tracking3Controller _tracking3Controller = new();
    private readonly Tracking3Controller _bellonaRightTracking3Controller = new();
    private Timer? _timer;
    private int _runId;
    private volatile bool _isRunning;
    private volatile bool _isSuspended;
    private string _suspendMessage = "Fishing suspended.";
    private bool _inTick;
    private double _nextProbeAt;
    private long _castStartedAt;
    private long _castReleasedAt;
    private long _lastShakedAt;
    private long _fishingLostAt;
    private long _castReadyAt;
    private long _completionRecastReadyAt;
    private long _fishingInputReadyAt;
    private bool _castBarSeen;
    private bool _completionReached;
    private bool _bellonaLeftCompletionReached;
    private bool _bellonaRightCompletionReached;
    private long _completionLatchedAt;
    private bool _outcomeResolved;
    private double _maxCompletionProgressThisCycle;
    private FishingReelStats _reelStats = FishingReelStats.Empty;
    private ReelMetrics? _lastReelMetrics;
    private bool _perfectCastingHolding;
    private bool _activatedUiNav;
    private bool _hadMetricsLastTick;
    private bool _startupAssistActive;
    private long _startupAssistStartedAt;
    private double _startupAssistStartFishCenter;
    private readonly FishingHoldGate _fishingGate = new();
    private readonly TranquilityController _tranquilityController;
    private RodProfile _rodProfile = new DefaultRodProfile();
    private bool _tranquilityEngaged;
    private long _lastRodDetectAt;
    private long _normalCastHoldStartedAt;
    private long _fishingMotionSignalAt;
    private long _fishingHoldStartedAt;
    private double? _lastFishingFishCenter;
    private double? _lastFishingBarCenter;
    private double? _lastFishingProgress;
    private bool _seenReelThisRun;
    private string _totemState = "IDLE";
    private long _totemLastMaintainCheckAt;
    private bool _totemPending;
    private bool _totemNeedsRodReequip;
    private bool _totemNeedsSettleDelay;
    private bool _totemAwaitNextFishCycle;
    private bool _totemSawFishingSinceRun;
    private long _totemAwaitStartedAt;
    private long _totemQueuedAt;
    private string _totemLastFlowLog = string.Empty;
    private string _totemLastDecisionLog = string.Empty;
    private long _totemRetryAfterAt;
    private long _lastInventoryActivationAt;
    private readonly HashSet<string> _surgeChatSeenEntries = new(StringComparer.Ordinal);
    private bool _surgeChatPrimed;
    private bool _chatShinyActive;
    private bool _chatSparklingActive;
    private bool _chatMutationActive;
    private long _chatShinyActivatedAt;
    private long _chatSparklingActivatedAt;
    private long _chatMutationActivatedAt;
    private long _lastRuntimeSignalAt;
    private long _lastRecoveryAt;
    private long _rodEquipRetryAt;
    private long _lastRodEquipAttemptAt;
    private long _rodEquipHoldoffUntil;
    private int _rodUnequippedStreak;
    private const int RuntimeSignalStallMs = 15000;
    private const int RecoveryCooldownMs = 10000;
    private const int ReelReacquireGraceMs = 2500;
    private const int FishingHoldStallMs = 3000;
    private const int RodEquipStateLagMs = 2200;
    private const int SovereignPendingStallMs = 8000;
    private const int SurgeMaxActiveMs = 20 * 60 * 1000;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private long _sovereignPendingSince;
    private string _lastCastGateReason = string.Empty;
    private string _lastDebugState = string.Empty;
    private long _debugTickSeq;
    private long _splitbranchLastCrateClickAt;
    private const int SplitbranchCrateClickCooldownMs = 700;
    private bool _miguCounterWasVisible;
    private bool _miguShiftFiredThisAppearance;
    private long _miguShiftFireAt;
    private const int MiguCounterShiftDelayMs = 480;
    private bool _bellonaRightHolding;
    private bool _bellonaRightLastSecondaryPresent;
    private long _bellonaRightLastLogAt;
    private bool _bellonaRightLastDesiredHolding;
    private long _bellonaLeftRightLogAt;
    private bool _bellonaSingleReelAssignRight;
    private long _bellonaRmbWatchdogLogAt;
    private bool _bellonaRightRawDesiredHolding;
    private long _bellonaRightRawDesiredChangedAt;
    private long _bellonaRightLastMetricsSeenAt;
    private double? _bellonaRightLastFishCenter;
    private double? _bellonaRightLastBarCenter;
    private long _bellonaRightLastMotionAt;
    private long _bellonaRightStaleSinceAt;
    private long _bellonaRightLastStateLogAt;
    private long _bellonaRightReacquireUntilAt;
    private long _bellonaRightHybridHoldUntilAt;
    private const int BellonaRightPressDebounceMs = 30;
    private const int BellonaRightLossGraceMs = 220;
    private const int BellonaRightStaleWatchdogMs = 700;
    private const int BellonaRightReacquireReleaseMs = 0;
    private const int BellonaRightHybridHoldBiasMs = 35;
    private long _masterlineNamesLastLogAt;

    public Tracking1FishingTracker(FishingTrackerMode mode = FishingTrackerMode.Tracking1)
    {
        _mode = mode;
        _memory = new RobloxMemory(OffsetsSourceProvider.Current);
        _context = new FishingRuntimeContext(_memory);
        _tranquilityController = new TranquilityController(_memory, _context);
        Status = new FishingTrackerStatus(false, "OFF", $"{GetModeLabel()} ready.", null, false);
    }

    public FishingTrackerMode Mode => _mode;

    public FishingTrackerStatus Status { get; private set; }

    public FishingCastingMode CastingMode { get; set; } = FishingCastingMode.Normal;

    public void Start()
    {
        if (_isRunning)
        {
            AppLog.Fishing("Tracker", "Start NO-OP (already running)");
            return;
        }
        AppLog.Fishing("Tracker", $"Start ENTER | mode={_mode} castingMode={CastingMode}");

        var runId = Interlocked.Increment(ref _runId);
        _isRunning = true;
        _isSuspended = false;
        _suspendMessage = "Fishing suspended.";
        _nextProbeAt = 0;
        _castStartedAt = Environment.TickCount64;
        _castReleasedAt = 0;
        _lastShakedAt = 0;
        _fishingLostAt = 0;
        _castReadyAt = 0;
        _completionRecastReadyAt = 0;
        _fishingInputReadyAt = 0;
        _castBarSeen = false;
        _completionReached = false;
        _bellonaLeftCompletionReached = false;
        _bellonaRightCompletionReached = false;
        _completionLatchedAt = 0;
        _outcomeResolved = false;
        _maxCompletionProgressThisCycle = 0;
        _hadMetricsLastTick = false;
        _startupAssistActive = false;
        _startupAssistStartedAt = 0;
        _startupAssistStartFishCenter = 0;
        _normalCastHoldStartedAt = 0;
        _completionRecastReadyAt = 0;
        _totemLastMaintainCheckAt = 0;
        _totemRetryAfterAt = 0;
        _totemAwaitNextFishCycle = false;
        _totemSawFishingSinceRun = false;
        _totemAwaitStartedAt = 0;
        _totemQueuedAt = 0;
        _totemLastFlowLog = string.Empty;
        _surgeChatSeenEntries.Clear();
        _surgeChatPrimed = false;
        _chatShinyActive = false;
        _chatSparklingActive = false;
        _chatMutationActive = false;
        _chatShinyActivatedAt = 0;
        _chatSparklingActivatedAt = 0;
        _chatMutationActivatedAt = 0;
        _lastRuntimeSignalAt = Environment.TickCount64;
        _fishingMotionSignalAt = _lastRuntimeSignalAt;
        _lastRecoveryAt = 0;
        _rodEquipRetryAt = 0;
        _lastRodEquipAttemptAt = 0;
        _rodEquipHoldoffUntil = 0;
        _rodUnequippedStreak = 0;
        _fishingHoldStartedAt = 0;
        _lastFishingFishCenter = null;
        _lastFishingBarCenter = null;
        _lastFishingProgress = null;
        _seenReelThisRun = false;
        _tranquilityEngaged = false;
        _lastRodDetectAt = 0;
        _miguCounterWasVisible = false;
        _miguShiftFiredThisAppearance = false;
        _miguShiftFireAt = 0;
        _bellonaRightHolding = false;
        _bellonaRightLastSecondaryPresent = false;
        _bellonaRightLastLogAt = 0;
        _bellonaRightLastDesiredHolding = false;
        _bellonaLeftRightLogAt = 0;
        _bellonaSingleReelAssignRight = false;
        _bellonaRmbWatchdogLogAt = 0;
        _bellonaRightRawDesiredHolding = false;
        _bellonaRightRawDesiredChangedAt = Environment.TickCount64;
        _bellonaRightLastMetricsSeenAt = 0;
        _bellonaRightLastFishCenter = null;
        _bellonaRightLastBarCenter = null;
        _bellonaRightLastMotionAt = 0;
        _bellonaRightStaleSinceAt = 0;
        _bellonaRightLastStateLogAt = 0;
        _bellonaRightReacquireUntilAt = 0;
        _bellonaRightHybridHoldUntilAt = 0;
        _masterlineNamesLastLogAt = 0;
        _fishingGate.Reset();
        _context.ResetCache();
        ResetActiveTrackingControllers();
        Status = Status with { IsRunning = true, Phase = "STARTING", Message = "Tracking 1 running." };
        _timer = new Timer(_ => Tick(runId), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_settings.TickMs));
    }

    public void Stop()
    {
        AppLog.Fishing("Tracker", $"Stop ENTER | wasRunning={_isRunning} wasSuspended={_isSuspended} phase={Status.Phase}");
        Interlocked.Increment(ref _runId);
        _isRunning = false;
        _isSuspended = false;
        _timer?.Dispose();
        _timer = null;
        // Drain any in-flight tick that may have passed the initial run/stop check.
        // Without this, that tick can still publish a transient running status after
        // stop has been requested, which appears as a one-press stop rebound.
        var wait = new SpinWait();
        var deadline = Environment.TickCount64 + 300;
        while (_inTick && Environment.TickCount64 < deadline)
        {
            wait.SpinOnce();
            Thread.Sleep(1);
        }
        ReleasePerfectCastingMouse();
        ResetActiveTrackingControllers();
        ReleaseFishingHold();
        BellonaDebugOverlayService.Hide();
        _tranquilityController.Reset();
        _tranquilityEngaged = false;
        _context.ResetCache();
        _hadMetricsLastTick = false;
        _startupAssistActive = false;
        _startupAssistStartedAt = 0;
        _startupAssistStartFishCenter = 0;
        _miguCounterWasVisible = false;
        _miguShiftFiredThisAppearance = false;
        _miguShiftFireAt = 0;
        ReleaseBellonaRightHold();
        ForceBellonaRightPhysicalUp();
        _bellonaRightLastSecondaryPresent = false;
        _bellonaRightLastLogAt = 0;
        _bellonaLeftRightLogAt = 0;
        _bellonaSingleReelAssignRight = false;
        _bellonaRmbWatchdogLogAt = 0;
        _bellonaRightRawDesiredHolding = false;
        _bellonaRightRawDesiredChangedAt = 0;
        _bellonaRightLastMetricsSeenAt = 0;
        _bellonaRightLastFishCenter = null;
        _bellonaRightLastBarCenter = null;
        _bellonaRightLastMotionAt = 0;
        _bellonaRightStaleSinceAt = 0;
        _bellonaRightLastStateLogAt = 0;
        _bellonaRightReacquireUntilAt = 0;
        _bellonaRightHybridHoldUntilAt = 0;
        _masterlineNamesLastLogAt = 0;
        _fishingInputReadyAt = 0;
        _completionLatchedAt = 0;
        _bellonaLeftCompletionReached = false;
        _bellonaRightCompletionReached = false;
        Status = new FishingTrackerStatus(false, "OFF", "Tracking 1 stopped.", null, false);
        Status = Status with { ReelStats = _reelStats };
    }

    public void Suspend(string message)
    {
        if (!_isRunning)
        {
            AppLog.Fishing("Tracker", $"Suspend NO-OP (not running) | msg=\"{message}\"");
            return;
        }

        var wasSuspended = _isSuspended;
        AppLog.Fishing("Tracker", $"Suspend ENTER | wasSuspended={wasSuspended} | msg=\"{message}\" | prevPhase={Status.Phase} latched={_completionLatchedAt} reached={_completionReached} castStarted={_castStartedAt} castReleased={_castReleasedAt} seenReel={_seenReelThisRun} hadMetricsLast={_hadMetricsLastTick}");
        _isSuspended = true;
        _suspendMessage = string.IsNullOrWhiteSpace(message) ? "Fishing suspended." : message;
        ReleasePerfectCastingMouse();
        ResetActiveTrackingControllers();
        ReleaseFishingHold();
        ForceBellonaRightPhysicalUp();
        BellonaDebugOverlayService.Hide();
        _startupAssistActive = false;
        Status = new FishingTrackerStatus(true, "SUSPENDED", _suspendMessage, null, false, _reelStats);
        AppLog.Fishing("Tracker", "Suspend EXIT | status -> SUSPENDED");
    }

    public void Resume()
    {
        if (!_isRunning)
        {
            AppLog.Fishing("Tracker", "Resume NO-OP (not running)");
            return;
        }

        var wasSuspended = _isSuspended;
        AppLog.Fishing("Tracker", $"Resume ENTER | wasSuspended={wasSuspended} | currPhase={Status.Phase} latched={_completionLatchedAt} reached={_completionReached} castStarted={_castStartedAt} castReleased={_castReleasedAt} seenReel={_seenReelThisRun} hadMetricsLast={_hadMetricsLastTick} fishingLostAt={_fishingLostAt}");
        _isSuspended = false;
        _suspendMessage = "Fishing suspended.";
        AppLog.Fishing("Tracker", "Resume EXIT");
    }

    public void Dispose()
    {
        Stop();
        _memory.Dispose();
    }

    private void Tick(int runId)
    {
        if (runId != Volatile.Read(ref _runId) || !_isRunning || _inTick)
        {
            return;
        }

        _inTick = true;
        try
        {
            if (_isSuspended)
            {
                TraceTick("SUSPENDED-early-out", $"msg=\"{_suspendMessage}\"");
                ReleasePerfectCastingMouse();
                ResetActiveTrackingControllers();
                ReleaseFishingHold();
                BellonaDebugOverlayService.Hide();
                Status = new FishingTrackerStatus(true, "SUSPENDED", _suspendMessage, null, false, _reelStats);
                return;
            }

            _memory.EnsureAttached();
            BellonaRmbWatchdog();
            MaybeDetectRod();
            TryHandleSplitbranchTwigCrateSelection();
            if (UpdateAutoTotem())
            {
                TraceTick("AutoTotem-returned");
                return;
            }

            if (AutoSovereignRechargeSettings.PauseFishingRequested)
            {
                TraceTick("Sovereign-paused");
                ReleasePerfectCastingMouse();
                ResetActiveTrackingControllers();
                _startupAssistActive = false;
                Status = new FishingTrackerStatus(true, "SOVEREIGN", "Auto Sovereign pending/running.", null, false);
                return;
            }

            if (_rodProfile.Kind == RodKind.Tranquility)
            {
                if (_context.IsTranquilityActive())
                {
                    TraceTick("Tranquility-active");
                    ReleaseFishingHold();
                    _tranquilityController.Update();
                    _tranquilityEngaged = true;
                    _lastRuntimeSignalAt = Environment.TickCount64;
                    Status = new FishingTrackerStatus(
                        true,
                        "TRANQUILITY",
                        "Tranquility minigame active.",
                        _context.ReadTranquilityProgressPercent(_context.GetTranquilityRoot()),
                        false);
                    return;
                }

                if (_tranquilityEngaged)
                {
                    _tranquilityEngaged = false;
                    _tranquilityController.Reset();
                    ResetCastingCycle();
                }
            }

            var now = (DateTimeOffset.UtcNow - _startedAt).TotalSeconds;
            var progress = _context.GetFishingCompletionPercent();
            var context = _context.GetReelContext();
            var metrics = context is null ? null : _context.ReadMetrics(context);
            _lastReelMetrics = metrics;
            TraceTick("post-memory-read", $"progress={(progress?.ToString("0.0") ?? "null")} reelCtx={(context is null ? "null" : "non-null")} metrics={(metrics is null ? "null" : $"fish={metrics.FishCenter:0.000}/bar={metrics.PlayerbarCenter:0.000}")} reelGuiVis={_context.IsReelGuiVisible()}");
            ReelContext? bellonaLeftContext = null;
            ReelContext? bellonaRightContext = null;
            ReelMetrics? bellonaLeftMetrics = null;
            ReelMetrics? bellonaRightMetrics = null;

            // Bellona runs two concurrent reel UIs. Force deterministic mapping:
            // leftmost reel -> normal LMB controller, rightmost reel -> RMB controller.
            if (_rodProfile.Kind == RodKind.BellonaWaraxe)
            {
                (bellonaLeftContext, bellonaRightContext) = ResolveBellonaSideContexts();
                bellonaLeftMetrics = bellonaLeftContext is null ? null : _context.ReadMetrics(bellonaLeftContext);
                bellonaRightMetrics = bellonaRightContext is null ? null : _context.ReadMetrics(bellonaRightContext);

                if (bellonaLeftContext is not null && bellonaLeftMetrics is not null)
                {
                    context = bellonaLeftContext;
                    metrics = bellonaLeftMetrics;
                }
                else if (bellonaRightContext is not null && bellonaRightMetrics is not null)
                {
                    // Single-side fallback: if only the right reel is currently visible,
                    // keep the runtime in FISHING instead of dropping to CASTING.
                    context = bellonaRightContext;
                    metrics = bellonaRightMetrics;
                }

                var nowTick = Environment.TickCount64;
                if (nowTick - _bellonaLeftRightLogAt >= 300)
                {
                    var leftBar = context?.Bar ?? 0;
                    var rightBar = bellonaRightContext?.Bar ?? 0;
                    AppLog.Info(
                        "BellonaDual",
                        $"LeftAssigned bar=0x{leftBar:X} metrics={(metrics is null ? "none" : $"fish={metrics.FishCenter:0.000},bar={metrics.PlayerbarCenter:0.000},w={metrics.PlayerbarWidth:0.000}")}; " +
                        $"RightAssigned bar=0x{rightBar:X}");
                    _bellonaLeftRightLogAt = nowTick;
                }
            }

            if (_rodProfile.Kind == RodKind.BellonaWaraxe)
            {
                var threshold = _settings.CompletionThreshold;
                var leftProgress = bellonaLeftContext is null ? (double?)null : _context.GetFishingCompletionPercentFromBar(bellonaLeftContext.Bar);
                var rightProgress = bellonaRightContext is null ? (double?)null : _context.GetFishingCompletionPercentFromBar(bellonaRightContext.Bar);

                if (leftProgress is { } lp && lp >= threshold)
                {
                    _bellonaLeftCompletionReached = true;
                }

                if (rightProgress is { } rp && rp >= threshold)
                {
                    _bellonaRightCompletionReached = true;
                }

                _completionReached = _bellonaLeftCompletionReached && _bellonaRightCompletionReached;
                _maxCompletionProgressThisCycle = 0;
                if (leftProgress is { } lpv)
                {
                    _maxCompletionProgressThisCycle = Math.Max(_maxCompletionProgressThisCycle, lpv);
                }

                if (rightProgress is { } rpv)
                {
                    _maxCompletionProgressThisCycle = Math.Max(_maxCompletionProgressThisCycle, rpv);
                }

                progress = leftProgress is null
                    ? rightProgress
                    : rightProgress is null
                        ? leftProgress
                        : Math.Min(leftProgress.Value, rightProgress.Value);

                // New Bellona cycle started: both progress bars have fallen back
                // from completion range. Clear stale completion latches so we
                // can re-enter active FISHING instead of looping in CASTING.
                if (_completionReached &&
                    leftProgress is { } leftNow &&
                    rightProgress is { } rightNow &&
                    leftNow < threshold - 12.0 &&
                    rightNow < threshold - 12.0)
                {
                    AppLog.Info(
                        "BellonaDual",
                        $"Resetting stale completion latch for new cycle. left={leftNow:0.0} right={rightNow:0.0} threshold={threshold:0.0}");
                    _completionReached = false;
                    _bellonaLeftCompletionReached = false;
                    _bellonaRightCompletionReached = false;
                    _completionLatchedAt = 0;
                    _outcomeResolved = false;
                }
            }
            else
            {
                var completionState = UpdateCompletionState(_completionReached, _maxCompletionProgressThisCycle, progress, _settings.CompletionThreshold);
                _completionReached = completionState.CompletionReached;
                _maxCompletionProgressThisCycle = completionState.MaxProgressThisCycle;
            }
            if (ShouldResetCompletionStateAfterReelLoss(_completionReached, progress, metrics is not null))
            {
                _completionReached = false;
                _maxCompletionProgressThisCycle = 0;
            }

            // Latch the first tick completion is reached, and intentionally
            // preserve the timestamp across ShouldResetCompletionStateAfterReelLoss
            // so the Auto Totem boundary has a stable post-catch reference even
            // after _completionReached is cleared when metrics disappear.
            if (_completionReached && _completionLatchedAt == 0)
            {
                _completionLatchedAt = Environment.TickCount64;
            }

            if (_completionReached)
            {
                TraceTick("completion-branch-entry");
                RecordReelOutcome(_completionReached);
                if (_totemPending)
                {
                    TraceTick("completion->TOTEM-pending-hold");
                    LogAutoTotemFlow($"flow: tick completion path, totem pending, holding ({DescribeAutoTotemGate()})");
                    _controller.Release();
                    Status = new FishingTrackerStatus(true, "TOTEM", "Auto Totem queued. Waiting for boundary.", progress, false);
                    return;
                }

                _context.ResetCache();
                if (_fishingLostAt == 0)
                {
                    var blockReason = ShouldBlockCastingForPendingSequence();
                    var holdMs = Math.Max(MinPostCatchHoldMs, blockReason ? PostCatchRecastHoldMs : 0);
                    TraceTick("completion-first-tick", $"shouldBlock={blockReason} setHoldMs={holdMs}");
                    _fishingLostAt = Environment.TickCount64;
                    ResetActiveTrackingControllers();
                    _startupAssistActive = false;
                    ResetCastingCycle();
                    _completionRecastReadyAt = Environment.TickCount64 + holdMs;
                }

                if (CastingMode == FishingCastingMode.Perfect)
                {
                    if (_completionRecastReadyAt == 0)
                    {
                        _completionRecastReadyAt = Environment.TickCount64 + PerfectPostFishHoldDelayMs;
                    }

                    TraceTick("completion->Perfect-recast");
                    UpdatePerfectCasting();
                    return;
                }

                if (_completionRecastReadyAt != 0 && Environment.TickCount64 < _completionRecastReadyAt)
                {
                    _controller.Release();
                    _controller.Reset();
                    ReleaseFishingHold();
                    var pendingNow = ShouldBlockCastingForPendingSequence();
                    TraceTick("completion->recast-hold-window", $"phase={(pendingNow ? "ADDON" : "CASTING")} remainingMs={_completionRecastReadyAt - Environment.TickCount64}");
                    LogAutoTotemDecision(pendingNow ? "completion-hold-pending" : "completion-hold-recast-delay");
                    Status = new FishingTrackerStatus(
                        true,
                        pendingNow ? "ADDON" : "CASTING",
                        pendingNow ? "Add-on pending; hold before recast." : "Waiting before recast.",
                        progress,
                        false);
                    return;
                }

                if (now >= _nextProbeAt)
                {
                    _nextProbeAt = now + _settings.ProbeDelaySeconds;
                }

                // Re-check Auto Totem requirements directly on completion path so
                // missing weather/time queues immediately, before any recast can
                // be permitted in this boundary window.
                if (TryQueueAutoTotemFromCompletionBoundary())
                {
                    LogAutoTotemDecision("completion-queue-true");
                    _controller.Release();
                    _controller.Reset();
                    ReleaseFishingHold();
                    Status = new FishingTrackerStatus(true, "ADDON", "Auto Totem queued. Waiting for boundary.", progress, false);
                    return;
                }

                if (IsAutoTotemRuntimeEnabled() &&
                    _completionLatchedAt != 0 &&
                    _totemState == "IDLE" &&
                    !_totemPending &&
                    !_totemAwaitNextFishCycle &&
                    Environment.TickCount64 >= _totemRetryAfterAt &&
                    _totemLastMaintainCheckAt < _completionLatchedAt)
                {
                    LogAutoTotemDecision("completion-hold-await-decision");
                    _controller.Release();
                    _controller.Reset();
                    ReleaseFishingHold();
                    Status = new FishingTrackerStatus(true, "ADDON", "Auto Totem evaluating boundary before recast.", progress, false);
                    return;
                }

                if (ShouldBlockCastingForPendingSequence())
                {
                    TraceTick("completion->ADDON-blocked-by-pending");
                    LogAutoTotemDecision("completion-blocked-by-pending-sequence");
                    AppLog.Info("CastGate", $"Blocked after completion. phase={Status.Phase}, pending={DescribePendingFlags()}");
                    _controller.Release();
                    _controller.Reset();
                    ReleaseFishingHold();
                    Status = new FishingTrackerStatus(true, "ADDON", "Add-on pending; holding cast until sequence completes.", progress, false);
                    return;
                }

                TraceTick("completion->ALLOW-CAST", $"penFlags=[{DescribePendingFlags()}]");
                AppLog.Info("CastGate", $"Allow cast after completion. pending={DescribePendingFlags()}");
                LogAutoTotemDecision("completion-allow-cast");
                var castDecision = _controller.UpdateCasting();
                Status = new FishingTrackerStatus(true, "CASTING", "Fish completed; casting again.", progress, castDecision.Holding);
                return;
            }

            if (metrics is not null)
            {
                TraceTick("fishing-branch-entry", $"newReel={!_hadMetricsLastTick}");
                TryHandleMiguCounterAttackShift();
                _lastRuntimeSignalAt = Environment.TickCount64;
                if (!_hadMetricsLastTick)
                {
                    _outcomeResolved = false;
                    _completionLatchedAt = 0;
                    ResetActiveTrackingControllers();
                    _startupAssistActive = _mode == FishingTrackerMode.Tracking1;
                    _startupAssistStartedAt = Environment.TickCount64;
                    _startupAssistStartFishCenter = metrics.FishCenter;
                    _fishingInputReadyAt = Environment.TickCount64 + ReelInputSettleMs;
                }

                _hadMetricsLastTick = true;
                _seenReelThisRun = true;
                _fishingLostAt = 0;

                // Bellona runs a second simultaneous reel that should continue
                // receiving right-click control even while left-side startup
                // branches (settle/startup-assist) are active.
                UpdateBellonaRightHold();

                if (_fishingInputReadyAt != 0 && Environment.TickCount64 < _fishingInputReadyAt)
                {
                    _controller.Release();
                    _startupAssistController.Reset();
                    Status = new FishingTrackerStatus(true, "FISHING", "Reel detected; releasing cast input.", progress, false);
                    return;
                }

                _fishingInputReadyAt = 0;
                if (TryRunRodEquipFailsafe(progress, "FISHING"))
                {
                    return;
                }

                if (_rodProfile.Kind == RodKind.Pinion)
                {
                    metrics = _rodProfile.AdjustTarget(metrics, _context.GetActiveNoteTarget());
                }

                if (_rodProfile.Kind == RodKind.BellonaWaraxe && context is { } primaryContext)
                {
                    UpdateBellonaDebugOverlay(primaryContext);
                }
                else
                {
                    BellonaDebugOverlayService.Hide();
                }

                UpdateFishingMotionSignal(metrics, progress);
                if (_startupAssistActive)
                {
                    var fishMoved = Math.Abs(metrics.FishCenter - _startupAssistStartFishCenter) >= 0.0125;
                    var assistTimedOut = Environment.TickCount64 - _startupAssistStartedAt >= 800;
                    if (fishMoved || assistTimedOut)
                    {
                        _startupAssistActive = false;
                        ResetActiveTrackingControllers();
                    }
                    else
                    {
                        var assistDecision = _startupAssistController.Update(metrics, _startupAssistSettings);
                        ApplyFishingHold(assistDecision.Holding, progress);
                        Status = new FishingTrackerStatus(
                            true,
                            "FISHING",
                            _rodProfile.Kind == RodKind.BellonaWaraxe
                                ? "Startup assist active (Bellona dual control)."
                                : "Startup assist active.",
                            progress,
                            assistDecision.Holding);
                        return;
                    }
                }

                switch (_mode)
                {
                    case FishingTrackerMode.Tracking2:
                    {
                        var decision = _tracking2Controller.Update(metrics, _tracking2Settings);
                        ApplyFishingHold(decision.Holding, progress);
                        UpdateBellonaRightHold();
                        if (TryBreakFishingHoldStall(progress, decision.Holding))
                        {
                            return;
                        }

                        Status = new FishingTrackerStatus(true, "FISHING", AppendMasterlineDebug($"Error {decision.Error:0.000}, duty {decision.Control:0.000}."), progress, decision.Holding);
                        return;
                    }
                    case FishingTrackerMode.Tracking3:
                    {
                        var decision = _tracking3Controller.Update(metrics, _tracking3Settings);
                        ApplyFishingHold(decision.DesiredHolding, progress);
                        UpdateBellonaRightHold();
                        if (TryBreakFishingHoldStall(progress, decision.DesiredHolding))
                        {
                            return;
                        }

                        Status = new FishingTrackerStatus(true, "FISHING", AppendMasterlineDebug($"{decision.Mode} err {decision.Error:0.000}, ctrl {decision.Control:0.000}."), progress, decision.DesiredHolding);
                        return;
                    }
                    default:
                    {
                        var decision = _controller.UpdateTracking(metrics, _settings);
                        ApplyFishingHold(decision.Holding, progress);
                        UpdateBellonaRightHold();
                        if (TryBreakFishingHoldStall(progress, decision.Holding))
                        {
                            return;
                        }

                        Status = new FishingTrackerStatus(true, "FISHING", AppendMasterlineDebug($"Error {decision.Error:0.000}, control {decision.Control:0.000}."), progress, decision.Holding);
                        return;
                    }
                }
            }

            _hadMetricsLastTick = false;
            _miguCounterWasVisible = false;
            _miguShiftFiredThisAppearance = false;
            _miguShiftFireAt = 0;
            _startupAssistActive = false;
            _fishingInputReadyAt = 0;
            _fishingHoldStartedAt = 0;
            if (_rodProfile.Kind != RodKind.BellonaWaraxe)
            {
                ReleaseBellonaRightHold();
            }
            BellonaDebugOverlayService.Hide();
            _lastFishingFishCenter = null;
            _lastFishingBarCenter = null;
            _lastFishingProgress = null;

            if (_rodProfile.Kind == RodKind.BellonaWaraxe)
            {
                var leftCtx = _context.GetLeftmostReelContext();
                var rightCtx = _context.GetRightmostReelContext();
                var leftReady = leftCtx is not null && _context.ReadMetrics(leftCtx) is not null;
                var rightReady = rightCtx is not null && _context.ReadMetrics(rightCtx) is not null;
                if (leftCtx is not null || rightCtx is not null)
                {
                    // Bellona can expose reel GUIs a few ticks before stable fish/playerbar
                    // metrics are readable. Hold here instead of drifting into recast paths.
                    Status = new FishingTrackerStatus(
                        true,
                        "FISHING",
                        $"Bellona reel settling (leftReady={leftReady}, rightReady={rightReady}).",
                        progress,
                        false);
                    return;
                }
            }

            _context.ResetCache();
            if (_fishingLostAt == 0)
            {
                _fishingLostAt = Environment.TickCount64;
            }

            var lostForMs = Environment.TickCount64 - _fishingLostAt;
            if (_seenReelThisRun && lostForMs < ReelReacquireGraceMs)
            {
                TraceTick("wait->reacquire-grace", $"lostForMs={lostForMs}");
                _controller.Release();
                _controller.Reset();
                _normalCastHoldStartedAt = 0;
                Status = new FishingTrackerStatus(true, "CASTING", $"Reacquiring reel ({lostForMs}ms).", progress, false);
                return;
            }

            if (_seenReelThisRun)
            {
                RecordReelOutcome(_completionReached);
            }

            // A queued totem is intentionally not run here: a lost fish (or the
            // cast-and-wait gap) is not a safe boundary. Keep fishing; the totem
            // runs at the next completed catch (see IsAutoTotemBoundary).

            if (CastingMode == FishingCastingMode.Perfect)
            {
                UpdatePerfectCasting();
                return;
            }

            if (TryRunRodEquipFailsafe(progress, "CASTING"))
            {
                return;
            }

            if (now >= _nextProbeAt)
            {
                _nextProbeAt = now + _settings.ProbeDelaySeconds;
            }

            if (ShouldBlockCastingForPendingSequence())
            {
                TraceTick("wait->ADDON-blocked");
                AppLog.Info("CastGate", $"Blocked in casting wait path. pending={DescribePendingFlags()}");
                _controller.Release();
                _controller.Reset();
                ReleaseFishingHold();
                _normalCastHoldStartedAt = 0;
                Status = new FishingTrackerStatus(true, "ADDON", "Add-on pending; casting paused.", progress, false);
                return;
            }

            if (_context.IsShakeButtonVisible())
            {
                _lastRuntimeSignalAt = Environment.TickCount64;
                // Ensure normal casting hold does not block reel shake interaction.
                _controller.Release();
                _controller.Reset();
                _normalCastHoldStartedAt = 0;

                if (_lastShakedAt == 0 || Environment.TickCount64 - _lastShakedAt >= _perfectCastingSettings.ShakeIntervalMs)
                {
                    NativeKeyboard.PressEnter();
                    _lastShakedAt = Environment.TickCount64;
                }

                Status = new FishingTrackerStatus(true, "SHAKE", "Sending shake input.", progress, _controller.IsHolding);
                return;
            }

            if (TryRunOvernightRecovery(progress))
            {
                return;
            }

            ResetCastingCycle();
            AppLog.Info("CastGate", $"Allow cast in wait path. pending={DescribePendingFlags()}");
            TraceTick("wait->ALLOW-CAST");
            var normalCastDecision = _controller.UpdateCasting();
            if (normalCastDecision.Holding)
            {
                if (_normalCastHoldStartedAt == 0)
                {
                    _normalCastHoldStartedAt = Environment.TickCount64;
                }
                else if (Environment.TickCount64 - _normalCastHoldStartedAt >= 1200)
                {
                    TraceTick("wait->cast-watchdog-reset", $"heldMs={Environment.TickCount64 - _normalCastHoldStartedAt}");
                    _controller.Release();
                    _controller.Reset();
                    _normalCastHoldStartedAt = 0;
                    Status = new FishingTrackerStatus(true, "CASTING", "Casting watchdog reset.", progress, false);
                    return;
                }
            }
            else
            {
                _normalCastHoldStartedAt = 0;
            }

            TraceTick("wait->CASTING-publish", $"holding={normalCastDecision.Holding}");
            Status = new FishingTrackerStatus(true, "CASTING", "Waiting for active reel minigame.", progress, normalCastDecision.Holding);
        }
        catch (Exception ex)
        {
            AppLog.FishingError("TrackerTick", "Tick threw", ex);
            ResetActiveTrackingControllers();
            _startupAssistActive = false;
            _context.ResetCache();
            Status = new FishingTrackerStatus(true, "ERROR", ex.Message, null, false);
        }
        finally
        {
            if (runId == Volatile.Read(ref _runId))
            {
                if (ShouldForceAddonPhaseForTotemPending() &&
                    string.Equals(Status.Phase, "CASTING", StringComparison.OrdinalIgnoreCase))
                {
                    AppLog.Info("DBG", "Status phase override CASTING->ADDON due to pending totem post-catch latch.");
                    Status = Status with
                    {
                        Phase = "ADDON",
                        Message = "Auto Totem queued. Waiting for boundary.",
                    };
                }

                var boundaryNow = IsAutoTotemBoundary();
                Status = Status with
                {
                    ReelStats = _reelStats,
                    IsAutomationGateOpen = boundaryNow,
                    ReelSnapshot = _lastReelMetrics is null
                        ? null
                        : new ReelSnapshotData(
                            _lastReelMetrics.FishCenter,
                            _lastReelMetrics.PlayerbarCenter,
                            _lastReelMetrics.PlayerbarWidth),
                };
                AppLog.Fishing("TrackerTick", $"FINAL phase={Status.Phase} msg=\"{Status.Message}\" holding={Status.IsHolding} gateOpen={boundaryNow} progress={(Status.ProgressPercent?.ToString("0.0") ?? "null")} | latched={_completionLatchedAt} reached={_completionReached} settleElapsed={(_completionLatchedAt != 0 && Environment.TickCount64 - _completionLatchedAt >= AutoTotemPostCatchSettleMs)} castBar={_castBarSeen} perfectHold={_perfectCastingHolding} reelGuiVis={_context.IsReelGuiVisible()} seenReel={_seenReelThisRun} hadMetricsLast={_hadMetricsLastTick}");

                LogDebugStateSnapshot();
            }
            _inTick = false;
        }
    }

    private void UpdatePerfectCasting()
    {
        if (!_activatedUiNav)
        {
            NativeKeyboard.PressBackslash();
            _activatedUiNav = true;
            _castReadyAt = Environment.TickCount64 + 50;
            Status = new FishingTrackerStatus(true, "CASTING", "Preparing cast.", null, false);
            return;
        }

        if (_castReadyAt != 0 && Environment.TickCount64 < _castReadyAt)
        {
            Status = new FishingTrackerStatus(true, "CASTING", "Preparing cast.", null, false);
            return;
        }

        if (_completionRecastReadyAt != 0 && Environment.TickCount64 < _completionRecastReadyAt)
        {
            Status = new FishingTrackerStatus(true, "CASTING", "Waiting before recast.", null, false);
            return;
        }

        if (_castReadyAt != 0)
        {
            _castStartedAt = Environment.TickCount64;
            _castReadyAt = 0;
        }
        _completionRecastReadyAt = 0;

        if (Environment.TickCount64 - _castStartedAt < _perfectCastingSettings.PreCastDelayMs)
        {
            Status = new FishingTrackerStatus(true, "CASTING", "Waiting for pre-cast delay.", null, false);
            return;
        }

        var targetPower = _perfectCastingSettings.CastPowerCustom;
        var power = _context.GetPowerBarPercent();
        if (power is null)
        {
            power = TryAcquirePowerBarPercentQuickly();
        }
        if (_castReleasedAt == 0)
        {
            if (!_perfectCastingHolding)
            {
                NativeMouse.LeftDown();
                _perfectCastingHolding = true;
            }

            if (power is null)
            {
                if (Environment.TickCount64 - _castStartedAt >= Math.Max(5000, _perfectCastingSettings.CastTimeoutMs))
                {
                    ResetCastingCycle();
                }

                Status = new FishingTrackerStatus(true, "CASTING", "Waiting for cast power bar.", null, true);
                return;
            }

            _castBarSeen = true;
            if (power >= targetPower || TryReleasePerfectCastNearTarget(targetPower))
            {
                NativeMouse.LeftUp();
                _perfectCastingHolding = false;
                _castReleasedAt = Environment.TickCount64;
                Status = new FishingTrackerStatus(true, "CASTED", "Perfect cast released.", null, false);
                return;
            }

            if (Environment.TickCount64 - _castStartedAt >= Math.Max(5000, _perfectCastingSettings.CastTimeoutMs))
            {
                ResetCastingCycle();
                return;
            }

            Status = new FishingTrackerStatus(true, "CASTING", _castBarSeen ? "Charging perfect cast." : "Waiting for cast bar.", null, true);
            return;
        }

        NativeMouse.LeftUp();
        _perfectCastingHolding = false;
        if (Environment.TickCount64 - _castReleasedAt < _perfectCastingSettings.PostCastDelayMs)
        {
            Status = new FishingTrackerStatus(true, "CASTED", "Waiting after cast.", null, false);
            return;
        }

        if (_context.IsShakeButtonVisible())
        {
            if (_lastShakedAt == 0 || Environment.TickCount64 - _lastShakedAt >= _perfectCastingSettings.ShakeIntervalMs)
            {
                NativeKeyboard.PressEnter();
                _lastShakedAt = Environment.TickCount64;
            }

            if (Environment.TickCount64 - _castReleasedAt >= _perfectCastingSettings.CastTimeoutMs)
            {
                ResetCastingCycle();
            }

            Status = new FishingTrackerStatus(true, "SHAKE", "Sending shake input.", null, false);
            return;
        }

        Status = new FishingTrackerStatus(true, "CASTED", "Waiting for shake button.", null, false);
    }

    private bool TryReleasePerfectCastNearTarget(double targetPower)
    {
        // Normal tick cadence can miss the first threshold-cross frame.
        // Burst-poll briefly when close to target to release sooner.
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start <= PerfectReleaseMicroPollMs)
        {
            var sample = _context.GetPowerBarPercent();
            if (sample is { } p && p >= targetPower)
            {
                return true;
            }

            Thread.Sleep(PerfectReleaseMicroPollStepMs);
        }

        return false;
    }

    private double? TryAcquirePowerBarPercentQuickly()
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start <= PerfectPowerDetectBurstMs)
        {
            var sample = _context.GetPowerBarPercent();
            if (sample is not null)
            {
                return sample;
            }

            Thread.Sleep(PerfectPowerDetectStepMs);
        }

        return null;
    }

    private void ResetCastingCycle()
    {
        ReleasePerfectCastingMouse();
        ReleaseFishingHold();
        _castStartedAt = Environment.TickCount64;
        _castReleasedAt = 0;
        _lastShakedAt = 0;
        _castReadyAt = 0;
        _completionRecastReadyAt = 0;
        _fishingInputReadyAt = 0;
        _castBarSeen = false;
        _startupAssistActive = false;
        _bellonaLeftCompletionReached = false;
        _bellonaRightCompletionReached = false;
        _startupAssistStartedAt = 0;
        _startupAssistStartFishCenter = 0;
        _normalCastHoldStartedAt = 0;
    }

    internal static CompletionState UpdateCompletionState(
        bool completionReached,
        double maxProgressThisCycle,
        double? progress,
        double completionThreshold)
    {
        if (progress is { } progressValue)
        {
            maxProgressThisCycle = Math.Max(maxProgressThisCycle, progressValue);
        }

        return new CompletionState(
            completionReached || maxProgressThisCycle >= completionThreshold,
            maxProgressThisCycle);
    }

    internal static bool ShouldResetCompletionStateAfterReelLoss(bool completionReached, double? progress, bool hasMetrics)
    {
        return completionReached && progress is null && !hasMetrics;
    }

    internal static ReelOutcomeState ResolveReelOutcome(bool outcomeResolved, FishingReelStats stats, bool completionReached)
    {
        if (outcomeResolved)
        {
            return new ReelOutcomeState(true, stats);
        }

        return new ReelOutcomeState(
            true,
            stats.Record(completionReached ? FishingReelOutcome.Caught : FishingReelOutcome.Lost));
    }

    private void RecordReelOutcome(bool completionReached)
    {
        var state = ResolveReelOutcome(_outcomeResolved, _reelStats, completionReached);
        _outcomeResolved = state.OutcomeResolved;
        _reelStats = state.Stats;
    }

    private bool TryRunOvernightRecovery(double? progress)
    {
        var now = Environment.TickCount64;
        if (now - _lastRuntimeSignalAt < RuntimeSignalStallMs)
        {
            return false;
        }

        if (now - _lastRecoveryAt < RecoveryCooldownMs)
        {
            return false;
        }

        _lastRecoveryAt = now;
        _lastRuntimeSignalAt = now;

        ReleasePerfectCastingMouse();
        ResetActiveTrackingControllers();
        _context.ResetCache();
        EnsureRodEquipped();
        Thread.Sleep(180);

        Status = new FishingTrackerStatus(true, "RECOVER", "Runtime signal stalled; recovery reset applied.", progress, false);
        return true;
    }

    private void ReleasePerfectCastingMouse()
    {
        if (!_perfectCastingHolding)
        {
            return;
        }

        NativeMouse.LeftUp();
        _perfectCastingHolding = false;
    }

    private void UpdateBellonaRightHold()
    {
        if (_rodProfile.Kind != RodKind.BellonaWaraxe)
        {
            ResetBellonaRightControlState(releaseInput: true);
            BellonaDebugOverlayService.Hide();
            return;
        }

        var now = Environment.TickCount64;
        var (_, rightContext) = ResolveBellonaSideContexts();
        if (rightContext is null)
        {
            if (_bellonaRightLastMetricsSeenAt != 0 && now - _bellonaRightLastMetricsSeenAt <= BellonaRightLossGraceMs)
            {
                AppLog.Info("BellonaDual", $"Right context missing, grace active ({now - _bellonaRightLastMetricsSeenAt}ms).");
                return;
            }

            ResetBellonaRightControlState(releaseInput: true);
            if (NativeMouse.IsRightButtonDown())
            {
                NativeMouse.RightUp();
                AppLog.Info("BellonaDual", "Forced RMB up (right context missing but button still down).");
            }
            if (_bellonaRightLastSecondaryPresent)
            {
                AppLog.Info("BellonaDual", "Secondary reel missing; releasing RMB.");
            }
            return;
        }

        _bellonaRightLastSecondaryPresent = true;

        var secondaryMetrics = _context.ReadMetrics(rightContext);
        if (secondaryMetrics is null)
        {
            if (_bellonaRightLastMetricsSeenAt != 0 && now - _bellonaRightLastMetricsSeenAt <= BellonaRightLossGraceMs)
            {
                AppLog.Info("BellonaDual", $"Right metrics missing, grace active ({now - _bellonaRightLastMetricsSeenAt}ms).");
                return;
            }

            ResetBellonaRightControlState(releaseInput: true);
            AppLog.Info("BellonaDual", "Secondary reel metrics unavailable; releasing RMB.");
            return;
        }

        _bellonaRightLastMetricsSeenAt = now;
        UpdateBellonaRightMotionWatchdog(secondaryMetrics, now);

        var desiredHolding = ResolveBellonaRightDesiredHolding(secondaryMetrics, now);

        if (now < _bellonaRightReacquireUntilAt)
        {
            AppLog.Info("BellonaDual", $"Right reacquire window active ({_bellonaRightReacquireUntilAt - now}ms left).");
        }

        if (_mode == FishingTrackerMode.Tracking3)
        {
            desiredHolding = ApplyBellonaRightHybridHoldBias(desiredHolding, secondaryMetrics, now);
        }

        // Bellona secondary reel uses RMB input.
        if (now - _bellonaRightLastLogAt >= 350 ||
            desiredHolding != _bellonaRightLastDesiredHolding)
        {
            AppLog.Info(
                "BellonaDual",
                $"secondary fish={secondaryMetrics.FishCenter:0.000} bar={secondaryMetrics.PlayerbarCenter:0.000} width={secondaryMetrics.PlayerbarWidth:0.000} desiredRmbHold={desiredHolding}");
            _bellonaRightLastLogAt = now;
            _bellonaRightLastDesiredHolding = desiredHolding;
        }
        // Keep Bellona right behavior identical to Hybrid (Tracking3) decisioning:
        // apply raw desired hold directly with no extra smoothing layer.
        if (_mode == FishingTrackerMode.Tracking3)
        {
            ApplyBellonaRightHold(desiredHolding);
            return;
        }

        var smoothedHolding = GetBellonaRightSmoothedHolding(desiredHolding);
        if (smoothedHolding != desiredHolding)
        {
            AppLog.Info("BellonaDual", $"RMB debounce: raw={desiredHolding} stable={smoothedHolding}");
        }

        ApplyBellonaRightHold(smoothedHolding);
    }

    private void ResetBellonaRightControlState(bool releaseInput)
    {
        if (releaseInput)
        {
            ReleaseBellonaRightHold();
        }

        _bellonaRightController.Reset();
        _bellonaRightTracking2Controller.Reset();
        _bellonaRightTracking3Controller.Reset();
        _bellonaRightLastSecondaryPresent = false;
        _bellonaRightRawDesiredHolding = false;
        _bellonaRightRawDesiredChangedAt = Environment.TickCount64;
        _bellonaRightLastFishCenter = null;
        _bellonaRightLastBarCenter = null;
        _bellonaRightLastMotionAt = 0;
        _bellonaRightStaleSinceAt = 0;
        _bellonaRightReacquireUntilAt = 0;
        _bellonaRightHybridHoldUntilAt = 0;
    }

    private bool ResolveBellonaRightDesiredHolding(ReelMetrics secondaryMetrics, long now)
    {
        var desiredHolding = _mode switch
        {
            FishingTrackerMode.Tracking2 => _bellonaRightTracking2Controller.Update(secondaryMetrics, _tracking2Settings).Holding,
            FishingTrackerMode.Tracking3 => _bellonaRightTracking3Controller.Update(secondaryMetrics, _tracking3Settings).DesiredHolding,
            _ => _bellonaRightController.UpdateTracking(secondaryMetrics, _settings).Holding,
        };

        if (_mode == FishingTrackerMode.Tracking3)
        {
            desiredHolding = ApplyBellonaRightHybridHoldBias(desiredHolding, secondaryMetrics, now);
        }

        return desiredHolding;
    }

    private bool ApplyBellonaRightHybridHoldBias(bool desiredHolding, ReelMetrics metrics, long now)
    {
        if (desiredHolding)
        {
            _bellonaRightHybridHoldUntilAt = now + BellonaRightHybridHoldBiasMs;
            return true;
        }

        // Very small hysteresis near equilibrium to prevent rare "fall" releases from one noisy frame.
        if (_bellonaRightHolding &&
            now < _bellonaRightHybridHoldUntilAt &&
            Math.Abs(metrics.FishCenter - metrics.PlayerbarCenter) <= 0.022)
        {
            AppLog.Info(
                "BellonaDual",
                $"Right hybrid hold-bias active ({_bellonaRightHybridHoldUntilAt - now}ms left, err={Math.Abs(metrics.FishCenter - metrics.PlayerbarCenter):0.000}).");
            return true;
        }

        return false;
    }

    private void UpdateBellonaRightMotionWatchdog(ReelMetrics metrics, long now)
    {
        var moved =
            !_bellonaRightLastFishCenter.HasValue ||
            !_bellonaRightLastBarCenter.HasValue ||
            Math.Abs(metrics.FishCenter - _bellonaRightLastFishCenter.Value) >= 0.0015 ||
            Math.Abs(metrics.PlayerbarCenter - _bellonaRightLastBarCenter.Value) >= 0.0015;

        if (moved)
        {
            _bellonaRightLastMotionAt = now;
            _bellonaRightStaleSinceAt = 0;
        }
        else if (_bellonaRightStaleSinceAt == 0)
        {
            _bellonaRightStaleSinceAt = now;
        }

        if (_bellonaRightStaleSinceAt != 0 && now - _bellonaRightStaleSinceAt >= BellonaRightStaleWatchdogMs)
        {
            AppLog.Info(
                "BellonaDual",
                $"Right metrics stale for {now - _bellonaRightStaleSinceAt}ms (fish={metrics.FishCenter:0.000}, bar={metrics.PlayerbarCenter:0.000}); resetting right controllers + cache.");
            _bellonaRightController.Reset();
            _bellonaRightTracking2Controller.Reset();
            _bellonaRightTracking3Controller.Reset();
            _context.ResetCache();
            _bellonaRightReacquireUntilAt = now + BellonaRightReacquireReleaseMs;
            _bellonaRightStaleSinceAt = now;
        }

        if (now - _bellonaRightLastStateLogAt >= 500)
        {
            var idleMs = _bellonaRightLastMotionAt == 0 ? -1 : now - _bellonaRightLastMotionAt;
            AppLog.Info(
                "BellonaDual",
                $"RightState: holding={_bellonaRightHolding}, fish={metrics.FishCenter:0.000}, bar={metrics.PlayerbarCenter:0.000}, idleMs={idleMs}");
            _bellonaRightLastStateLogAt = now;
        }

        _bellonaRightLastFishCenter = metrics.FishCenter;
        _bellonaRightLastBarCenter = metrics.PlayerbarCenter;
    }

    private (ReelContext? Left, ReelContext? Right) ResolveBellonaSideContexts()
    {
        var ordered = _context.GetOrderedReelContexts();
        if (ordered.Count == 0)
        {
            return (null, null);
        }

        if (ordered.Count >= 2)
        {
            _bellonaSingleReelAssignRight = true;
            return (ordered[0].Context, ordered[^1].Context);
        }

        // Single reel fallback with hysteresis + sticky side assignment.
        var only = ordered[0];
        if (_bellonaSingleReelAssignRight)
        {
            if (only.BarX <= 0.42)
            {
                _bellonaSingleReelAssignRight = false;
            }
        }
        else
        {
            if (only.BarX >= 0.58)
            {
                _bellonaSingleReelAssignRight = true;
            }
        }

        if (_bellonaSingleReelAssignRight)
        {
            return (null, only.Context);
        }

        return (only.Context, null);
    }

    private void BellonaRmbWatchdog()
    {
        if (_rodProfile.Kind == RodKind.BellonaWaraxe)
        {
            return;
        }

        if (!_bellonaRightHolding && NativeMouse.IsRightButtonDown())
        {
            NativeMouse.RightUp();
            var now = Environment.TickCount64;
            if (now - _bellonaRmbWatchdogLogAt >= 500)
            {
                AppLog.Info("BellonaDual", "Watchdog forced RMB up outside Bellona mode.");
                _bellonaRmbWatchdogLogAt = now;
            }
        }
    }

    private void ApplyBellonaRightHold(bool desiredHolding)
    {
        if (desiredHolding == _bellonaRightHolding)
        {
            // Recover from external/input desync: internal state says "holding",
            // but physical button is no longer down (or vice versa).
            if (desiredHolding && !NativeMouse.IsRightButtonDown())
            {
                NativeMouse.RightDown();
                AppLog.Info("BellonaDual", "RMB resync down (internal hold true, physical up)");
            }
            return;
        }

        if (desiredHolding)
        {
            NativeMouse.RightDown();
            AppLog.Info("BellonaDual", "RMB down");
        }
        else
        {
            NativeMouse.RightUp();
            AppLog.Info("BellonaDual", "RMB up");
        }

        _bellonaRightHolding = desiredHolding;
    }

    private bool GetBellonaRightSmoothedHolding(bool desiredHolding)
    {
        var now = Environment.TickCount64;
        if (desiredHolding != _bellonaRightRawDesiredHolding)
        {
            _bellonaRightRawDesiredHolding = desiredHolding;
            _bellonaRightRawDesiredChangedAt = now;
            return _bellonaRightHolding;
        }

        if (desiredHolding == _bellonaRightHolding)
        {
            return desiredHolding;
        }

        // Release immediately so we do not produce long sticky pulses.
        if (!desiredHolding)
        {
            return false;
        }

        // Debounce only press transitions to suppress high-frequency chatter.
        if (now - _bellonaRightRawDesiredChangedAt < BellonaRightPressDebounceMs)
        {
            return _bellonaRightHolding;
        }

        return true;
    }

    private void ReleaseBellonaRightHold()
    {
        // Guard against internal/physical desync where RMB may still be down
        // even if our state flag is false.
        if (!_bellonaRightHolding && !NativeMouse.IsRightButtonDown())
        {
            return;
        }

        NativeMouse.RightUp();
        _bellonaRightHolding = false;
    }

    private void ForceBellonaRightPhysicalUp()
    {
        if (NativeMouse.IsRightButtonDown())
        {
            NativeMouse.RightUp();
            AppLog.Info("BellonaDual", "Forced RMB up on stop/suspend.");
        }

        _bellonaRightHolding = false;
    }

    private string AppendMasterlineDebug(string message)
    {
        if (_rodProfile.Kind != RodKind.MasterlineRod)
        {
            return message;
        }

        var names = _context.GetMasterlineOverlayRodNames();
        var list = names.Count == 0 ? "none" : string.Join(" | ", names);
        var now = Environment.TickCount64;
        if (now - _masterlineNamesLastLogAt >= 500)
        {
            _masterlineNamesLastLogAt = now;
        }

        return $"{message} Masterline rods: {list}.";
    }

    private void UpdateBellonaDebugOverlay(ReelContext primaryContext)
    {
        var boxes = new List<BellonaDebugBox>(8);

        TryAddBox(boxes, primaryContext.Bar, new Color(255, 0, 255, 0));      // panel left
        TryAddBox(boxes, primaryContext.Fish, new Color(255, 255, 64, 64));    // fish left
        TryAddBox(boxes, primaryContext.Playerbar, new Color(255, 64, 200, 255)); // control left

        var secondary = _context.GetRightmostReelContext();
        if (secondary is { } secondaryContext)
        {
            TryAddBox(boxes, secondaryContext.Bar, new Color(255, 255, 200, 0));       // panel right
            TryAddBox(boxes, secondaryContext.Fish, new Color(255, 255, 140, 0));       // fish right
            TryAddBox(boxes, secondaryContext.Playerbar, new Color(255, 200, 80, 255)); // control right
        }

        BellonaDebugOverlayService.Update(boxes);
    }

    private void TryAddBox(List<BellonaDebugBox> boxes, ulong address, Color color)
    {
        var bounds = _memory.ReadGuiBounds(address, false);
        if (bounds is null)
        {
            return;
        }

        var origin = GetClientScreenOrigin();
        boxes.Add(new BellonaDebugBox(
            origin.X + bounds.Value.X,
            origin.Y + bounds.Value.Y,
            bounds.Value.Width,
            bounds.Value.Height,
            color));
    }

    private void UpdateFishingMotionSignal(ReelMetrics metrics, double? progress)
    {
        var now = Environment.TickCount64;
        var fish = metrics.FishCenter;
        var bar = metrics.PlayerbarCenter;
        var moved =
            !_lastFishingFishCenter.HasValue ||
            !_lastFishingBarCenter.HasValue ||
            Math.Abs(fish - _lastFishingFishCenter.Value) >= 0.0015 ||
            Math.Abs(bar - _lastFishingBarCenter.Value) >= 0.0015;

        var progressChanged =
            progress.HasValue &&
            (!_lastFishingProgress.HasValue || Math.Abs(progress.Value - _lastFishingProgress.Value) >= 0.02);

        if (moved || progressChanged)
        {
            _fishingMotionSignalAt = now;
        }

        _lastFishingFishCenter = fish;
        _lastFishingBarCenter = bar;
        _lastFishingProgress = progress;
    }

    private bool TryBreakFishingHoldStall(double? progress, bool isHolding)
    {
        var now = Environment.TickCount64;
        if (!isHolding)
        {
            _fishingHoldStartedAt = 0;
            return false;
        }

        if (_fishingHoldStartedAt == 0)
        {
            _fishingHoldStartedAt = now;
            return false;
        }

        if (now - _fishingHoldStartedAt < 1200)
        {
            return false;
        }

        if (now - _fishingMotionSignalAt < FishingHoldStallMs)
        {
            return false;
        }

        ResetActiveTrackingControllers();
        _startupAssistActive = false;
        _fishingHoldStartedAt = 0;
        _fishingInputReadyAt = now + ReelInputSettleMs;
        _fishingMotionSignalAt = now;
        Status = new FishingTrackerStatus(true, "RECOVER", "Fishing hold watchdog reset.", progress, false);
        return true;
    }

    private bool TryRunRodEquipFailsafe(double? progress, string phase)
    {
        var now = Environment.TickCount64;
        var equippedTool = GetEquippedToolName();
        var rodEquipped = IsRodEquipped(equippedTool) || IsSelectedRodEquipped(equippedTool);
        if (rodEquipped)
        {
            _rodUnequippedStreak = 0;
            return false;
        }

        _rodUnequippedStreak = Math.Min(_rodUnequippedStreak + 1, 10);
        if (_rodUnequippedStreak < 2)
        {
            return false;
        }

        if (now < _rodEquipRetryAt || now < _rodEquipHoldoffUntil || now - _lastRodEquipAttemptAt < 1200)
        {
            return false;
        }

        var equipped = EnsureRodEquipped();
        _lastRodEquipAttemptAt = now;
        _rodEquipRetryAt = now + RodEquipStateLagMs;
        _rodEquipHoldoffUntil = now + RodEquipStateLagMs;
        Status = new FishingTrackerStatus(
            true,
            phase,
            equipped
                ? $"Rod fail-safe: re-equipping slot {HotbarSlotSettings.RodSlot}."
                : $"Rod fail-safe: retry slot {HotbarSlotSettings.RodSlot}.",
            progress,
            false);
        return true;
    }

    private bool ShouldBlockCastingForPendingSequence()
    {
        // Block casting for totem only in the post-catch boundary window.
        // Outside that window, fishing should continue so a boundary can be reached.
        if (_totemPending && _completionLatchedAt != 0)
        {
            LogCastGateDecision("totem-pending-postcatch");
            return true;
        }

        var sovereignPending = AutoSovereignRechargeSettings.PendingRequested;
        var sovereignStart = AutoSovereignRechargeSettings.SequenceStartRequested;
        var sovereignBusy = AutoSovereignRechargeSettings.RuntimeBusy;
        var sovereignPause = AutoSovereignRechargeSettings.PauseFishingRequested;
        var sovereignBlocks = sovereignPending || sovereignStart || sovereignBusy || sovereignPause;
        if (sovereignBlocks)
        {
            var now = Environment.TickCount64;
            if (_sovereignPendingSince == 0)
            {
                _sovereignPendingSince = now;
            }
            else if (!sovereignBusy && !sovereignStart && sovereignPending &&
                now - _sovereignPendingSince >= SovereignPendingStallMs)
            {
                AppLog.Info("CastGate", $"Sovereign pending stale for {now - _sovereignPendingSince}ms (pause={sovereignPause}). Clearing stale pending latch.");
                AutoSovereignRechargeSettings.PendingRequested = false;
                AutoSovereignRechargeSettings.PauseFishingRequested = false;
                _sovereignPendingSince = 0;
                LogCastGateDecision("allow-after-sovereign-stale-clear");
                return false;
            }

            LogCastGateDecision("sovereign-pending");
            return true;
        }

        _sovereignPendingSince = 0;

        // Another add-on currently owns the shared automation gate.
        var heldByOther = AutomationInputGate.IsHeldByOther(AutoTotemGateOwner);
        LogCastGateDecision(heldByOther ? "other-addon-gate-held" : "allow");
        return heldByOther;
    }

    private void LogAutoTotemDecision(string reason)
    {
        var decision =
            $"Decision={reason}, phase={Status.Phase}, state={_totemState}, pending={_totemPending}, " +
            $"await={_totemAwaitNextFishCycle}, boundary={IsAutoTotemBoundary()}, " +
            $"latched={_completionLatchedAt}, reached={_completionReached}, " +
            $"lastMaintain={_totemLastMaintainCheckAt}, queuedAt={_totemQueuedAt}, retryAfter={_totemRetryAfterAt}, " +
            $"reelVisible={_context.IsReelGuiVisible()}, castBar={_castBarSeen}, perfectHold={_perfectCastingHolding}";

        if (string.Equals(_totemLastDecisionLog, decision, StringComparison.Ordinal))
        {
            return;
        }

        _totemLastDecisionLog = decision;
        AppLog.Info("AutoTotemDecision", $"{GetModeLabel()} | {decision}");
    }

    private string DescribePendingFlags()
    {
        return $"totemPending={_totemPending}, sovPending={AutoSovereignRechargeSettings.PendingRequested}, " +
               $"sovStart={AutoSovereignRechargeSettings.SequenceStartRequested}, sovBusy={AutoSovereignRechargeSettings.RuntimeBusy}, " +
               $"sovPause={AutoSovereignRechargeSettings.PauseFishingRequested}";
    }

    private void TraceTick(string branch, string extra = "")
    {
        var settleElapsed = _completionLatchedAt != 0 &&
            Environment.TickCount64 - _completionLatchedAt >= AutoTotemPostCatchSettleMs;
        var line =
            $"branch={branch} mode={_mode} phase={Status.Phase} latched={_completionLatchedAt} reached={_completionReached} " +
            $"settleElapsed={settleElapsed} castBarSeen={_castBarSeen} perfectHold={_perfectCastingHolding} " +
            $"castStarted={_castStartedAt} castReleased={_castReleasedAt} fishingLostAt={_fishingLostAt} " +
            $"seenReel={_seenReelThisRun} hadMetricsLast={_hadMetricsLastTick} recastReadyAt={_completionRecastReadyAt} " +
            $"inputReadyAt={_fishingInputReadyAt} startupAssist={_startupAssistActive}";
        if (!string.IsNullOrEmpty(extra))
        {
            line += " | " + extra;
        }
        AppLog.Fishing("TrackerTick", line);
    }

    private void LogCastGateDecision(string reason)
    {
        if (string.Equals(_lastCastGateReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _lastCastGateReason = reason;
        AppLog.Info(
            "CastGate",
            $"Decision={reason}, phase={Status.Phase}, completionLatched={_completionLatchedAt != 0}, boundary={IsAutoTotemBoundary()}, pending={DescribePendingFlags()}");
    }

    private bool ShouldForceAddonPhaseForTotemPending()
    {
        // Once a totem is queued after a completed catch, never present CASTING
        // until the queued workflow boundary handoff is done.
        return _totemPending && _completionLatchedAt != 0;
    }

    private bool TryQueueAutoTotemFromCompletionBoundary()
    {
        if (_totemPending || _totemAwaitNextFishCycle || !IsAutoTotemRuntimeEnabled())
        {
            LogAutoTotemDecision("completion-queue-skip-disabled-or-pending");
            return false;
        }

        var now = Environment.TickCount64;
        if (now < _totemRetryAfterAt)
        {
            LogAutoTotemDecision("completion-queue-skip-retry-cooldown");
            return false;
        }

        if (!TryGetSelectedTotemConfig(out var config))
        {
            LogAutoTotemDecision("completion-queue-skip-no-config");
            return false;
        }

        if (!TryReadActiveState(out var active))
        {
            LogAutoTotemDecision("completion-queue-skip-active-unreadable");
            return false;
        }

        _totemLastMaintainCheckAt = now;

        var requiredTime = GetRequiredTime(config.WeatherCondition, config.TimePreference);
        var missingWeather = IsWeatherMissing(config.WeatherCondition, active.Weather);
        var missingSpecial = IsSpecialMissing(config.Special, active);
        var missingTime = !string.IsNullOrWhiteSpace(requiredTime) &&
            !string.Equals(active.Cycle, requiredTime, StringComparison.OrdinalIgnoreCase);

        if (!missingWeather && !missingSpecial && !missingTime)
        {
            LogAutoTotemDecision("completion-queue-skip-no-missing");
            return false;
        }

        _totemPending = true;
        _totemQueuedAt = now;
        _totemNeedsSettleDelay = true;
        LogAutoTotemDecision("completion-queue-set-pending");
        AppLog.Info(
            "AutoTotem",
            $"{GetModeLabel()} | completion-boundary queue set (weather={missingWeather}, special={missingSpecial}, time={missingTime}); {DescribeAutoTotemGate()}");
        return true;
    }

    private void LogDebugStateSnapshot()
    {
        var seq = Interlocked.Increment(ref _debugTickSeq);
        var state =
            $"seq={seq};mode={_mode};statusPhase={Status.Phase};msg={Status.Message};" +
            $"completionLatched={_completionLatchedAt};completionReached={_completionReached};" +
            $"totemState={_totemState};totemPending={_totemPending};totemQueuedAt={_totemQueuedAt};" +
            $"totemAwait={_totemAwaitNextFishCycle};totemRetryAfter={_totemRetryAfterAt};" +
            $"boundary={IsAutoTotemBoundary()};reelVisible={_context.IsReelGuiVisible()};" +
            $"castBar={_castBarSeen};perfectHold={_perfectCastingHolding};controllerHold={_controller.IsHolding};" +
            $"fishingLostAt={_fishingLostAt};seenReel={_seenReelThisRun};" +
            $"pendingFlags=[{DescribePendingFlags()}]";

        if (string.Equals(state, _lastDebugState, StringComparison.Ordinal))
        {
            return;
        }

        _lastDebugState = state;
        AppLog.Info("DBG", state);
    }

    private bool UpdateAutoTotem()
    {
        if (!IsAutoTotemRuntimeEnabled())
        {
            LogAutoTotemDecision("runtime-disabled");
            if (_totemState != "IDLE" || _totemPending || _totemNeedsRodReequip || _totemNeedsSettleDelay)
            {
                LogAutoTotem("runtime disabled, clearing workflow state");
                ReleasePerfectCastingMouse();
                ResetActiveTrackingControllers();
                _startupAssistActive = false;
                ResetAutoTotemControl();
            }

            LogAutoTotemFlow("flow: auto totem runtime disabled");
            return false;
        }

        if (!TryGetSelectedTotemConfig(out var config))
        {
            LogAutoTotemDecision("no-config");
            LogAutoTotemFlow("flow: no totem config selected");
            return false;
        }

        var now = Environment.TickCount64;
        if (_totemAwaitNextFishCycle)
        {
            LogAutoTotemDecision("await-gate-active");
            if (_context.IsReelGuiVisible())
            {
                LogAutoTotemFlow("flow: await-gate, fishing cycle in progress (reel GUI visible)");
                _totemSawFishingSinceRun = true;
                return false;
            }

            // Aquarium-style gate: after a run, wait until one full fishing
            // cycle has been seen and ended, then re-check need. The timeout is
            // a safety valve: if the macro never returns to fishing (e.g. the
            // workflow failed and left the game off the fishing screen) the
            // reel GUI is never observed, so without this the gate would latch
            // forever and Auto Totem would never run again after the first run.
            if (_totemSawFishingSinceRun ||
                now - _totemAwaitStartedAt >= AutoTotemAwaitFishCycleTimeoutMs)
            {
                LogAutoTotem(_totemSawFishingSinceRun
                    ? "flow: await-gate released after a fishing cycle completed"
                    : $"flow: await-gate released by {AutoTotemAwaitFishCycleTimeoutMs}ms timeout (no fishing cycle observed)");
                _totemAwaitNextFishCycle = false;
                _totemSawFishingSinceRun = false;
                _totemLastMaintainCheckAt = now - 1000;
                return false;
            }

            LogAutoTotemFlow($"flow: await-gate waiting for fish cycle (sawFishing={_totemSawFishingSinceRun}, {DescribeAutoTotemGate()})");
            return false;
        }

        if (_totemPending && !IsAutoTotemBoundary())
        {
            LogAutoTotemDecision("pending-not-boundary");
            // If we've already latched a completion boundary, keep ADDON hold.
            // Otherwise keep fishing to reach the next safe boundary.
            if (_completionLatchedAt != 0)
            {
                ReleasePerfectCastingMouse();
                _controller.Release();
                ResetActiveTrackingControllers();
                _startupAssistActive = false;
                LogAutoTotemFlow($"flow: totem queued, boundary pending hold ({DescribeAutoTotemGate()})");
                Status = new FishingTrackerStatus(true, "ADDON", "Auto Totem queued. Waiting for boundary.", null, false);
                return true;
            }

            LogAutoTotemFlow($"flow: totem queued, fishing until next catch ({DescribeAutoTotemGate()})");
            return false;
        }

        if (_totemPending && IsAutoTotemBoundary())
        {
            LogAutoTotemDecision("pending-at-boundary-start-workflow");
            LogAutoTotem($"flow: totem pending + at boundary, starting workflow ({DescribeAutoTotemGate()})");
            if (!TryReadActiveState(out var pendingActive))
            {
                LogAutoTotemDecision("pending-at-boundary-active-unreadable");
                LogAutoTotem("flow: world state unreadable, holding totem at boundary");
                ReleasePerfectCastingMouse();
                _controller.Release();
                Status = new FishingTrackerStatus(true, "TOTEM", "Auto Totem queued. Waiting for boundary.", null, false);
                return true;
            }

            var pendingRequiredTime = GetRequiredTime(config.WeatherCondition, config.TimePreference);
            _totemPending = false;
            _totemState = "TOTEM_APPLY";
            _totemNeedsRodReequip = false;

            ReleasePerfectCastingMouse();
            ResetActiveTrackingControllers();
            _startupAssistActive = false;

            if (_totemNeedsSettleDelay)
            {
                Thread.Sleep(Math.Max(0, _perfectCastingSettings.PreCastDelayMs));
                _totemNeedsSettleDelay = false;
            }

            if (!TryRunAutoTotemWorkflow(config, pendingRequiredTime, pendingActive, out var pendingSuccess))
            {
                return true;
            }

            CompleteAutoTotemWorkflow(pendingSuccess);
            if (pendingSuccess)
            {
                _totemAwaitNextFishCycle = true;
                _totemSawFishingSinceRun = false;
                _totemAwaitStartedAt = Environment.TickCount64;
                _totemLastMaintainCheckAt = Environment.TickCount64;
                LogAutoTotem("flow: pending workflow finished (success=True), awaiting next fishing cycle");
            }
            else
            {
                _totemAwaitNextFishCycle = false;
                _totemSawFishingSinceRun = false;
                _totemAwaitStartedAt = 0;
                _totemLastMaintainCheckAt = Environment.TickCount64;
                LogAutoTotem("flow: pending workflow finished (success=False), await-gate skipped; will retry on normal maintain cadence");
            }
            return true;
        }

        if (!_totemPending && now < _totemRetryAfterAt)
        {
            LogAutoTotemDecision("retry-cooldown");
            LogAutoTotemFlow("flow: maintain skipped, in retry cooldown");
            return false;
        }

        // If no workflow is queued yet, never initiate/queue a fresh totem
        // decision while in cast/reacquire. This avoids weather flips during
        // casting from triggering add-on activity before the next fish cycle.
        if (!_totemPending &&
            (string.Equals(Status.Phase, "CASTING", StringComparison.OrdinalIgnoreCase) ||
             (_fishingLostAt != 0 && !_context.IsReelGuiVisible())))
        {
            LogAutoTotemDecision("maintain-defer-during-casting");
            return false;
        }

        if (!_totemPending && !IsAutoTotemBoundary() && now - _totemLastMaintainCheckAt < 500)
        {
            return false;
        }

        if (!TryReadActiveState(out var active))
        {
            LogAutoTotemDecision("maintain-active-unreadable");
            LogAutoTotemFlow("flow: maintain check, world state unreadable");
            if (_totemPending)
            {
                ReleasePerfectCastingMouse();
                _controller.Release();
                Status = new FishingTrackerStatus(true, "TOTEM", "Auto Totem queued. Waiting for boundary.", null, false);
                return true;
            }

            return false;
        }

        var requiredTime = GetRequiredTime(config.WeatherCondition, config.TimePreference);
        var missingWeather = IsWeatherMissing(config.WeatherCondition, active.Weather);
        var missingSpecial = IsSpecialMissing(config.Special, active);
        var missingTime = !string.IsNullOrWhiteSpace(requiredTime) && !string.Equals(active.Cycle, requiredTime, StringComparison.OrdinalIgnoreCase);
        if (!missingWeather && !missingSpecial && !missingTime)
        {
            LogAutoTotemDecision("maintain-satisfied");
            LogAutoTotemFlow($"flow: maintain idle, conditions satisfied (weather='{active.Weather}', cycle='{active.Cycle}')");
            _totemPending = false;
            _totemLastMaintainCheckAt = now;
            return false;
        }

        LogAutoTotem(
            $"selected config weather='{config.WeatherCondition}', weather_totem='{config.WeatherTotemItemName}', special='{config.Special}', selected_time='{config.TimePreference}'");
        LogAutoTotem($"required time='{requiredTime}', current time='{active.Cycle}', current weather='{active.Weather}', surges: shiny={active.Shiny}, sparkling={active.Sparkling}, mutation={active.Mutation}");
        LogAutoTotem($"flow: missing detected (weather={missingWeather}, special={missingSpecial}, time={missingTime}); {DescribeAutoTotemGate()}");

        if (!IsAutoTotemBoundary())
        {
            _totemPending = true;
            _totemQueuedAt = now;
            _totemNeedsSettleDelay = true;
            _totemLastMaintainCheckAt = now;
            LogAutoTotemDecision("maintain-missing-queue-pending");
            LogAutoTotem("pending set immediately; casting will stay blocked until boundary/workflow.");
            LogAutoTotem("missing conditions detected; queued, will run at next catch boundary");
            return false;
        }

        LogAutoTotemDecision("maintain-missing-at-boundary-start-workflow");
        LogAutoTotem("flow: missing conditions + at boundary, starting workflow now");

        _totemPending = false;
        _totemState = "TOTEM_APPLY";
        _totemNeedsRodReequip = false;

        ReleasePerfectCastingMouse();
        ResetActiveTrackingControllers();
        _startupAssistActive = false;

        if (_totemNeedsSettleDelay)
        {
            Thread.Sleep(Math.Max(0, _perfectCastingSettings.PreCastDelayMs));
            _totemNeedsSettleDelay = false;
        }

        if (!TryRunAutoTotemWorkflow(config, requiredTime, active, out var success))
        {
            return true;
        }

        CompleteAutoTotemWorkflow(success);
        if (success)
        {
            _totemAwaitNextFishCycle = true;
            _totemSawFishingSinceRun = false;
            _totemAwaitStartedAt = Environment.TickCount64;
            _totemLastMaintainCheckAt = Environment.TickCount64;
            LogAutoTotem("flow: workflow finished (success=True), awaiting next fishing cycle before re-checking");
        }
        else
        {
            _totemAwaitNextFishCycle = false;
            _totemSawFishingSinceRun = false;
            _totemAwaitStartedAt = 0;
            _totemLastMaintainCheckAt = Environment.TickCount64;
            LogAutoTotem("flow: workflow finished (success=False), await-gate skipped; retry will occur via maintain checks");
        }
        return true;
    }

    private bool TryRunAutoTotemWorkflow(
        SelectedTotemConfig config,
        string requiredTime,
        ActiveTotemState active,
        out bool success)
    {
        success = false;
        if (!AutomationInputGate.TryEnter(AutoTotemGateOwner, AutoTotemGatePriority))
        {
            LogAutoTotemFlow("flow: workflow blocked, AutomationInputGate held by another add-on, re-queued");
            _totemPending = true;
            ReleasePerfectCastingMouse();
            _controller.Release();
            Status = new FishingTrackerStatus(true, "TOTEM", "Auto Totem waiting for other add-on.", null, false);
            return false;
        }

        try
        {
            LogAutoTotem("flow: workflow gate acquired, running totem workflow");
            Status = new FishingTrackerStatus(true, "TOTEM", "Auto Totem: applying selected conditions.", null, false);
            success = RunAutoTotemWorkflow(config, requiredTime, active);
            return true;
        }
        finally
        {
            AutomationInputGate.Exit(AutoTotemGateOwner);
        }
    }

    private bool IsAutoTotemRuntimeEnabled()
    {
        return AutoTotemSettings.Enabled;
    }

    private bool IsAutoTotemBoundary()
    {
        var latched = _completionLatchedAt != 0;
        var settleElapsed = latched &&
            Environment.TickCount64 - _completionLatchedAt >= AutoTotemPostCatchSettleMs;
        return ComputeAutoTotemBoundary(latched, settleElapsed, _perfectCastingHolding, _castBarSeen);
    }

    // The only safe point to run the Totem workflow is right after a fish is
    // caught and the visible minigame has actually dismissed: no line is in
    // the water, so no bite or minigame can begin underneath the multi-second
    // workflow. The boundary uses a persistent post-completion latch plus a
    // small settle delay rather than _completionReached directly, because
    // completion latches at the moment progress crosses the catch threshold
    // — which can still be visibly mid-minigame. IsReelGuiVisible() is
    // deliberately not used either: it stays true on the post-catch screen
    // (which used to deadlock a pending totem) and is false during cast-and-
    // wait (which let the workflow start while a fish was about to bite).
    internal static bool ComputeAutoTotemBoundary(bool completionLatched, bool settleElapsed, bool perfectCastingHolding, bool castBarSeen)
    {
        return completionLatched && settleElapsed && !perfectCastingHolding && !castBarSeen;
    }

    private bool RunAutoTotemWorkflow(SelectedTotemConfig config, string requiredTime, ActiveTotemState activeAtStart)
    {
        var current = activeAtStart;

        LogAutoTotem($"workflow start state='{_totemState}'");
        if (!EnsureRequiredTime(requiredTime, ref current))
        {
            return false;
        }

        var sequence = new List<string>();
        if (IsWeatherMissing(config.WeatherCondition, current.Weather) &&
            !string.IsNullOrWhiteSpace(config.WeatherTotemItemName))
        {
            sequence.Add(config.WeatherTotemItemName);
        }

        if (IsSpecialMissing(config.Special, current))
        {
            var specialItem = GetSpecialTotemItemName(config.Special);
            if (!string.IsNullOrWhiteSpace(specialItem))
            {
                sequence.Add(specialItem);
            }
        }

        if (sequence.Count > 0)
        {
            LogAutoTotem($"using totem sequence: {string.Join(" -> ", sequence)}");
            if (!UseTotemSequence(sequence))
            {
                ScheduleAutoTotemRetry("failed to apply one or more totems");
                return false;
            }

            if (!TryReadActiveState(out current))
            {
                return false;
            }
        }

        var weatherStillMissing = IsWeatherMissing(config.WeatherCondition, current.Weather);
        var specialStillMissing = IsSpecialMissing(config.Special, current);
        if (weatherStillMissing || specialStillMissing)
        {
            LogAutoTotem($"post-apply check still missing: weather={weatherStillMissing}, special={specialStillMissing}");
            ScheduleAutoTotemRetry("conditions still missing after apply");
            return false;
        }

        LogAutoTotem("workflow completed successfully");
        return true;
    }

    private bool EnsureRequiredTime(string requiredTime, ref ActiveTotemState active)
    {
        if (string.IsNullOrWhiteSpace(requiredTime))
        {
            return true;
        }

        if (string.Equals(active.Cycle, requiredTime, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        LogAutoTotem($"required time '{requiredTime}' missing, using Sundial Totem once");
        if (!UseTotemSequence(["Sundial Totem"], out var usedInventoryForSundial, finalizeWithRod: false))
        {
            ScheduleAutoTotemRetry("failed to use Sundial Totem");
            return false;
        }

        var settleDelayMs = Math.Max(12000, AutoTotemSettings.TimeChangeWaitMs);
        var waitMs = settleDelayMs;
        if (usedInventoryForSundial && _lastInventoryActivationAt > 0)
        {
            var elapsedSinceCenterClick = Math.Max(0L, Environment.TickCount64 - _lastInventoryActivationAt);
            waitMs = (int)Math.Max(0, settleDelayMs - elapsedSinceCenterClick);
            LogAutoTotem($"sundial used from inventory, waiting {waitMs}ms after center click before verifying time");
        }
        else
        {
            LogAutoTotem($"sundial used, waiting {waitMs}ms before verifying time");
        }

        if (waitMs > 0)
        {
            Thread.Sleep(waitMs);
        }

        var deadline = Environment.TickCount64 + 20000;
        while (Environment.TickCount64 < deadline)
        {
            Thread.Sleep(250);
            if (!TryReadActiveState(out active))
            {
                continue;
            }

            if (string.Equals(active.Cycle, requiredTime, StringComparison.OrdinalIgnoreCase))
            {
                LogAutoTotem($"required time reached: '{active.Cycle}'");
                return true;
            }
        }

        ScheduleAutoTotemRetry($"time did not change to '{requiredTime}' within 20000ms");
        return false;
    }

    private void ScheduleAutoTotemRetry(string reason)
    {
        _totemRetryAfterAt = Environment.TickCount64 + AutoTotemRetryDelayMs;
        LogAutoTotem($"{reason}; retry in {AutoTotemRetryDelayMs}ms");
    }

    private void CompleteAutoTotemWorkflow(bool success)
    {
        var needsRodReequip = _totemNeedsRodReequip;
        if (!success)
        {
            LogAutoTotem("workflow failed");
        }

        ResetAutoTotemControl();
        if (needsRodReequip)
        {
            EnsureRodEquipped();
        }
    }

    private void ResetAutoTotemControl()
    {
        _totemState = "IDLE";
        _totemPending = false;
        _totemQueuedAt = 0;
        _totemNeedsRodReequip = false;
        _totemNeedsSettleDelay = false;
        _totemAwaitNextFishCycle = false;
        _totemSawFishingSinceRun = false;
        if (!_isRunning || !AutoTotemSettings.Enabled)
        {
            _totemRetryAfterAt = 0;
        }
    }

    private bool UseTotemSequence(IReadOnlyList<string> sequence, bool finalizeWithRod = true)
    {
        return UseTotemSequence(sequence, out _, finalizeWithRod);
    }

    private bool UseTotemSequence(IReadOnlyList<string> sequence, out bool usedInventory, bool finalizeWithRod = true)
    {
        usedInventory = false;
        var inventoryOpen = false;
        foreach (var itemName in sequence)
        {
            if (TryUseHotbarItem(itemName))
            {
                LogAutoTotem($"found totem in hotbar: {itemName}");
                _totemNeedsRodReequip = true;
                continue;
            }

            LogAutoTotem($"totem not in hotbar, using inventory search: {itemName}");
            usedInventory = true;
            if (!inventoryOpen)
            {
                if (!OpenInventoryIfNeeded())
                {
                    return false;
                }

                inventoryOpen = true;
            }

            if (!SearchAndSelectInventoryTotem(itemName))
            {
                return false;
            }

            if (!ActivateSelectedInventoryItem(itemName))
            {
                return false;
            }

            _totemNeedsRodReequip = true;
        }

        if (!finalizeWithRod)
        {
            if (inventoryOpen)
            {
                Thread.Sleep(AutoTotemMediumActionDelayMs);
                _ = CloseInventoryIfOpen();
            }

            return true;
        }

        if (inventoryOpen && !CloseInventoryIfOpen())
        {
            return false;
        }

        Thread.Sleep(1000);
        Thread.Sleep(Math.Max(0, AutoTotemSettings.UseSettleDelayMs));
        EnsureRodEquipped();
        LogAutoTotem($"resuming fishing with rod slot {HotbarSlotSettings.RodSlot}");
        return true;
    }

    private bool ActivateSelectedInventoryItem(string itemName)
    {
        // Activation from inventory is applied by clicking gameplay center.
        // Retry once because UI focus can momentarily remain on search/list.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Thread.Sleep(AutoTotemShortActionDelayMs);
            ClickRobloxCenter();
            _lastInventoryActivationAt = Environment.TickCount64;
            LogAutoTotem($"activating inventory item '{itemName}' via center click ({attempt + 1}/2)");
            Thread.Sleep(AutoTotemMediumActionDelayMs);
        }

        return true;
    }

    private bool TryUseHotbarItem(string itemName)
    {
        foreach (var variant in GetTotemNameVariants(itemName))
        {
            var slot = GetHotbarItemSlot(variant);
            if (slot == 0)
            {
                continue;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                // Equip the totem by clicking its hotbar button. Synthetic digit
                // keypresses are not honoured for hotbar slot switching while the
                // fishing UI is up; clicking the slot button is (the inventory
                // flow relies on the same UI-click path and works there).
                ClickGuiCenter(slot);
                Thread.Sleep(AutoTotemMediumActionDelayMs);
                var equipped = GetEquippedToolName();
                LogAutoTotem($"hotbar equip '{variant}' attempt {attempt + 1}/2: equipped tool='{equipped}'");
                if (string.Equals(equipped, variant, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(equipped, itemName, StringComparison.OrdinalIgnoreCase))
                {
                    // Totem is held; deploy it by clicking the game world.
                    Thread.Sleep(AutoTotemShortActionDelayMs);
                    ClickRobloxCenter();
                    Thread.Sleep(AutoTotemMediumActionDelayMs);
                    return true;
                }

                Thread.Sleep(AutoTotemShortActionDelayMs);
            }
        }

        return false;
    }

    private bool SelectHotbarSlot(int slot)
    {
        if (slot < 1 || slot > 9)
        {
            return false;
        }

        NativeKeyboard.PressDigit(slot, _memory.WindowHandle);
        return true;
    }

    private bool EnsureRodEquipped()
    {
        var slot = Math.Clamp(HotbarSlotSettings.RodSlot, 1, 9);
        return SelectHotbarSlot(slot);
    }

    private static bool IsRodEquipped(string equippedToolName)
    {
        if (string.IsNullOrWhiteSpace(equippedToolName))
        {
            return false;
        }

        return equippedToolName.Contains("rod", StringComparison.OrdinalIgnoreCase) ||
            equippedToolName.Contains("aria", StringComparison.OrdinalIgnoreCase) ||
            equippedToolName.Contains("castbound", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSelectedRodEquipped(string equippedToolName)
    {
        if (string.IsNullOrWhiteSpace(equippedToolName))
        {
            return false;
        }

        var selectedDisplay = HotbarRodResolver.GetHotbarRodDisplayText(_memory);
        var selected = HotbarRodResolver.NormalizeRodDisplayText(selectedDisplay);
        var equipped = HotbarRodResolver.NormalizeRodDisplayText(equippedToolName);
        if (selected.Length == 0 || equipped.Length == 0)
        {
            return false;
        }

        if (TextRoughMatch(selected, equipped))
        {
            return true;
        }

        foreach (var line in selected.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (TextRoughMatch(line, equipped))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TextRoughMatch(string left, string right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase) ||
            left.Contains(right, StringComparison.OrdinalIgnoreCase) ||
            right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }

    private ulong GetHotbarItemSlot(string itemName)
    {
        var hotbar = GetHotbarGui();
        if (hotbar == 0)
        {
            return 0;
        }

        var slots = new List<ulong>();
        var slotNames = new List<string>();
        foreach (var slot in _memory.ReadChildren(hotbar))
        {
            if (!string.Equals(_memory.ReadClass(slot), "ImageButton", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_memory.ReadName(slot), "ItemTemplate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nameLabel = _memory.FindChildByName(slot, "ItemName");
            slots.Add(slot);
            slotNames.Add(nameLabel == 0 ? string.Empty : _memory.ReadGuiText(nameLabel));
        }

        var index = ResolveHotbarSlotIndex(slotNames, itemName);
        LogAutoTotem($"hotbar scan for '{itemName}': [{string.Join(" | ", slotNames)}] -> index={index}");
        return index >= 0 && index < slots.Count ? slots[index] : 0;
    }

    // Returns the 0-based position of the hotbar slot whose item label matches
    // itemName, or -1 when no slot matches.
    internal static int ResolveHotbarSlotIndex(IReadOnlyList<string> orderedSlotNames, string itemName)
    {
        var target = NormalizeHotbarItemName(itemName);
        if (target.Length == 0)
        {
            return -1;
        }

        for (var index = 0; index < orderedSlotNames.Count; index++)
        {
            var slotName = NormalizeHotbarItemName(orderedSlotNames[index]);
            if (slotName.Length != 0 && slotName.Contains(target, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    // Strips rich-text markup and collapses whitespace so hotbar slot labels
    // (which can carry <font> tags or quantity suffixes) compare cleanly.
    private static string NormalizeHotbarItemName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stripped = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", string.Empty);
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, "\\s+", " ");
        return stripped.Trim().ToLowerInvariant();
    }

    private ulong GetHotbarGui()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "backpack");
        return backpack == 0 ? 0 : _memory.FindChildByName(backpack, "hotbar");
    }

    private string GetEquippedToolName()
    {
        var workspace = _memory.FindWorkspace();
        var localPlayer = _memory.GetLocalPlayer();
        if (workspace == 0 || localPlayer == 0)
        {
            return string.Empty;
        }

        var playerName = _memory.ReadName(localPlayer);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return string.Empty;
        }

        var character = _memory.FindChildByName(workspace, playerName);
        if (character == 0)
        {
            return string.Empty;
        }

        foreach (var child in _memory.ReadChildren(character))
        {
            if (string.Equals(_memory.ReadClass(child), "Tool", StringComparison.OrdinalIgnoreCase))
            {
                return Normalize(_memory.ReadName(child));
            }
        }

        return string.Empty;
    }

    private bool GetWorldStatusVisible(string statusName)
    {
        var status = ResolveWorldStatus(statusName);
        return status != 0 &&
            _memory.IsVisible(status, "FrameVisible") &&
            _memory.ReadGuiBounds(status, true) is not null;
    }

    private string GetWorldStatusText(string statusName)
    {
        var status = ResolveWorldStatus(statusName);
        if (status == 0 || !_memory.IsVisible(status, "FrameVisible") || _memory.ReadGuiBounds(status, true) is null)
        {
            return string.Empty;
        }

        var label = _memory.FindChildByName(status, "label");
        if (label == 0)
        {
            return string.Empty;
        }

        return Normalize(_memory.ReadGuiText(label));
    }

    private ulong ResolveWorldStatus(string statusName)
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var hud = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "hud");
        var safezone = hud == 0 ? 0 : _memory.FindChildByName(hud, "safezone");
        var worldStatuses = safezone == 0 ? 0 : _memory.FindChildByName(safezone, "worldstatuses");
        return worldStatuses == 0 ? 0 : _memory.FindChildByName(worldStatuses, statusName);
    }

    private static string Normalize(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }

    private static string NormalizeCompare(string text)
    {
        return Normalize(text).Replace("  ", " ", StringComparison.Ordinal);
    }

    private static string NormalizeLoose(string text)
    {
        return NormalizeCompare(text).ToLowerInvariant();
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        var haystack = NormalizeLoose(text);
        var needle = NormalizeLoose(phrase);
        if (haystack.Length == 0 || needle.Length == 0)
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            haystack,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(needle).Replace("\\ ", "\\s+")}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private bool TryReadActiveState(out ActiveTotemState state)
    {
        var eventText = GetWorldStatusText("2_event");
        var cycleText = GetWorldStatusText("4_cycle");
        var statusEntries = GetAllVisibleWorldStatusEntries();
        var allStatuses = new List<string>(statusEntries.Count);
        foreach (var entry in statusEntries)
        {
            allStatuses.Add(entry.Text);
        }

        var combined = string.Join(" ", allStatuses);
        var weatherText = ResolvePrimaryWeatherText(statusEntries);
        var weather = ResolveWeather(weatherText, combined);
        var cycle = ResolveCycle(cycleText, allStatuses);
        UpdateSpecialSurgesFromChat();
        var shiny = _chatShinyActive;
        var sparkling = _chatSparklingActive;
        var mutation = _chatMutationActive;
        state = new ActiveTotemState(weather, cycle, shiny, sparkling, mutation);
        return true;
    }

    private void UpdateSpecialSurgesFromChat()
    {
        var now = Environment.TickCount64;
        var entries = ReadChatEntriesForSurges();
        if (!_surgeChatPrimed)
        {
            foreach (var entry in entries)
            {
                _surgeChatSeenEntries.Add($"{entry.Address}:{NormalizeCompare(entry.Text)}");
            }
            _surgeChatPrimed = true;
            return;
        }

        foreach (var entry in entries)
        {
            var entryKey = $"{entry.Address}:{NormalizeCompare(entry.Text)}";
            if (!_surgeChatSeenEntries.Add(entryKey))
            {
                continue;
            }

            var text = NormalizeCompare(entry.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (ContainsPhrase(text, "There is currently a Shiny surge"))
            {
                _chatShinyActive = true;
                _chatSparklingActive = false;
                _chatMutationActive = false;
                _chatShinyActivatedAt = now;
                _chatSparklingActivatedAt = 0;
                _chatMutationActivatedAt = 0;
            }
            else if (ContainsPhrase(text, "Shiny Surge is now over"))
            {
                _chatShinyActive = false;
                _chatShinyActivatedAt = 0;
            }

            if (ContainsPhrase(text, "Today is the Day of the Luminous") ||
                ContainsPhrase(text, "Tonight is the Night of the Luminous"))
            {
                _chatShinyActive = false;
                _chatSparklingActive = true;
                _chatMutationActive = false;
                _chatShinyActivatedAt = 0;
                _chatSparklingActivatedAt = now;
                _chatMutationActivatedAt = 0;
            }
            else if (ContainsPhrase(text, "Night of the Luminous is now over"))
            {
                _chatSparklingActive = false;
                _chatSparklingActivatedAt = 0;
            }

            if (ContainsPhrase(text, "There is currently a Mutation surge"))
            {
                _chatShinyActive = false;
                _chatSparklingActive = false;
                _chatMutationActive = true;
                _chatShinyActivatedAt = 0;
                _chatSparklingActivatedAt = 0;
                _chatMutationActivatedAt = now;
            }
            else if (ContainsPhrase(text, "Mutation Surge is now over"))
            {
                _chatMutationActive = false;
                _chatMutationActivatedAt = 0;
            }
        }

        if (_chatShinyActive && _chatShinyActivatedAt != 0 && now - _chatShinyActivatedAt >= SurgeMaxActiveMs)
        {
            _chatShinyActive = false;
            _chatShinyActivatedAt = 0;
            LogAutoTotem("chat surge timeout: shiny marked gone after 20 minutes");
        }

        if (_chatSparklingActive && _chatSparklingActivatedAt != 0 && now - _chatSparklingActivatedAt >= SurgeMaxActiveMs)
        {
            _chatSparklingActive = false;
            _chatSparklingActivatedAt = 0;
            LogAutoTotem("chat surge timeout: sparkling marked gone after 20 minutes");
        }

        if (_chatMutationActive && _chatMutationActivatedAt != 0 && now - _chatMutationActivatedAt >= SurgeMaxActiveMs)
        {
            _chatMutationActive = false;
            _chatMutationActivatedAt = 0;
            LogAutoTotem("chat surge timeout: mutation marked gone after 20 minutes");
        }
    }

    private List<TotemChatEntry> ReadChatEntriesForSurges()
    {
        var results = new List<TotemChatEntry>();
        var dataModel = _memory.GetDataModel();
        if (dataModel == 0)
        {
            return results;
        }

        var textChatService = _memory.FindDescendantByClass(dataModel, "TextChatService");
        if (textChatService == 0)
        {
            return results;
        }

        var stack = new Stack<ulong>();
        var seen = new HashSet<ulong>();
        stack.Push(textChatService);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var className = _memory.ReadClass(current);
            if (className.Contains("TextChatMessage", StringComparison.OrdinalIgnoreCase))
            {
                var text = ReadTextChatMessageText(current);
                if (text.Length > 0)
                {
                    results.Add(new TotemChatEntry(current, text));
                }
            }

            foreach (var child in _memory.ReadChildren(current))
            {
                stack.Push(child);
            }
        }

        return results;
    }

    private string ReadTextChatMessageText(ulong address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        var text = Normalize(_memory.ReadGuiText(address));
        if (text.Length > 0)
        {
            return text;
        }

        try
        {
            var textOffset = _memory.GetOffset("Text");
            var indirect = Normalize(_memory.ReadString(_memory.ReadPtr(address + textOffset)));
            if (indirect.Length > 0)
            {
                return indirect;
            }
        }
        catch
        {
            // Best-effort chat parsing.
        }

        return string.Empty;
    }

    private static string ResolveWeather(string weatherText, string combined)
    {
        var source = string.IsNullOrWhiteSpace(weatherText) ? combined : weatherText;
        if (ContainsAny(source, "aurora"))
        {
            return "Aurora Borealis";
        }

        if (ContainsAny(source, "starfall"))
        {
            return "Starfall";
        }

        if (ContainsAny(source, "eclipse"))
        {
            return "Eclipse";
        }

        if (ContainsAny(source, "rainbow"))
        {
            return "Rainbow";
        }

        if (ContainsAny(source, "rain", "rainy"))
        {
            return "Rain";
        }

        if (ContainsAny(source, "wind", "windy"))
        {
            return "Windy";
        }

        if (ContainsAny(source, "fog", "foggy"))
        {
            return "Foggy";
        }

        if (ContainsAny(source, "clear"))
        {
            return "Clear";
        }

        // Fallback to full combined statuses if the weather slot is empty/noisy.
        if (!ReferenceEquals(source, combined))
        {
            return ResolveWeather(string.Empty, combined);
        }

        return string.Empty;
    }

    private static string ResolveCycle(string cycleText, IReadOnlyList<string> statuses)
    {
        if (ContainsPhrase(cycleText, "night"))
        {
            return "Night";
        }

        if (ContainsPhrase(cycleText, "day"))
        {
            return "Day";
        }

        var combined = string.Join(" ", statuses).Replace("Night of the Luminous", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (ContainsPhrase(combined, "night"))
        {
            return "Night";
        }

        if (ContainsPhrase(combined, "day"))
        {
            return "Day";
        }

        return string.Empty;
    }

    private static bool AnyStatusMatches(
        IEnumerable<string> statuses,
        string phrase,
        bool requireActiveSignal = false,
        bool requirePlusMarker = false)
    {
        foreach (var text in statuses)
        {
            if (!IsNamedStatus(text, phrase))
            {
                continue;
            }

            if (requirePlusMarker && !HasPlusMarkerForPhrase(text, phrase))
            {
                continue;
            }

            if (requireActiveSignal && !HasActiveStatusSignal(text))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasActiveStatusSignal(string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                @"\d",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            normalized.Contains('%', StringComparison.Ordinal) ||
            normalized.Contains('+', StringComparison.Ordinal) ||
            ContainsPhrase(normalized, "increased") ||
            ContainsPhrase(normalized, "boost");
    }

    private static bool HasPlusMarkerForPhrase(string text, string phrase)
    {
        var normalizedText = Normalize(text);
        var normalizedPhrase = Normalize(phrase);
        if (normalizedText.Length == 0 || normalizedPhrase.Length == 0)
        {
            return false;
        }

        var parts = normalizedPhrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var phrasePattern = string.Join(@"\s+", Array.ConvertAll(parts, System.Text.RegularExpressions.Regex.Escape));
        return System.Text.RegularExpressions.Regex.IsMatch(
            normalizedText,
            $@"\+\s*{phrasePattern}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool IsSurgeActiveFromEvent(string eventText, string surgePhrase)
    {
        return ContainsPhrase(eventText, surgePhrase);
    }

    private static string ResolveActiveSurge(string eventText, IReadOnlyList<string> allStatuses)
    {
        if (IsNamedStatus(eventText, "shiny surge"))
        {
            return "shiny";
        }

        if (IsNamedStatus(eventText, "sparkling surge") || IsNamedStatus(eventText, "night of the luminous"))
        {
            return "sparkling";
        }

        if (IsNamedStatus(eventText, "mutation surge"))
        {
            return "mutation";
        }

        if (AnyStatusMatches(allStatuses, "shiny surge"))
        {
            return "shiny";
        }

        if (AnyStatusMatches(allStatuses, "sparkling surge") || AnyStatusMatches(allStatuses, "night of the luminous"))
        {
            return "sparkling";
        }

        if (AnyStatusMatches(allStatuses, "mutation surge"))
        {
            return "mutation";
        }

        return string.Empty;
    }

    private static bool IsNamedStatus(string text, string phrase)
    {
        var haystack = NormalizeLoose(text);
        var needle = NormalizeLoose(phrase);
        if (haystack.Length == 0 || needle.Length == 0)
        {
            return false;
        }

        if (!haystack.StartsWith(needle, StringComparison.Ordinal))
        {
            return false;
        }

        if (haystack.Length == needle.Length)
        {
            return true;
        }

        var next = haystack[needle.Length];
        return next == ' ' || next == ':' || next == '-' || next == '(';
    }

    private IReadOnlyList<WorldStatusEntry> GetAllVisibleWorldStatusEntries()
    {
        var values = new List<WorldStatusEntry>();
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return values;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var hud = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "hud");
        var safezone = hud == 0 ? 0 : _memory.FindChildByName(hud, "safezone");
        var worldStatuses = safezone == 0 ? 0 : _memory.FindChildByName(safezone, "worldstatuses");
        if (worldStatuses == 0)
        {
            return values;
        }

        foreach (var status in _memory.ReadChildren(worldStatuses))
        {
            if (!_memory.IsVisible(status, "FrameVisible") || _memory.ReadGuiBounds(status, true) is null)
            {
                continue;
            }

            var label = _memory.FindChildByName(status, "label");
            if (label == 0)
            {
                continue;
            }

            var text = Normalize(_memory.ReadGuiText(label));
            if (!string.IsNullOrWhiteSpace(text))
            {
                values.Add(new WorldStatusEntry(Normalize(_memory.ReadName(status)), text));
            }
        }

        return values;
    }

    private static string ResolvePrimaryWeatherText(IReadOnlyList<WorldStatusEntry> entries)
    {
        // Read every visible weather-designated slot first (e.g. 3_weather and
        // variants) before falling back to global combined text.
        var weatherTexts = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.Name.Contains("weather", StringComparison.OrdinalIgnoreCase))
            {
                weatherTexts.Add(entry.Text);
            }
        }

        if (weatherTexts.Count > 0)
        {
            return string.Join(" ", weatherTexts);
        }

        return string.Empty;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (ContainsPhrase(text, needle))
            {
                return true;
            }
        }

        var normalized = NormalizeLoose(text);
        foreach (var needle in needles)
        {
            var token = NormalizeLoose(needle);
            if (token.Length > 0 && normalized.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetSelectedTotemConfig(out SelectedTotemConfig config)
    {
        var weatherName = Normalize(AutoTotemSettings.TotemName);
        var weatherCondition = GetWeatherConditionFromSelection(weatherName);
        var weatherTotem = GetWeatherTotemItemFromSelection(weatherName);
        var special = AutoTotemSettings.Special;
        var timePreference = AutoTotemSettings.TimePreference;
        var hasWeather = !string.IsNullOrWhiteSpace(weatherCondition);
        var hasSpecial = special != AutoTotemSpecial.None;
        var hasTime = timePreference != AutoTotemTimePreference.None;
        if (!hasWeather && !hasSpecial && !hasTime)
        {
            config = default;
            return false;
        }

        config = new SelectedTotemConfig(weatherCondition, weatherTotem, special, timePreference);
        return true;
    }

    private static string GetRequiredTime(string weatherCondition, AutoTotemTimePreference selectedTime)
    {
        if (string.Equals(weatherCondition, "Rainbow", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(weatherCondition, "Eclipse", StringComparison.OrdinalIgnoreCase))
        {
            return "Day";
        }

        if (string.Equals(weatherCondition, "Aurora Borealis", StringComparison.OrdinalIgnoreCase))
        {
            return "Night";
        }

        return selectedTime switch
        {
            AutoTotemTimePreference.Day => "Day",
            AutoTotemTimePreference.Night => "Night",
            _ => string.Empty,
        };
    }

    private static bool IsWeatherMissing(string selectedWeather, string activeWeather)
    {
        if (string.IsNullOrWhiteSpace(selectedWeather))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(activeWeather))
        {
            // If a weather target is selected but no weather is readable, treat as missing
            // so weather totem flow can still run.
            return true;
        }

        var selected = NormalizeLoose(selectedWeather);
        var active = NormalizeLoose(activeWeather);
        if (selected.Length == 0 || active.Length == 0)
        {
            return true;
        }

        // Accept exact or embedded weather labels (e.g. "windy, day").
        if (string.Equals(selected, active, StringComparison.OrdinalIgnoreCase) ||
            active.StartsWith(selected + " ", StringComparison.OrdinalIgnoreCase) ||
            active.StartsWith(selected + ",", StringComparison.OrdinalIgnoreCase) ||
            active.Contains(" " + selected + " ", StringComparison.OrdinalIgnoreCase) ||
            active.Contains(" " + selected + ",", StringComparison.OrdinalIgnoreCase) ||
            active.EndsWith(" " + selected, StringComparison.OrdinalIgnoreCase) ||
            active.EndsWith("," + selected, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool IsSpecialMissing(AutoTotemSpecial special, ActiveTotemState active)
    {
        return special switch
        {
            AutoTotemSpecial.Shiny => !active.Shiny,
            AutoTotemSpecial.Sparkling => !active.Sparkling,
            AutoTotemSpecial.Mutation => !active.Mutation,
            _ => false,
        };
    }

    private static string GetSpecialTotemItemName(AutoTotemSpecial special)
    {
        return special switch
        {
            AutoTotemSpecial.Shiny => "Shiny Totem",
            AutoTotemSpecial.Sparkling => "Sparkling Totem",
            AutoTotemSpecial.Mutation => "Mutation Totem",
            _ => string.Empty,
        };
    }

    private static string GetWeatherConditionFromSelection(string selectedTotemName)
    {
        return selectedTotemName switch
        {
            "Tempest Totem" or "Rain Totem" => "Rain",
            "Windset Totem" or "Windy Totem" => "Windy",
            "Smokescreen Totem" or "Foggy Totem" => "Foggy",
            "Clearcast Totem" => "Clear",
            "Eclipse Totem" => "Eclipse",
            "Starfall Totem" => "Starfall",
            "Aurora Totem" or "Aurora Borealis Totem" => "Aurora Borealis",
            "Rainbow Totem" => "Rainbow",
            _ => string.Empty,
        };
    }

    private static string GetWeatherTotemItemFromSelection(string selectedTotemName)
    {
        return selectedTotemName switch
        {
            "Tempest Totem" or "Rain Totem" => "Tempest Totem",
            "Windset Totem" or "Windy Totem" => "Windset Totem",
            "Smokescreen Totem" or "Foggy Totem" => "Smokescreen Totem",
            "Clearcast Totem" => "Clearcast Totem",
            "Eclipse Totem" => "Eclipse Totem",
            "Starfall Totem" => "Starfall Totem",
            "Aurora Totem" or "Aurora Borealis Totem" => "Aurora Totem",
            "Rainbow Totem" => "Rainbow Totem",
            _ => string.Empty,
        };
    }

    private static IReadOnlyList<string> GetTotemNameVariants(string canonicalName)
    {
        return canonicalName switch
        {
            "Aurora Borealis Totem" => ["Aurora Totem"],
            "Rain Totem" => ["Tempest Totem"],
            "Windy Totem" => ["Windset Totem"],
            "Foggy Totem" => ["Smokescreen Totem"],
            _ => [canonicalName],
        };
    }

    private bool OpenInventoryIfNeeded()
    {
        LogAutoTotem("opening inventory");
        Thread.Sleep(AutoTotemShortActionDelayMs);
        NativeKeyboard.PressG(_memory.WindowHandle);
        Thread.Sleep(AutoTotemMediumActionDelayMs);
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(80);
            if (IsInventoryOpen())
            {
                return true;
            }
        }

        // If the first toggle likely closed an already-open inventory (or missed),
        // try one more toggle to guarantee we end in open state.
        LogAutoTotem("inventory not open after first toggle, retrying");
        Thread.Sleep(AutoTotemShortActionDelayMs);
        NativeKeyboard.PressG(_memory.WindowHandle);
        Thread.Sleep(AutoTotemMediumActionDelayMs);
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(80);
            if (IsInventoryOpen())
            {
                return true;
            }
        }

        LogAutoTotem("failed to open inventory");
        return false;
    }

    private bool CloseInventoryIfOpen()
    {
        if (!IsInventoryOpen())
        {
            return true;
        }

        LogAutoTotem("closing inventory");
        Thread.Sleep(AutoTotemMediumActionDelayMs);
        NativeKeyboard.PressG(_memory.WindowHandle);
        Thread.Sleep(AutoTotemShortActionDelayMs);
        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(80);
            if (!IsInventoryOpen())
            {
                return true;
            }
        }

        LogAutoTotem("failed to close inventory");
        return false;
    }

    private bool SearchAndSelectInventoryTotem(string canonicalItemName)
    {
        var searchFrame = ResolveInventorySearchFrame();
        var itemContainer = ResolveInventoryItemContainer();
        if (searchFrame == 0 || itemContainer == 0)
        {
            LogAutoTotem("inventory search targets not found");
            return false;
        }

        foreach (var variant in GetTotemNameVariants(canonicalItemName))
        {
            LogAutoTotem($"searching inventory for {variant}");
            Thread.Sleep(1000);
            ClickGuiCenter(searchFrame);
            Thread.Sleep(AutoTotemShortActionDelayMs);
            ClearInventorySearchInput();
            NativeKeyboard.TypeText(variant, _memory.WindowHandle);
            Thread.Sleep(AutoTotemLongActionDelayMs);
            if (SelectInventoryTotem(itemContainer, variant))
            {
                return true;
            }
        }

        LogAutoTotem($"inventory item not found: {canonicalItemName}");
        return false;
    }

    private void ClearInventorySearchInput()
    {
        // Some systems intermittently miss the first Ctrl+A/Backspace due to focus jitter.
        // Run a short, deterministic clear sequence to improve reliability.
        for (var i = 0; i < 2; i++)
        {
            NativeKeyboard.PressCtrlA(_memory.WindowHandle);
            Thread.Sleep(AutoTotemShortActionDelayMs);
            NativeKeyboard.PressBackspace(_memory.WindowHandle);
            Thread.Sleep(AutoTotemShortActionDelayMs);
        }
    }

    private bool SelectInventoryTotem(ulong itemContainer, string totemName)
    {
        var target = FindInventoryItemTarget(itemContainer, totemName);
        if (target == 0)
        {
            return false;
        }

        LogAutoTotem($"selecting inventory totem: {totemName}");
        ClickGuiCenter(target);
        Thread.Sleep(AutoTotemMediumActionDelayMs);
        return true;
    }

    private ulong FindInventoryItemTarget(ulong itemContainer, string totemName)
    {
        var needle = NormalizeLoose(totemName);
        foreach (var node in Traverse(itemContainer, 32))
        {
            var cls = _memory.ReadClass(node);
            if (!cls.Contains("Text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = NormalizeLoose(_memory.ReadGuiText(node));
            if (text.Length == 0)
            {
                continue;
            }

            if (!text.Equals(needle, StringComparison.OrdinalIgnoreCase) &&
                !text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var clickable = FindClickableAncestor(node, itemContainer);
            if (clickable != 0)
            {
                return clickable;
            }
        }

        return 0;
    }

    private ulong FindClickableAncestor(ulong node, ulong stopRoot)
    {
        var current = node;
        for (var i = 0; i < 10 && RobloxMemory.IsValidAddress(current); i++)
        {
            var cls = _memory.ReadClass(current);
            if (cls.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Frame", StringComparison.OrdinalIgnoreCase))
            {
                if (_memory.ReadGuiBounds(current, false) is { Width: > 10, Height: > 10 })
                {
                    return current;
                }
            }

            if (current == stopRoot)
            {
                break;
            }

            var parent = _memory.ReadPtr(current + _memory.GetOffset("Parent"));
            if (!RobloxMemory.IsValidAddress(parent) || parent == current)
            {
                break;
            }

            current = parent;
        }

        return 0;
    }

    private bool IsInventoryOpen()
    {
        var inventory = ResolveInventoryFrame();
        return inventory != 0 && _memory.IsVisible(inventory, "FrameVisible");
    }

    private ulong ResolveInventoryFrame()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "backpack");
        return backpack == 0 ? 0 : _memory.FindChildByName(backpack, "inventory");
    }

    private ulong ResolveInventorySearchFrame()
    {
        var inventory = ResolveInventoryFrame();
        if (inventory == 0)
        {
            return 0;
        }

        var byPath = FindByPath(inventory, "Search", "Frame", "topbar", "search");
        if (byPath != 0)
        {
            return byPath;
        }

        return FindByPath(inventory, "Search", string.Empty, "topbar");
    }

    private ulong ResolveInventoryItemContainer()
    {
        var inventory = ResolveInventoryFrame();
        if (inventory == 0)
        {
            return 0;
        }

        var byName = _memory.FindChildByName(inventory, "itemContainer");
        if (byName != 0)
        {
            return byName;
        }

        return FindByPath(inventory, "itemContainer", "Frame");
    }

    private void ClickRobloxCenter()
    {
        var rect = GetClientScreenRectangle();
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        NativeMouse.ClickAt(rect.X + rect.Width / 2, rect.Y + rect.Height / 2 - 200);
    }

    private void ClickGuiCenter(ulong address)
    {
        var bounds = _memory.ReadGuiBounds(address, false);
        if (bounds is null)
        {
            return;
        }

        var origin = GetClientScreenOrigin();
        var x = (int)Math.Round(origin.X + bounds.Value.X + bounds.Value.Width * 0.5f);
        var y = (int)Math.Round(origin.Y + bounds.Value.Y + bounds.Value.Height * 0.5f);
        NativeMouse.ClickAt(x, y);
    }

    private WinPoint GetClientScreenOrigin()
    {
        var point = new WinPoint(0, 0);
        ClientToScreen(_memory.WindowHandle, ref point);
        return point;
    }

    private ClientRectangle GetClientScreenRectangle()
    {
        if (!GetClientRect(_memory.WindowHandle, out var rect))
        {
            return new ClientRectangle(0, 0, 0, 0);
        }

        var origin = GetClientScreenOrigin();
        return new ClientRectangle(origin.X, origin.Y, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
    }

    private ulong FindByPath(ulong root, string name, string classNeedle, params string[] segments)
    {
        foreach (var item in Traverse(root, 64))
        {
            var nameMatches = string.IsNullOrWhiteSpace(name) || string.Equals(_memory.ReadName(item), name, StringComparison.OrdinalIgnoreCase);
            if (!nameMatches)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(classNeedle))
            {
                var className = _memory.ReadClass(item);
                if (!className.Contains(classNeedle, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(className, classNeedle, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var path = BuildPath(item).ToLowerInvariant();
            var matched = true;
            foreach (var segment in segments)
            {
                if (!path.Contains((segment ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal))
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return item;
            }
        }

        return 0;
    }

    private IEnumerable<ulong> Traverse(ulong root, int maxDepth)
    {
        var seen = new HashSet<ulong>();
        var queue = new Queue<(ulong Address, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (address, depth) = queue.Dequeue();
            if (!seen.Add(address))
            {
                continue;
            }

            yield return address;
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(address))
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private string BuildPath(ulong instance)
    {
        var parts = new List<string>();
        var current = instance;
        for (var i = 0; i < 64 && RobloxMemory.IsValidAddress(current); i++)
        {
            var name = _memory.ReadName(current);
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name);
            }

            var parent = _memory.ReadPtr(current + _memory.GetOffset("Parent"));
            if (!RobloxMemory.IsValidAddress(parent) || parent == current)
            {
                break;
            }

            current = parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private void LogAutoTotem(string message)
    {
        AppLog.Info("AutoTotem", $"{GetModeLabel()} | {message}");
    }

    // Execution-flow trace for Auto Totem. De-duplicated: a decision point is
    // only written when its message differs from the previous one, so a stuck
    // state logs a single line instead of spamming the file every tick.
    private void LogAutoTotemFlow(string message)
    {
        if (string.Equals(message, _totemLastFlowLog, StringComparison.Ordinal))
        {
            return;
        }

        _totemLastFlowLog = message;
        LogAutoTotem(message);
    }

    private string DescribeAutoTotemGate()
    {
        return $"reelGui={_context.IsReelGuiVisible()}, holding={_perfectCastingHolding}, " +
               $"castBar={_castBarSeen}, boundary={IsAutoTotemBoundary()}";
    }

    private readonly record struct SelectedTotemConfig(
        string WeatherCondition,
        string WeatherTotemItemName,
        AutoTotemSpecial Special,
        AutoTotemTimePreference TimePreference);

    private readonly record struct WorldStatusEntry(string Name, string Text);

    internal readonly record struct CompletionState(bool CompletionReached, double MaxProgressThisCycle);

    internal readonly record struct ReelOutcomeState(bool OutcomeResolved, FishingReelStats Stats);

    private readonly record struct ActiveTotemState(
        string Weather,
        string Cycle,
        bool Shiny,
        bool Sparkling,
        bool Mutation);

    private readonly record struct TotemChatEntry(ulong Address, string Text);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint point);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;

        public WinPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly record struct ClientRectangle(int X, int Y, int Width, int Height);

    private void ResetActiveTrackingControllers()
    {
        _controller.Reset();
        _bellonaRightController.Reset();
        _startupAssistController.Reset();
        _tracking2Controller.Reset();
        _bellonaRightTracking2Controller.Reset();
        _tracking3Controller.Reset();
        _bellonaRightTracking3Controller.Reset();
        ReleaseFishingHold();
        ReleaseBellonaRightHold();
        _fishingGate.Reset();
        _rodProfile.Reset();
        _normalCastHoldStartedAt = 0;
    }

    private void MaybeDetectRod()
    {
        var now = Environment.TickCount64;
        if (_lastRodDetectAt != 0 && now - _lastRodDetectAt < 1500)
        {
            return;
        }

        _lastRodDetectAt = now;
        try
        {
            var kind = RodClassifier.Classify(HotbarRodResolver.GetHotbarRodDisplayText(_memory));
            if (kind == RodKind.MasterlineRod)
            {
                var overlayKind = ResolveMasterlineOverlayKind();
                if (overlayKind is not RodKind.Default and not RodKind.MasterlineRod)
                {
                    kind = overlayKind;
                }
            }

            if (kind != _rodProfile.Kind)
            {
                _rodProfile = RodProfile.For(kind);
            }
        }
        catch
        {
            // Keep the current profile if detection fails this tick.
        }
    }

    private void TryHandleSplitbranchTwigCrateSelection()
    {
        if (_rodProfile.Kind != RodKind.SplitbranchTwig)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _splitbranchLastCrateClickAt < SplitbranchCrateClickCooldownMs)
        {
            return;
        }

        var target = FindSplitbranchCommonCrateTarget();
        if (target == 0)
        {
            return;
        }

        ClickGuiCenter(target);
        _splitbranchLastCrateClickAt = now;
        AppLog.Info("SplitbranchTwig", "Clicked FishSelection entry (non-empty text).");
    }

    private void TryHandleMiguCounterAttackShift()
    {
        if (_rodProfile.Kind != RodKind.MiguRod)
        {
            if (_miguCounterWasVisible || _miguShiftFiredThisAppearance || _miguShiftFireAt != 0)
            {
                AppLog.Info("MiguCounter", "state reset (not Migu rod)");
            }
            _miguCounterWasVisible = false;
            _miguShiftFiredThisAppearance = false;
            _miguShiftFireAt = 0;
            return;
        }

        var now = Environment.TickCount64;
        var counterAttack = ResolveCounterAttackFrame();
        var visible = counterAttack != 0 &&
                      _memory.IsVisible(counterAttack, "FrameVisible") &&
                      _memory.ReadGuiBounds(counterAttack, true) is not null;

        if (visible && !_miguCounterWasVisible)
        {
            _miguCounterWasVisible = true;
            _miguShiftFiredThisAppearance = false;
            _miguShiftFireAt = now + MiguCounterShiftDelayMs;
            AppLog.Info("MiguCounter", $"APPEARED -> armed shift in {MiguCounterShiftDelayMs}ms (addr=0x{counterAttack:X})");
            return;
        }

        if (!visible && _miguCounterWasVisible)
        {
            _miguCounterWasVisible = false;
            _miguShiftFiredThisAppearance = false;
            _miguShiftFireAt = 0;
            AppLog.Info("MiguCounter", "DISAPPEARED -> reset");
            return;
        }

        if (!visible)
        {
            return;
        }

        if (!_miguShiftFiredThisAppearance && _miguShiftFireAt != 0 && now >= _miguShiftFireAt)
        {
            NativeKeyboard.PressShift(_memory.WindowHandle);
            _miguShiftFiredThisAppearance = true;
            AppLog.Info("MiguCounter", $"SHIFT fired (delay={MiguCounterShiftDelayMs}ms)");
        }
    }

    private RodKind ResolveMasterlineOverlayKind()
    {
        var names = _context.GetMasterlineOverlayRodNames();
        foreach (var name in names)
        {
            var kind = RodClassifier.Classify(name);
            if (kind is not RodKind.Default and not RodKind.MasterlineRod)
            {
                return kind;
            }
        }

        return RodKind.MasterlineRod;
    }

    private ulong ResolveCounterAttackFrame()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        if (playerGui == 0)
        {
            return 0;
        }

        var screenGui = _memory.FindChildByName(playerGui, "reel");
        if (screenGui == 0)
        {
            screenGui = _memory.FindDescendantByName(playerGui, "reel");
            if (screenGui == 0)
            {
                return 0;
            }
        }

        var counterAttack = _memory.FindDescendantByName(screenGui, "counterAttack");
        if (counterAttack == 0)
        {
            return 0;
        }

        var cls = _memory.ReadClass(counterAttack);
        return cls.Equals("Frame", StringComparison.OrdinalIgnoreCase) ? counterAttack : 0;
    }

    private ulong FindSplitbranchCommonCrateTarget()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        if (playerGui == 0)
        {
            return 0;
        }

        var fishSelection = _memory.FindChildByName(playerGui, "FishSelection");
        if (fishSelection == 0)
        {
            return 0;
        }

        var listRoot = _memory.FindDescendantByName(fishSelection, "List");
        if (listRoot == 0)
        {
            listRoot = fishSelection;
        }

        foreach (var node in Traverse(listRoot, 64))
        {
            var cls = _memory.ReadClass(node);
            if (!string.Equals(cls, "TextLabel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = NormalizeLoose(_memory.ReadGuiText(node));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            // Keep this constrained to the FishSelection list button path shape.
            var path = BuildPath(node).ToLowerInvariant();
            if (!path.Contains("fishselection", StringComparison.Ordinal) ||
                !path.Contains("list", StringComparison.Ordinal) ||
                !path.Contains("button", StringComparison.Ordinal))
            {
                continue;
            }

            var clickable = FindClickableAncestor(node, fishSelection);
            if (clickable != 0)
            {
                return clickable;
            }
        }

        return 0;
    }

    private void ApplyFishingHold(bool desired, double? progress)
    {
        var transformed = _rodProfile.TransformHold(desired, progress);
        var action = _fishingGate.Decide(transformed, Environment.TickCount64, _rodProfile.FishingActionDelayMs);
        if (action == FishingHoldAction.Press)
        {
            NativeMouse.LeftDown();
        }
        else if (action == FishingHoldAction.Release)
        {
            NativeMouse.LeftUp();
        }
    }

    private void ReleaseFishingHold()
    {
        if (_fishingGate.ForceRelease(Environment.TickCount64) == FishingHoldAction.Release)
        {
            NativeMouse.LeftUp();
        }
    }

    private string GetModeLabel()
    {
        return _mode switch
        {
            FishingTrackerMode.Tracking2 => "Tracking 2",
            FishingTrackerMode.Tracking3 => "Tracking 3",
            _ => "Tracking 1",
        };
    }
}
