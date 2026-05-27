using System.ComponentModel;

namespace Client.ViewModels;

public sealed class OtherAutomationViewModel : ViewModelBase
{
    private readonly AutoAnglerViewModel _autoAnglerViewModel;
    private readonly EnchantViewModel _enchantViewModel;
    private readonly AppraiseViewModel _appraiseViewModel;
    private readonly TreasureAppraiseViewModel _treasureAppraiseViewModel;
    private object? _activeAutomation;

    public OtherAutomationViewModel(
        AutoAnglerViewModel autoAnglerViewModel,
        EnchantViewModel enchantViewModel,
        AppraiseViewModel appraiseViewModel,
        TreasureAppraiseViewModel treasureAppraiseViewModel)
    {
        _autoAnglerViewModel = autoAnglerViewModel;
        _enchantViewModel = enchantViewModel;
        _appraiseViewModel = appraiseViewModel;
        _treasureAppraiseViewModel = treasureAppraiseViewModel;

        _autoAnglerViewModel.PropertyChanged += HandleAutoAnglerPropertyChanged;
        _enchantViewModel.PropertyChanged += HandleEnchantPropertyChanged;
        _appraiseViewModel.PropertyChanged += HandleAppraisePropertyChanged;
        _treasureAppraiseViewModel.PropertyChanged += HandleTreasurePropertyChanged;
        UpdateActiveAutomation();
    }

    public bool AutoAnglerEnabled
    {
        get => _autoAnglerViewModel.AutoAnglerEnabled;
        set
        {
            if (_autoAnglerViewModel.AutoAnglerEnabled == value)
            {
                return;
            }

            _autoAnglerViewModel.AutoAnglerEnabled = value;
            OnPropertyChanged(nameof(AutoAnglerEnabled));
            UpdateActiveAutomation();
        }
    }

    public bool EnchantEnabled
    {
        get => _enchantViewModel.AutoEnchantEnabled;
        set
        {
            if (_enchantViewModel.AutoEnchantEnabled == value)
            {
                return;
            }

            _enchantViewModel.AutoEnchantEnabled = value;
            OnPropertyChanged(nameof(EnchantEnabled));
            UpdateActiveAutomation();
        }
    }

    public bool AppraiseEnabled
    {
        get => _appraiseViewModel.AutoAppraiseEnabled;
        set
        {
            if (_appraiseViewModel.AutoAppraiseEnabled == value)
            {
                return;
            }

            _appraiseViewModel.AutoAppraiseEnabled = value;
            OnPropertyChanged(nameof(AppraiseEnabled));
            UpdateActiveAutomation();
        }
    }

    public bool TreasureAppraiseEnabled
    {
        get => _treasureAppraiseViewModel.AutoTreasureEnabled;
        set
        {
            if (_treasureAppraiseViewModel.AutoTreasureEnabled == value)
            {
                return;
            }

            _treasureAppraiseViewModel.AutoTreasureEnabled = value;
            OnPropertyChanged(nameof(TreasureAppraiseEnabled));
            UpdateActiveAutomation();
        }
    }

    public bool HasActiveAutomation => _activeAutomation is not null;

    public bool ShowAutomationTabs => !HasActiveAutomation;

    public object? ActiveAutomation => _activeAutomation;

    public string ActiveAutomationTitle => _activeAutomation switch
    {
        AutoAnglerViewModel => "Other Automation - Angler",
        EnchantViewModel => "Other Automation - Enchant",
        AppraiseViewModel => "Other Automation - Appraise",
        TreasureAppraiseViewModel => "Other Automation - Treasure Appraise",
        _ => "Other Automation",
    };

    private void HandleAutoAnglerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AutoAnglerViewModel.AutoAnglerEnabled))
        {
            OnPropertyChanged(nameof(AutoAnglerEnabled));
            UpdateActiveAutomation();
        }
    }

    private void HandleEnchantPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EnchantViewModel.AutoEnchantEnabled))
        {
            OnPropertyChanged(nameof(EnchantEnabled));
            UpdateActiveAutomation();
        }
    }

    private void HandleAppraisePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppraiseViewModel.AutoAppraiseEnabled))
        {
            OnPropertyChanged(nameof(AppraiseEnabled));
            UpdateActiveAutomation();
        }
    }

    private void HandleTreasurePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TreasureAppraiseViewModel.AutoTreasureEnabled))
        {
            OnPropertyChanged(nameof(TreasureAppraiseEnabled));
            UpdateActiveAutomation();
        }
    }

    private void UpdateActiveAutomation()
    {
        object? next = null;
        if (_autoAnglerViewModel.AutoAnglerEnabled)
        {
            next = _autoAnglerViewModel;
        }
        else if (_enchantViewModel.AutoEnchantEnabled)
        {
            next = _enchantViewModel;
        }
        else if (_appraiseViewModel.AutoAppraiseEnabled)
        {
            next = _appraiseViewModel;
        }
        else if (_treasureAppraiseViewModel.AutoTreasureEnabled)
        {
            next = _treasureAppraiseViewModel;
        }

        if (ReferenceEquals(_activeAutomation, next))
        {
            return;
        }

        _activeAutomation = next;
        OnPropertyChanged(nameof(ActiveAutomation));
        OnPropertyChanged(nameof(HasActiveAutomation));
        OnPropertyChanged(nameof(ShowAutomationTabs));
        OnPropertyChanged(nameof(ActiveAutomationTitle));
    }
}
