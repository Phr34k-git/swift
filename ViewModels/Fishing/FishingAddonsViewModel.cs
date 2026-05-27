using System.ComponentModel;
using System.Threading.Tasks;

namespace Client.ViewModels;

public sealed class FishingAddonsViewModel : ViewModelBase
{
    private readonly FishingViewModel _fishingViewModel;
    private readonly AutoTotemViewModel _autoTotemViewModel;
    private readonly AutoSovereignRechargeViewModel _autoSovereignRechargeViewModel;
    private readonly HuntDetectViewModel _huntDetectViewModel;
    private bool _isAutoAquariumExpanded;
    private bool _isAutoTotemExpanded;
    private bool _isAutoSovereignRechargeExpanded;
    private bool _isHuntDetectExpanded;

    public FishingAddonsViewModel(
        FishingViewModel fishingViewModel,
        AutoTotemViewModel autoTotemViewModel,
        AutoSovereignRechargeViewModel autoSovereignRechargeViewModel,
        HuntDetectViewModel? huntDetectViewModel = null)
    {
        _fishingViewModel = fishingViewModel;
        _autoTotemViewModel = autoTotemViewModel;
        _autoSovereignRechargeViewModel = autoSovereignRechargeViewModel;
        _huntDetectViewModel = huntDetectViewModel ?? new HuntDetectViewModel();
        _isAutoAquariumExpanded = false;
        _isAutoTotemExpanded = false;
        _isAutoSovereignRechargeExpanded = false;
        _isHuntDetectExpanded = false;

        ToggleAutoAquariumExpandedCommand = new RelayCommand(_ =>
        {
            if (IsAutoAquariumExpanded)
            {
                IsAutoAquariumExpanded = false;
            }
            else
            {
                IsAutoAquariumExpanded = true;
                IsAutoTotemExpanded = false;
                IsAutoSovereignRechargeExpanded = false;
                IsHuntDetectExpanded = false;
            }

            return Task.CompletedTask;
        });

        ToggleAutoTotemExpandedCommand = new RelayCommand(_ =>
        {
            if (IsAutoTotemExpanded)
            {
                IsAutoTotemExpanded = false;
            }
            else
            {
                IsAutoTotemExpanded = true;
                IsAutoAquariumExpanded = false;
                IsAutoSovereignRechargeExpanded = false;
                IsHuntDetectExpanded = false;
            }

            return Task.CompletedTask;
        });

        ToggleAutoSovereignRechargeExpandedCommand = new RelayCommand(_ =>
        {
            if (IsAutoSovereignRechargeExpanded)
            {
                IsAutoSovereignRechargeExpanded = false;
            }
            else
            {
                IsAutoSovereignRechargeExpanded = true;
                IsAutoAquariumExpanded = false;
                IsAutoTotemExpanded = false;
                IsHuntDetectExpanded = false;
            }

            return Task.CompletedTask;
        });

        ToggleHuntDetectExpandedCommand = new RelayCommand(_ =>
        {
            if (IsHuntDetectExpanded)
            {
                IsHuntDetectExpanded = false;
            }
            else
            {
                IsHuntDetectExpanded = true;
                IsAutoAquariumExpanded = false;
                IsAutoTotemExpanded = false;
                IsAutoSovereignRechargeExpanded = false;
            }

            return Task.CompletedTask;
        });

        _fishingViewModel.PropertyChanged += HandleFishingPropertyChanged;
        _autoTotemViewModel.PropertyChanged += HandleTotemPropertyChanged;
        _autoSovereignRechargeViewModel.PropertyChanged += HandleSovereignRechargePropertyChanged;
        _huntDetectViewModel.PropertyChanged += HandleHuntDetectPropertyChanged;
    }

    public bool AutoAquariumEnabled
    {
        get => _fishingViewModel.AutoAquariumEnabled;
        set
        {
            if (_fishingViewModel.AutoAquariumEnabled == value)
            {
                return;
            }

            _fishingViewModel.AutoAquariumEnabled = value;
            OnPropertyChanged(nameof(AutoAquariumEnabled));
        }
    }

    public double AutoAquariumCycleDelayMinutes
    {
        get => _fishingViewModel.AutoAquariumCycleDelayMinutes;
        set
        {
            if (_fishingViewModel.AutoAquariumCycleDelayMinutes == value)
            {
                return;
            }

            _fishingViewModel.AutoAquariumCycleDelayMinutes = value;
            OnPropertyChanged(nameof(AutoAquariumCycleDelayMinutes));
        }
    }

    public bool AutoTotemEnabled
    {
        get => _autoTotemViewModel.AutoTotemEnabled;
        set
        {
            if (_autoTotemViewModel.AutoTotemEnabled == value)
            {
                return;
            }

            _autoTotemViewModel.AutoTotemEnabled = value;
            OnPropertyChanged(nameof(AutoTotemEnabled));
        }
    }

    public AutoTotemViewModel AutoTotem => _autoTotemViewModel;

    public bool AutoSovereignRechargeEnabled
    {
        get => _autoSovereignRechargeViewModel.Enabled;
        set
        {
            if (_autoSovereignRechargeViewModel.Enabled == value)
            {
                return;
            }

            _autoSovereignRechargeViewModel.Enabled = value;
            OnPropertyChanged(nameof(AutoSovereignRechargeEnabled));
        }
    }

