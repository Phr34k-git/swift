using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class AutoAnglerViewModel : ViewModelBase
{
    private static readonly bool AnglerDiagnosticMode = string.Equals(
        Environment.GetEnvironmentVariable("OPENMACRO_ANGLER_DIAG"),
        "1",
        StringComparison.Ordinal);
    private static readonly bool AnglerSelfTestMode = string.Equals(
        Environment.GetEnvironmentVariable("OPENMACRO_ANGLER_SELFTEST"),
        "1",
        StringComparison.Ordinal);

    private static readonly bool HideCurrentFish = string.Equals(
        Environment.GetEnvironmentVariable("OPENMACRO_HIDE_ANGLER_FISH"),
        "1",
        StringComparison.Ordinal);

    private readonly AutoAnglerRunner _runner = new();
    private readonly Timer _timer;
    private bool _autoAnglerEnabled;
    private bool _isRunning;
    private string _statusText = "---";
    private string _currentFishText = "None";
    private string _clickXText = string.Empty;
    private string _clickYText = string.Empty;
    private bool _captureNextClick;
    private bool _lastLeftDown;
    private int _tickInProgress;
    private DateTimeOffset _nextSelfTestAt = DateTimeOffset.MinValue;
    private int _completingDotPhase;

    public AutoAnglerViewModel()
    {
        UseCursorPositionCommand = new RelayCommand(_ => UseCursorPositionAsync());
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
    }

    public bool AutoAnglerEnabled
    {
        get => _autoAnglerEnabled;
        set
        {
            if (!SetProperty(ref _autoAnglerEnabled, value))
            {
                return;
            }

            if (!value && IsRunning)
            {
                _ = StopAsync();
            }
            else if (!IsRunning)
            {
                StatusText = "---";
            }

            if (value)
            {
                TryRefreshFishPreview();
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string CurrentFishText
    {
        get => _currentFishText;
        private set => SetProperty(ref _currentFishText, value);
    }

    public string ClickXText
    {
        get => _clickXText;
        set => SetProperty(ref _clickXText, value);
    }

    public string ClickYText
    {
        get => _clickYText;
        set => SetProperty(ref _clickYText, value);
    }

    public RelayCommand UseCursorPositionCommand { get; }

    public Task ToggleAsync()
    {
        return IsRunning ? StopAsync() : StartAsync();
    }

    public Task StartAsync()
    {
        AutoAnglerEnabled = true;
        _runner.Reset();
        IsRunning = true;
        StatusText = "---";
        try
        {
            var initialFish = _runner.ReadCurrentQuestFish();
            var displayFish = GetDisplayFish(initialFish);
            CurrentFishText = displayFish;
        }
        catch
        {
            // Tick loop will retry.
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _runner.Reset();
        StatusText = "---";
        CurrentFishText = "None";
        return Task.CompletedTask;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _tickInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            Dispatcher.UIThread.Post(TryCaptureClickPoint);

            if (!IsRunning)
            {
                if (AutoAnglerEnabled)
                {
                    TryRefreshFishPreview();
                }

                if (AnglerSelfTestMode)
                {
                    TryRunSelfTestProbe();
                }

                return;
            }

            try
            {
                var diagnostic = AnglerDiagnosticMode
                    ? _runner.ReadCurrentQuestFishDiagnostic()
                    : new AutoAnglerQuestDiagnostic(_runner.ReadCurrentQuestFish(), string.Empty);
                Dispatcher.UIThread.Post(() =>
                {
                    var displayFish = GetDisplayFish(diagnostic.Fish);
                    CurrentFishText = displayFish;
                    if (AnglerDiagnosticMode && !string.IsNullOrWhiteSpace(diagnostic.Summary))
                    {
                        StatusText = diagnostic.Summary;
                    }
                });
            }
            catch
            {
                // Ignore transient memory read failures between steps.
            }

            var settings = BuildSettings();
            var result = _runner.Step(settings);
            Dispatcher.UIThread.Post(() =>
            {
                var displayFish = GetDisplayFish(result.CurrentFish);
                CurrentFishText = displayFish;
                StatusText = BuildStatusText(result.Status);

                if (result.Failed)
                {
                    IsRunning = false;
                    _runner.Reset();
                    StatusText = result.Status;
                }
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                _runner.Reset();
                StatusText = ex.Message;
            });
        }
        finally
        {
            Interlocked.Exchange(ref _tickInProgress, 0);
        }
    }

    private AutoAnglerSettings BuildSettings()
    {
        var clickX = int.TryParse((ClickXText ?? string.Empty).Trim(), out var x) ? x : 0;
        var clickY = int.TryParse((ClickYText ?? string.Empty).Trim(), out var y) ? y : 0;
        return new AutoAnglerSettings(clickX, clickY);
    }

    private Task UseCursorPositionAsync()
    {
        _captureNextClick = true;
        _lastLeftDown = NativeMouse.IsLeftButtonDown();
        StatusText = "Click once to set.";
        return Task.CompletedTask;
    }

    private void TryCaptureClickPoint()
    {
        if (!_captureNextClick)
        {
            return;
        }

        var isDown = NativeMouse.IsLeftButtonDown();
        if (isDown && !_lastLeftDown)
        {
            var (x, y) = NativeMouse.GetCursorPosition();
            ClickXText = x.ToString();
            ClickYText = y.ToString();
            _captureNextClick = false;
            StatusText = $"Click Location set: {x}, {y}.";
        }

        _lastLeftDown = isDown;
    }

    private static string GetDisplayFish(string fish)
    {
        if (string.IsNullOrWhiteSpace(fish) || string.Equals(fish, "None", StringComparison.OrdinalIgnoreCase))
        {
            return "None";
        }

        return HideCurrentFish ? "Detected" : fish;
    }

    private void TryRefreshFishPreview()
    {
        try
        {
            var fish = _runner.ReadCurrentQuestFish();
            var displayFish = GetDisplayFish(fish);
            CurrentFishText = displayFish;
        }
        catch
        {
            // Keep UI stable if memory read is transiently unavailable.
        }
    }

    private void TryRunSelfTestProbe()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextSelfTestAt)
        {
            return;
        }

        _nextSelfTestAt = now.AddSeconds(2);
        try
        {
            var diagnostic = _runner.ReadCurrentQuestFishDiagnostic();
            AppLog.Info("AutoAnglerSelfTest", $"{diagnostic.Summary} -> fish={diagnostic.Fish}");
            StatusText = "---";
            CurrentFishText = GetDisplayFish(diagnostic.Fish);
        }
        catch (Exception ex)
        {
            AppLog.Info("AutoAnglerSelfTest", $"probe-failed: {ex.Message}");
        }
    }

    private string BuildStatusText(string status)
    {
        if (status.StartsWith("COUNTDOWN:", StringComparison.OrdinalIgnoreCase))
        {
            var raw = status["COUNTDOWN:".Length..];
            if (int.TryParse(raw, out var seconds))
            {
                if (seconds < 0)
                {
                    seconds = 0;
                }

                var minutes = seconds / 60;
                var remainingSeconds = seconds % 60;
                return $"{minutes}m {remainingSeconds:00}s";
            }
        }

        if (string.Equals(status, "COMPLETING", StringComparison.OrdinalIgnoreCase))
        {
            _completingDotPhase = (_completingDotPhase % 3) + 1;
            return "Completing" + new string('.', _completingDotPhase);
        }

        return "---";
    }
}
