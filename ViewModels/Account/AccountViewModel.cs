using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// View model for the Account tab. Backed by AccountApiClient with
/// stale-while-refreshing semantics and centralized auth-failure handling.
/// </summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private const string UsernameSaveText = "Save";
    private const string UsernameSavedText = "Saved!";

    private readonly AccountApiClient _accountApi;
    private readonly AppStateService _appState;
    private readonly AvatarImageService _avatarImageService = new();
    private readonly AccountCooldowns _cooldowns = new();
    private readonly RelayCommand _saveUsernameCommand;
    private readonly RelayCommand _savePasswordCommand;
    private readonly RelayCommand _retryLoadCommand;

    private AccountSnapshot? _snapshot;

    private string _username = string.Empty;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;
    private string _usernameErrorMessage = string.Empty;
    private string _usernameSaveButtonText = UsernameSaveText;
    private string _passwordErrorMessage = string.Empty;
    private string _loadErrorMessage = string.Empty;
    private Bitmap? _avatarImage;
    private bool _isPasswordEditorVisible;
    private bool _isSavingUsername;
    private bool _isSavingPassword;
    private bool _isLoading;
    private bool _isAvatarLoading;
    private bool _isAttached;
    private CancellationTokenSource _lifetimeCts = new();

    /// <summary>
    /// Creates the account view model.
    /// </summary>
    public AccountViewModel(AccountApiClient accountApi, AppStateService appState)
    {
        _accountApi = accountApi;
        _appState = appState;

        _saveUsernameCommand = new RelayCommand(_ => SaveUsernameAsync(), _ => CanSaveUsername());
        _savePasswordCommand = new RelayCommand(_ => SavePasswordAsync(), _ => CanSavePassword());
        _retryLoadCommand = new RelayCommand(_ => LoadAsync(), _ => CanRetryLoad());

        ShowPasswordEditorCommand = new RelayCommand(_ => ShowPasswordEditorAsync());
        CancelPasswordEditorCommand = new RelayCommand(_ => CancelPasswordEditorAsync());
    }

    /// <summary>
    /// Gets the Discord display name shown in the identity row.
    /// </summary>
    public string DisplayName =>
        _snapshot?.DiscordDisplayName
        ?? _snapshot?.DiscordUsername
        ?? string.Empty;

    /// <summary>
    /// Gets the username line shown in the identity section.
    /// </summary>
    public string DisplayUsername
    {
        get
        {
            if (_snapshot is null)
            {
                return string.Empty;
            }
            return string.IsNullOrWhiteSpace(_snapshot.Username)
                ? "Username not set"
                : $"@{_snapshot.Username}";
        }
    }

    /// <summary>
    /// Gets the avatar URL from the latest snapshot.
    /// </summary>
    public string? AvatarUrl => _snapshot?.DiscordAvatarUrl;

    /// <summary>
    /// Gets the loaded avatar bitmap, when available.
    /// </summary>
    public Bitmap? AvatarImage
    {
        get => _avatarImage;
        private set
        {
            if (SetProperty(ref _avatarImage, value))
            {
                OnPropertyChanged(nameof(HasAvatar));
            }
        }
    }

    /// <summary>
    /// Gets whether the avatar bitmap has been loaded.
    /// </summary>
    public bool HasAvatar => _avatarImage is not null;

    /// <summary>
    /// Gets the two-letter avatar fallback initials.
    /// </summary>
    public string AvatarInitials
    {
        get
        {
            var source = DisplayName;
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            var parts = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }
            var first = parts[0];
            return first.Length >= 2
                ? first.Substring(0, 2).ToUpperInvariant()
                : first.ToUpperInvariant();
        }
    }

    /// <summary>
    /// Gets whether a snapshot has been loaded at least once.
    /// </summary>
    public bool HasSnapshot => _snapshot is not null;

    /// <summary>
    /// Gets whether the loaded profile content should be visible.
    /// </summary>
    public bool IsProfileContentVisible => HasSnapshot && !ShowProfileSkeleton;

    /// <summary>
    /// Gets whether the first-load skeleton should be visible.
    /// </summary>
    public bool ShowProfileSkeleton =>
        (IsLoading && _snapshot is null) ||
        (_snapshot is not null && _isAvatarLoading && AvatarImage is null);

    /// <summary>
    /// Gets or sets the username input value.
    /// </summary>
    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value) && !IsSavingUsername)
            {
                UsernameSaveButtonText = UsernameSaveText;
                UsernameErrorMessage = string.Empty;
                _saveUsernameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the current password input value.
    /// </summary>
    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (SetProperty(ref _currentPassword, value))
            {
                _savePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the new password input value.
    /// </summary>
    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (SetProperty(ref _newPassword, value))
            {
                _savePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets or sets the confirmation password input value.
    /// </summary>
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (SetProperty(ref _confirmPassword, value))
            {
                _savePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether a password is set on the server.
    /// </summary>
    public bool HasPassword => _snapshot?.HasPassword ?? false;

    /// <summary>
    /// Gets whether the inline password editor is visible.
    /// </summary>
    public bool IsPasswordEditorVisible
    {
        get => _isPasswordEditorVisible;
        private set
        {
            if (!SetProperty(ref _isPasswordEditorVisible, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsCurrentPasswordVisible));
            OnPropertyChanged(nameof(IsSetPasswordEditorVisible));
            OnPropertyChanged(nameof(IsChangePasswordEditorVisible));
        }
    }

    /// <summary>
    /// Gets whether the username save action is in progress.
    /// </summary>
    public bool IsSavingUsername
    {
        get => _isSavingUsername;
        private set
        {
            if (SetProperty(ref _isSavingUsername, value))
            {
                OnPropertyChanged(nameof(IsUsernameSaveTextVisible));
                _saveUsernameCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether the username save text should be visible.
    /// </summary>
    public bool IsUsernameSaveTextVisible => !IsSavingUsername;

    /// <summary>
    /// Gets whether the password save action is in progress.
    /// </summary>
    public bool IsSavingPassword
    {
        get => _isSavingPassword;
        private set
        {
            if (SetProperty(ref _isSavingPassword, value))
            {
                OnPropertyChanged(nameof(IsPasswordSaveTextVisible));
                _savePasswordCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets whether the password save text should be visible.
    /// </summary>
    public bool IsPasswordSaveTextVisible => !IsSavingPassword;

    /// <summary>
    /// Gets whether an account fetch is in flight.
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(ShowProfileSkeleton));
                OnPropertyChanged(nameof(IsProfileContentVisible));
                _retryLoadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// Gets the load error message displayed at the top of the page.
    /// </summary>
    public string LoadErrorMessage
    {
        get => _loadErrorMessage;
        private set
        {
            if (SetProperty(ref _loadErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsLoadErrorVisible));
            }
        }
    }

    /// <summary>
    /// Gets whether the top-of-page load error should be shown.
    /// </summary>
    public bool IsLoadErrorVisible => !string.IsNullOrWhiteSpace(LoadErrorMessage);

    /// <summary>
    /// Gets the current password state text.
    /// </summary>
    public string PasswordStateText => HasPassword ? "Password is set" : "Password not set";

    /// <summary>
    /// Gets the button text that opens the password editor.
    /// </summary>
    public string PasswordEditorButtonText => HasPassword ? "Change password" : "Set password";

    /// <summary>
    /// Gets the button text that saves the password editor.
    /// </summary>
    public string PasswordSubmitButtonText => HasPassword ? "Change password" : "Save password";

    /// <summary>
    /// Gets whether the current password field should be visible.
    /// </summary>
    public bool IsCurrentPasswordVisible => HasPassword && IsPasswordEditorVisible;

    /// <summary>
    /// Gets whether the set-password editor should be visible.
    /// </summary>
    public bool IsSetPasswordEditorVisible => IsPasswordEditorVisible && !HasPassword;

    /// <summary>
    /// Gets whether the change-password editor should be visible.
    /// </summary>
    public bool IsChangePasswordEditorVisible => IsPasswordEditorVisible && HasPassword;

    /// <summary>
    /// Gets the confirm password label for the current mode.
    /// </summary>
    public string ConfirmPasswordLabel => HasPassword ? "Confirm new password" : "Confirm password";

    /// <summary>
    /// Gets the username save button text.
    /// </summary>
    public string UsernameSaveButtonText
    {
        get => _usernameSaveButtonText;
        private set => SetProperty(ref _usernameSaveButtonText, value);
    }

    /// <summary>
    /// Gets the username validation message.
    /// </summary>
    public string UsernameErrorMessage
    {
        get => _usernameErrorMessage;
        private set
        {
            if (SetProperty(ref _usernameErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsUsernameErrorVisible));
            }
        }
    }

    /// <summary>
    /// Gets the password validation message.
    /// </summary>
    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set
        {
            if (SetProperty(ref _passwordErrorMessage, value))
            {
                OnPropertyChanged(nameof(IsPasswordErrorVisible));
            }
        }
    }

    /// <summary>
    /// Gets whether the username error message should be shown.
    /// </summary>
    public bool IsUsernameErrorVisible => !string.IsNullOrWhiteSpace(UsernameErrorMessage);

    /// <summary>
    /// Gets whether the password error message should be shown.
    /// </summary>
    public bool IsPasswordErrorVisible => !string.IsNullOrWhiteSpace(PasswordErrorMessage);

    /// <summary>
    /// Gets the command that saves the username.
    /// </summary>
    public ICommand SaveUsernameCommand => _saveUsernameCommand;

    /// <summary>
    /// Gets the command that shows the inline password editor.
    /// </summary>
    public ICommand ShowPasswordEditorCommand { get; }

    /// <summary>
    /// Gets the command that hides the inline password editor.
    /// </summary>
    public ICommand CancelPasswordEditorCommand { get; }

    /// <summary>
    /// Gets the command that saves the password.
    /// </summary>
    public ICommand SavePasswordCommand => _savePasswordCommand;

    /// <summary>
    /// Gets the command that retries an account fetch after a load error.
    /// </summary>
    public ICommand RetryLoadCommand => _retryLoadCommand;

    /// <summary>
    /// Loads the account snapshot. Reuses the existing snapshot for stale-while-refreshing.
    /// </summary>
    public async Task LoadAsync()
    {
        HydrateUsernameFromSnapshot(onlyWhenEmpty: true);

        if (IsLoading || _cooldowns.IsLoadActive)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var snapshot = await _accountApi.GetAccountAsync();
            ApplySnapshot(snapshot);
            LoadErrorMessage = string.Empty;
        }
        catch (AccountApiException ex)
        {
            HandleLoadError(ex);
        }
        catch (Exception)
        {
            LoadErrorMessage = "Could not load your account.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Attaches state subscriptions when the view is loaded.
    /// </summary>
    public void Attach()
    {
        if (!_isAttached)
        {
            _appState.AccessTokenChanged += OnAccessTokenChanged;
            _isAttached = true;
        }

        HydrateUsernameFromSnapshot(onlyWhenEmpty: true);
    }

    /// <summary>
    /// Detaches state subscriptions when the view is unloaded.
    /// </summary>
    public void Detach()
    {
        if (_isAttached)
        {
            _appState.AccessTokenChanged -= OnAccessTokenChanged;
            _isAttached = false;
        }

        _avatarImageService.Cancel();
        _lifetimeCts.Cancel();
        _lifetimeCts.Dispose();
        _lifetimeCts = new();
    }

    private void OnAccessTokenChanged(string? token)
    {
        if (token is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (IsLoading)
            {
                return;
            }
            _ = LoadAsync();
        });
    }

    private bool CanSaveUsername()
    {
        return _snapshot is not null &&
            !IsSavingUsername &&
            !_cooldowns.IsUsernameActive &&
            !IsUsernameSameAsSnapshot(Username.Trim());
    }

    private bool CanSavePassword()
    {
        return _snapshot is not null &&
            !IsSavingPassword &&
            !_cooldowns.IsPasswordActive;
    }

    private bool CanRetryLoad()
    {
        return !IsLoading && !_cooldowns.IsLoadActive;
    }

    private async Task SaveUsernameAsync()
    {
        if (IsSavingUsername || _snapshot is null)
        {
            return;
        }

        var normalizedUsername = Username.Trim();
        UsernameSaveButtonText = UsernameSaveText;

        if (IsUsernameSameAsSnapshot(normalizedUsername))
        {
            Username = _snapshot.Username ?? string.Empty;
            UsernameErrorMessage = string.Empty;
            return;
        }

        if (!UsernamePattern().IsMatch(normalizedUsername))
        {
            UsernameErrorMessage = "Use 3-24 letters, numbers, or underscores.";
            return;
        }

        IsSavingUsername = true;
        try
        {
            var snapshot = await _accountApi.UpdateUsernameAsync(normalizedUsername);
            ApplySnapshot(snapshot);
            Username = snapshot.Username ?? normalizedUsername;
            UsernameErrorMessage = string.Empty;
            UsernameSaveButtonText = UsernameSavedText;
        }
        catch (AccountApiException ex)
        {
            StartUsernameCooldown(ex);
            UsernameErrorMessage = MapUsernameError(ex);
            return;
        }
        catch (Exception)
        {
            UsernameErrorMessage = "Could not reach the server.";
            return;
        }
        finally
        {
            IsSavingUsername = false;
        }

        try
        {
            await Task.Delay(1400, _lifetimeCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (string.Equals(UsernameSaveButtonText, UsernameSavedText, StringComparison.Ordinal) &&
            string.Equals(Username, normalizedUsername, StringComparison.Ordinal))
        {
            UsernameSaveButtonText = UsernameSaveText;
        }
    }

    private Task ShowPasswordEditorAsync()
    {
        PasswordErrorMessage = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        IsPasswordEditorVisible = true;
        return Task.CompletedTask;
    }

    private Task CancelPasswordEditorAsync()
    {
        PasswordErrorMessage = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        IsPasswordEditorVisible = false;
        return Task.CompletedTask;
    }

    private async Task SavePasswordAsync()
    {
        if (IsSavingPassword || _snapshot is null)
        {
            return;
        }

        if (HasPassword && string.IsNullOrWhiteSpace(CurrentPassword))
        {
            PasswordErrorMessage = "Enter your current password.";
            return;
        }

        if (NewPassword.Length < 8)
        {
            PasswordErrorMessage = "Password must be at least 8 characters.";
            return;
        }

        if (!string.Equals(NewPassword, ConfirmPassword, StringComparison.Ordinal))
        {
            PasswordErrorMessage = "Passwords do not match.";
            return;
        }

        IsSavingPassword = true;
        try
        {
            var currentForRequest = HasPassword ? CurrentPassword : null;
            var snapshot = await _accountApi.UpdatePasswordAsync(currentForRequest, NewPassword);
            ApplySnapshot(snapshot);
            IsPasswordEditorVisible = false;
            CurrentPassword = string.Empty;
            NewPassword = string.Empty;
            ConfirmPassword = string.Empty;
            PasswordErrorMessage = string.Empty;
        }
        catch (AccountApiException ex)
        {
            StartPasswordCooldown(ex);
            PasswordErrorMessage = MapPasswordError(ex);
        }
        catch (Exception)
        {
            PasswordErrorMessage = "Could not reach the server.";
        }
        finally
        {
            IsSavingPassword = false;
        }
    }

    private void ApplySnapshot(AccountSnapshot snapshot)
    {
        var currentSnapshotUsername = _snapshot?.Username ?? string.Empty;
        var shouldHydrateUsername = _snapshot is null ||
            string.IsNullOrWhiteSpace(Username) ||
            string.Equals(Username, currentSnapshotUsername, StringComparison.Ordinal);
        var shouldWaitForAvatar =
            !string.IsNullOrWhiteSpace(snapshot.DiscordAvatarUrl) &&
            !string.Equals(snapshot.DiscordAvatarUrl, _avatarImageService.LoadedAvatarUrl, StringComparison.Ordinal) &&
            AvatarImage is null;

        _snapshot = snapshot;
        IsAvatarLoading = shouldWaitForAvatar;
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayUsername));
        OnPropertyChanged(nameof(AvatarUrl));
        OnPropertyChanged(nameof(AvatarInitials));
        OnPropertyChanged(nameof(HasSnapshot));
        OnPropertyChanged(nameof(ShowProfileSkeleton));
        OnPropertyChanged(nameof(IsProfileContentVisible));
        OnPropertyChanged(nameof(HasPassword));
        OnPropertyChanged(nameof(PasswordStateText));
        OnPropertyChanged(nameof(PasswordEditorButtonText));
        OnPropertyChanged(nameof(PasswordSubmitButtonText));
        OnPropertyChanged(nameof(IsCurrentPasswordVisible));
        OnPropertyChanged(nameof(IsSetPasswordEditorVisible));
        OnPropertyChanged(nameof(IsChangePasswordEditorVisible));
        OnPropertyChanged(nameof(ConfirmPasswordLabel));
        _saveUsernameCommand.RaiseCanExecuteChanged();
        _savePasswordCommand.RaiseCanExecuteChanged();

        if (shouldHydrateUsername)
        {
            Username = snapshot.Username ?? string.Empty;
        }

        _ = RefreshAvatarAsync(snapshot.DiscordAvatarUrl);
    }

    private async Task RefreshAvatarAsync(string? url)
    {
        if (string.Equals(url, _avatarImageService.LoadedAvatarUrl, StringComparison.Ordinal) && AvatarImage is not null)
        {
            IsAvatarLoading = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(url) && AvatarImage is null)
        {
            IsAvatarLoading = true;
        }

        var result = await _avatarImageService.LoadAsync(url);

        if (result.WasCanceled)
        {
            return;
        }

        AvatarImage = result.AvatarImage;
        IsAvatarLoading = false;
    }

    private bool IsAvatarLoading
    {
        get => _isAvatarLoading;
        set
        {
            if (SetProperty(ref _isAvatarLoading, value))
            {
                OnPropertyChanged(nameof(ShowProfileSkeleton));
                OnPropertyChanged(nameof(IsProfileContentVisible));
            }
        }
    }

    private void HandleLoadError(AccountApiException ex)
    {
        switch (ex.Kind)
        {
            case AccountApiErrorKind.AuthFailureHandled:
            case AccountApiErrorKind.Unauthenticated:
                LoadErrorMessage = string.Empty;
                return;
            case AccountApiErrorKind.RateLimited:
                _cooldowns.StartLoad(ex, _retryLoadCommand);
                LoadErrorMessage = "Too many requests. Try again shortly.";
                return;
            default:
                LoadErrorMessage = "Could not load your account.";
                return;
        }
    }

    private static string MapUsernameError(AccountApiException ex)
    {
        return ex.Kind switch
        {
            AccountApiErrorKind.AuthFailureHandled => string.Empty,
            AccountApiErrorKind.Unauthenticated => string.Empty,
            AccountApiErrorKind.Conflict => "That username is already taken.",
            AccountApiErrorKind.ValidationError => "Use 3-24 letters, numbers, or underscores.",
            AccountApiErrorKind.InvalidRequest => ex.Message,
            AccountApiErrorKind.RateLimited => "Too many changes. Try again shortly.",
            _ => "Could not reach the server.",
        };
    }

    private static string MapPasswordError(AccountApiException ex)
    {
        return ex.Kind switch
        {
            AccountApiErrorKind.AuthFailureHandled => string.Empty,
            AccountApiErrorKind.Unauthenticated => string.Empty,
            AccountApiErrorKind.InvalidRequest => ex.Message,
            AccountApiErrorKind.ValidationError => "Password must be 8-128 characters.",
            AccountApiErrorKind.RateLimited => "Too many changes. Try again shortly.",
            _ => "Could not reach the server.",
        };
    }

    private bool IsUsernameSameAsSnapshot(string value)
    {
        return string.Equals(value, _snapshot?.Username ?? string.Empty, StringComparison.Ordinal);
    }

    private void HydrateUsernameFromSnapshot(bool onlyWhenEmpty)
    {
        if (_snapshot?.Username is not string snapshotUsername)
        {
            return;
        }

        if (onlyWhenEmpty && !string.IsNullOrWhiteSpace(Username))
        {
            return;
        }

        Username = snapshotUsername;
        UsernameErrorMessage = string.Empty;
        UsernameSaveButtonText = UsernameSaveText;
    }

    private void StartUsernameCooldown(AccountApiException ex)
    {
        _cooldowns.StartUsername(ex, _saveUsernameCommand);
    }

    private void StartPasswordCooldown(AccountApiException ex)
    {
        _cooldowns.StartPassword(ex, _savePasswordCommand);
    }

    [GeneratedRegex("^[A-Za-z0-9_]{3,24}$")]
    private static partial Regex UsernamePattern();
}
