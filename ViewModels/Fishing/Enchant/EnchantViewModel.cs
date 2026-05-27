using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class EnchantViewModel : ViewModelBase
{
    private const double FixedCycleMs = 2800;
    private readonly EnchantDetector _detector = new();
    private readonly AutoEnchantRunner _runner = new();
    private readonly List<EnchantOptionViewModel> _allTargetEnchants;
    private readonly Timer _refreshTimer;
    private EnchantOptionViewModel _selectedTargetEnchant;
    private string _equippedRodText = "---";
    private string _currentEnchantText = "---";
    private string _statusText = "Waiting for rod.";
    private IReadOnlyList<ColoredTextLineViewModel> _equippedRodLines = ColoredEnchantText.BuildLines("---");
    private IReadOnlyList<ColoredTextLineViewModel> _currentEnchantLines = ColoredEnchantText.BuildLines("---");
    private IReadOnlyList<ColoredTextLineViewModel> _statusLines = ColoredEnchantText.BuildLines("Waiting for rod.");
    private string _targetSearchText = string.Empty;
    private string _newEnchantText = string.Empty;
    private EnchantRollMode _rollMode = EnchantRollMode.Gamepass;
    private bool _autoEnchantEnabled;
    private bool _isRunning;
    private int _rollingDotStep;
    private DateTimeOffset _statusLockUntil;
    private int _stepRunning;

    public EnchantViewModel()
    {
        _allTargetEnchants = EnchantCatalog.All
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Select(item => new EnchantOptionViewModel(item))
            .ToList();
        TargetEnchants = new ObservableCollection<EnchantOptionViewModel>(_allTargetEnchants);
        _selectedTargetEnchant = TargetEnchants[0];
        StartCommand = new RelayCommand(_ => StartAsync(), _ => !IsRunning);
        StopCommand = new RelayCommand(_ => StopAsync(), _ => IsRunning);
        AddEnchantCommand = new RelayCommand(_ => AddEnchantAsync());
        _refreshTimer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(40));
    }

    public ObservableCollection<EnchantOptionViewModel> TargetEnchants { get; }

    public EnchantOptionViewModel SelectedTargetEnchant
    {
        get => _selectedTargetEnchant;
        set
        {
            if (value is not null && SetProperty(ref _selectedTargetEnchant, value))
            {
                OnPropertyChanged(nameof(SelectedTargetEnchantName));
                OnPropertyChanged(nameof(SelectedTargetEnchantBrush));
                UpdateStatus();
            }
        }
    }

    public string SelectedTargetEnchantName => SelectedTargetEnchant.Name;

    public IBrush SelectedTargetEnchantBrush => EnchantColors.GetEnchantBrush(SelectedTargetEnchantName);

    public string EquippedRodText
    {
        get => _equippedRodText;
        private set => SetProperty(ref _equippedRodText, value);
    }

    public string CurrentEnchantText
    {
        get => _currentEnchantText;
        private set => SetProperty(ref _currentEnchantText, value);
    }

    public IBrush CurrentEnchantBrush => EnchantColors.GetEnchantBrush(CurrentEnchantText);

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                StatusLines = ColoredEnchantText.BuildLines(value);
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public IBrush StatusBrush => EnchantColors.GetEnchantBrush(EnchantColors.FindEnchantName(StatusText));

    public IBrush EquippedRodBrush => EnchantColors.GetEnchantBrush(EnchantColors.FindEnchantName(EquippedRodText));

    public IReadOnlyList<ColoredTextLineViewModel> EquippedRodLines
    {
        get => _equippedRodLines;
        private set => SetProperty(ref _equippedRodLines, value);
    }

    public IReadOnlyList<ColoredTextLineViewModel> CurrentEnchantLines
    {
        get => _currentEnchantLines;
        private set => SetProperty(ref _currentEnchantLines, value);
    }

    public IReadOnlyList<ColoredTextLineViewModel> StatusLines
    {
        get => _statusLines;
        private set => SetProperty(ref _statusLines, value);
    }

    public string TargetSearchText
    {
        get => _targetSearchText;
        set
        {
            if (SetProperty(ref _targetSearchText, value))
            {
                RefilterTargetEnchants();
            }
        }
    }

    public string NewEnchantText
    {
        get => _newEnchantText;
        set => SetProperty(ref _newEnchantText, value);
    }

    public bool AutoEnchantEnabled
    {
        get => _autoEnchantEnabled;
        set
        {
            if (!SetProperty(ref _autoEnchantEnabled, value))
            {
                return;
            }

            if (!value && IsRunning)
            {
                _ = StopAsync();
            }
            else if (!IsRunning)
            {
                StatusText = value ? "Auto enchant armed." : "Auto enchant off.";
            }
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(StartStopText));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StartStopText => IsRunning ? "Running" : "Stopped";

    public bool IsGamepassMode
    {
        get => _rollMode == EnchantRollMode.Gamepass;
        set
        {
            if (value)
            {
                SetRollMode(EnchantRollMode.Gamepass);
            }
        }
    }

    public bool IsNormalMode
    {
        get => _rollMode == EnchantRollMode.Normal;
        set
        {
            if (value)
            {
                SetRollMode(EnchantRollMode.Normal);
            }
        }
    }

    public RelayCommand StartCommand { get; }

    public RelayCommand StopCommand { get; }

    public RelayCommand AddEnchantCommand { get; }

    public Task ToggleAsync()
    {
        return IsRunning ? StopAsync() : StartAsync();
    }

    public Task StartAsync()
    {
        AutoEnchantEnabled = true;
        _runner.Reset();
        _rollingDotStep = 0;
        IsRunning = true;
        StatusText = "Rolling";
        _statusLockUntil = DateTimeOffset.UtcNow.AddMilliseconds(250);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _runner.Reset();
        StatusText = "Stopped.";
        _statusLockUntil = DateTimeOffset.UtcNow.AddMilliseconds(300);
        return Task.CompletedTask;
    }

    private void Tick()
    {
        if (IsRunning)
        {
            RunEnchantStep();
            return;
        }

        RefreshEnchantState();
    }

    private void RunEnchantStep()
    {
        if (Interlocked.Exchange(ref _stepRunning, 1) == 1)
        {
            return;
        }

        var cycleMs = FixedCycleMs;
        try
        {
            var result = _runner.Step(SelectedTargetEnchantName, _rollMode, cycleMs);
            SetEnchantState(
                string.IsNullOrWhiteSpace(result.Snapshot.RodText) ? "---" : result.Snapshot.RodText,
                string.IsNullOrWhiteSpace(result.Snapshot.Enchant) ? "---" : result.Snapshot.Enchant,
                result.Completed ? result.Status : BuildRollingText("Rolling"));

            if (result.Completed)
            {
                IsRunning = false;
                _runner.Reset();
                return;
            }
        }
        catch
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = BuildRollingText("Rolling");
                if (string.IsNullOrWhiteSpace(EquippedRodText) || EquippedRodText == "---")
                {
                    EquippedRodText = "---";
                }

                if (string.IsNullOrWhiteSpace(CurrentEnchantText) || CurrentEnchantText == "---")
                {
                    CurrentEnchantText = "---";
                }
            });
        }
        finally
        {
            Volatile.Write(ref _stepRunning, 0);
        }
    }

    private void RefreshEnchantState()
    {
        try
        {
            var snapshot = _detector.Read();
            SetEnchantState(
                string.IsNullOrWhiteSpace(snapshot.RodText) ? "---" : snapshot.RodText,
                string.IsNullOrWhiteSpace(snapshot.Enchant) ? "---" : snapshot.Enchant,
                null);
        }
        catch
        {
            SetEnchantState("---", "---", null);
        }
    }

    private void SetEnchantState(string rod, string enchant, string? status)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetEnchantState(rod, enchant, status));
            return;
        }

        EquippedRodText = rod;
        CurrentEnchantText = enchant;
        EquippedRodLines = ColoredEnchantText.BuildLines(rod);
        CurrentEnchantLines = ColoredEnchantText.BuildLines(enchant);
        OnPropertyChanged(nameof(EquippedRodBrush));
        OnPropertyChanged(nameof(CurrentEnchantBrush));
        if (!string.IsNullOrWhiteSpace(status))
        {
            StatusText = status;
            OnPropertyChanged(nameof(StatusBrush));
        }
        else
        {
            UpdateStatus();
        }
    }

    private void UpdateStatus()
    {
        if (DateTimeOffset.UtcNow < _statusLockUntil)
        {
            return;
        }

        if (!IsRunning)
        {
            StatusText = CurrentEnchantText;
            return;
        }

        if (CurrentEnchantText == "---")
        {
            StatusText = "Current enchant not detected.";
            return;
        }

        StatusText = string.Equals(CurrentEnchantText, SelectedTargetEnchantName, StringComparison.OrdinalIgnoreCase)
            ? $"Target found: {CurrentEnchantText}."
            : $"Current enchant: {CurrentEnchantText}.";
        StatusLines = ColoredEnchantText.BuildLines(StatusText);
    }

    private Task AddEnchantAsync()
    {
        var value = (NewEnchantText ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!_allTargetEnchants.Any(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase)))
        {
            _allTargetEnchants.Add(new EnchantOptionViewModel(value));
            _allTargetEnchants.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        }

        TargetSearchText = string.Empty;
        RefilterTargetEnchants();
        SelectedTargetEnchant = _allTargetEnchants.First(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase));
        NewEnchantText = string.Empty;
        return Task.CompletedTask;
    }

    private void RefilterTargetEnchants()
    {
        var query = (TargetSearchText ?? string.Empty).Trim();
        var selected = SelectedTargetEnchantName;
        var filtered = _allTargetEnchants
            .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        TargetEnchants.Clear();
        foreach (var enchant in filtered)
        {
            TargetEnchants.Add(enchant);
        }

        if (!string.IsNullOrWhiteSpace(selected) &&
            TargetEnchants.FirstOrDefault(item => string.Equals(item.Name, selected, StringComparison.OrdinalIgnoreCase)) is { } matching)
        {
            SelectedTargetEnchant = matching;
        }
        else if (TargetEnchants.Count > 0)
        {
            SelectedTargetEnchant = TargetEnchants[0];
        }
    }

    private string BuildRollingText(string core)
    {
        _rollingDotStep = (_rollingDotStep + 1) % 4;
        return core + new string('.', _rollingDotStep);
    }

    private void SetRollMode(EnchantRollMode mode)
    {
        if (_rollMode == mode)
        {
            return;
        }

        _rollMode = mode;
        OnPropertyChanged(nameof(IsGamepassMode));
        OnPropertyChanged(nameof(IsNormalMode));
    }
}

public sealed class EnchantOptionViewModel
{
    public EnchantOptionViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IBrush Brush => EnchantColors.GetEnchantBrush(Name);

    public override string ToString()
    {
        return Name;
    }
}
