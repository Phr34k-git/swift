using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Client.Models;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// View model for the Discord login view.
/// </summary>
public sealed class LoginViewModel : ViewModelBase
{
    private const int CallbackPort = 9999;
    private static readonly TimeSpan CallbackTimeout = TimeSpan.FromMinutes(5);

    private readonly AppStateService _appStateService;
    private readonly Func<Task> _restartUpdateAsync;
    private readonly RelayCommand _loginCommand;
    private readonly RelayCommand _showCredentialLoginCommand;
    private readonly RelayCommand _showDiscordLoginCommand;
    private readonly RelayCommand _credentialLoginCommand;
    private readonly RelayCommand _showManualCodeCommand;
    private readonly RelayCommand _hideManualCodeCommand;
    private readonly RelayCommand _submitManualCodeCommand;
    private readonly RelayCommand _restartUpdateCommand;
    private bool _isLoginInProgress;
    private bool _isLoginButtonVisible;
    private bool _isCredentialLoginInProgress;
    private bool _isCredentialMode;
    private bool _isManualCodeMode;
    private bool _isManualCodeSubmitting;
    private bool _isUpdateAvailable;
    private bool _isRestartingUpdate;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _manualCode = string.Empty;
    private string _loginErrorMessage = string.Empty;
    private string _credentialErrorMessage = string.Empty;
    private string _manualCodeErrorMessage = string.Empty;
    private string _updateVersion = string.Empty;
    private string _updateStatusText = string.Empty;

    /// <summary>
    /// Creates the login view model and subscribes to app state changes.
    /// </summary>
    public LoginViewModel(AppStateService appStateService, Func<Task>? restartUpdateAsync = null)
    {
        _appStateService = appStateService;
        _restartUpdateAsync = restartUpdateAsync ?? (() => Task.CompletedTask);
        _loginCommand = new RelayCommand(_ => ExecuteLoginAsync());
        _showCredentialLoginCommand = new RelayCommand(_ => ShowCredentialLoginAsync());
        _showDiscordLoginCommand = new RelayCommand(_ => ShowDiscordLoginAsync());
        _credentialLoginCommand = new RelayCommand(
            _ => ExecuteCredentialLoginAsync(),
            _ => CanSubmitCredentials());
        _showManualCodeCommand = new RelayCommand(_ => ShowManualCodeAsync());
        _hideManualCodeCommand = new RelayCommand(_ => HideManualCodeAsync());
        _submitManualCodeCommand = new RelayCommand(
            _ => ExecuteManualCodeLoginAsync(),
            _ => CanSubmitManualCode());
        _restartUpdateCommand = new RelayCommand(
            _ => RestartUpdateAsync(),
            _ => IsUpdateAvailable && !IsRestartingUpdate);
        _appStateService.StateChanged += HandleStateChanged;
        ApplyState(_appStateService.CurrentState);

        var previewUpdateVersion = Environment.GetEnvironmentVariable(
            ShellViewModel.PreviewUpdateVersionEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(previewUpdateVersion))
        {
            ShowUpdateAvailable(previewUpdateVersion);
        }
    }

