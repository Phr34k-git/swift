using System;
using System.Collections.Generic;
using Client.Services;

namespace Client.Services.Fishing;

internal sealed class FishingRuntimeContext
{
    // Re-verify the cached reel ScreenGui against PlayerGui.reel at most this
    // often. Defends against the Roblox slab-allocator reusing a freed
    // Instance's memory for a new same-named Instance, which would otherwise
    // keep IsCached returning true forever against a detached pointer.
    private const long ReelGuiReverifyIntervalMs = 500;
    private const long PowerBarStickyMs = 160;

    private readonly RobloxMemory _memory;
    private ulong _reelGui;
    private ulong _bar;
    private ulong _fish;
    private ulong _playerbar;
    private ulong _progressBar;
    private ulong _powerBar;
    private ulong _shakeGui;
    private ulong _shakeSafezone;
    private ulong _shakeButton;
    private long _reelGuiVerifiedAt;
    private double? _lastPowerPercent;
    private long _lastPowerSeenAt;

    public FishingRuntimeContext(RobloxMemory memory)
    {
        _memory = memory;
    }

    public void ResetCache()
    {
        _reelGui = 0;
        _bar = 0;
        _fish = 0;
        _playerbar = 0;
        _progressBar = 0;
        _powerBar = 0;
        _shakeGui = 0;
        _shakeSafezone = 0;
        _shakeButton = 0;
        _reelGuiVerifiedAt = 0;
        _lastPowerPercent = null;
        _lastPowerSeenAt = 0;
    }

    public ReelContext? GetReelContext()
    {
        var reelGui = GetReelGui();
        if (reelGui == 0)
        {
            ResetReelCache();
            return null;
        }

        if (!IsReelGuiVisible(reelGui))
        {
            // Previously did not reset the cache here — that caused the cached
            // _reelGui (and its child pointers) to persist after the live reel
            // GUI was destroyed/replaced, leaving trackers running the PID
            // against frozen metrics ("stuck balancing" until Stop/Start).
            ResetReelCache();
            return null;
        }

        if (IsCached(_bar, "bar") && IsCached(_fish, "fish") && IsCached(_playerbar, "playerbar") &&
            _memory.IsVisible(_bar, "FrameVisible") &&
            _memory.IsVisible(_fish, "FrameVisible") &&
            _memory.IsVisible(_playerbar, "FrameVisible"))
        {
            return new ReelContext(_bar, _fish, _playerbar);
        }

        _bar = _memory.FindChildByName(reelGui, "bar");
        if (_bar == 0)
        {
            ResetReelCache();
            return null;
        }

        _fish = _memory.FindChildByName(_bar, "fish");
        _playerbar = _memory.FindChildByName(_bar, "playerbar");
        if (_fish == 0)
        {
            _fish = _memory.FindDescendantByName(_bar, "fish");
        }

        if (_playerbar == 0)
        {
            _playerbar = _memory.FindDescendantByName(_bar, "playerbar");
        }

        if (_fish == 0 || _playerbar == 0 ||
            !_memory.IsVisible(_bar, "FrameVisible") ||
            !_memory.IsVisible(_fish, "FrameVisible") ||
            !_memory.IsVisible(_playerbar, "FrameVisible"))
        {
            ResetReelCache();
            return null;
        }

        return new ReelContext(_bar, _fish, _playerbar);
    }

    public bool IsReelGuiVisible(ulong reelGui = 0)
    {
        reelGui = reelGui == 0 ? GetReelGui() : reelGui;
        return reelGui != 0 && _memory.IsVisible(reelGui, "ScreenGuiEnabled");
    }

    public ulong GetPrimaryReelGuiAddress()
    {
        return GetReelGui();
    }

    public double? GetFishingCompletionPercent()
    {
        var reelGui = GetReelGui();
        if (reelGui == 0)
        {
            _progressBar = 0;
            return null;
        }

        if (!IsCached(_progressBar, "bar"))
        {
            var bar = IsCached(_bar, "bar") ? _bar : _memory.FindChildByName(reelGui, "bar");
            if (bar == 0)
            {
                return null;
            }

            var progressFrame = _memory.FindChildByName(bar, "progress");
            _progressBar = progressFrame == 0 ? 0 : _memory.FindChildByName(progressFrame, "bar");
        }

        if (_progressBar == 0)
        {
            return null;
        }

        return Clamp(_memory.ReadFrameSize(_progressBar).XScale * 100.0, 0, 100);
    }

