using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace Client.Services;

/// <summary>
/// Loads Discord avatar bitmaps with the same per-session URL memoization used by the account view model.
/// </summary>
internal sealed class AvatarImageService
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string? _loadedAvatarUrl;
    private Bitmap? _loadedAvatar;
    private CancellationTokenSource? _loadCts;

    public string? LoadedAvatarUrl => _loadedAvatarUrl;

    public Bitmap? LoadedAvatar => _loadedAvatar;

    public void Cancel()
    {
        _loadCts?.Cancel();
    }

    public async Task<AvatarImageResult> LoadAsync(string? url)
    {
        if (string.Equals(url, _loadedAvatarUrl, StringComparison.Ordinal) && _loadedAvatar is not null)
        {
            return new AvatarImageResult(_loadedAvatarUrl, _loadedAvatar, false, false);
        }

        _loadCts?.Cancel();

        if (string.IsNullOrWhiteSpace(url))
        {
            _loadedAvatarUrl = null;
            _loadedAvatar = null;
            return new AvatarImageResult(null, null, false, false);
        }

        var cts = new CancellationTokenSource();
        _loadCts = cts;
        var token = cts.Token;
        var requestedUrl = url;

        try
        {
            var bytes = await _httpClient.GetByteArrayAsync(requestedUrl, token);
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);

            if (token.IsCancellationRequested)
            {
                bitmap.Dispose();
                return new AvatarImageResult(_loadedAvatarUrl, _loadedAvatar, true, true);
            }

            _loadedAvatarUrl = requestedUrl;
            _loadedAvatar = bitmap;
            return new AvatarImageResult(_loadedAvatarUrl, _loadedAvatar, false, false);
        }
        catch
        {
            if (token.IsCancellationRequested)
            {
                return new AvatarImageResult(_loadedAvatarUrl, _loadedAvatar, true, true);
            }

            _loadedAvatarUrl = null;
            _loadedAvatar = null;
            return new AvatarImageResult(null, null, false, true);
        }
    }
}

internal sealed record AvatarImageResult(
    string? LoadedAvatarUrl,
    Bitmap? AvatarImage,
    bool WasCanceled,
    bool Failed);
