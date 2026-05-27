namespace Client.Models;

/// <summary>
/// Represents the high-level authentication and runtime state of the app.
/// </summary>
public enum AppState
{
    /// <summary>
    /// The app just launched and is checking local credentials and hardware identity.
    /// </summary>
    Initializing,

    /// <summary>
    /// A future update is available.
    /// </summary>
    UpdateAvailable,

    /// <summary>
    /// No valid refresh token is available and the user must sign in.
    /// </summary>
    Login,

    /// <summary>
    /// The app is exchanging or refreshing authentication tokens.
    /// </summary>
    Authenticating,

    /// <summary>
    /// The app has an active in-memory access token and refresh loop.
    /// </summary>
    Running,

    /// <summary>
    /// The server revoked or rejected the active credentials (backward-compat alias, maps to "revoked" copy).
    /// </summary>
    Revoked,

    /// <summary>
    /// The server rejected the session for a specific policy reason (banned, chargeback, compromised, etc.).
    /// The reason is surfaced through <see cref="Services.AppStateService.LockoutReason"/>.
    /// </summary>
    LockedOut,

    /// <summary>
    /// The server is temporarily unreachable.
    /// </summary>
    Unreachable,

    /// <summary>
    /// An unrecoverable error occurred.
    /// </summary>
    FatalError
}
