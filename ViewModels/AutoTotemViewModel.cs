using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Media;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

public sealed class AutoTotemViewModel : ViewModelBase
{
    private bool _autoTotemEnabled;
    private TotemOptionViewModel _selectedTotem;
    private string _currentWeather = string.Empty;
    private IReadOnlyList<ColoredTextLineViewModel> _statusLines = BuildStatusLines(string.Empty);
    private bool _useShinyTotem;
    private bool _useSparklingTotem;
    private bool _useMutationTotem;
    private bool _stayDay;
    private bool _stayNight;
    private AutoTotemTimePreference? _forcedTimePreference;
    private string _currentCycle = "Auto";
    private bool _activeShinySurge;
    private bool _activeSparklingSurge;
    private bool _activeMutationSurge;
    private readonly WorldStatusReader _statusReader = new();
    private readonly Timer _statusTimer;

    public AutoTotemViewModel()
    {
        TotemOptions = new ObservableCollection<TotemOptionViewModel>
        {
            new("None", "None"),
            new("Clearcast Totem", "Clear"),
            new("Tempest Totem", "Rain"),
            new("Windset Totem", "Windy"),
            new("Aurora Totem", "Aurora Borealis"),
            new("Smokescreen Totem", "Foggy"),
            new("Eclipse Totem", "Eclipse"),
            new("Starfall Totem", "Starfall"),
        };
        _selectedTotem = TotemOptions[0];
        WeatherOptions = new ObservableCollection<string>
        {
            "Clear",
            "Foggy",
            "Windy",
            "Rain",
            "Eclipse",
            "Starfall",
            "Aurora Borealis",
            "Rainbow",
            "Shiny Surge",
            "Sparkling Surge",
            "Mutation Surge",
        };
        AutoTotemSettings.Mode = AutoTotemMode.Expire;
        AutoTotemSettings.TotemName = _selectedTotem.Name;
        AutoTotemSettings.Enabled = _autoTotemEnabled;
        AutoTotemSettings.Special = AutoTotemSpecial.None;
        AutoTotemSettings.TimePreference = AutoTotemTimePreference.None;
        _statusTimer = new Timer(_ => RefreshFromMemory(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(700));
        RefreshStatus();
    }

    public ObservableCollection<TotemOptionViewModel> TotemOptions { get; }

    public ObservableCollection<string> WeatherOptions { get; }

    public bool AutoTotemEnabled
    {
        get => _autoTotemEnabled;
        set
        {
            if (!SetProperty(ref _autoTotemEnabled, value))
            {
                return;
            }

            AutoTotemSettings.Enabled = value;
            RefreshStatus();
        }
    }

    public TotemOptionViewModel SelectedTotem
    {
        get => _selectedTotem;
        set
        {
            if (!SetProperty(ref _selectedTotem, value))
            {
                return;
            }

            AutoTotemSettings.TotemName = value.Name;
            ApplyAutomaticTimePreference(value.Name);
            RefreshStatus();
        }
    }

    public bool UseShinyTotem
    {
        get => _useShinyTotem;
        set
        {
            if (SetProperty(ref _useShinyTotem, value))
            {
                if (value)
                {
                    UseSparklingTotem = false;
                    UseMutationTotem = false;
                }

                AutoTotemSettings.Special = value
                    ? AutoTotemSpecial.Shiny
                    : UseSparklingTotem
                        ? AutoTotemSpecial.Sparkling
                        : UseMutationTotem
                            ? AutoTotemSpecial.Mutation
                            : AutoTotemSpecial.None;
                RefreshStatus();
            }
        }
    }

    public bool UseSparklingTotem
    {
        get => _useSparklingTotem;
        set
        {
            if (SetProperty(ref _useSparklingTotem, value))
            {
                if (value)
                {
                    UseShinyTotem = false;
                    UseMutationTotem = false;
                }

                AutoTotemSettings.Special = value
                    ? AutoTotemSpecial.Sparkling
                    : UseShinyTotem
                        ? AutoTotemSpecial.Shiny
                        : UseMutationTotem
                            ? AutoTotemSpecial.Mutation
                            : AutoTotemSpecial.None;
                RefreshStatus();
            }
        }
    }

    public bool UseMutationTotem
    {
        get => _useMutationTotem;
        set
        {
            if (SetProperty(ref _useMutationTotem, value))
            {
                if (value)
                {
                    UseShinyTotem = false;
                    UseSparklingTotem = false;
                }

                AutoTotemSettings.Special = value
                    ? AutoTotemSpecial.Mutation
                    : UseShinyTotem
                        ? AutoTotemSpecial.Shiny
                        : UseSparklingTotem
                            ? AutoTotemSpecial.Sparkling
                            : AutoTotemSpecial.None;
                RefreshStatus();
            }
        }
    }

    public bool StayDay
    {
        get => _forcedTimePreference == AutoTotemTimePreference.Day || (_forcedTimePreference is null && _stayDay);
        set
        {
            if (_forcedTimePreference is not null)
            {
                return;
            }

            if (SetProperty(ref _stayDay, value))
            {
                if (value)
                {
                    StayNight = false;
                }

                AutoTotemSettings.TimePreference = value
                    ? AutoTotemTimePreference.Day
                    : StayNight
                        ? AutoTotemTimePreference.Night
                        : AutoTotemTimePreference.None;
                RefreshStatus();
            }
        }
    }

    public bool StayNight
    {
        get => _forcedTimePreference == AutoTotemTimePreference.Night || (_forcedTimePreference is null && _stayNight);
        set
        {
            if (_forcedTimePreference is not null)
            {
                return;
            }

            if (SetProperty(ref _stayNight, value))
            {
                if (value)
                {
                    StayDay = false;
                }

                AutoTotemSettings.TimePreference = value
                    ? AutoTotemTimePreference.Night
                    : StayDay
                        ? AutoTotemTimePreference.Day
                        : AutoTotemTimePreference.None;
                RefreshStatus();
            }
        }
    }

    public bool IsTimePreferenceEditable => _forcedTimePreference is null;

    /// <summary>Day/Night/Auto cycle string most recently read from the world. Updated by the status timer.</summary>
    public string CurrentCycle => _currentCycle;

    /// <summary>True while a Shiny surge is active in the world.</summary>
    public bool ActiveShinySurge => _activeShinySurge;

    /// <summary>True while a Sparkling surge is active in the world.</summary>
    public bool ActiveSparklingSurge => _activeSparklingSurge;

    /// <summary>True while a Mutation surge is active in the world.</summary>
    public bool ActiveMutationSurge => _activeMutationSurge;

    public string CurrentWeather
    {
        get => _currentWeather;
        set
        {
            if (SetProperty(ref _currentWeather, value))
            {
                RefreshStatus();
            }
        }
    }

    public IReadOnlyList<ColoredTextLineViewModel> StatusLines
    {
        get => _statusLines;
        private set => SetProperty(ref _statusLines, value);
    }

    private void RefreshStatus()
    {
        if (!AutoTotemEnabled)
        {
            StatusLines = BuildStatusLines(string.Empty);
            return;
        }

        var segments = new List<ColoredTextSegmentViewModel>();

        var hasPrefix = false;
        if (_activeShinySurge)
        {
            segments.Add(new ColoredTextSegmentViewModel("Shiny", GetWeatherBrush("Shiny Surge")));
            hasPrefix = true;
        }

        if (_activeSparklingSurge)
        {
            if (hasPrefix)
            {
                segments.Add(new ColoredTextSegmentViewModel(", ", GetTextPrimary()));
            }

            segments.Add(new ColoredTextSegmentViewModel("Sparkling", GetWeatherBrush("Sparkling Surge")));
            hasPrefix = true;
        }

        if (_activeMutationSurge)
        {
            if (hasPrefix)
            {
                segments.Add(new ColoredTextSegmentViewModel(", ", GetTextPrimary()));
            }

            segments.Add(new ColoredTextSegmentViewModel("Mutation", GetWeatherBrush("Mutation Surge")));
            hasPrefix = true;
        }

        if (hasPrefix)
        {
            if (!string.IsNullOrWhiteSpace(CurrentWeather) || !string.IsNullOrWhiteSpace(_currentCycle))
            {
                segments.Add(new ColoredTextSegmentViewModel(", ", GetTextPrimary()));
            }
        }

        var hasWeather = !string.IsNullOrWhiteSpace(CurrentWeather);
        var hasCycle = !string.IsNullOrWhiteSpace(_currentCycle);
        if (hasWeather)
        {
            segments.Add(new ColoredTextSegmentViewModel(CurrentWeather, GetWeatherBrush(CurrentWeather)));
        }

        if (hasWeather && hasCycle)
        {
            segments.Add(new ColoredTextSegmentViewModel(", ", GetTextPrimary()));
        }

        if (hasCycle)
        {
            segments.Add(new ColoredTextSegmentViewModel(_currentCycle, GetTextPrimary()));
        }

        if (segments.Count == 0)
        {
            segments.Add(new ColoredTextSegmentViewModel("---", GetTextPrimary()));
        }

        StatusLines = [new ColoredTextLineViewModel(segments)];
    }

    private void RefreshFromMemory()
    {
        if (!_statusReader.TryRead(out var weather, out var cycle, out var shinySurge, out var sparklingSurge, out var mutationSurge))
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!_activeShinySurge && !_activeSparklingSurge && !_activeMutationSurge)
                {
                    return;
                }

                _activeShinySurge = false;
                _activeSparklingSurge = false;
                _activeMutationSurge = false;
                RefreshStatus();
            });
            return;
        }

        weather = NormalizeWeatherDisplay(weather);

        Dispatcher.UIThread.Post(() =>
        {
            AppLog.Info(
                "AutoTotemStatus",
                $"reader: weather='{weather}', cycle='{cycle}', shiny={shinySurge}, sparkling={sparklingSurge}, mutation={mutationSurge}");
            var changed = false;
            if (!string.Equals(_currentCycle, cycle, StringComparison.OrdinalIgnoreCase))
            {
                _currentCycle = cycle;
                OnPropertyChanged(nameof(CurrentCycle));
                changed = true;
            }

            if (_activeShinySurge != shinySurge)
            {
                _activeShinySurge = shinySurge;
                OnPropertyChanged(nameof(ActiveShinySurge));
                changed = true;
            }

            if (_activeSparklingSurge != sparklingSurge)
            {
                _activeSparklingSurge = sparklingSurge;
                OnPropertyChanged(nameof(ActiveSparklingSurge));
                changed = true;
            }

            if (_activeMutationSurge != mutationSurge)
            {
                _activeMutationSurge = mutationSurge;
                OnPropertyChanged(nameof(ActiveMutationSurge));
                changed = true;
            }

            if (!string.Equals(_currentWeather, weather, StringComparison.OrdinalIgnoreCase))
            {
                _currentWeather = weather;
                OnPropertyChanged(nameof(CurrentWeather));
                changed = true;
            }

            if (changed)
            {
                AppLog.Info(
                    "AutoTotemStatus",
                    $"ui-updated: weather='{_currentWeather}', cycle='{_currentCycle}', shiny={_activeShinySurge}, sparkling={_activeSparklingSurge}, mutation={_activeMutationSurge}");
                RefreshStatus();
            }
        });
    }

    private static IBrush GetTextPrimary()
    {
        if (Application.Current?.Resources.TryGetResource("TextPrimary", null, out var res) == true && res is IBrush brush)
            return brush;
        return Brushes.White;
    }

    private static IReadOnlyList<ColoredTextLineViewModel> BuildStatusLines(string weather)
    {
        var text = string.IsNullOrWhiteSpace(weather) ? "---" : weather;
        var segment = new ColoredTextSegmentViewModel(text, GetWeatherBrush(weather));
        return [new ColoredTextLineViewModel([segment])];
    }

    private static IBrush GetWeatherBrush(string weather)
    {
        return weather switch
        {
            "Clear" => new SolidColorBrush(Color.FromArgb(255, 240, 240, 240)),
            "Foggy" => new SolidColorBrush(Color.FromArgb(255, 120, 170, 190)),
            "Windy" => new SolidColorBrush(Color.FromArgb(255, 150, 180, 255)),
            "Rain" => new SolidColorBrush(Color.FromArgb(255, 70, 120, 255)),
            "Eclipse" => new SolidColorBrush(Color.FromArgb(255, 255, 120, 0)),
            "Starfall" => new SolidColorBrush(Color.FromArgb(255, 170, 120, 255)),
            "Aurora Borealis" => new SolidColorBrush(Color.FromArgb(255, 120, 255, 220)),
            "Rainbow" => new SolidColorBrush(Color.FromArgb(255, 255, 140, 200)),
            "Shiny Surge" => new SolidColorBrush(Color.FromArgb(255, 255, 245, 180)),
            "Sparkling Surge" => new SolidColorBrush(Color.FromArgb(255, 255, 240, 170)),
            "Mutation Surge" => new SolidColorBrush(Color.FromArgb(255, 120, 255, 120)),
            _ => GetTextPrimary(),
        };
    }

    internal static IBrush GetStatusBrush(string weather)
    {
        return GetWeatherBrush(weather);
    }

    private static string NormalizeWeatherDisplay(string weather)
    {
        if (string.IsNullOrWhiteSpace(weather))
        {
            return string.Empty;
        }

        return Regex.Replace(
            weather,
            @"\s*\+\s*sovereign\s*surge\s*",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Trim();
    }

    private void ApplyAutomaticTimePreference(string totemName)
    {
        AutoTotemTimePreference? forced = null;
        if (string.Equals(totemName, "Aurora Totem", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(totemName, "Starfall Totem", StringComparison.OrdinalIgnoreCase))
        {
            forced = AutoTotemTimePreference.Night;
        }
        else if (string.Equals(totemName, "Eclipse Totem", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(totemName, "Rainbow Totem", StringComparison.OrdinalIgnoreCase))
        {
            forced = AutoTotemTimePreference.Day;
        }

        _forcedTimePreference = forced;
        OnPropertyChanged(nameof(IsTimePreferenceEditable));
        OnPropertyChanged(nameof(StayDay));
        OnPropertyChanged(nameof(StayNight));

        if (forced == AutoTotemTimePreference.Day)
        {
            SetProperty(ref _stayDay, true);
            SetProperty(ref _stayNight, false);
            AutoTotemSettings.TimePreference = AutoTotemTimePreference.Day;
            return;
        }

        if (forced == AutoTotemTimePreference.Night)
        {
            SetProperty(ref _stayNight, true);
            SetProperty(ref _stayDay, false);
            AutoTotemSettings.TimePreference = AutoTotemTimePreference.Night;
            return;
        }

        AutoTotemSettings.TimePreference = _stayDay
            ? AutoTotemTimePreference.Day
            : _stayNight
                ? AutoTotemTimePreference.Night
                : AutoTotemTimePreference.None;
    }
}

public sealed class TotemOptionViewModel
{
    public TotemOptionViewModel(string name, string colorKey)
    {
        Name = name;
        ColorKey = colorKey;
    }

    public string Name { get; }

    public string ColorKey { get; }

    public string DisplayPrimary => Name.EndsWith(" Totem", StringComparison.OrdinalIgnoreCase)
        ? Name.Replace(" Totem", string.Empty, StringComparison.OrdinalIgnoreCase).TrimEnd()
        : Name.TrimEnd();

    public string DisplaySuffix => Name.EndsWith(" Totem", StringComparison.OrdinalIgnoreCase)
        ? " Totem"
        : string.Empty;

    public IBrush Brush => AutoTotemViewModel.GetStatusBrush(ColorKey);

    public override string ToString() => Name;
}
