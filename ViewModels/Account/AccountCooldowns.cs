using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Client.Services;

namespace Client.ViewModels;

/// <summary>
/// Tracks account action cooldowns from rate-limit responses.
/// </summary>
internal sealed class AccountCooldowns
{
    private DateTimeOffset _loadUntil;
    private DateTimeOffset _usernameUntil;
    private DateTimeOffset _passwordUntil;

    public bool IsLoadActive => IsCooldownActive(_loadUntil);

    public bool IsUsernameActive => IsCooldownActive(_usernameUntil);

    public bool IsPasswordActive => IsCooldownActive(_passwordUntil);

    public void StartLoad(AccountApiException ex, RelayCommand command)
    {
        Start(ex, command, until => _loadUntil = until);
    }

    public void StartUsername(AccountApiException ex, RelayCommand command)
    {
        Start(ex, command, until => _usernameUntil = until);
    }

    public void StartPassword(AccountApiException ex, RelayCommand command)
    {
        Start(ex, command, until => _passwordUntil = until);
    }

    private static void Start(
        AccountApiException ex,
        RelayCommand command,
        Action<DateTimeOffset> setUntil)
    {
        if (TryGetCooldownUntil(ex, out var until))
        {
            setUntil(until);
            command.RaiseCanExecuteChanged();
            _ = RaiseCanExecuteWhenCooldownEndsAsync(until, command);
        }
    }

    private static bool IsCooldownActive(DateTimeOffset until)
    {
        return until > DateTimeOffset.UtcNow;
    }

    private static bool TryGetCooldownUntil(AccountApiException ex, out DateTimeOffset until)
    {
        if (ex.Kind == AccountApiErrorKind.RateLimited &&
            ex.RetryAfterSeconds is int seconds &&
            seconds > 0)
        {
            until = DateTimeOffset.UtcNow.AddSeconds(seconds);
            return true;
        }

        until = default;
        return false;
    }

    private static async Task RaiseCanExecuteWhenCooldownEndsAsync(
        DateTimeOffset until,
        RelayCommand command)
    {
        var delay = until - DateTimeOffset.UtcNow;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay);
        }

        Dispatcher.UIThread.Post(command.RaiseCanExecuteChanged);
    }
}
