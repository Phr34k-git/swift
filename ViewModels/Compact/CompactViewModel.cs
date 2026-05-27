using System;
using System.Threading;
using Avalonia.Threading;
using Client.Services.Fishing;

namespace Client.ViewModels;

/// <summary>
/// Aggregator view model rendered while the macro is running. Pulls live state
/// from the existing tier-specific view models and surfaces it as a single
/// flat surface for the compact view to bind to. Refresh cadence (100 ms)
/// matches the underlying tracker status timer.
/// </summary>
public sealed class CompactViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan OffsetsVersionPollInterval = TimeSpan.FromSeconds(2);

    private readonly GeneralViewModel _general;
    private readonly FishingViewModel _fishing;
    private readonly AutoTotemViewModel _autoTotem;
    private readonly Timer _tickTimer;

    private DateTimeOffset _lastOffsetsPollAt = DateTimeOffset.MinValue;
    private string _offsetsVersion = "—";
    private bool _disposed;

    public CompactViewModel(GeneralViewModel general, FishingViewModel fishing, AutoTotemViewModel autoTotem)
    {
        _general = general ?? throw new ArgumentNullException(nameof(general));
        _fishing = fishing ?? throw new ArgumentNullException(nameof(fishing));
        _autoTotem = autoTotem ?? throw new ArgumentNullException(nameof(autoTotem));

        _tickTimer = new Timer(_ => Tick(), null, TickInterval, TickInterval);
    }

    // ─────────────── Tier 1 — always visible ───────────────

    public string ActiveMacroText => _general.ActiveMacroText;

    public string Phase => _fishing.Phase;

    public string StatusMessage => _general.StatusMessage;

    public string HotkeyText => _general.HotkeyText;

    public string RunTimeText
    {
        get
        {
            if (_general.MacroStartedAt is not { } startedAt)
            {
                return "00:00:00";
            }

            var elapsed = DateTimeOffset.UtcNow - startedAt;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return elapsed.ToString(elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
        }
    }

    public string CaughtText => ExtractStat("Caught");

    public string LostText => ExtractStat("Lost");

    public string SuccessRateText => ExtractStat("Success Rate");

    // ─────────────── Tier 2 — live minigame ───────────────

    public bool HasReelSnapshot => _fishing.CurrentReelSnapshot is not null;

    /// <summary>Fish position as percent across the playerbar container (0..100).</summary>
    public double FishPercent => Clamp01(GetReelSnapshot()?.FishCenter ?? 0) * 100.0;

    /// <summary>Playerbar center as percent across the container (0..100).</summary>
    public double PlayerbarPercent => Clamp01(GetReelSnapshot()?.PlayerbarCenter ?? 0) * 100.0;

    /// <summary>Playerbar width as percent of the container (0..100). Floor at 4% for visibility.</summary>
    public double PlayerbarWidthPercent
    {
        get
        {
            var w = GetReelSnapshot()?.PlayerbarWidth ?? 0;
            return Math.Max(4.0, Clamp01(w) * 100.0);
        }
    }

    public bool IsPerfectHolding => _fishing.HoldingText == "Holding";

    // Reel visualizer is laid out on a Canvas of width ReelCanvasWidth. The
    // VM converts the 0..1 normalized coordinates from the tracker into pixel
    // offsets so the XAML can use plain Canvas.Left / Width bindings without
    // value converters.
    public const double ReelCanvasWidth = 360.0;
    public const double FishMarkerSize = 12.0;
    public const double PlayerbarHeight = 14.0;

    public double PlayerbarLeftPx
    {
        get
        {
            var center = Clamp01(GetReelSnapshot()?.PlayerbarCenter ?? 0.5);
            var width = Math.Max(0.04, Clamp01(GetReelSnapshot()?.PlayerbarWidth ?? 0.10));
            var leftFrac = Math.Max(0, Math.Min(1 - width, center - width / 2));
            return leftFrac * ReelCanvasWidth;
        }
    }

    public double PlayerbarWidthPx
    {
        get
        {
            var width = Math.Max(0.04, Clamp01(GetReelSnapshot()?.PlayerbarWidth ?? 0.10));
            return width * ReelCanvasWidth;
        }
    }

    public double FishMarkerLeftPx
    {
        get
        {
            var center = Clamp01(GetReelSnapshot()?.FishCenter ?? 0.5);
            return center * ReelCanvasWidth - FishMarkerSize / 2.0;
        }
    }

    // ─────────────── Tier 3 — schedules ───────────────

    public bool AutoTotemEnabled => _autoTotem.AutoTotemEnabled;
    public string SelectedTotemName => _autoTotem.AutoTotemEnabled ? _autoTotem.SelectedTotem?.Name ?? "None" : "Off";
    public string CurrentWeather => string.IsNullOrWhiteSpace(_autoTotem.CurrentWeather) ? "—" : _autoTotem.CurrentWeather;
    public string CurrentCycle => string.IsNullOrWhiteSpace(_autoTotem.CurrentCycle) ? "Auto" : _autoTotem.CurrentCycle;
    public bool ShinySurgeActive => _autoTotem.ActiveShinySurge;
    public bool SparklingSurgeActive => _autoTotem.ActiveSparklingSurge;
    public bool MutationSurgeActive => _autoTotem.ActiveMutationSurge;
    public bool AnySurgeActive => ShinySurgeActive || SparklingSurgeActive || MutationSurgeActive;

    public bool AutoAquariumEnabled => _fishing.AutoAquariumEnabled;
    public string AquariumStatusText => _fishing.AquariumStatusText;

    // ─────────────── Tier 4 — health ───────────────

    public string OffsetsVersion
    {
        get => _offsetsVersion;
        private set => SetProperty(ref _offsetsVersion, value);
    }

    // ─────────────── Internals ───────────────

    private ReelSnapshotData? GetReelSnapshot() => _fishing.CurrentReelSnapshot;

    private string ExtractStat(string key)
    {
        foreach (var item in _fishing.StatsItems)
        {
            if (string.Equals(item.Entry, key, StringComparison.Ordinal))
            {
                return item.Value ?? "—";
            }
        }
        return "—";
    }

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    private void Tick()
    {
        if (_disposed)
        {
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Tick);
            return;
        }

        // Tier 4 health: refresh offsets version every ~2s. Cheap.
        var now = DateTimeOffset.UtcNow;
        if (now - _lastOffsetsPollAt >= OffsetsVersionPollInterval)
        {
            _lastOffsetsPollAt = now;
            OffsetsVersion = ResolveOffsetsVersion();
        }

        // Everything else: just raise notifications so the view re-reads.
        OnPropertyChanged(nameof(ActiveMacroText));
        OnPropertyChanged(nameof(Phase));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(HotkeyText));
        OnPropertyChanged(nameof(RunTimeText));
        OnPropertyChanged(nameof(CaughtText));
        OnPropertyChanged(nameof(LostText));
        OnPropertyChanged(nameof(SuccessRateText));
        OnPropertyChanged(nameof(HasReelSnapshot));
        OnPropertyChanged(nameof(FishPercent));
        OnPropertyChanged(nameof(PlayerbarPercent));
        OnPropertyChanged(nameof(PlayerbarWidthPercent));
        OnPropertyChanged(nameof(PlayerbarLeftPx));
        OnPropertyChanged(nameof(PlayerbarWidthPx));
        OnPropertyChanged(nameof(FishMarkerLeftPx));
        OnPropertyChanged(nameof(IsPerfectHolding));
        OnPropertyChanged(nameof(AutoTotemEnabled));
        OnPropertyChanged(nameof(SelectedTotemName));
        OnPropertyChanged(nameof(CurrentWeather));
        OnPropertyChanged(nameof(CurrentCycle));
        OnPropertyChanged(nameof(ShinySurgeActive));
        OnPropertyChanged(nameof(SparklingSurgeActive));
        OnPropertyChanged(nameof(MutationSurgeActive));
        OnPropertyChanged(nameof(AnySurgeActive));
        OnPropertyChanged(nameof(AutoAquariumEnabled));
        OnPropertyChanged(nameof(AquariumStatusText));
    }

    private static string ResolveOffsetsVersion()
    {
        try
        {
            var version = OffsetsSourceProvider.Current.Version;
            return string.IsNullOrWhiteSpace(version) ? "unknown" : version!;
        }
        catch
        {
            return "—";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tickTimer.Dispose();
    }
}
