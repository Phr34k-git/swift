namespace Launcher;

internal static class ReleaseClient
{
    private const string BaseUrl = "https://openmacro.net/api/v1/swift/releases";
    private const int MaxManifestBytes = 512 * 1024;
    private const int MaxSigBytes = 256;
    private const long MaxPatchBytes = 200L * 1024 * 1024;
    private const long MaxFullZipBytes = 500L * 1024 * 1024;
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15)
    })
    {
        // Full.zip downloads can exceed the default 30s. Set a long ceiling so
        // a slow connection still completes rather than failing mid-download.
        Timeout = TimeSpan.FromMinutes(10),
        DefaultRequestHeaders = { { "User-Agent", "OpenMacro-Swift-Launcher/1.0" } }
    };

    internal static async Task<byte[]> GetManifestBytesAsync()
        => await GetBytesAsync($"{BaseUrl}/manifest.json", MaxManifestBytes);

    internal static async Task<string> GetSignatureAsync()
    {
        var bytes = await GetBytesAsync($"{BaseUrl}/manifest.sig", MaxSigBytes);
        return System.Text.Encoding.UTF8.GetString(bytes).Trim();
    }

    internal static async Task DownloadPatchAsync(string patchRelativePath, string destPath)
    {
        // patchRelativePath is like "patches/1.0.0-1.0.1.patch"
        var url = $"{BaseUrl}/{patchRelativePath}";
        var tmpPath = destPath + ".tmp";
        await DownloadFileAsync(url, tmpPath, MaxPatchBytes);
        File.Move(tmpPath, destPath, overwrite: true);
    }

    internal static async Task DownloadFullZipAsync(string version, string destPath)
    {
        var url = $"{BaseUrl}/{Uri.EscapeDataString(version)}/full.zip";
        var tmpPath = destPath + ".tmp";
        await DownloadFileAsync(url, tmpPath, MaxFullZipBytes);
        File.Move(tmpPath, destPath, overwrite: true);
    }

    private static async Task<byte[]> GetBytesAsync(string url, int maxBytes)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync();
                var buffer = new byte[maxBytes + 1];
                int total = 0, read;
                while ((read = await stream.ReadAsync(buffer.AsMemory(total))) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new InvalidOperationException($"Response exceeds {maxBytes} bytes");
                }
                return buffer[..total];
            }
            catch (Exception ex) when (attempt < 2)
            {
                Log.Warn($"GET {url} attempt {attempt + 1} failed: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
        throw new InvalidOperationException($"Failed to GET {url} after 3 attempts");
    }

    private static async Task DownloadFileAsync(string url, string destPath, long maxBytes)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                using var stream = await resp.Content.ReadAsStreamAsync();
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                using var file = new FileStream(
                    destPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
                long total = 0;
                var buf = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buf)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new InvalidOperationException($"Download exceeds {maxBytes} bytes");
                    await file.WriteAsync(buf.AsMemory(0, read));
                }
                return;
            }
            catch (Exception ex) when (attempt < 2)
            {
                Log.Warn($"Download {url} attempt {attempt + 1} failed: {ex.Message}");
                if (File.Exists(destPath)) File.Delete(destPath);
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
            }
        }
        throw new InvalidOperationException($"Failed to download {url} after 3 attempts");
    }
}
