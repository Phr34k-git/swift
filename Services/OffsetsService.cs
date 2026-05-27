using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Client.Services.Fishing;

namespace Client.Services;

/// <summary>
/// Fetches the active Roblox offsets payload from the API and caches it in
/// memory for the lifetime of the process. Never touches disk: a cracked
/// client cannot harvest a copy off another machine, and a logout/lockout
/// drops the cache so revocation propagates immediately.
/// </summary>
public sealed class OffsetsService : IOffsetsRuntime
{
    // Bootstrap and heartbeat-driven refreshes both go through this schedule.
    // Picks up transient blips (DNS, brief 5xx) without dumping the user into
    // Unreachable on every cold start. Total worst-case added latency before
    // surfacing failure: ~1.75s.
    private static readonly IReadOnlyList<TimeSpan> DefaultRetryDelays = new[]
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
    };

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly object _sync = new();
    private IReadOnlyDictionary<string, ulong> _offsets = new Dictionary<string, ulong>(0);

    public OffsetsService(HttpClient? httpClient = null)
        : this(httpClient, DefaultRetryDelays)
    {
    }

    // Test-only seam: lets tests inject a zero-delay (or empty) schedule so
    // the existing single-shot specs don't have to enqueue N retry responses.
    internal OffsetsService(HttpClient? httpClient, IReadOnlyList<TimeSpan> retryDelays)
    {
        _httpClient = httpClient ?? ApiHttp.SharedClient;
        _retryDelays = retryDelays ?? Array.Empty<TimeSpan>();
    }

    public string? Version { get; private set; }

    public bool IsPopulated => _offsets.Count > 0;

    public bool TryGetOffset(string key, out ulong value)
    {
        var snapshot = _offsets;
        return snapshot.TryGetValue(key, out value);
    }

    /// <summary>
    /// GETs <c>/api/v1/swift/offsets</c> with the supplied access token and
    /// atomically replaces the in-memory dictionary on success. Transient
    /// failures (DNS, connection reset, 5xx, request timeout) are retried with
    /// an exponential-ish backoff before surfacing; non-transient auth/lockout
    /// responses are propagated immediately so the state machine can route
    /// them. Throws <see cref="AuthApiException"/> on auth failures so the
    /// caller can route it through the same lockout / unreachable state
    /// machine as <c>GetSessionStatusAsync</c>.
    /// </summary>
    public async Task RefreshAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        AppLog.Info("OffsetsService", "Refresh started.");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await RefreshOnceAsync(accessToken, cancellationToken);
                return;
            }
            catch (AuthApiException ex) when (ex.IsTransient && attempt < _retryDelays.Count)
            {
                var delay = _retryDelays[attempt];
                AppLog.Info(
                    "OffsetsService",
                    $"Transient refresh failure on attempt {attempt + 1}/{_retryDelays.Count + 1} ({ex.Message}); retrying in {delay.TotalMilliseconds:0}ms.");
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private async Task RefreshOnceAsync(string accessToken, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/swift/offsets");
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Error("OffsetsService", "Refresh timed out.", ex);
            throw AuthApiException.Transient("Offsets fetch timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            AppLog.Error("OffsetsService", "Refresh connection failed.", ex);
            throw AuthApiException.Transient("Could not connect to the API.", ex);
        }

        try
        {
            if (!response.IsSuccessStatusCode)
            {
                var (reason, detail) = await ApiHttp.ReadErrorAsync(response);
                var message = string.IsNullOrWhiteSpace(detail)
                    ? $"Offsets fetch failed with status code {(int)response.StatusCode} ({response.StatusCode})."
                    : detail;
                AppLog.Info("OffsetsService", $"Refresh failed. status={(int)response.StatusCode} reason={reason ?? "none"}");
                throw new AuthApiException(response.StatusCode, reason, message);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;

            if (!root.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Offsets payload missing version.");
            }

            var version = versionElement.GetString();
            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException("Offsets payload missing version.");
            }

            if (!root.TryGetProperty("offsets", out var offsetsElement) ||
                offsetsElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Offsets payload missing offsets object.");
            }

            var flat = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
            foreach (var ns in offsetsElement.EnumerateObject())
            {
                if (ns.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in ns.Value.EnumerateObject())
                {
                    if (field.Value.ValueKind != JsonValueKind.Number ||
                        !field.Value.TryGetUInt64(out var value))
                    {
                        continue;
                    }

                    flat[$"{ns.Name}.{field.Name}"] = value;
                    flat.TryAdd(field.Name, value);
                }
            }

            if (flat.Count == 0)
            {
                throw new InvalidDataException("Offsets payload contained no usable entries.");
            }

            lock (_sync)
            {
                _offsets = flat;
                Version = version;
            }

            AppLog.Info("OffsetsService", $"Loaded {flat.Count} offsets at version {version}.");
        }
        finally
        {
            response.Dispose();
        }
    }

    /// <summary>Drops the in-memory offsets. Call on logout / lockout / revocation.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _offsets = new Dictionary<string, ulong>(0);
            Version = null;
        }
        AppLog.Info("OffsetsService", "Cleared cached offsets.");
    }
}
