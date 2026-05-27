using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Client.Services.Fishing;

public sealed class Tracking2FishingTracker : IFishingTracker
{
    private const int ReelInputSettleMs = 125;
    private const int PostCatchSettleMs = 600;

    private readonly Tracking2Settings _settings = new();
    private readonly RobloxMemory _memory;
    private readonly FishingRuntimeContext _context;
    private readonly Tracking2Controller _controller = new();
    private Timer? _timer;
    private bool _isRunning;
    private bool _isSuspended;
    private string _suspendMessage = "Fishing suspended.";
    private bool _inTick;
    private string _phase = "OFF";
    private double? _progressPercent;
    private long _castStartedAt;
    private long _castReleasedAt;
    private long _fishingLostAt;
    private long _lastShakedAt;
    private long _lastActionAt;
    private long _castReadyAt;
    private long _fishingInputReadyAt;
    private bool _castBarSeen;
    private bool _completionReached;
    private long _completionLatchedAt;
    private bool _outcomeResolved;
    private FishingReelStats _reelStats = FishingReelStats.Empty;
    private bool _isHolding;
    private bool _activatedUiNav;
    private readonly Tracking1Controller _normalCastingController = new();
    private string _totemState = "IDLE";
    private int _totemRetryCount;
    private long _totemWaitStartedAt;
    private long _lastTotemSuccessAt;
    private long _lastTotemAttemptAt;
    private bool _totemPending;
    private bool _totemBlockedUntilCatchEnd;
    private bool _totemNightCovered;
    private bool _totemNeedsRodReequip;
    private bool _totemNeedsSettleDelay;

    public Tracking2FishingTracker()
    {
        _memory = new RobloxMemory(OffsetsSourceProvider.Current);
        _context = new FishingRuntimeContext(_memory);
        Status = new FishingTrackerStatus(false, "OFF", "Tracking 2 ready.", null, false);
    }

    public FishingTrackerMode Mode => FishingTrackerMode.Tracking2;

    public FishingTrackerStatus Status { get; private set; }

