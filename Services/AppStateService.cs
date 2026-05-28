using System;
using System.Threading.Tasks;
using Client.Models;
using Client.Services.Fishing;

namespace Client.Services;

// No-security/offline app state: bypasses login/auth/heartbeat and keeps the app in Running.
public sealed class AppStateService : IDisposable
{
    private readonly IOffsetsRuntime _offsetsService;

    public AppStateService()
    {
        _offsetsService = new LocalOffsetsRuntime();
        OffsetsSourceProvider.Register(_offsetsService);
        CurrentState = AppState.Running;
        AccessToken = "offline";
    }

    public AppState CurrentState { get; private set; } = AppState.Running;
    public string? ErrorMessage { get; private set; }
    public string? AccessToken { get; private set; }
    public string? LockoutReason { get; private set; }

    public event Action<AppState>? StateChanged;
    public event Action<string?>? AccessTokenChanged;

    public void HandleApiAuthFailure(AuthApiException _)
    {
        // Intentionally ignored in no-security mode.
    }

    public string GetAuthUrl() => string.Empty;

    public Task InitializeAsync()
    {
        TransitionTo(AppState.Running);
        return Task.CompletedTask;
    }

    public Task CompleteLoginAsync(string code)
    {
        _ = code;
        TransitionTo(AppState.Running);
        return Task.CompletedTask;
    }

    public Task CompleteCredentialsLoginAsync(string username, string password)
    {
        _ = username;
        _ = password;
        TransitionTo(AppState.Running);
        return Task.CompletedTask;
    }

    public Task LogoutAsync()
    {
        // Keep macro available; no auth lifecycle in this copy.
        TransitionTo(AppState.Running);
        return Task.CompletedTask;
    }

    private void TransitionTo(AppState state, string? error = null)
    {
        CurrentState = state;
        ErrorMessage = error;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
    }
}