    public double? GetFishingCompletionPercent(ulong reelGui)
    {
        if (reelGui == 0)
        {
            return null;
        }

        var bar = _memory.FindChildByName(reelGui, "bar");
        if (bar == 0)
        {
            return null;
        }

        var progressFrame = _memory.FindChildByName(bar, "progress");
        var progressBar = progressFrame == 0 ? 0 : _memory.FindChildByName(progressFrame, "bar");
        if (progressBar == 0)
        {
            return null;
        }

        return Clamp(_memory.ReadFrameSize(progressBar).XScale * 100.0, 0, 100);
    }

    public double? GetFishingCompletionPercentFromBar(ulong bar)
    {
        if (bar == 0)
        {
            return null;
        }

        var progressFrame = _memory.FindChildByName(bar, "progress");
        var progressBar = progressFrame == 0 ? 0 : _memory.FindChildByName(progressFrame, "bar");
        if (progressBar == 0)
        {
            return null;
        }

        return Clamp(_memory.ReadFrameSize(progressBar).XScale * 100.0, 0, 100);
    }

    public double? GetPowerBarPercent()
    {
        var now = Environment.TickCount64;
        if (!IsCached(_powerBar, "bar"))
        {
            _powerBar = ResolvePowerBar();
        }

        if (_powerBar == 0)
        {
            return _lastPowerPercent.HasValue && now - _lastPowerSeenAt <= PowerBarStickyMs
                ? _lastPowerPercent
                : null;
        }

        var scaleY = _memory.ReadFrameSize(_powerBar).YScale;
        if (double.IsNaN(scaleY) || double.IsInfinity(scaleY) || scaleY < -0.05 || scaleY > 1.5)
        {
            _powerBar = 0;
            return _lastPowerPercent.HasValue && now - _lastPowerSeenAt <= PowerBarStickyMs
                ? _lastPowerPercent
                : null;
        }

        var percent = Clamp(scaleY * 100.0, 0, 100);
        _lastPowerPercent = percent;
        _lastPowerSeenAt = now;
        return percent;
    }

