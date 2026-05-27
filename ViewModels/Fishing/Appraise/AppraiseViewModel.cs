using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class AppraiseViewModel : ViewModelBase
{
    private static readonly string[] DefaultMutations =
    [
        "None",
        "Albino",
        "Darkened",
        "Negative",
        "Glossy",
        "Lunar",
        "Translucent",
        "Electric",
        "Hexed",
        "Silver",
        "Frozen",
        "Mosaic",
        "Scorched",
        "Amber",
        "Abyssal",
        "Coral",
        "Decayed",
        "Poisoned",
        "Fossilized",
        "Vined",
        "Crimson",
        "Honey",
        "Midas",
        "Boreal",
        "Fallen",
        "Greedy",
        "Spirit",
        "Mourned",
        "Mythical",
        "Shrouded"
    ];

    private readonly Tracking2AppraiseRunner _runner = new();
    private readonly Timer _timer;
    private readonly List<AppraiseMutationOptionViewModel> _allMutations;
    private bool _autoAppraiseEnabled;
    private bool _isRunning;
    private string _statusText = "---";
    private string _heldFishText = "---";
    private IReadOnlyList<ColoredTextLineViewModel> _statusLines = ColoredAppraiseText.BuildLines("---");
    private IReadOnlyList<ColoredTextLineViewModel> _heldFishLines = ColoredAppraiseText.BuildLines("---");
    private string _mutationSearchText = string.Empty;
    private string _newMutationText = string.Empty;
    private string _clickXText = string.Empty;
    private string _clickYText = string.Empty;
    private int _rollingDotStep;
    private EnchantRollMode _rollMode = EnchantRollMode.Gamepass;
    private bool _requireShiny;
    private bool _requireSparkling;
    private bool _requireTiny;
    private bool _requireSmall;
    private bool _requireBig;
    private bool _requireGiant;
    private bool _captureNextClick;
    private bool _lastLeftDown;
    private int _tickWorkInProgress;
    private double _gamepassSpeed = 0.6;

    public AppraiseViewModel()
    {
        _allMutations = DefaultMutations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => new AppraiseMutationOptionViewModel(x))
            .ToList();
        foreach (var mutation in _allMutations)
        {
            mutation.PropertyChanged += HandleMutationOptionChanged;
        }

        _allMutations.FirstOrDefault(x => string.Equals(x.Name, "None", StringComparison.OrdinalIgnoreCase))!.IsSelected = true;
        Mutations = new ObservableCollection<AppraiseMutationOptionViewModel>(_allMutations);
        AddMutationCommand = new RelayCommand(_ => AddMutationAsync());
        UseCursorPositionCommand = new RelayCommand(_ => UseCursorPositionAsync());
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
    }

    public ObservableCollection<AppraiseMutationOptionViewModel> Mutations { get; }

    public string SelectedMutationsSummary
    {
        get
        {
            var selected = GetSelectedMutationNames();
            if (selected.Count == 0)
            {
                return "None";
            }

            return selected.Count == 1 ? selected[0] : $"{selected.Count} Selected";
        }
    }

    public string MutationSearchText
    {
        get => _mutationSearchText;
        set
        {
            if (SetProperty(ref _mutationSearchText, value))
            {
                RefilterMutations();
            }
        }
    }

    public string NewMutationText
    {
        get => _newMutationText;
        set => SetProperty(ref _newMutationText, value);
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

    public bool RequireShiny
    {
        get => _requireShiny;
        set => SetProperty(ref _requireShiny, value);
    }

    public bool RequireSparkling
    {
        get => _requireSparkling;
        set => SetProperty(ref _requireSparkling, value);
    }

    public bool RequireBig
    {
        get => _requireBig;
        set => SetProperty(ref _requireBig, value);
    }

    public bool RequireGiant
    {
        get => _requireGiant;
        set => SetProperty(ref _requireGiant, value);
    }

    public bool RequireTiny
    {
        get => _requireTiny;
        set => SetProperty(ref _requireTiny, value);
    }

    public bool RequireSmall
    {
        get => _requireSmall;
        set => SetProperty(ref _requireSmall, value);
    }

    public Avalonia.Media.IBrush ShinyBrush => AppraiseColors.GetAppraiseBrush("Shiny");
    public Avalonia.Media.IBrush SparklingBrush => AppraiseColors.GetAppraiseBrush("Sparkling");
    public Avalonia.Media.IBrush TinyBrush => AppraiseColors.GetAppraiseBrush("Tiny");
    public Avalonia.Media.IBrush SmallBrush => AppraiseColors.GetAppraiseBrush("Small");
    public Avalonia.Media.IBrush BigBrush => AppraiseColors.GetAppraiseBrush("Big");
    public Avalonia.Media.IBrush GiantBrush => AppraiseColors.GetAppraiseBrush("Giant");

    public bool AutoAppraiseEnabled
    {
        get => _autoAppraiseEnabled;
        set
        {
            if (!SetProperty(ref _autoAppraiseEnabled, value))
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

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (SetProperty(ref _statusText, value))
            {
                StatusLines = ColoredAppraiseText.BuildLines(value);
            }
        }
    }

    public string HeldFishText
    {
        get => _heldFishText;
        private set
        {
            if (SetProperty(ref _heldFishText, value))
            {
                HeldFishLines = ColoredAppraiseText.BuildLines(value);
            }
        }
    }

    public IReadOnlyList<ColoredTextLineViewModel> StatusLines
    {
        get => _statusLines;
        private set => SetProperty(ref _statusLines, value);
    }

    public IReadOnlyList<ColoredTextLineViewModel> HeldFishLines
    {
        get => _heldFishLines;
        private set => SetProperty(ref _heldFishLines, value);
    }

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

    public RelayCommand AddMutationCommand { get; }

    public RelayCommand UseCursorPositionCommand { get; }

    public double GamepassSpeed
    {
        get => _gamepassSpeed;
        set
        {
            var clamped = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 0.6;
            if (SetProperty(ref _gamepassSpeed, clamped))
            {
                OnPropertyChanged(nameof(GamepassSpeedPercentText));
            }
        }
    }

    public string GamepassSpeedPercentText => $"{Math.Round(GamepassSpeed * 100)}%";

    public Task ToggleAsync()
    {
        return IsRunning ? StopAsync() : StartAsync();
    }

    public Task StartAsync()
    {
        AutoAppraiseEnabled = true;
        _runner.Reset();
        _rollingDotStep = 0;
        IsRunning = true;
        StatusText = "Rolling";
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        IsRunning = false;
        _runner.Reset();
        StatusText = "---";
        return Task.CompletedTask;
    }

    private void Tick()
    {
        if (Interlocked.Exchange(ref _tickWorkInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            // Keep click-capture responsive, but do it on UI thread.
            Dispatcher.UIThread.Post(TryCaptureClickPoint);

            string heldFish;
            try
            {
                heldFish = _runner.ReadHeldFishText();
            }
            catch
            {
                heldFish = "---";
            }

            Dispatcher.UIThread.Post(() => HeldFishText = heldFish);

            if (!IsRunning)
            {
                return;
            }

            var settings = BuildSettings();
            var result = _runner.Step(settings);
            Dispatcher.UIThread.Post(() =>
            {
                if (!IsRunning)
                {
                    StatusText = "---";
                    return;
                }

                if (result.Completed)
                {
                    IsRunning = false;
                    _runner.Reset();
                    StatusText = result.Status;
                    return;
                }

                if (result.Failed)
                {
                    IsRunning = false;
                    _runner.Reset();
                    StatusText = result.Status;
                    return;
                }

                StatusText = BuildRollingText("Rolling");
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
            Interlocked.Exchange(ref _tickWorkInProgress, 0);
        }
    }

    private void RefreshHeldFish()
    {
        try
        {
            HeldFishText = _runner.ReadHeldFishText();
        }
        catch
        {
            HeldFishText = "---";
        }
    }

    private AppraiseSettings BuildSettings()
    {
        var clickX = int.TryParse((ClickXText ?? string.Empty).Trim(), out var x) ? x : 0;
        var clickY = int.TryParse((ClickYText ?? string.Empty).Trim(), out var y) ? y : 0;
        return new AppraiseSettings(
            GetSelectedMutationNames(),
            RequireShiny,
            RequireSparkling,
            RequireTiny,
            RequireSmall,
            RequireBig,
            RequireGiant,
            clickX,
            clickY,
            GamepassSpeed,
            _rollMode == EnchantRollMode.Gamepass ? AppraiseRunMode.Gamepass : AppraiseRunMode.Normal);
    }

    private void RefilterMutations()
    {
        var query = (MutationSearchText ?? string.Empty).Trim();
        var filtered = _allMutations
            .Where(item => query.Length == 0 || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Mutations.Clear();
        foreach (var item in filtered)
        {
            Mutations.Add(item);
        }
    }

    private Task AddMutationAsync()
    {
        var value = (NewMutationText ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return Task.CompletedTask;
        }

        if (!_allMutations.Any(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase)))
        {
            var added = new AppraiseMutationOptionViewModel(value);
            added.PropertyChanged += HandleMutationOptionChanged;
            _allMutations.Add(added);
            _allMutations.Sort((a, b) =>
            {
                if (string.Equals(a.Name, "None", StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(b.Name, "None", StringComparison.OrdinalIgnoreCase) ? 0 : -1;
                }

                if (string.Equals(b.Name, "None", StringComparison.OrdinalIgnoreCase))
                {
                    return 1;
                }

                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        MutationSearchText = string.Empty;
        RefilterMutations();
        if (_allMutations.FirstOrDefault(x => string.Equals(x.Name, value, StringComparison.OrdinalIgnoreCase)) is { } selected)
        {
            selected.IsSelected = true;
        }

        NewMutationText = string.Empty;
        return Task.CompletedTask;
    }

    private Task UseCursorPositionAsync()
    {
        _captureNextClick = true;
        _lastLeftDown = NativeMouse.IsLeftButtonDown();
        StatusText = "Click once to set.";
        return Task.CompletedTask;
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

    private void HandleMutationOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppraiseMutationOptionViewModel.IsSelected) || sender is not AppraiseMutationOptionViewModel changed)
        {
            return;
        }

        if (changed.IsSelected)
        {
            if (string.Equals(changed.Name, "None", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in _allMutations.Where(x => !ReferenceEquals(x, changed)))
                {
                    item.IsSelected = false;
                }
            }
            else
            {
                var none = _allMutations.FirstOrDefault(x => string.Equals(x.Name, "None", StringComparison.OrdinalIgnoreCase));
                if (none is not null)
                {
                    none.IsSelected = false;
                }
            }
        }
        else
        {
            if (!_allMutations.Any(x => x.IsSelected && !string.Equals(x.Name, "None", StringComparison.OrdinalIgnoreCase)))
            {
                var none = _allMutations.FirstOrDefault(x => string.Equals(x.Name, "None", StringComparison.OrdinalIgnoreCase));
                if (none is not null)
                {
                    none.IsSelected = true;
                }
            }
        }

        OnPropertyChanged(nameof(SelectedMutationsSummary));
    }

    private List<string> GetSelectedMutationNames()
    {
        return _allMutations
            .Where(x => x.IsSelected && !string.Equals(x.Name, "None", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public sealed class AppraiseMutationOptionViewModel : ViewModelBase
{
    private static readonly IReadOnlyDictionary<string, string> Multipliers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Decayed"] = "0.45x",
            ["Poisoned"] = "0.9x",
            ["Amber"] = "1.2x",
            ["Negative"] = "1.3x",
            ["Translucent"] = "1.3x",
            ["Albino"] = "1.5x",
            ["Mosaic"] = "1.5x",
            ["Frozen"] = "1.5x",
            ["Darkened"] = "1.5x",
            ["Glossy"] = "1.6x",
            ["Coral"] = "1.8x",
            ["Silver"] = "1.8x",
            ["Electric"] = "2.1x",
            ["Lunar"] = "2.5x",
            ["Midas"] = "2.5x",
            ["Honey"] = "2.6x",
            ["Hexed"] = "3x",
            ["Scorched"] = "3x",
            ["Fossilized"] = "3.3x",
            ["Vined"] = "3.5x",
            ["Crimson"] = "4x",
            ["Boreal"] = "4x",
            ["Greedy"] = "5x",
            ["Spirit"] = "5.2x",
            ["Mythical"] = "5.5x",
            ["Abyssal"] = "5.5x",
            ["Fallen"] = "6x",
            ["Shrouded"] = "7x",
            ["Mourned"] = "7.5x",
        };

    public AppraiseMutationOptionViewModel(string name)
    {
        Name = name;
    }

    public string Name { get; }
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string MultiText => Multipliers.TryGetValue(Name, out var value) ? value : string.Empty;

    public Avalonia.Media.IBrush Brush => AppraiseColors.GetAppraiseBrush(Name);

    public override string ToString()
    {
        return Name;
    }
}
