using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class TreasureAppraiseViewModel : ViewModelBase
{
    private readonly TreasureAppraiser _runner = new(OffsetsSourceProvider.Current);
    private readonly Timer _timer;
    private bool _autoTreasureEnabled;
    private bool _isRunning;
    private string _clickDelaySecondsText = "0.25";
    private string _targetWeightText = string.Empty;
    private string _statusText = "---";
    private int _rollingDotStep;
    private int _tickWorkInProgress;

    public TreasureAppraiseViewModel()
    {
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }

    public bool AutoTreasureEnabled
    {
        get => _autoTreasureEnabled;
        set
        {
            if (!SetProperty(ref _autoTreasureEnabled, value))
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
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public string ClickDelaySecondsText
    {
        get => _clickDelaySecondsText;
        set => SetProperty(ref _clickDelaySecondsText, value);
    }

    public string TargetWeightText
    {
        get => _targetWeightText;
        set => SetProperty(ref _targetWeightText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Task ToggleAsync()
    {
        return IsRunning ? StopAsync() : StartAsync();
    }

    public Task StartAsync()
    {
        AutoTreasureEnabled = true;
        _runner.Reset(ReadSettings());
        _rollingDotStep = 0;
        IsRunning = true;
        StatusText = "Rolling";
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _runner.Reset(ReadSettings());
        StatusText = "---";
        return Task.CompletedTask;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _tickWorkInProgress, 1) != 0)
        {
            return;
        }

        if (!IsRunning)
        {
            Interlocked.Exchange(ref _tickWorkInProgress, 0);
            return;
        }

        try
        {
            _runner.Settings = ReadSettings();
            var result = _runner.RunStep();
            if (result.Completed)
            {
                IsRunning = false;
                _runner.Reset(ReadSettings());
                StatusText = string.IsNullOrWhiteSpace(result.FinalValue)
                    ? "Complete"
                    : $"Complete: {result.FinalValue}";
                return;
            }

            StatusText = BuildRollingText("Rolling");
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsRunning = false;
                _runner.Reset(ReadSettings());
                StatusText = ex.Message;
            });
        }
        finally
        {
            Interlocked.Exchange(ref _tickWorkInProgress, 0);
        }
    }

    private TreasureAppraiseSettings ReadSettings()
    {
        return new TreasureAppraiseSettings(ParseDelay(ClickDelaySecondsText, 0.25), ParseOptionalDouble(TargetWeightText));
    }

    private static double ParseDelay(string text, double fallback)
    {
        if (!double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return fallback;
        }

        return Math.Clamp(value, 0.0, 5.0);
    }

    private static double? ParseOptionalDouble(string text)
    {
        var valueText = (text ?? string.Empty).Trim();
        if (valueText.Length == 0)
        {
            return null;
        }

        if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse(valueText, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
        {
            return null;
        }

        return value;
    }

    private string BuildRollingText(string core)
    {
        _rollingDotStep = (_rollingDotStep + 1) % 4;
        return core + new string('.', _rollingDotStep);
    }

}
