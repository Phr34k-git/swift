using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Threading;
using Client.Models;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// Hosts the current top-level view model for the main window.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly AppStateService _appStateService;
    private readonly BlankViewModel _blankViewModel;
    private readonly LoginViewModel _loginViewModel;
    private readonly ShellViewModel _shellViewModel;
    private readonly LockedOutViewModel _lockedOutViewModel;
    private readonly bool _hasStartupLockout;
    private object _currentView;

    /// <summary>
    /// Creates the main window view model.
    /// </summary>
    public MainWindowViewModel(AppStateService appStateService, AccountApiClient accountApiClient, string? startupLockoutReason = null)
    {
        _appStateService = appStateService;
        _blankViewModel = new BlankViewModel();
        _loginViewModel = new LoginViewModel(
            appStateService,
            restartUpdateAsync: () => { Environment.Exit(86); return Task.CompletedTask; });
        _shellViewModel = new ShellViewModel(appStateService, accountApiClient,
            restartUpdateAsync: () => { Environment.Exit(86); return Task.CompletedTask; });
        _lockedOutViewModel = new LockedOutViewModel();
        _hasStartupLockout = !string.IsNullOrWhiteSpace(startupLockoutReason);
        _currentView = _blankViewModel;
        _appStateService.StateChanged += HandleStateChanged;
        _shellViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.IsCompactMode))
            {
                OnPropertyChanged(nameof(IsCompactMode));
            }
        };

        if (_hasStartupLockout)
        {
            _lockedOutViewModel.Reason = startupLockoutReason;
            _currentView = _lockedOutViewModel;
        }
        else
        {
            ApplyState(_appStateService.CurrentState);
        }
    }

    /// <summary>
    /// Gets the currently displayed view model.
    /// </summary>
    public object CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    /// <summary>
    /// Starts the app authentication state machine.
    /// </summary>
    public Task InitializeAsync()
    {
        if (_hasStartupLockout)
        {
            return Task.CompletedTask;
        }

        return _appStateService.InitializeAsync();
    }

    public bool HandleKey(Key key)
    {
        return CurrentView is ShellViewModel shell && shell.HandleKey(key);
    }

    public Key StartStopHotkey => _shellViewModel.StartStopHotkey;

    /// <summary>
    /// True while the shell is in compact mode (any macro is running). Drives
    /// MainWindow's size — bound there so the window shrinks to a focused
    /// layout while running and restores when stopped.
    /// </summary>
    public bool IsCompactMode => CurrentView is ShellViewModel shell && shell.IsCompactMode;

    public Task ToggleMacroAsync()
    {
        return CurrentView is ShellViewModel ? _shellViewModel.ToggleMacroAsync() : Task.CompletedTask;
    }

    public Task ToggleMacroFromHotkeyAsync()
    {
        return CurrentView is ShellViewModel ? _shellViewModel.ToggleMacroFromHotkeyAsync() : Task.CompletedTask;
    }

    private void HandleStateChanged(AppState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplyState(state);
            return;
        }

        Dispatcher.UIThread.Post(() => ApplyState(state));
    }

    private void ApplyState(AppState state)
    {
        switch (state)
        {
            case AppState.Initializing:
            case AppState.Authenticating:
            case AppState.Unreachable:
                CurrentView = _blankViewModel;
                break;
            case AppState.Running:
                CurrentView = _shellViewModel;
                break;
            case AppState.Login:
            case AppState.FatalError:
                CurrentView = _loginViewModel;
                break;
            case AppState.Revoked:
                _lockedOutViewModel.Reason = "revoked";
                CurrentView = _lockedOutViewModel;
                break;
            case AppState.LockedOut:
                _lockedOutViewModel.Reason = _appStateService.LockoutReason ?? "revoked";
                CurrentView = _lockedOutViewModel;
                break;
        }
    }

}