    public FishingCastingMode CastingMode { get; set; } = FishingCastingMode.Perfect;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _isRunning = true;
        _isSuspended = false;
        _suspendMessage = "Fishing suspended.";
        _timer = new Timer(_ => Tick(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_settings.UpdateRateMs));
        StartCycle();
    }

    public void Stop()
    {
        _isRunning = false;
        _isSuspended = false;
        _timer?.Dispose();
        _timer = null;
        StopCycle("OFF", "Tracking 2 stopped.");
    }

    public void Suspend(string message)
    {
        if (!_isRunning)
        {
            return;
        }

        _isSuspended = true;
        _suspendMessage = string.IsNullOrWhiteSpace(message) ? "Fishing suspended." : message;
        ReleaseMouse(true);
        _controller.Reset();
        _normalCastingController.Reset();
        Status = new FishingTrackerStatus(true, "SUSPENDED", _suspendMessage, _progressPercent, false, _reelStats);
    }

    public void Resume()
    {
        if (!_isRunning)
        {
            return;
        }

        _isSuspended = false;
        _suspendMessage = "Fishing suspended.";
    }

    public void Dispose()
    {
        Stop();
        _memory.Dispose();
    }

    private void Tick()
    {
        if (!_isRunning || _inTick)
        {
            return;
        }

        _inTick = true;
        try
        {
            if (_isSuspended)
            {
                ReleaseMouse(true);
                _controller.Reset();
                _normalCastingController.Reset();
                Status = new FishingTrackerStatus(true, "SUSPENDED", _suspendMessage, _progressPercent, false, _reelStats);
                return;
            }

            _memory.EnsureAttached();
            if (UpdateAutoTotem())
            {
                return;
            }

            switch (_phase)
            {
                case "CASTING":
                    UpdateCastingPhase();
                    break;
                case "CASTED":
                    UpdateCastedPhase();
                    break;
                case "SHAKE":
                    UpdateShakePhase();
                    break;
                case "FISHING":
                    UpdateFishingPhase();
                    break;
                case "DONE":
                    StartCycle();
                    break;
            }
        }
        catch (Exception ex)
        {
            ReleaseMouse(true);
            _controller.Reset();
            _normalCastingController.Reset();
            _context.ResetCache();
            Status = new FishingTrackerStatus(true, "ERROR", ex.Message, _progressPercent, _isHolding);
        }
        finally
        {
            _inTick = false;
        }
    }

    private void StartCycle()
    {
        ReleaseMouse(true);
        _controller.Reset();
        _context.ResetCache();
        _progressPercent = null;
        _castStartedAt = Environment.TickCount64;
        _castReleasedAt = 0;
        _fishingLostAt = 0;
        _lastShakedAt = 0;
        _lastActionAt = 0;
        _castReadyAt = 0;
        _fishingInputReadyAt = 0;
        _castBarSeen = false;
        _completionReached = false;
        _outcomeResolved = false;
        _normalCastingController.Reset();
        _totemBlockedUntilCatchEnd = false;
        _phase = "CASTING";
        if (CastingMode == FishingCastingMode.Normal)
        {
            var castState = _normalCastingController.UpdateCasting();
            _isHolding = castState.Holding;
        }

        SetStatus("CASTING", "Casting with Tracking 2 defaults.");
    }

    private void StopCycle(string nextPhase, string message)
    {
        if (string.Equals(nextPhase, "DONE", StringComparison.OrdinalIgnoreCase))
        {
            RecordReelOutcome(_completionReached);
        }

        ReleaseMouse(true);
        _controller.Reset();
        _normalCastingController.Reset();
        _context.ResetCache();
        _progressPercent = null;
        _fishingInputReadyAt = 0;
        _phase = nextPhase;
        SetStatus(nextPhase, message);
    }

    private void UpdateCastingPhase()
    {
        if (CastingMode == FishingCastingMode.Normal)
        {
            var progress = _context.GetFishingCompletionPercent();
            _progressPercent = progress;
            var context = _context.GetReelContext();
            var metrics = context is null ? null : _context.ReadMetrics(context);
            if (metrics is not null)
            {
                EnterFishingPhase("Reel detected; releasing cast input.");
                return;
            }

            var state = _normalCastingController.UpdateCasting();
            _isHolding = state.Holding;
            SetStatus("CASTING", "Normal casting until reel starts.");
            return;
        }

        _progressPercent = null;
        if (!_activatedUiNav)
        {
            NativeKeyboard.PressBackslash();
            _activatedUiNav = true;
            _castReadyAt = Environment.TickCount64 + 50;
            SetStatus("CASTING", "Preparing cast.");
            return;
        }

        if (_castReadyAt != 0 && Environment.TickCount64 < _castReadyAt)
        {
            SetStatus("CASTING", "Preparing cast.");
            return;
        }

        if (_castReadyAt != 0)
        {
            _castStartedAt = Environment.TickCount64;
            _castReadyAt = 0;
        }

        if (Environment.TickCount64 - _castStartedAt < _settings.PreCastDelayMs)
        {
            SetStatus("CASTING", "Waiting for pre-cast delay.");
            return;
        }

        HoldMouse();
        var powerPercent = _context.GetPowerBarPercent();
        if (powerPercent is null)
        {
            if (Environment.TickCount64 - _castStartedAt >= Math.Max(5000, _settings.CastTimeoutMs))
            {
                if (_settings.CastOnTimeout)
                {
                    StartCycle();
                }
                else
                {
                    StopCycle("OFF", "Cast timed out.");
                }
            }
            else
            {
                SetStatus("CASTING", "Waiting for cast power bar.");
            }

            return;
        }

        _castBarSeen = true;
        if (powerPercent >= ResolveCastThreshold())
        {
            ReleaseMouse(true);
            _castReleasedAt = Environment.TickCount64;
            _phase = "CASTED";
            SetStatus("CASTED", "Cast released.");
            return;
        }

        if (Environment.TickCount64 - _castStartedAt >= Math.Max(5000, _settings.CastTimeoutMs))
        {
            if (_settings.CastOnTimeout)
            {
                StartCycle();
            }
            else
            {
                StopCycle("OFF", "Cast timed out.");
            }
        }
        else
        {
            SetStatus("CASTING", _castBarSeen ? "Charging cast." : "Waiting for cast bar.");
        }
    }

    private void UpdateCastedPhase()
    {
        _progressPercent = null;
        ReleaseMouse(true);
        _castReleasedAt = _castReleasedAt == 0 ? Environment.TickCount64 : _castReleasedAt;
        if (Environment.TickCount64 - _castReleasedAt < _settings.PostCastDelayMs)
        {
            SetStatus("CASTED", "Waiting after cast.");
            return;
        }

        _lastShakedAt = 0;
        _phase = "SHAKE";
        SetStatus("SHAKE", "Shaking until reel starts.");
    }

    private void UpdateShakePhase()
    {
        _progressPercent = null;
        ReleaseMouse(true);

        var context = _context.GetReelContext();
        var metrics = context is null ? null : _context.ReadMetrics(context);
        if (metrics is not null)
        {
            EnterFishingPhase("Reel detected; releasing cast input.");
            return;
        }

        if (_lastShakedAt == 0 || Environment.TickCount64 - _lastShakedAt >= _settings.ShakeIntervalMs)
        {
            SendEnter();
            _lastShakedAt = Environment.TickCount64;
        }

        if (_castReleasedAt != 0 && Environment.TickCount64 - _castReleasedAt >= _settings.CastTimeoutMs)
        {
            StartCycle();
        }
        else
        {
            SetStatus("SHAKE", "Sending shake input.");
        }
    }

    private void UpdateFishingPhase()
    {
        var context = _context.GetReelContext();
        var metrics = context is null ? null : _context.ReadMetrics(context);
        _progressPercent = _context.GetFishingCompletionPercent();
        if (CastingMode == FishingCastingMode.Normal && metrics is null)
        {
            ReleaseMouse(true);
            _controller.Reset();
            _fishingLostAt = 0;
            _phase = "CASTING";
            _normalCastingController.Reset();
            var castState = _normalCastingController.UpdateCasting();
            _isHolding = castState.Holding;
            SetStatus("CASTING", "Waiting for active reel minigame.");
            return;
        }

        if (_progressPercent >= _settings.CompletionThreshold)
        {
            _completionReached = true;
        }

        if (_completionReached)
        {
            // Latch BEFORE StartCycle so it survives the cycle reset — the gate
            // must persist across the post-catch settle window even though
            // StartCycle clears _completionReached. EnterFishingPhase clears the
            // latch when the next minigame begins.
            if (_completionLatchedAt == 0)
            {
                _completionLatchedAt = Environment.TickCount64;
            }

            RecordReelOutcome(true);
            ReleaseMouse(true);
            _controller.Reset();
            _fishingInputReadyAt = 0;
            // Prevent a one-tick cast/recast blip between catch completion and
            // Auto Totem workflow handoff.
            if (IsAutoTotemRuntimeEnabled())
            {
                if (!_totemPending && IsAutoTotemDue() && _phase != "OFF")
                {
                    _totemNeedsSettleDelay = true;
                    _totemPending = true;
                }

                if (_totemPending)
                {
                    _phase = "CASTING";
                    _normalCastingController.Reset();
                    _castStartedAt = 0;
                    _castReleasedAt = 0;
                    _castBarSeen = false;
                    SetStatus("ADDON", "Auto Totem queued. Waiting for boundary.");
                    return;
                }
            }

            StartCycle();
            SetStatus("CASTING", "Fish completed; casting again.");
            return;
        }

        if (metrics is not null)
        {
            if (_fishingInputReadyAt != 0 && Environment.TickCount64 < _fishingInputReadyAt)
            {
                ReleaseMouse(true);
                SetStatus("FISHING", "Reel detected; releasing cast input.");
                return;
            }

            _fishingInputReadyAt = 0;
            _fishingLostAt = 0;
            var decision = _controller.Update(metrics, _settings);
            _isHolding = decision.Holding;
            SetStatus("FISHING", $"Error {decision.Error:0.000}, duty {decision.Control:0.000}.");
            return;
        }

        ReleaseMouse(true);
        _controller.Reset();
        if (CastingMode == FishingCastingMode.Normal || _progressPercent is null)
        {
            _fishingInputReadyAt = 0;
            _fishingLostAt = 0;
            _phase = "CASTING";
            _normalCastingController.Reset();
            var castState = _normalCastingController.UpdateCasting();
            _isHolding = castState.Holding;
            SetStatus("CASTING", "Waiting for active reel minigame.");
            return;
        }
        _fishingInputReadyAt = 0;
        _fishingLostAt = _fishingLostAt == 0 ? Environment.TickCount64 : _fishingLostAt;
        if (Environment.TickCount64 - _fishingLostAt >= 100)
        {
            StopCycle("DONE", _completionReached ? "Fish completed." : "Fishing ended.");
        }
        else
        {
            SetStatus(_completionReached ? "CASTING" : "FISHING", _completionReached ? "Fish completed; preparing next cast." : "Reel context lost; waiting for grace period.");
        }
    }

    private double ResolveCastThreshold()
    {
        if (CastingMode == FishingCastingMode.Perfect)
        {
            return 96.0;
        }

        return _settings.CastMode switch
        {
            "short" => 28.0,
            "custom" => Math.Clamp(_settings.CastPowerCustom, 1.0, 100.0),
            _ => 96.0,
        };
    }

    private void EnterFishingPhase(string message)
    {
        ReleaseMouse(true);
        _normalCastingController.Reset();
        _controller.Reset();
        _lastShakedAt = 0;
        _fishingLostAt = 0;
        _fishingInputReadyAt = Environment.TickCount64 + ReelInputSettleMs;
        _phase = "FISHING";
        _completionLatchedAt = 0;
        SetStatus("FISHING", message);
    }

    private void HoldMouse()
    {
        if (_isHolding)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (_phase == "FISHING" && _settings.FishingActionDelayMs > 0 && _lastActionAt != 0 &&
            now - _lastActionAt < _settings.FishingActionDelayMs)
        {
            return;
        }

        NativeMouse.LeftDown();
        _isHolding = true;
        _lastActionAt = now;
    }

    private void ReleaseMouse(bool force = false)
    {
        if (!_isHolding)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (!force && _phase == "FISHING" && _settings.FishingActionDelayMs > 0 && _lastActionAt != 0 &&
            now - _lastActionAt < _settings.FishingActionDelayMs)
        {
            return;
        }

        NativeMouse.LeftUp();
        _isHolding = false;
        _lastActionAt = now;
    }

    private void SetStatus(string phase, string message)
    {
        if (_totemPending && string.Equals(phase, "CASTING", StringComparison.OrdinalIgnoreCase))
        {
            phase = "ADDON";
            message = "Auto Totem queued. Waiting for boundary.";
        }

        Status = new FishingTrackerStatus(_isRunning, phase, message, _progressPercent, _isHolding, _reelStats, IsAutomationBoundaryOpen());
    }

    // Shared post-catch settle gate. Opens only after a fish has actually been
    // caught (_completionLatchedAt set), the post-catch input-lockout window
    // has elapsed (PostCatchSettleMs), and the macro is not in the middle of
    // charging a cast.
    private bool IsAutomationBoundaryOpen()
    {
        if (_completionLatchedAt == 0) return false;
        if (Environment.TickCount64 - _completionLatchedAt < PostCatchSettleMs) return false;
        return string.Equals(_phase, "CASTING", StringComparison.OrdinalIgnoreCase) && !_isHolding && !_castBarSeen;
    }

    private void RecordReelOutcome(bool completionReached)
    {
        var state = Tracking1FishingTracker.ResolveReelOutcome(_outcomeResolved, _reelStats, completionReached);
        _outcomeResolved = state.OutcomeResolved;
        _reelStats = state.Stats;
    }

    private static void SendEnter()
    {
        NativeKeyboard.PressEnter();
    }


    private bool UpdateAutoTotem()
    {
        if (!IsAutoTotemRuntimeEnabled())
        {
            if (_totemState != "IDLE" || _totemPending || _totemBlockedUntilCatchEnd)
            {
                ReleaseMouse(true);
                _controller.Reset();
                if (_totemState != "IDLE" && _totemNeedsRodReequip)
                {
                    EnsureRodEquipped();
                }

                ResetAutoTotemControl();
            }

            return false;
        }

        if (_totemState != "IDLE")
        {
            _progressPercent = null;
            ReleaseMouse(true);
            _controller.Reset();
            UpdateAutoTotemState();
            SetStatus("TOTEM", $"Auto Totem: {_totemState}");
            return true;
        }

        if (_totemPending && IsAutoTotemBoundary())
        {
            BeginAutoTotemWorkflow();
            SetStatus("TOTEM", "Auto Totem: begin pending workflow.");
            return true;
        }

        // Once a catch has latched and totem is pending, keep casting paused
        // until boundary opens and workflow starts. This prevents brief recasts
        // in the post-catch settle window.
        if (_totemPending && _completionLatchedAt != 0)
        {
            _progressPercent = null;
            ReleaseMouse(true);
            _controller.Reset();
            SetStatus("ADDON", "Auto Totem queued. Waiting for boundary.");
            return true;
        }

        if (_totemBlockedUntilCatchEnd)
        {
            return false;
        }

        if (IsAutoTotemDue())
        {
            if (IsAutoTotemBoundary())
            {
                BeginAutoTotemWorkflow();
                SetStatus("TOTEM", "Auto Totem: begin workflow.");
                return true;
            }

            if (!_totemPending && _phase != "OFF")
            {
                _totemNeedsSettleDelay = true;
            }

            _totemPending = true;
            _progressPercent = null;
            ReleaseMouse(true);
            _controller.Reset();
            SetStatus("ADDON", "Auto Totem queued. Waiting for boundary.");
            return true;
        }

        return false;
    }

    private bool IsAutoTotemRuntimeEnabled()
    {
        return AutoTotemSettings.Enabled &&
            string.Equals(AutoTotemSettings.TotemName, "Aurora Totem", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsAutoTotemBoundary()
    {
        return IsAutomationBoundaryOpen();
    }

    private bool IsAutoTotemDue()
    {
        if (_totemNightCovered)
        {
            var cycleText = GetWorldStatusText("4_cycle");
            if (string.IsNullOrWhiteSpace(cycleText) ||
                cycleText.Contains("night", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _totemNightCovered = false;
        }

        return true;
    }

    private void BeginAutoTotemWorkflow()
    {
        _progressPercent = null;
        _totemPending = false;
        _totemRetryCount = 0;
        _totemWaitStartedAt = 0;
        _lastTotemAttemptAt = Environment.TickCount64;
        _totemNeedsRodReequip = false;

        ReleaseMouse(true);
        _controller.Reset();
        if (_totemNeedsSettleDelay)
        {
            _totemState = "TOTEM_SETTLE";
            _totemWaitStartedAt = Environment.TickCount64;
            return;
        }

        RunAutoTotemWorkflowStep();
    }

    private void RunAutoTotemWorkflowStep()
    {
        if (IsAuroraActive())
        {
            CompleteAutoTotemWorkflow(true);
            return;
        }

        if (IsNightCycle())
        {
            if (!TryUseAutoTotemItem("Aurora Totem"))
            {
                CompleteAutoTotemWorkflow(false);
                return;
            }

            _totemState = "TOTEM_WAIT_AURORA";
            _totemWaitStartedAt = Environment.TickCount64;
            return;
        }

        if (!TryUseAutoTotemItem("Sundial Totem"))
        {
            CompleteAutoTotemWorkflow(false);
            return;
        }

        _totemState = "TOTEM_WAIT_NIGHT";
        _totemWaitStartedAt = Environment.TickCount64;
    }

    private void UpdateAutoTotemState()
    {
        if (IsAuroraActive())
        {
            CompleteAutoTotemWorkflow(true);
            return;
        }

        switch (_totemState)
        {
            case "TOTEM_SETTLE":
                if (Environment.TickCount64 - _totemWaitStartedAt < _settings.PreCastDelayMs)
                {
                    return;
                }

                _totemNeedsSettleDelay = false;
                _totemWaitStartedAt = 0;
                RunAutoTotemWorkflowStep();
                return;
            case "TOTEM_WAIT_NIGHT":
                var sundialSettleMs = Math.Max(12000, AutoTotemSettings.TimeChangeWaitMs);
                if (Environment.TickCount64 - _totemWaitStartedAt < sundialSettleMs)
                {
                    return;
                }

                if (IsNightCycle())
                {
                    _totemRetryCount = 0;
                    if (!TryUseAutoTotemItem("Aurora Totem"))
                    {
                        CompleteAutoTotemWorkflow(false);
                        return;
                    }

                    _totemState = "TOTEM_WAIT_AURORA";
                    _totemWaitStartedAt = Environment.TickCount64;
                    return;
                }

                if (_totemRetryCount >= 1)
                {
                    CompleteAutoTotemWorkflow(false);
                    return;
                }

                if (!TryUseAutoTotemItem("Sundial Totem"))
                {
                    CompleteAutoTotemWorkflow(false);
                    return;
                }

                _totemRetryCount += 1;
                _totemWaitStartedAt = Environment.TickCount64;
                return;
            case "TOTEM_WAIT_AURORA":
                if (Environment.TickCount64 - _totemWaitStartedAt < 3000)
                {
                    return;
                }

                if (_totemRetryCount >= 1)
                {
                    CompleteAutoTotemWorkflow(false);
                    return;
                }

                if (!TryUseAutoTotemItem("Aurora Totem"))
                {
                    CompleteAutoTotemWorkflow(false);
                    return;
                }

                _totemRetryCount += 1;
                _totemWaitStartedAt = Environment.TickCount64;
                return;
        }
    }

    private bool TryUseAutoTotemItem(string itemName)
    {
        if (!TryUseHotbarItem(itemName))
        {
            return false;
        }

        _totemNeedsRodReequip = true;
        return true;
    }

    private void CompleteAutoTotemWorkflow(bool success)
    {
        var needsRodReequip = _totemNeedsRodReequip;
        if (success)
        {
            _lastTotemSuccessAt = Environment.TickCount64;
            _totemNightCovered = true;
        }

        ResetAutoTotemControl();
        if (needsRodReequip)
        {
            EnsureRodEquipped();
        }

        if (!success)
        {
            _totemBlockedUntilCatchEnd = true;
        }
    }

    private void ResetAutoTotemControl()
    {
        _totemState = "IDLE";
        _totemRetryCount = 0;
        _totemWaitStartedAt = 0;
        _totemPending = false;
        _totemBlockedUntilCatchEnd = false;
        _totemNeedsRodReequip = false;
        _totemNeedsSettleDelay = false;
    }

    private bool TryUseHotbarItem(string itemName)
    {
        var slotKey = GetHotbarItemSlotKey(itemName);
        if (slotKey <= 0)
        {
            return false;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!SelectHotbarSlot(slotKey))
            {
                return false;
            }

            Thread.Sleep(175);
            NativeMouse.LeftDown();
            Thread.Sleep(15);
            NativeMouse.LeftUp();
            Thread.Sleep(100);
            if (string.Equals(GetEquippedToolName(), itemName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            Thread.Sleep(125);
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

    private int GetHotbarItemSlotKey(string itemName)
    {
        var hotbar = GetHotbarGui();
        if (hotbar == 0)
        {
            return 0;
        }

        foreach (var slot in _memory.ReadChildren(hotbar))
        {
            if (!string.Equals(_memory.ReadClass(slot), "ImageButton", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_memory.ReadName(slot), "ItemTemplate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var nameLabel = _memory.FindChildByName(slot, "ItemName");
            var text = nameLabel == 0 ? string.Empty : Normalize(_memory.ReadGuiText(nameLabel));
            if (!string.Equals(text, itemName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(slot))
            {
                if (!string.Equals(_memory.ReadClass(child), "TextLabel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var raw = Normalize(_memory.ReadGuiText(child));
                if (int.TryParse(raw, out var parsed) && parsed is >= 1 and <= 9)
                {
                    return parsed;
                }
            }
        }

        return 0;
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

    private bool IsNightCycle()
    {
        return GetWorldStatusTexts("4_cycle", visibleOnly: true)
            .Any(text => text.Contains("night", StringComparison.OrdinalIgnoreCase));
    }

    private bool IsAuroraActive()
    {
        return IsWorldStatusMatchVisible("2_event", "aurora") || IsWorldStatusMatchVisible("3_weather", "aurora");
    }

    private bool IsWorldStatusMatchVisible(string statusName, string needle)
    {
        foreach (var status in ResolveWorldStatuses(statusName))
        {
            if (!_memory.IsVisible(status, "FrameVisible"))
            {
                continue;
            }

            if (ReadWorldStatusText(status).Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool GetWorldStatusVisible(string statusName)
    {
        foreach (var status in ResolveWorldStatuses(statusName))
        {
            if (_memory.IsVisible(status, "FrameVisible"))
            {
                return true;
            }
        }

        return false;
    }

    private string GetWorldStatusText(string statusName)
    {
        foreach (var text in GetWorldStatusTexts(statusName, visibleOnly: true))
        {
            return text;
        }

        foreach (var text in GetWorldStatusTexts(statusName, visibleOnly: false))
        {
            return text;
        }

        return string.Empty;
    }

    private IEnumerable<string> GetWorldStatusTexts(string statusName, bool visibleOnly)
    {
        foreach (var status in ResolveWorldStatuses(statusName))
        {
            if (visibleOnly && !_memory.IsVisible(status, "FrameVisible"))
            {
                continue;
            }

            var text = ReadWorldStatusText(status);
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return text;
            }
        }
    }

    private string ReadWorldStatusText(ulong status)
    {
        if (status == 0)
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

    private List<ulong> ResolveWorldStatuses(string statusName)
    {
        var worldStatuses = ResolveWorldStatusesContainer();
        if (worldStatuses == 0)
        {
            return [];
        }

        var normalizedTarget = Normalize(statusName);
        if (string.IsNullOrWhiteSpace(normalizedTarget))
        {
            return [];
        }

        var suffixIndex = normalizedTarget.IndexOf('_');
        var slotSuffix = suffixIndex >= 0 ? normalizedTarget[suffixIndex..] : string.Empty;
        var exact = new List<ulong>();
        var suffix = new List<ulong>();

        foreach (var child in _memory.ReadChildren(worldStatuses))
        {
            var childName = Normalize(_memory.ReadName(child));
            if (string.IsNullOrWhiteSpace(childName))
            {
                continue;
            }

            if (string.Equals(childName, normalizedTarget, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(child);
                continue;
            }

            if (!string.IsNullOrEmpty(slotSuffix) &&
                childName.EndsWith(slotSuffix, StringComparison.OrdinalIgnoreCase))
            {
                suffix.Add(child);
            }
        }

        if (exact.Count > 0)
        {
            exact.AddRange(suffix);
            return exact;
        }

        return suffix;
    }

    private ulong ResolveWorldStatusesContainer()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var hud = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "hud");
        var safezone = hud == 0 ? 0 : _memory.FindChildByName(hud, "safezone");
        return safezone == 0 ? 0 : _memory.FindChildByName(safezone, "worldstatuses");
    }

    private static string Normalize(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }
}