    /// <summary>
    /// Gets whether the Discord login button is visible.
    /// </summary>
    public bool IsLoginButtonVisible
    {
        get => _isLoginButtonVisible;
        private set
        {
            if (SetProperty(ref _isLoginButtonVisible, value))
            {
                _loginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether the Discord login flow is waiting on the browser or API.
    /// </summary>
    public bool IsLoginInProgress
    {
        get => _isLoginInProgress;
        private set
        {
            if (SetProperty(ref _isLoginInProgress, value))
            {
                OnPropertyChanged(nameof(IsLoginTextVisible));
            }
        }
    }

    /// <summary>
    /// Gets whether the login button should show its normal label.
    /// </summary>
    public bool IsLoginTextVisible => !IsLoginInProgress;

    /// <summary>
    /// Gets whether the credential login flow is processing.
    /// </summary>
    public bool IsCredentialLoginInProgress
    {
        get => _isCredentialLoginInProgress;
        private set
        {
            if (SetProperty(ref _isCredentialLoginInProgress, value))
            {
                OnPropertyChanged(nameof(IsCredentialTextVisible));
                _credentialLoginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether the credential login button should show its normal label.
    /// </summary>
    public bool IsCredentialTextVisible => !IsCredentialLoginInProgress;

    /// <summary>
    /// Gets whether the username/password login form is visible.
    /// </summary>
    public bool IsCredentialMode
    {
        get => _isCredentialMode;
        private set
        {
            if (SetProperty(ref _isCredentialMode, value))
            {
                OnPropertyChanged(nameof(IsDiscordPaneVisible));
            }
        }
    }

    /// <summary>
    /// Discord pane is shown when neither the credential form nor the manual
    /// code paste form is active. Computed so the view doesn't have to express
    /// a multi-binding inline.
    /// </summary>
    public bool IsDiscordPaneVisible => !IsCredentialMode && !IsManualCodeMode;

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

    /// <summary>
    /// Gets or sets the entered username.
    /// </summary>
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
            {
                CredentialErrorMessage = string.Empty;
                _credentialLoginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the entered password.
    /// </summary>
    public string Password
    {
        get => _password;
        set
        {
            if (SetProperty(ref _password, value))
            {
                CredentialErrorMessage = string.Empty;
                _credentialLoginCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the inline username/password login error.
    /// </summary>
    public string CredentialErrorMessage
    {
        get => _credentialErrorMessage;
        private set
        {
            if (SetProperty(ref _credentialErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsCredentialErrorVisible));
            }
        }
    }

    /// <summary>
    /// Gets whether the credentials error message should be visible.
    /// </summary>
    public bool IsCredentialErrorVisible => !string.IsNullOrWhiteSpace(CredentialErrorMessage);

    public string LoginErrorMessage
    {
        get => _loginErrorMessage;
        private set
        {
            if (SetProperty(ref _loginErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsLoginErrorVisible));
            }
        }
    }

    public bool IsLoginErrorVisible => !string.IsNullOrWhiteSpace(LoginErrorMessage);

    /// <summary>
    /// Gets the command that starts Discord OAuth login.
    /// </summary>
    public ICommand LoginCommand => _loginCommand;

    /// <summary>
    /// Gets the command that starts username/password sign in.
    /// </summary>
    public ICommand ShowCredentialLoginCommand => _showCredentialLoginCommand;

    /// <summary>
    /// Gets the command that returns to Discord sign in.
    /// </summary>
    public ICommand ShowDiscordLoginCommand => _showDiscordLoginCommand;

    /// <summary>
    /// Gets the command that submits username/password sign in.
    /// </summary>
    public ICommand CredentialLoginCommand => _credentialLoginCommand;

    // --- Manual code paste fallback. Surfaced when the local OAuth callback
    // listener can't be reached (port in use, antivirus blocks loopback,
    // hostname resolution broken). The API renders the exchange code on its
    // own success page so the user can copy it; this view consumes it. -----

    /// <summary>
    /// Whether the manual code paste textbox is visible.
    /// </summary>
    public bool IsManualCodeMode
    {
        get => _isManualCodeMode;
        private set
        {
            if (SetProperty(ref _isManualCodeMode, value))
            {
                OnPropertyChanged(nameof(IsDiscordPaneVisible));
                _submitManualCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsManualCodeSubmitting
    {
        get => _isManualCodeSubmitting;
        private set
        {
            if (SetProperty(ref _isManualCodeSubmitting, value))
            {
                OnPropertyChanged(nameof(IsManualCodeSubmitTextVisible));
                _submitManualCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsManualCodeSubmitTextVisible => !IsManualCodeSubmitting;

    public string ManualCode
    {
        get => _manualCode;
        set
        {
            if (SetProperty(ref _manualCode, value))
            {
                ManualCodeErrorMessage = string.Empty;
                _submitManualCodeCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ManualCodeErrorMessage
    {
        get => _manualCodeErrorMessage;
        private set
        {
            if (SetProperty(ref _manualCodeErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsManualCodeErrorVisible));
            }
        }
    }

    public bool IsManualCodeErrorVisible => !string.IsNullOrWhiteSpace(ManualCodeErrorMessage);

    public ICommand ShowManualCodeCommand => _showManualCodeCommand;
    public ICommand HideManualCodeCommand => _hideManualCodeCommand;
    public ICommand SubmitManualCodeCommand => _submitManualCodeCommand;

    public ICommand RestartUpdateCommand => _restartUpdateCommand;

    public void ShowUpdateAvailable(string version)
    {
        UpdateVersion = version;
        UpdateStatusText = $"{version} is ready to install.";
        IsRestartingUpdate = false;
        IsUpdateAvailable = true;
    }

    private async Task ExecuteLoginAsync()
    {
        if (IsLoginInProgress)
        {
            return;
        }

        IsLoginInProgress = true;
        IsLoginButtonVisible = true;
        LoginErrorMessage = string.Empty;

        LocalOAuthCallbackServer? callbackServer = null;
        try
        {
            AppLog.Info("LoginViewModel", "Discord login started.");
            try
            {
                callbackServer = LocalOAuthCallbackServer.Start(CallbackPort);
            }
            catch (LocalOAuthCallbackBindException bindEx)
            {
                // Loopback listener couldn't bind. The user can still finish
                // signing in by copying the code from the API success page —
                // open the paste UI immediately so they have a clear next step.
                AppLog.Error("LoginViewModel", $"Local listener bind failed. cause={bindEx.Cause}", bindEx);
                LoginErrorMessage = bindEx.Message;
                IsManualCodeMode = true;
                IsLoginButtonVisible = true;
                return;
            }

            var authUrl = _appStateService.GetAuthUrl();
            AppLog.Info("LoginViewModel", "Local callback server started; launching browser.");

            if (!BrowserLauncher.TryOpen(authUrl, out var browserError))
            {
                throw new InvalidOperationException($"Could not open browser: {browserError}");
            }

            var code = await callbackServer.WaitForCodeAsync(CallbackTimeout);
            AppLog.Info("LoginViewModel", $"OAuth callback code received. length={code.Length}.");

            await _appStateService.CompleteLoginAsync(code);
            AppLog.Info("LoginViewModel", $"Discord login completed with state {_appStateService.CurrentState}.");

            if (_appStateService.CurrentState == AppState.FatalError)
            {
                IsLoginButtonVisible = true;
            }

            if (_appStateService.CurrentState != AppState.Running &&
                !string.IsNullOrWhiteSpace(_appStateService.ErrorMessage))
            {
                LoginErrorMessage = _appStateService.ErrorMessage;
            }
        }
        catch (TimeoutException timeoutEx)
        {
            // Browser never delivered the redirect within the window. Surface
            // the paste UI so users with extension/firewall interference have a
            // recovery path instead of having to restart the whole OAuth flow.
            AppLog.Error("LoginViewModel", "Discord callback timed out.", timeoutEx);
            LoginErrorMessage = timeoutEx.Message;
            IsManualCodeMode = true;
            IsLoginButtonVisible = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("LoginViewModel", "Discord login failed.", ex);
            LoginErrorMessage = ex is LocalOAuthCallbackException callbackError
                ? callbackError.Message
                : $"Login failed. See log: {AppLog.LogPath}";
            IsLoginButtonVisible = true;
        }
        finally
        {
            callbackServer?.Dispose();
            IsLoginInProgress = false;
            _loginCommand.RaiseCanExecuteChanged();
        }
    }

    private Task ShowCredentialLoginAsync()
    {
        CredentialErrorMessage = string.Empty;
        LoginErrorMessage = string.Empty;
        IsCredentialMode = true;
        IsManualCodeMode = false;
        return Task.CompletedTask;
    }

    private Task ShowDiscordLoginAsync()
    {
        CredentialErrorMessage = string.Empty;
        LoginErrorMessage = string.Empty;
        IsCredentialMode = false;
        IsManualCodeMode = false;
        return Task.CompletedTask;
    }

    private Task ShowManualCodeAsync()
    {
        ManualCodeErrorMessage = string.Empty;
        IsManualCodeMode = true;
        return Task.CompletedTask;
    }

    private Task HideManualCodeAsync()
    {
        ManualCodeErrorMessage = string.Empty;
        ManualCode = string.Empty;
        IsManualCodeMode = false;
        return Task.CompletedTask;
    }

    private async Task ExecuteManualCodeLoginAsync()
    {
        if (!CanSubmitManualCode())
        {
            return;
        }

        var trimmed = ManualCode.Trim();

        IsManualCodeSubmitting = true;
        ManualCodeErrorMessage = string.Empty;
        LoginErrorMessage = string.Empty;

        try
        {
            AppLog.Info("LoginViewModel", $"Manual code login started. length={trimmed.Length}.");
            await _appStateService.CompleteLoginAsync(trimmed);
            AppLog.Info("LoginViewModel", $"Manual code login completed with state {_appStateService.CurrentState}.");

            if (_appStateService.CurrentState == AppState.Running)
            {
                ManualCode = string.Empty;
                IsManualCodeMode = false;
            }
            else if (!string.IsNullOrWhiteSpace(_appStateService.ErrorMessage))
            {
                ManualCodeErrorMessage = _appStateService.ErrorMessage;
            }
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("LoginViewModel", "Manual code login API failure.", ex);
            ManualCodeErrorMessage = MapManualCodeError(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("LoginViewModel", "Manual code login failed unexpectedly.", ex);
            ManualCodeErrorMessage = $"Could not finish sign-in. See log: {AppLog.LogPath}";
        }
        finally
        {
            IsManualCodeSubmitting = false;
        }
    }

    private static string MapManualCodeError(AuthApiException ex)
    {
        if (ex.IsTransient)
        {
            return "Could not reach the server. Check your connection and try again.";
        }

        // The exchange endpoint returns 400 "Invalid, used, or expired code" when
        // the code is wrong or has been redeemed already.
        if (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            return "That code didn't work. It may have already been used or expired (5-minute limit). Start the Discord sign-in again.";
        }

        return string.IsNullOrWhiteSpace(ex.Message)
            ? "Sign-in failed. Start the Discord sign-in again."
            : ex.Message;
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

    private async Task ExecuteCredentialLoginAsync()
    {
        if (IsCredentialLoginInProgress)
        {
            return;
        }

        if (!CanSubmitCredentials())
        {
            CredentialErrorMessage = "Enter username and password.";
            return;
        }

        IsCredentialLoginInProgress = true;
        CredentialErrorMessage = string.Empty;
        LoginErrorMessage = string.Empty;

        try
        {
            AppLog.Info("LoginViewModel", "Credential login started.");
            await _appStateService.CompleteCredentialsLoginAsync(Username.Trim(), Password);
            AppLog.Info("LoginViewModel", $"Credential login completed with state {_appStateService.CurrentState}.");

            if (_appStateService.CurrentState == AppState.Running)
            {
                Password = string.Empty;
            }
        }
        catch (AuthApiException ex)
        {
            AppLog.Error("LoginViewModel", "Credential login API failure.", ex);
            CredentialErrorMessage = CredentialLoginErrorMapper.Map(ex);
        }
        catch (Exception ex)
        {
            AppLog.Error("LoginViewModel", "Credential login failed unexpectedly.", ex);
            CredentialErrorMessage = $"Could not reach the server. See log: {AppLog.LogPath}";
        }
        finally
        {
            IsCredentialLoginInProgress = false;
        }
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
        AppLog.Info("LoginViewModel", $"Applying state {state}.");
        switch (state)
        {
            case AppState.Login:
                IsLoginButtonVisible = true;
                if (!string.IsNullOrWhiteSpace(_appStateService.ErrorMessage))
                {
                    LoginErrorMessage = _appStateService.ErrorMessage;
                }
                break;
            case AppState.Authenticating:
                IsLoginButtonVisible = IsLoginInProgress;
                break;
            case AppState.Running:
                IsLoginInProgress = false;
                IsCredentialLoginInProgress = false;
                IsLoginButtonVisible = false;
                LoginErrorMessage = string.Empty;
                Password = string.Empty;
                break;
            case AppState.Revoked:
                IsLoginInProgress = false;
                IsCredentialLoginInProgress = false;
                IsCredentialMode = false;
                IsLoginButtonVisible = true;
                break;
            case AppState.Unreachable:
                IsLoginInProgress = false;
                IsCredentialLoginInProgress = false;
                LoginErrorMessage = _appStateService.ErrorMessage ?? "Could not reach the server.";
                break;
            case AppState.FatalError:
                IsLoginInProgress = false;
                IsCredentialLoginInProgress = false;
                IsLoginButtonVisible = false;
                LoginErrorMessage = _appStateService.ErrorMessage ?? $"Fatal error. See log: {AppLog.LogPath}";
                break;
        }
    }

    private bool CanSubmitCredentials()
    {
        return !IsCredentialLoginInProgress &&
            IsLoginButtonVisible &&
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password);
    }

    private bool CanSubmitManualCode()
    {
        return IsManualCodeMode &&
            !IsManualCodeSubmitting &&
            !string.IsNullOrWhiteSpace(ManualCode) &&
            ManualCode.Trim().Length >= 8;
    }
}
