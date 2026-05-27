using System;
using System.Windows.Input;
using Avalonia.Media;
using Client.Services;

namespace Client.ViewModels;

public sealed class SettingsClientViewModel : ViewModelBase, IDisposable
{
    private readonly Action _navigateBack;
    private string _backgroundHex = string.Empty;
    private string _surfaceHex = string.Empty;
    private string _borderHex = string.Empty;
    private string _accentHex = string.Empty;
    private string _textPrimaryHex = string.Empty;
    private bool _suppressHexSync;

    public SettingsClientViewModel(Action navigateBack)
    {
        _navigateBack = navigateBack;
        BackCommand = new RelayCommand(_ =>
        {
            _navigateBack();
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SelectDarkCommand = new RelayCommand(_ =>
        {
            SetTheme(AppTheme.Dark);
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SelectLightCommand = new RelayCommand(_ =>
        {
            SetTheme(AppTheme.Light);
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SelectSlateCommand = new RelayCommand(_ =>
        {
            SetTheme(AppTheme.Slate);
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SelectPinkCommand = new RelayCommand(_ =>
        {
            SetTheme(AppTheme.Pink);
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SelectCustomCommand = new RelayCommand(_ =>
        {
            SeedCustomFromCurrentIfUnset();
            SetTheme(AppTheme.Custom);
            return System.Threading.Tasks.Task.CompletedTask;
        });

        SyncHexFromAnchors();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    public AppTheme SelectedTheme => ThemeService.Current;

    public bool IsDarkSelected => ThemeService.Current == AppTheme.Dark;
    public bool IsLightSelected => ThemeService.Current == AppTheme.Light;
    public bool IsSlateSelected => ThemeService.Current == AppTheme.Slate;
    public bool IsPinkSelected => ThemeService.Current == AppTheme.Pink;
    public bool IsCustomSelected => ThemeService.Current == AppTheme.Custom;

    public ICommand BackCommand { get; }
    public ICommand SelectDarkCommand { get; }
    public ICommand SelectLightCommand { get; }
    public ICommand SelectSlateCommand { get; }
    public ICommand SelectPinkCommand { get; }
    public ICommand SelectCustomCommand { get; }

    public string BackgroundHex
    {
        get => _backgroundHex;
        set { if (SetProperty(ref _backgroundHex, value)) TryApplyHexEdit(); }
    }

    public string SurfaceHex
    {
        get => _surfaceHex;
        set { if (SetProperty(ref _surfaceHex, value)) TryApplyHexEdit(); }
    }

    public string BorderHex
    {
        get => _borderHex;
        set { if (SetProperty(ref _borderHex, value)) TryApplyHexEdit(); }
    }

    public string AccentHex
    {
        get => _accentHex;
        set { if (SetProperty(ref _accentHex, value)) TryApplyHexEdit(); }
    }

    public string TextPrimaryHex
    {
        get => _textPrimaryHex;
        set { if (SetProperty(ref _textPrimaryHex, value)) TryApplyHexEdit(); }
    }

    public IBrush BackgroundSwatch => new SolidColorBrush(ThemeService.CustomAnchors.Background);
    public IBrush SurfaceSwatch => new SolidColorBrush(ThemeService.CustomAnchors.Surface);
    public IBrush BorderSwatch => new SolidColorBrush(ThemeService.CustomAnchors.Border);
    public IBrush AccentSwatch => new SolidColorBrush(ThemeService.CustomAnchors.Accent);
    public IBrush TextPrimarySwatch => new SolidColorBrush(ThemeService.CustomAnchors.TextPrimary);

    public Color BackgroundColor
    {
        get => ThemeService.CustomAnchors.Background;
        set => ApplyAnchorEdit(value, a => a.Background, (a, c) => a.Background = c);
    }

    public Color SurfaceColor
    {
        get => ThemeService.CustomAnchors.Surface;
        set => ApplyAnchorEdit(value, a => a.Surface, (a, c) => a.Surface = c);
    }

    public Color BorderColor
    {
        get => ThemeService.CustomAnchors.Border;
        set => ApplyAnchorEdit(value, a => a.Border, (a, c) => a.Border = c);
    }

    public Color AccentColor
    {
        get => ThemeService.CustomAnchors.Accent;
        set => ApplyAnchorEdit(value, a => a.Accent, (a, c) => a.Accent = c);
    }

    public Color TextPrimaryColor
    {
        get => ThemeService.CustomAnchors.TextPrimary;
        set => ApplyAnchorEdit(value, a => a.TextPrimary, (a, c) => a.TextPrimary = c);
    }

    private void ApplyAnchorEdit(Color value, Func<CustomThemeAnchors, Color> read, Action<CustomThemeAnchors, Color> write)
    {
        if (_suppressHexSync) return;
        if (read(ThemeService.CustomAnchors) == value) return;

        var anchors = ThemeService.CustomAnchors.Clone();
        write(anchors, value);
        ThemeService.SetCustomAnchors(anchors, apply: ThemeService.Current == AppTheme.Custom);
        SyncHexFromAnchors();
    }

    private void SetTheme(AppTheme theme)
    {
        ThemeService.Apply(theme);
    }

    private void SeedCustomFromCurrentIfUnset()
    {
        // Only seed on the very first activation, before the user has ever
        // customized or loaded a saved custom theme. After that, the stored
        // anchors must survive switching to other presets and back.
        if (ThemeService.HasCustomAnchors) return;

        var src = ThemeService.Current switch
        {
            AppTheme.Light => ThemeColors.Light,
            AppTheme.Slate => ThemeColors.Slate,
            AppTheme.Pink => ThemeColors.Pink,
            _ => ThemeColors.Dark,
        };

        var anchors = new CustomThemeAnchors
        {
            Background = src["Background"],
            Surface = src["Surface"],
            Border = src["Border"],
            Accent = src["Accent"],
            TextPrimary = src["TextPrimary"],
        };
        ThemeService.SetCustomAnchors(anchors, apply: false);
        SyncHexFromAnchors();
    }

    private void TryApplyHexEdit()
    {
        if (_suppressHexSync) return;

        var anchors = ThemeService.CustomAnchors.Clone();
        var changed = false;
        if (Color.TryParse(_backgroundHex, out var bg) && bg != anchors.Background) { anchors.Background = bg; changed = true; }
        if (Color.TryParse(_surfaceHex, out var sf) && sf != anchors.Surface) { anchors.Surface = sf; changed = true; }
        if (Color.TryParse(_borderHex, out var br) && br != anchors.Border) { anchors.Border = br; changed = true; }
        if (Color.TryParse(_accentHex, out var ac) && ac != anchors.Accent) { anchors.Accent = ac; changed = true; }
        if (Color.TryParse(_textPrimaryHex, out var tx) && tx != anchors.TextPrimary) { anchors.TextPrimary = tx; changed = true; }

        if (!changed) return;

        ThemeService.SetCustomAnchors(anchors, apply: ThemeService.Current == AppTheme.Custom);
        RaiseSwatchChanged();
    }

    private void SyncHexFromAnchors()
    {
        _suppressHexSync = true;
        try
        {
            BackgroundHex = FormatHex(ThemeService.CustomAnchors.Background);
            SurfaceHex = FormatHex(ThemeService.CustomAnchors.Surface);
            BorderHex = FormatHex(ThemeService.CustomAnchors.Border);
            AccentHex = FormatHex(ThemeService.CustomAnchors.Accent);
            TextPrimaryHex = FormatHex(ThemeService.CustomAnchors.TextPrimary);
        }
        finally
        {
            _suppressHexSync = false;
        }
        RaiseSwatchChanged();
    }

    private void RaiseSwatchChanged()
    {
        OnPropertyChanged(nameof(BackgroundSwatch));
        OnPropertyChanged(nameof(SurfaceSwatch));
        OnPropertyChanged(nameof(BorderSwatch));
        OnPropertyChanged(nameof(AccentSwatch));
        OnPropertyChanged(nameof(TextPrimarySwatch));
        OnPropertyChanged(nameof(BackgroundColor));
        OnPropertyChanged(nameof(SurfaceColor));
        OnPropertyChanged(nameof(BorderColor));
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(TextPrimaryColor));
    }

    private static string FormatHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private void OnThemeChanged(AppTheme _)
    {
        OnPropertyChanged(nameof(SelectedTheme));
        OnPropertyChanged(nameof(IsDarkSelected));
        OnPropertyChanged(nameof(IsLightSelected));
        OnPropertyChanged(nameof(IsSlateSelected));
        OnPropertyChanged(nameof(IsPinkSelected));
        OnPropertyChanged(nameof(IsCustomSelected));
        SyncHexFromAnchors();
    }

    public void Dispose()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
    }
}
