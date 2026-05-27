using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Client.Models;
using Client.Services;
using Client.Services.Fishing;

namespace Client.ViewModels;

/// <summary>
/// Hosts sidebar navigation and the active logged-in page.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    public const string PreviewUpdateVersionEnvironmentVariable = "SWIFT_UPDATE_PREVIEW_VERSION";
    // Channel coords for the Roblox fix guide. discord:// is the undocumented
    // but widely-used Windows deep-link scheme (route format from the unofficial
    // protocol gist: discord://-/channels/<guildId>/<channelId>). Tried first
    // so users with the desktop client land directly in the channel; falls back
    // to the equivalent https:// URL via the browser if the protocol isn't
    // registered (Discord not installed).
    private const string RobloxFixGuideDeepLink = "discord://-/channels/1489588973532745849/1503677096210468894";
    private const string RobloxFixGuideWebUrl   = "https://discord.com/channels/1489588973532745849/1503677096210468894";

    private readonly AppStateService _appStateService;
    private readonly FishingViewModel _fishingViewModel = new();
    private readonly AutoTotemViewModel _autoTotemViewModel = new();
    private readonly AutoSovereignRechargeViewModel _autoSovereignRechargeViewModel;
    private readonly HuntDetectViewModel _huntDetectViewModel = new();
    private readonly FishingAddonsViewModel _fishingAddonsViewModel;
    private readonly AutoAnglerViewModel _autoAnglerViewModel = new();
    private readonly GeneralViewModel _generalViewModel;
    private readonly AppraiseViewModel _appraiseViewModel = new();
    private readonly TreasureAppraiseViewModel _treasureAppraiseViewModel = new();
    private readonly EnchantViewModel _enchantViewModel = new();
    private readonly OtherAutomationViewModel _otherAutomationViewModel;
    private readonly AccountViewModel _accountViewModel;
    private readonly SettingsViewModel _settingsViewModel = new();
    private readonly Func<Task> _restartUpdateAsync;
    private readonly RelayCommand _restartUpdateCommand;
    private readonly UserSettingsStore _userSettingsStore = new();
    private bool _applyingSavedSettings;
    private object _currentPage;
    private bool _isUpdateAvailable;
    private bool _isRestartingUpdate;
    private string _updateVersion = string.Empty;
    private string _updateStatusText = string.Empty;
    private readonly RelayCommand _robloxFixGuideCommand;
    private bool _isRobloxBannerVisible;
    private string _robloxBannerTitle = string.Empty;
    private string _robloxBannerSubtext = string.Empty;
    private bool _isRobloxFixButtonVisible;

    /// <summary>
    /// Creates the logged-in shell view model.
    /// </summary>
    public ShellViewModel(
        AppStateService appStateService,
        AccountApiClient accountApiClient,
        Func<Task>? restartUpdateAsync = null)
    {
        _appStateService = appStateService;
        _restartUpdateAsync = restartUpdateAsync ?? (() => Task.CompletedTask);
        _restartUpdateCommand = new RelayCommand(
            _ => RestartUpdateAsync(),
            _ => IsUpdateAvailable && !IsRestartingUpdate);
        _robloxFixGuideCommand = new RelayCommand(
            _ =>
            {
                OpenRobloxFixGuide();
                return Task.CompletedTask;
            },
            _ => IsRobloxFixButtonVisible);
        _autoSovereignRechargeViewModel = new AutoSovereignRechargeViewModel(_fishingViewModel);

        _generalViewModel = new GeneralViewModel(_fishingViewModel, _enchantViewModel, _appraiseViewModel, _treasureAppraiseViewModel, _autoAnglerViewModel);
        _fishingAddonsViewModel = new FishingAddonsViewModel(_fishingViewModel, _autoTotemViewModel, _autoSovereignRechargeViewModel, _huntDetectViewModel);
        CompactViewModel = new CompactViewModel(_generalViewModel, _fishingViewModel, _autoTotemViewModel);
        _generalViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneralViewModel.IsRunning))
            {
                OnPropertyChanged(nameof(IsCompactMode));
            }
        };
        _otherAutomationViewModel = new OtherAutomationViewModel(_autoAnglerViewModel, _enchantViewModel, _appraiseViewModel, _treasureAppraiseViewModel);
        appStateService.StateChanged += OnAppStateChanged;
        _accountViewModel = new AccountViewModel(accountApiClient, appStateService);
        LoadSavedSettings();
        HookSettingsPersistence();
        _enchantViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EnchantViewModel.AutoEnchantEnabled) &&
                _enchantViewModel.AutoEnchantEnabled)
            {
                if (_appraiseViewModel.AutoAppraiseEnabled)
                {
                    _appraiseViewModel.AutoAppraiseEnabled = false;
                }

                if (_treasureAppraiseViewModel.AutoTreasureEnabled)
                {
                    _treasureAppraiseViewModel.AutoTreasureEnabled = false;
                }

                if (_autoAnglerViewModel.AutoAnglerEnabled)
                {
                    _autoAnglerViewModel.AutoAnglerEnabled = false;
                }
            }
        };
        _appraiseViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppraiseViewModel.AutoAppraiseEnabled) &&
                _appraiseViewModel.AutoAppraiseEnabled)
            {
                if (_enchantViewModel.AutoEnchantEnabled)
                {
                    _enchantViewModel.AutoEnchantEnabled = false;
                }

                if (_treasureAppraiseViewModel.AutoTreasureEnabled)
                {
                    _treasureAppraiseViewModel.AutoTreasureEnabled = false;
                }

                if (_autoAnglerViewModel.AutoAnglerEnabled)
                {
                    _autoAnglerViewModel.AutoAnglerEnabled = false;
                }
            }
        };
        _treasureAppraiseViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TreasureAppraiseViewModel.AutoTreasureEnabled) &&
                _treasureAppraiseViewModel.AutoTreasureEnabled)
            {
                if (_enchantViewModel.AutoEnchantEnabled)
                {
                    _enchantViewModel.AutoEnchantEnabled = false;
                }

                if (_appraiseViewModel.AutoAppraiseEnabled)
                {
                    _appraiseViewModel.AutoAppraiseEnabled = false;
                }

                if (_autoAnglerViewModel.AutoAnglerEnabled)
                {
                    _autoAnglerViewModel.AutoAnglerEnabled = false;
                }
            }
        };
        _autoAnglerViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AutoAnglerViewModel.AutoAnglerEnabled) &&
                _autoAnglerViewModel.AutoAnglerEnabled)
            {
                if (_enchantViewModel.AutoEnchantEnabled)
                {
                    _enchantViewModel.AutoEnchantEnabled = false;
                }

                if (_appraiseViewModel.AutoAppraiseEnabled)
                {
                    _appraiseViewModel.AutoAppraiseEnabled = false;
                }

                if (_treasureAppraiseViewModel.AutoTreasureEnabled)
                {
                    _treasureAppraiseViewModel.AutoTreasureEnabled = false;
                }
            }
        };
        NavigationItems = new[]
        {
            new ShellNavigationItemViewModel("General", _generalViewModel, Navigate),
            new ShellNavigationItemViewModel("Fishing", _fishingViewModel, Navigate),
            new ShellNavigationItemViewModel("Fishing Add-ons", _fishingAddonsViewModel, Navigate),
            new ShellNavigationItemViewModel("Other Automation", _otherAutomationViewModel, Navigate),
            new ShellNavigationItemViewModel("Account", _accountViewModel, Navigate),
            new ShellNavigationItemViewModel("Settings", _settingsViewModel, Navigate),
        };

        var fishingAddonsItem = NavigationItems[2];
        _fishingViewModel.NavigateToAutoTotemAction = () =>
        {
            Navigate(fishingAddonsItem);
            _fishingAddonsViewModel.IsAutoTotemExpanded = true;
            _fishingAddonsViewModel.IsAutoAquariumExpanded = false;
        };
        _fishingViewModel.NavigateToAutoAquariumAction = () =>
        {
            Navigate(fishingAddonsItem);
            _fishingAddonsViewModel.IsAutoAquariumExpanded = true;
            _fishingAddonsViewModel.IsAutoTotemExpanded = false;
        };

        _currentPage = _generalViewModel;
        NavigationItems[0].IsSelected = true;

        var previewUpdateVersion = Environment.GetEnvironmentVariable(PreviewUpdateVersionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(previewUpdateVersion))
            ShowUpdateAvailable(previewUpdateVersion);

        _ = RunRobloxVersionCheckAsync();
    }

    /// <summary>
    /// Gets the sidebar navigation items.
    /// </summary>
    public IReadOnlyList<ShellNavigationItemViewModel> NavigationItems { get; }

    /// <summary>
    /// Gets the active page view model.
    /// </summary>
    public object CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    /// <summary>
    /// Aggregator VM rendered in place of the normal shell layout while
    /// the macro is running. Bound to a dedicated CompactView.
    /// </summary>
    public CompactViewModel CompactViewModel { get; }

    /// <summary>
    /// True while any macro is running — drives the compact-mode UI swap
    /// and signals MainWindow to shrink to a focused layout.
    /// </summary>
    public bool IsCompactMode => _generalViewModel.IsRunning;

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
            {
                _restartUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsRestartingUpdate
    {
        get => _isRestartingUpdate;
        private set
        {
            if (SetProperty(ref _isRestartingUpdate, value))
            {
                _restartUpdateCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UpdateVersion
    {
        get => _updateVersion;
        private set
        {
            if (SetProperty(ref _updateVersion, value))
            {
                OnPropertyChanged(nameof(RestartUpdateButtonText));
            }
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    public string RestartUpdateButtonText =>
        string.IsNullOrWhiteSpace(UpdateVersion)
            ? "Restart to update"
            : $"Restart to {UpdateVersion}";

    public ICommand RestartUpdateCommand => _restartUpdateCommand;

    public void ShowUpdateAvailable(string version)
    {
        UpdateVersion = version;
        UpdateStatusText = $"{version} is ready to install.";
        IsRestartingUpdate = false;
        IsUpdateAvailable = true;
    }

    public void ClearUpdateAvailable()
    {
        IsUpdateAvailable = false;
        IsRestartingUpdate = false;
        UpdateVersion = string.Empty;
        UpdateStatusText = string.Empty;
    }

    public bool IsRobloxBannerVisible
    {
        get => _isRobloxBannerVisible;
        private set => SetProperty(ref _isRobloxBannerVisible, value);
    }

    public string RobloxBannerTitle
    {
        get => _robloxBannerTitle;
        private set => SetProperty(ref _robloxBannerTitle, value);
    }

    public string RobloxBannerSubtext
    {
        get => _robloxBannerSubtext;
        private set => SetProperty(ref _robloxBannerSubtext, value);
    }

    public bool IsRobloxFixButtonVisible
    {
        get => _isRobloxFixButtonVisible;
        private set
        {
            if (SetProperty(ref _isRobloxFixButtonVisible, value))
            {
                _robloxFixGuideCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand RobloxFixGuideCommand => _robloxFixGuideCommand;

    private static void OpenRobloxFixGuide()
    {
        // Step 1: try the desktop client via the discord:// protocol handler.
        // Process.Start with UseShellExecute=true triggers ShellExecuteEx,
        // which returns SE_ERR_NOASSOC (Win32 error 1155) when no handler is
        // registered — surfaced as a Win32Exception that we catch and fall
        // through to the web fallback.
        try
        {
            Process.Start(new ProcessStartInfo(RobloxFixGuideDeepLink) { UseShellExecute = true });
            return;
        }
        catch
        {
            // No discord:// handler. Fall through.
        }

        // Step 2: open the equivalent https:// URL via the standard browser
        // chain (BrowserLauncher tries default browser → rundll32 → Edge →
        // explorer). Discord.com's web app will itself attempt to relaunch
        // into the desktop client if it's installed but the protocol was
        // somehow unregistered.
        BrowserLauncher.TryOpen(RobloxFixGuideWebUrl, out string _);
    }

    private async Task RunRobloxVersionCheckAsync()
    {
        RobloxVersionCheck check;
        try
        {
            check = await RobloxVersionService.CheckAsync().ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyRobloxVersionCheck(check));
    }

    private void ApplyRobloxVersionCheck(RobloxVersionCheck check)
    {
        switch (check.Result)
        {
            case RobloxVersionCheckResult.Mismatch:
                RobloxBannerTitle = "Version Mismatch";
                RobloxBannerSubtext = "Roblox outdated";
                IsRobloxFixButtonVisible = true;
                IsRobloxBannerVisible = true;
                break;

            case RobloxVersionCheckResult.UwpNotSupported:
                RobloxBannerTitle = "Unsupported Roblox version";
                RobloxBannerSubtext = "The Microsoft Store (UWP) version of Roblox is not supported. Please install Roblox from the Roblox website.";
                IsRobloxFixButtonVisible = false;
                IsRobloxBannerVisible = true;
                break;

            case RobloxVersionCheckResult.RobloxNotFound:
                RobloxBannerTitle = "Roblox not found";
                RobloxBannerSubtext = "Unable to compare Roblox version to the latest build. Restart the macro with Roblox open.";
                IsRobloxFixButtonVisible = false;
                IsRobloxBannerVisible = true;
                break;

            case RobloxVersionCheckResult.Match:
            case RobloxVersionCheckResult.LatestUnknown:
            case RobloxVersionCheckResult.NotChecked:
            default:
                IsRobloxBannerVisible = false;
                IsRobloxFixButtonVisible = false;
                break;
        }
    }

    private void Navigate(ShellNavigationItemViewModel item)
    {
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = ReferenceEquals(navigationItem, item);
        }

        // Re-entering Settings should always show the main settings page, not
        // the sub-page (e.g. Client/Theme) that was open on the previous visit.
        if (ReferenceEquals(item.Page, _settingsViewModel))
        {
            _settingsViewModel.ResetToMainView();
        }

        CurrentPage = item.Page;
    }

    public bool HandleKey(Key key)
    {
        return _generalViewModel.HandleKey(key);
    }

    public Key StartStopHotkey => _generalViewModel.StartStopHotkey;

    public Task ToggleMacroAsync()
    {
        return _generalViewModel.ToggleMacroAsync();
    }

    public Task ToggleMacroFromHotkeyAsync()
    {
        return _generalViewModel.ToggleMacroFromHotkeyAsync();
    }

    private async Task RestartUpdateAsync()
    {
        if (!IsUpdateAvailable || IsRestartingUpdate)
        {
            return;
        }

        IsRestartingUpdate = true;
        UpdateStatusText = "Restarting...";
        try
        {
            await _restartUpdateAsync();
        }
        catch (Exception)
        {
            IsRestartingUpdate = false;
            UpdateStatusText = "Could not start updater. Try again.";
        }
    }

    private void LoadSavedSettings()
    {
        _applyingSavedSettings = true;
        try
        {
            var saved = _userSettingsStore.Load();

            if (Enum.TryParse<FishingTrackerMode>(saved.Fishing.TrackerMode, true, out var trackerMode))
            {
                foreach (var option in _fishingViewModel.TrackerOptions)
                {
                    if (option.Mode == trackerMode)
                    {
                        _fishingViewModel.SelectedTracker = option;
                        break;
                    }
                }
            }

            if (Enum.TryParse<FishingCastingMode>(saved.Fishing.CastingMode, true, out var castingMode))
            {
                _fishingViewModel.SelectedCastingMode = castingMode;
            }

            _fishingViewModel.AutoAquariumEnabled = saved.Fishing.AutoAquariumEnabled;
            _fishingViewModel.AutoAquariumCycleDelayMinutes = saved.Fishing.AutoAquariumCycleDelayMinutes;

            _generalViewModel.SelectedRodSlot = Math.Clamp(saved.General.RodSlot, 1, 9);

            if (!string.IsNullOrWhiteSpace(saved.General.StartStopHotkey) &&
                Enum.TryParse<Key>(saved.General.StartStopHotkey, true, out var savedHotkey) &&
                savedHotkey != Key.None)
            {
                _generalViewModel.StartStopHotkey = savedHotkey;
            }

            if (saved.CustomTheme is { } customTheme)
            {
                var anchors = ThemeService.CustomAnchors.Clone();
                anchors.Background = ParseColorOr(customTheme.Background, anchors.Background);
                anchors.Surface = ParseColorOr(customTheme.Surface, anchors.Surface);
                anchors.Border = ParseColorOr(customTheme.Border, anchors.Border);
                anchors.Accent = ParseColorOr(customTheme.Accent, anchors.Accent);
                anchors.TextPrimary = ParseColorOr(customTheme.TextPrimary, anchors.TextPrimary);
                ThemeService.SetCustomAnchors(anchors, apply: false);
            }

            if (!string.IsNullOrWhiteSpace(saved.Theme) &&
                Enum.TryParse<AppTheme>(saved.Theme, true, out var savedTheme))
            {
                ThemeService.Apply(savedTheme);
            }

            _autoTotemViewModel.AutoTotemEnabled = saved.AutoTotem.Enabled;
            if (!string.IsNullOrWhiteSpace(saved.AutoTotem.TotemName))
            {
                foreach (var option in _autoTotemViewModel.TotemOptions)
                {
                    if (string.Equals(option.Name, saved.AutoTotem.TotemName, StringComparison.OrdinalIgnoreCase))
                    {
                        _autoTotemViewModel.SelectedTotem = option;
                        break;
                    }
                }
            }

            _autoTotemViewModel.UseShinyTotem = saved.AutoTotem.UseShinyTotem;
            _autoTotemViewModel.UseSparklingTotem = saved.AutoTotem.UseSparklingTotem;
            _autoTotemViewModel.UseMutationTotem = saved.AutoTotem.UseMutationTotem;
            _autoTotemViewModel.StayDay = saved.AutoTotem.StayDay;
            _autoTotemViewModel.StayNight = saved.AutoTotem.StayNight;

            _autoSovereignRechargeViewModel.MinimumPercent = saved.AutoSovereign.MinimumPercent;
            _autoSovereignRechargeViewModel.MaximumPercent = saved.AutoSovereign.MaximumPercent;
            _autoSovereignRechargeViewModel.Enabled = saved.AutoSovereign.Enabled;

            _huntDetectViewModel.Enabled = saved.HuntDetect.Enabled;
            _huntDetectViewModel.UseHuntColors = true;
            _huntDetectViewModel.DiscordWebhook = saved.HuntDetect.DiscordWebhook ?? string.Empty;
            _huntDetectViewModel.RestoreSelectedTargets(saved.HuntDetect.SelectedTargets);
        }
        finally
        {
            _applyingSavedSettings = false;
        }
    }

    private void HookSettingsPersistence()
    {
        _fishingViewModel.PropertyChanged += (_, _) => SaveSettings();
        _generalViewModel.PropertyChanged += (_, _) => SaveSettings();
        _autoTotemViewModel.PropertyChanged += (_, _) => SaveSettings();
        _autoSovereignRechargeViewModel.PropertyChanged += (_, _) => SaveSettings();
        _huntDetectViewModel.PropertyChanged += (_, _) => SaveSettings();
        ThemeService.ThemeChanged += OnThemeChangedForPersistence;
    }

    private void OnThemeChangedForPersistence(AppTheme _) => SaveSettings();

    private static Color ParseColorOr(string? value, Color fallback)
        => !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out var parsed) ? parsed : fallback;

    private static string FormatHex(Color c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void SaveSettings()
    {
        if (_applyingSavedSettings)
        {
            return;
        }

        var snapshot = new UserSettingsSnapshot
        {
            Fishing = new FishingSettingsSnapshot
            {
                TrackerMode = _fishingViewModel.SelectedMode.ToString(),
                CastingMode = _fishingViewModel.SelectedCastingMode.ToString(),
                AutoAquariumEnabled = _fishingViewModel.AutoAquariumEnabled,
                AutoAquariumCycleDelayMinutes = _fishingViewModel.AutoAquariumCycleDelayMinutes,
            },
            General = new GeneralSettingsSnapshot
            {
                RodSlot = _generalViewModel.SelectedRodSlot,
                StartStopHotkey = _generalViewModel.StartStopHotkey.ToString(),
            },
            Theme = ThemeService.Current.ToString(),
            CustomTheme = new CustomThemeSnapshot
            {
                Background = FormatHex(ThemeService.CustomAnchors.Background),
                Surface = FormatHex(ThemeService.CustomAnchors.Surface),
                Border = FormatHex(ThemeService.CustomAnchors.Border),
                Accent = FormatHex(ThemeService.CustomAnchors.Accent),
                TextPrimary = FormatHex(ThemeService.CustomAnchors.TextPrimary),
            },
            AutoTotem = new AutoTotemSettingsSnapshot
            {
                Enabled = _autoTotemViewModel.AutoTotemEnabled,
                TotemName = _autoTotemViewModel.SelectedTotem?.Name,
                UseShinyTotem = _autoTotemViewModel.UseShinyTotem,
                UseSparklingTotem = _autoTotemViewModel.UseSparklingTotem,
                UseMutationTotem = _autoTotemViewModel.UseMutationTotem,
                StayDay = _autoTotemViewModel.StayDay,
                StayNight = _autoTotemViewModel.StayNight,
            },
            AutoSovereign = new AutoSovereignSettingsSnapshot
            {
                Enabled = _autoSovereignRechargeViewModel.Enabled,
                MinimumPercent = _autoSovereignRechargeViewModel.MinimumPercent,
                MaximumPercent = _autoSovereignRechargeViewModel.MaximumPercent,
            },
            HuntDetect = new HuntDetectSettingsSnapshot
            {
                Enabled = _huntDetectViewModel.Enabled,
                UseHuntColors = true,
                DiscordWebhook = _huntDetectViewModel.DiscordWebhook,
                SelectedTargets = _huntDetectViewModel.GetSelectedTargetNames().ToArray(),
            },
        };

        _userSettingsStore.Save(snapshot);
    }

    private async void OnAppStateChanged(AppState state)
    {
        if (state is AppState.LockedOut or AppState.Revoked or AppState.FatalError)
        {
            await _generalViewModel.StopAllAsync();
        }
    }

}