    public AutoSovereignRechargeViewModel AutoSovereignRecharge => _autoSovereignRechargeViewModel;

    public bool HuntDetectEnabled
    {
        get => _huntDetectViewModel.Enabled;
        set
        {
            if (_huntDetectViewModel.Enabled == value)
            {
                return;
            }

            _huntDetectViewModel.Enabled = value;
            OnPropertyChanged(nameof(HuntDetectEnabled));
        }
    }

    public HuntDetectViewModel HuntDetect => _huntDetectViewModel;

    public bool IsAutoAquariumExpanded
    {
        get => _isAutoAquariumExpanded;
        set
        {
            if (!SetProperty(ref _isAutoAquariumExpanded, value))
            {
                return;
            }

            RaiseAddonVisibilityStateChanged();
            OnPropertyChanged(nameof(AutoAquariumExpandGlyph));
        }
    }

    public bool IsAutoTotemExpanded
    {
        get => _isAutoTotemExpanded;
        set
        {
            if (!SetProperty(ref _isAutoTotemExpanded, value))
            {
                return;
            }

            RaiseAddonVisibilityStateChanged();
            OnPropertyChanged(nameof(AutoTotemExpandGlyph));
        }
    }

    public bool IsAutoSovereignRechargeExpanded
    {
        get => _isAutoSovereignRechargeExpanded;
        set
        {
            if (!SetProperty(ref _isAutoSovereignRechargeExpanded, value))
            {
                return;
            }

            RaiseAddonVisibilityStateChanged();
            OnPropertyChanged(nameof(AutoSovereignRechargeExpandGlyph));
        }
    }

    public bool IsHuntDetectExpanded
    {
        get => _isHuntDetectExpanded;
        set
        {
            if (!SetProperty(ref _isHuntDetectExpanded, value))
            {
                return;
            }

            RaiseAddonVisibilityStateChanged();
            OnPropertyChanged(nameof(HuntDetectExpandGlyph));
        }
    }

    public bool HasOpenAddon => IsAutoAquariumExpanded || IsAutoTotemExpanded || IsAutoSovereignRechargeExpanded || IsHuntDetectExpanded;

    public bool ShowAutoAquariumSection => IsAutoAquariumExpanded || !HasOpenAddon;

    public bool ShowAutoTotemSection => IsAutoTotemExpanded || !HasOpenAddon;
    public bool ShowAutoSovereignRechargeSection => IsAutoSovereignRechargeExpanded || !HasOpenAddon;
    public bool ShowHuntDetectSection => IsHuntDetectExpanded || !HasOpenAddon;

    public string AutoAquariumExpandGlyph => IsAutoAquariumExpanded ? "Hide" : "Open";

    public string AutoTotemExpandGlyph => IsAutoTotemExpanded ? "Hide" : "Open";
    public string AutoSovereignRechargeExpandGlyph => IsAutoSovereignRechargeExpanded ? "Hide" : "Open";
    public string HuntDetectExpandGlyph => IsHuntDetectExpanded ? "Hide" : "Open";

    public string ActiveAddonTitle => IsAutoAquariumExpanded
        ? "Fishing Add-ons - Auto Aquarium"
        : IsAutoTotemExpanded
            ? "Fishing Add-ons - Auto Totem"
            : IsAutoSovereignRechargeExpanded
                ? "Fishing Add-ons - Auto Sovereign Recharge"
                : IsHuntDetectExpanded
                    ? "Fishing Add-ons - Hunt Detect"
            : "Fishing Add-ons";

    public RelayCommand ToggleAutoAquariumExpandedCommand { get; }

    public RelayCommand ToggleAutoTotemExpandedCommand { get; }
    public RelayCommand ToggleAutoSovereignRechargeExpandedCommand { get; }
    public RelayCommand ToggleHuntDetectExpandedCommand { get; }

    private void HandleFishingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FishingViewModel.AutoAquariumEnabled))
        {
            OnPropertyChanged(nameof(AutoAquariumEnabled));
        }

        if (e.PropertyName is nameof(FishingViewModel.AutoAquariumCycleDelayMinutes))
        {
            OnPropertyChanged(nameof(AutoAquariumCycleDelayMinutes));
        }
    }

    private void HandleTotemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutoTotemViewModel.AutoTotemEnabled))
        {
            OnPropertyChanged(nameof(AutoTotemEnabled));
        }
    }

    private void HandleSovereignRechargePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutoSovereignRechargeViewModel.Enabled))
        {
            OnPropertyChanged(nameof(AutoSovereignRechargeEnabled));
        }
    }

    private void HandleHuntDetectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HuntDetectViewModel.Enabled))
        {
            OnPropertyChanged(nameof(HuntDetectEnabled));
        }
    }

    private void RaiseAddonVisibilityStateChanged()
    {
        OnPropertyChanged(nameof(HasOpenAddon));
        OnPropertyChanged(nameof(ShowAutoAquariumSection));
        OnPropertyChanged(nameof(ShowAutoTotemSection));
        OnPropertyChanged(nameof(ShowAutoSovereignRechargeSection));
        OnPropertyChanged(nameof(ShowHuntDetectSection));
        OnPropertyChanged(nameof(ActiveAddonTitle));
    }
}