    public bool IsShakeButtonVisible()
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            ResetShakeCache();
            return false;
        }

        if (!IsCached(_shakeGui, "shakeui"))
        {
            _shakeGui = _memory.FindChildByName(playerGui, "shakeui");
            if (_shakeGui == 0)
            {
                _shakeGui = _memory.FindDescendantByName(playerGui, "shakeui");
            }
        }

        if (_shakeGui == 0 || !_memory.IsVisible(_shakeGui, "ScreenGuiEnabled"))
        {
            ResetShakeCache();
            return false;
        }

        if (!IsCached(_shakeSafezone, "safezone"))
        {
            _shakeSafezone = _memory.FindChildByName(_shakeGui, "safezone");
            if (_shakeSafezone == 0)
            {
                _shakeSafezone = _memory.FindDescendantByName(_shakeGui, "safezone");
            }
        }

        if (_shakeSafezone == 0)
        {
            _shakeButton = 0;
            return false;
        }

        if (!IsCached(_shakeButton, "button"))
        {
            _shakeButton = _memory.FindChildByName(_shakeSafezone, "button");
            if (_shakeButton == 0)
            {
                _shakeButton = _memory.FindDescendantByName(_shakeSafezone, "button");
            }
        }

        return _shakeButton != 0 &&
            string.Equals(_memory.ReadClass(_shakeButton), "ImageButton", StringComparison.OrdinalIgnoreCase) &&
            _memory.IsVisible(_shakeButton, "FrameVisible");
    }

    public ReelMetrics? ReadMetrics(ReelContext context)
    {
        if (!IsReelGuiVisible() ||
            !_memory.IsVisible(context.Bar, "FrameVisible") ||
            !_memory.IsVisible(context.Fish, "FrameVisible") ||
            !_memory.IsVisible(context.Playerbar, "FrameVisible"))
        {
            ResetReelCache();
            return null;
        }

        var fishPosition = _memory.ReadFramePosition(context.Fish);
        var fishSize = _memory.ReadFrameSize(context.Fish);
        var playerbarPosition = _memory.ReadFramePosition(context.Playerbar);
        var playerbarSize = _memory.ReadFrameSize(context.Playerbar);
        var fishCenter = fishPosition.XScale + fishSize.XScale / 2.0;
        var playerbarWidth = Math.Max(0.001, playerbarSize.XScale);
        if (!IsReasonableScale(fishCenter) ||
            !IsReasonableScale(playerbarPosition.XScale) ||
            playerbarWidth > 1.5)
        {
            ResetReelCache();
            return null;
        }

        return new ReelMetrics(
            FishCenter: fishCenter,
            PlayerbarCenter: playerbarPosition.XScale,
            PlayerbarWidth: playerbarWidth);
    }

    // Bellona's Waraxe uses two concurrent reel UIs. This resolves a second
    // visible reel context (if present) separate from the primary cached one.
    public ReelContext? GetSecondaryReelContext(ulong primaryReelGui)
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            return null;
        }

        foreach (var child in _memory.ReadChildren(playerGui))
        {
            if (child == primaryReelGui)
            {
                continue;
            }

            if (!string.Equals(_memory.ReadName(child), "reel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_memory.IsVisible(child, "ScreenGuiEnabled"))
            {
                continue;
            }

            var ctx = BuildReelContextFromGui(child);
            if (ctx is not null)
            {
                return ctx;
            }
        }

        return null;
    }

    // For Bellona: choose the rightmost visible reel by on-screen X position.
    public ReelContext? GetRightmostReelContext()
    {
        var context = GetExtremeReelContext(rightmost: true);
        AppLog.Info("BellonaCtx", context is null
            ? "GetRightmostReelContext -> none"
            : $"GetRightmostReelContext -> bar=0x{context.Bar:X} fish=0x{context.Fish:X} playerbar=0x{context.Playerbar:X}");
        return context;
    }

    public ReelContext? GetLeftmostReelContext()
    {
        var context = GetExtremeReelContext(rightmost: false);
        AppLog.Info("BellonaCtx", context is null
            ? "GetLeftmostReelContext -> none"
            : $"GetLeftmostReelContext -> bar=0x{context.Bar:X} fish=0x{context.Fish:X} playerbar=0x{context.Playerbar:X}");
        return context;
    }

    private ReelContext? GetExtremeReelContext(bool rightmost)
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            return null;
        }

        ReelContext? best = null;
        var bestX = rightmost ? double.MinValue : double.MaxValue;
        foreach (var child in _memory.ReadChildren(playerGui))
        {
            if (!string.Equals(_memory.ReadName(child), "reel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_memory.IsVisible(child, "ScreenGuiEnabled"))
            {
                continue;
            }

            var ctx = BuildReelContextFromGui(child);
            if (ctx is null)
            {
                continue;
            }

            var bounds = _memory.ReadGuiBounds(ctx.Bar, false);
            if (bounds is null)
            {
                continue;
            }

            var x = bounds.Value.X;
            if ((rightmost && x > bestX) || (!rightmost && x < bestX))
            {
                bestX = x;
                best = ctx;
            }
        }

        return best;
    }

    public IReadOnlyList<ReelLocatedContext> GetOrderedReelContexts()
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            return Array.Empty<ReelLocatedContext>();
        }

        var contexts = new List<ReelLocatedContext>();
        foreach (var child in _memory.ReadChildren(playerGui))
        {
            if (!string.Equals(_memory.ReadName(child), "reel", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!_memory.IsVisible(child, "ScreenGuiEnabled"))
            {
                continue;
            }

            var ctx = BuildReelContextFromGui(child);
            if (ctx is null)
            {
                continue;
            }

            var bounds = _memory.ReadGuiBounds(ctx.Bar, false);
            if (bounds is null)
            {
                continue;
            }

            contexts.Add(new ReelLocatedContext(ctx, bounds.Value.X));
        }

        contexts.Sort((a, b) => a.BarX.CompareTo(b.BarX));
        return contexts;
    }

    public IReadOnlyList<string> GetMasterlineOverlayRodNames()
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            return Array.Empty<string>();
        }

        var hud = _memory.FindChildByName(playerGui, "hud");
        if (hud == 0)
        {
            hud = _memory.FindDescendantByName(playerGui, "hud");
        }

        if (hud == 0)
        {
            return Array.Empty<string>();
        }

        // Prefer the exact HUD chain first:
        // hud -> Frame -> safezone -> statuses
        var hudFrame = _memory.FindChildByName(hud, "Frame");
        if (hudFrame == 0)
        {
            hudFrame = _memory.FindDescendantByName(hud, "Frame");
        }

        var safezone = hudFrame == 0 ? 0 : _memory.FindChildByName(hudFrame, "safezone");
        if (safezone == 0)
        {
            safezone = _memory.FindDescendantByName(hud, "safezone");
        }

        if (safezone == 0)
        {
            return Array.Empty<string>();
        }

        var statuses = _memory.FindChildByName(safezone, "statuses");
        if (statuses == 0)
        {
            statuses = _memory.FindDescendantByName(safezone, "statuses");
        }

        if (statuses == 0)
        {
            return Array.Empty<string>();
        }

        ulong masterlineStatus = 0;
        foreach (var child in _memory.ReadChildren(statuses))
        {
            var name = _memory.ReadName(child);
            if (name.StartsWith("Masterline", StringComparison.OrdinalIgnoreCase))
            {
                masterlineStatus = child;
                break;
            }
        }

        if (masterlineStatus == 0)
        {
            masterlineStatus = _memory.FindDescendantByName(statuses, "Masterline");
        }

        if (masterlineStatus == 0)
        {
            return Array.Empty<string>();
        }

        var tooltip = _memory.FindChildByName(masterlineStatus, "tooltip");
        if (tooltip == 0)
        {
            tooltip = _memory.FindDescendantByName(masterlineStatus, "tooltip");
        }

        if (tooltip == 0)
        {
            return Array.Empty<string>();
        }

        var text = _memory.ReadGuiText(tooltip).Trim();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(4);
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            line = line.TrimStart('•').Trim();
            if (line.Length > 0)
            {
                names.Add(line);
            }
        }

        return names;
    }

    // ── Pinion's Aria notes ──────────────────────────────────────────────────

    // Returns the lowest on-screen note (greatest Sy) in the reel's
    // noteContainer, or null. Port of AHK GetNoteContainer + GetActiveNoteTarget.
    public NoteTarget? GetActiveNoteTarget()
    {
        var bar = IsCached(_bar, "bar") ? _bar : 0;
        if (bar == 0)
        {
            // Pinion notes can be available before fish/playerbar pass the
            // stricter reel-context visibility checks. Resolve bar directly
            // from reel GUI so note detection can still drive transitions.
            var reelGui = GetReelGui();
            if (reelGui == 0 || !IsReelGuiVisible(reelGui))
            {
                return null;
            }

            bar = _memory.FindChildByName(reelGui, "bar");
            if (bar == 0 || !_memory.IsVisible(bar, "FrameVisible"))
            {
                return null;
            }

            _bar = bar;
        }

        var noteContainer = _memory.FindChildByName(bar, "noteContainer");
        if (noteContainer == 0)
        {
            return null;
        }

        NoteTarget? best = null;
        var bestY = -999999.0;
        foreach (var noteName in new[] { "note1", "note2" })
        {
            var noteAddr = _memory.FindChildByName(noteContainer, noteName);
            if (noteAddr == 0)
            {
                continue;
            }

            var pos = _memory.ReadFramePosition(noteAddr);
            double sx = pos.XScale;
            double sy = pos.YScale;
            if (sy > 0.55 || sy < -30)
            {
                continue;
            }

            if (sy > bestY)
            {
                bestY = sy;
                best = new NoteTarget(sx, sy);
            }
        }

        return best;
    }

    // ── Tranquility rhythm minigame ──────────────────────────────────────────

    public ulong GetTranquilityRoot()
    {
        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            return 0;
        }

        var gui = _memory.FindChildByName(playerGui, "TranquilityRodRhythmGame");
        return gui == 0 ? 0 : _memory.FindChildByName(gui, "RhythmGame");
    }

    public ulong GetTranquilityLaneContainer(ulong root)
    {
        return root == 0 ? 0 : _memory.FindChildByName(root, "LaneContainer");
    }

    public ulong GetTranquilityLane(ulong container, int index)
    {
        return container == 0 ? 0 : _memory.FindChildByName(container, $"Lane{index}");
    }

    public bool IsTranquilityActive()
    {
        var root = GetTranquilityRoot();
        return root != 0 && GetTranquilityLaneContainer(root) != 0;
    }

    public double? ReadTranquilityProgressPercent(ulong root)
    {
        if (root == 0)
        {
            return null;
        }

        var healthBar = _memory.FindChildByName(root, "HealthBar");
        var fill = healthBar == 0 ? 0 : _memory.FindChildByName(healthBar, "Fill");
        if (fill == 0)
        {
            return null;
        }

        return Clamp(_memory.ReadFrameSize(fill).XScale * 100.0, 0, 100);
    }

    public string GetTranquilityLaneKey(ulong root, ulong lane, int index)
    {
        string[] fallback = ["", "A", "S", "D", "F"];

        var label = root == 0 ? 0 : _memory.FindChildByName(root, $"KeyLabel{index}");
        if (label == 0 && lane != 0)
        {
            label = _memory.FindChildByName(lane, "KeyLabel");
        }

        if (label != 0)
        {
            var keyText = _memory.ReadGuiText(label).Trim();
            if (keyText.Length == 1)
            {
                return keyText.ToUpperInvariant();
            }
        }

        return index >= 1 && index <= 4 ? fallback[index] : string.Empty;
    }

    private ulong GetReelGui()
    {
        var now = Environment.TickCount64;
        var needsReverify = _reelGuiVerifiedAt == 0 ||
            now - _reelGuiVerifiedAt >= ReelGuiReverifyIntervalMs ||
            !IsCached(_reelGui, "reel");

        if (!needsReverify)
        {
            return _reelGui;
        }

        var playerGui = _memory.FindPlayerGui();
        var live = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "reel");

        if (live != _reelGui)
        {
            // The reel ScreenGui Instance was destroyed/recreated. Invalidate
            // all child caches so they are re-resolved from the live address.
            _bar = 0;
            _fish = 0;
            _playerbar = 0;
            _progressBar = 0;
        }

        _reelGui = live;
        _reelGuiVerifiedAt = now;
        return _reelGui;
    }

    private ulong ResolvePowerBar()
    {
        var workspace = _memory.FindWorkspace();
        var localPlayer = _memory.GetLocalPlayer();
        if (workspace == 0 || localPlayer == 0)
        {
            return 0;
        }

        var playerName = _memory.ReadName(localPlayer);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return 0;
        }

        var character = _memory.FindChildByName(workspace, playerName);
        var rootPart = character == 0 ? 0 : _memory.FindChildByName(character, "HumanoidRootPart");
        var powerGui = rootPart == 0 ? 0 : _memory.FindChildByName(rootPart, "power");
        var bar = powerGui == 0 ? 0 : _memory.FindDescendantFrameByName(powerGui, "bar");
        if (bar != 0)
        {
            return bar;
        }

        powerGui = rootPart == 0 ? 0 : _memory.FindDescendantByName(rootPart, "power");
        bar = powerGui == 0 ? 0 : _memory.FindDescendantFrameByName(powerGui, "bar");
        if (bar != 0)
        {
            return bar;
        }

        powerGui = character == 0 ? 0 : _memory.FindDescendantByName(character, "power");
        return powerGui == 0 ? 0 : _memory.FindDescendantFrameByName(powerGui, "bar");
    }

    private ReelContext? BuildReelContextFromGui(ulong reelGui)
    {
        var bar = _memory.FindChildByName(reelGui, "bar");
        if (bar == 0 || !_memory.IsVisible(bar, "FrameVisible"))
        {
            return null;
        }

        var fish = _memory.FindChildByName(bar, "fish");
        if (fish == 0)
        {
            fish = _memory.FindDescendantByName(bar, "fish");
        }

        var playerbar = _memory.FindChildByName(bar, "playerbar");
        if (playerbar == 0)
        {
            playerbar = _memory.FindDescendantByName(bar, "playerbar");
        }

        if (fish == 0 || playerbar == 0)
        {
            return null;
        }

        if (!_memory.IsVisible(fish, "FrameVisible") || !_memory.IsVisible(playerbar, "FrameVisible"))
        {
            return null;
        }

        return new ReelContext(bar, fish, playerbar);
    }

    private bool IsCached(ulong address, string expectedName)
    {
        if (address == 0)
        {
            return false;
        }

        try
        {
            return string.Equals(_memory.ReadName(address), expectedName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void ResetReelCache()
    {
        _reelGui = 0;
        _bar = 0;
        _fish = 0;
        _playerbar = 0;
        _progressBar = 0;
    }

    private void ResetShakeCache()
    {
        _shakeGui = 0;
        _shakeSafezone = 0;
        _shakeButton = 0;
    }

    private static double Clamp(double value, double min, double max)
    {
        return value < min ? min : value > max ? max : value;
    }

    private static bool IsReasonableScale(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value >= -0.05 && value <= 1.05;
    }
}

internal sealed record ReelContext(ulong Bar, ulong Fish, ulong Playerbar);

internal sealed record ReelMetrics(double FishCenter, double PlayerbarCenter, double PlayerbarWidth);

internal readonly record struct ReelLocatedContext(ReelContext Context, double BarX);
