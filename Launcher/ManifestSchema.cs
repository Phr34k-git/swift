using System.Text.Json.Serialization;

namespace Launcher;

internal sealed record Manifest(
    string Version,
    string ReleasedAt,
    IReadOnlyList<ManifestFile> Files,
    IReadOnlyList<PatchEntry> History,
    FullZipEntry? FullZip);

internal sealed record ManifestFile(string Name, string Sha256, long Size);

internal sealed record PatchEntry(string From, string To, string File, string Sha256, long Size);

internal sealed record FullZipEntry(string File, string Sha256, long Size);

internal sealed record PendingUpdate(string Version, string NewExePath, string ManifestJson);

[JsonSerializable(typeof(Manifest))]
[JsonSerializable(typeof(ManifestFile))]
[JsonSerializable(typeof(PatchEntry))]
[JsonSerializable(typeof(FullZipEntry))]
[JsonSerializable(typeof(PendingUpdate))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = false,
    WriteIndented = false)]
internal partial class LauncherJsonContext : JsonSerializerContext { }
