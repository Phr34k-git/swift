using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Client.Services.Fishing;

namespace Client.Tests.Fakes;

/// <summary>
/// Minimal in-memory <see cref="IOffsetsRuntime"/> for tests. Tracks call counts
/// and lets tests choose whether <see cref="RefreshAsync"/> populates, throws,
/// or no-ops.
/// </summary>
internal sealed class FakeOffsetsRuntime : IOffsetsRuntime
{
    private readonly Dictionary<string, ulong> _offsets = new(StringComparer.OrdinalIgnoreCase);

    public string? Version { get; set; }

    public int RefreshCallCount { get; private set; }
    public int ClearCallCount { get; private set; }

    public Exception? ThrowOnRefresh { get; set; }
    public string? VersionAfterRefresh { get; set; } = "fake-version";
    public Dictionary<string, ulong> OffsetsAfterRefresh { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Sentinel"] = 1,
    };

    public bool IsPopulated => _offsets.Count > 0;

    public bool TryGetOffset(string key, out ulong value) => _offsets.TryGetValue(key, out value);

    public Task RefreshAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        RefreshCallCount++;
        if (ThrowOnRefresh is { } ex)
        {
            return Task.FromException(ex);
        }

        _offsets.Clear();
        foreach (var (k, v) in OffsetsAfterRefresh)
        {
            _offsets[k] = v;
        }
        Version = VersionAfterRefresh;
        return Task.CompletedTask;
    }

    public void Clear()
    {
        ClearCallCount++;
        _offsets.Clear();
        Version = null;
    }
}
