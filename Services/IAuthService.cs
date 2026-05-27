using System.Threading;
using System.Threading.Tasks;

namespace Client.Services;

public interface IAuthService
{
    string? LoadRefreshToken();
    void StoreRefreshToken(string token);
    void DeleteRefreshToken();
    void DeleteLegacyJwt();
    string GetAuthUrl();
    Task<AuthTokens> ExchangeCodeAsync(string code, string hwidHash);
    Task<AuthTokens> LoginAsync(string username, string password, string hwidHash);
    Task<AuthTokens> RefreshAsync(string refreshToken, string hwidHash);
    Task LogoutAsync(string refreshToken);

    /// <summary>
    /// Calls the read-only heartbeat endpoint. Returns the parsed response on 200; throws <see cref="AuthApiException"/> otherwise.
    /// Must not rotate refresh tokens or cause any DB writes.
    /// </summary>
    Task<SessionStatusInfo> GetSessionStatusAsync(string accessToken, CancellationToken cancellationToken = default);
}
