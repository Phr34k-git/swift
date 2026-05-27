using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using System.Threading;
using Client.Services;
using Client.ViewModels;

namespace Client;

/// <summary>
/// Main application window with custom chrome and hosted app content.
/// </summary>
public partial class MainWindow : Window
{
    private const int HotkeyToggleDebounceMs = 120;
    private const double NormalWidth = 900;
    private const double NormalHeight = 600;
    private const double CompactWidth = 440;
    private const double CompactHeight = 280;

    private readonly AppStateService _appStateService;
    private readonly MainWindowViewModel _viewModel;
    private readonly GlobalHotkeyService _globalHotkeyService = new();
    private long _lastHotkeyToggleAt;
    private int _hotkeyToggleInFlight;

    /// <summary>
    /// Creates the main application window.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        AppLog.Info("MainWindow", $"Starting. log={AppLog.LogPath}");
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(0, 0);

        _appStateService = new AppStateService();
        var accountApiClient = new AccountApiClient(_appStateService);
        _viewModel = new MainWindowViewModel(_appStateService, accountApiClient);
        DataContext = _viewModel;
        _globalHotkeyService.SetHotkey(_viewModel.StartStopHotkey);
        _globalHotkeyService.Pressed += GlobalHotkeyService_OnPressed;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        KeyDown += MainWindow_OnKeyDown;
        Loaded += MainWindow_OnLoaded;
    }

    private void ViewModel_OnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsCompactMode))
        {
            return;
        }

        var compact = _viewModel.IsCompactMode;
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyCompactSize(compact);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplyCompactSize(compact));
        }
    }

    private void ApplyCompactSize(bool compact)
    {
        Width = compact ? CompactWidth : NormalWidth;
        Height = compact ? CompactHeight : NormalHeight;
    }

    private void GlobalHotkeyService_OnPressed()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastHotkeyToggleAt);
        if (now - last < HotkeyToggleDebounceMs)
        {
            AppLog.Info("Hotkey", $"MainWindow debounce suppressed. dt={now - last}ms");
            return;
        }

        Interlocked.Exchange(ref _lastHotkeyToggleAt, now);
        if (Interlocked.Exchange(ref _hotkeyToggleInFlight, 1) == 1)
        {
            AppLog.Info("Hotkey", "MainWindow in-flight suppressed.");
            return;
        }

        AppLog.Info("Hotkey", "MainWindow accepted press; dispatching toggle.");
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await _viewModel.ToggleMacroFromHotkeyAsync();
            }
            finally
            {
                Volatile.Write(ref _hotkeyToggleInFlight, 0);
                AppLog.Info("Hotkey", "MainWindow toggle dispatch complete.");
            }
        });
    }

    private void MainWindow_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel.HandleKey(e.Key))
        {
            _globalHotkeyService.SetHotkey(_viewModel.StartStopHotkey);
            e.Handled = true;
        }
    }

    private async void MainWindow_OnLoaded(object? sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_OnLoaded;
        AppLog.Info("MainWindow", "Loaded; initializing app state.");

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error("MainWindow", "InitializeAsync bubbled unexpectedly.", ex);
            throw;
        }
        finally
        {
            AppLog.Info("MainWindow", "Startup auth initialization finished.");
        }
    }

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Control source && source.FindAncestorOfType<Button>() is not null)
        {
            return;
        }

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _appStateService.Dispose();
        _globalHotkeyService.Dispose();
        base.OnClosed(e);
    }
}
