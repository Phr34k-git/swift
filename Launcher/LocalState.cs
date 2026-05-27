using System.Diagnostics;
using System.Text.Json;

namespace Launcher;

internal static class LocalState
{
    private static string PendingUpdatePath(string installDir) =>
        Path.Combine(installDir, "pending-update.json");

    private static string CurrentManifestPath(string installDir) =>
        Path.Combine(installDir, "current.manifest.json");

    internal static string GetLocalVersion(string swiftExePath)
    {
        var info = FileVersionInfo.GetVersionInfo(swiftExePath);
        // FileVersion is "1.0.1.0"; strip the trailing ".0" to get "1.0.1"
        var fv = info.FileVersion ?? "0.0.0.0";
        var parts = fv.Split('.');
        return parts.Length >= 3 ? $"{parts[0]}.{parts[1]}.{parts[2]}" : fv;
    }

    internal static PendingUpdate? ReadPendingUpdate(string installDir)
    {
        var path = PendingUpdatePath(installDir);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, LauncherJsonContext.Default.PendingUpdate);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read pending-update.json: {ex.Message}");
            return null;
        }
    }

    internal static void WritePendingUpdate(string installDir, PendingUpdate update)
    {
        var json = JsonSerializer.Serialize(update, LauncherJsonContext.Default.PendingUpdate);
        File.WriteAllText(PendingUpdatePath(installDir), json);
    }

    internal static void DeletePendingUpdate(string installDir)
    {
        var path = PendingUpdatePath(installDir);
        if (File.Exists(path)) File.Delete(path);
    }

    internal static void WriteCurrentManifest(string installDir, string manifestJson)
    {
        File.WriteAllText(CurrentManifestPath(installDir), manifestJson);
    }

    internal static Manifest? ReadCurrentManifest(string installDir)
    {
        var path = CurrentManifestPath(installDir);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, LauncherJsonContext.Default.Manifest);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not read current.manifest.json: {ex.Message}");
            return null;
        }
    }
}
