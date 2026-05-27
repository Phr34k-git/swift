using System.Threading;
using System.Threading.Tasks;

namespace Client.Services.Fishing;

/// <summary>
/// Read-only view of the active offsets dictionary. Populated at runtime by
/// <see cref="Client.Services.OffsetsService"/> from the API. <see cref="RobloxMemory"/>
/// reads through this interface so the static <c>offsets.hpp</c> file no longer
/// needs to ship with the client.
/// </summary>
public interface IOffsetsSource
{
    string? Version { get; }
    bool IsPopulated { get; }
    bool TryGetOffset(string key, out ulong value);
}

/// <summary>
/// Lifecycle surface for the offsets cache used by <see cref="Client.Services.AppStateService"/>.
/// Separated from <see cref="IOffsetsSource"/> so tests can inject a no-op without subclassing
/// <see cref="Client.Services.OffsetsService"/>.
/// </summary>
public interface IOffsetsRuntime : IOffsetsSource
{
    Task RefreshAsync(string accessToken, CancellationToken cancellationToken = default);
    void Clear();
}
