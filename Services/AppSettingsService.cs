using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Client;

namespace Client.Services;

public static class AppSettingsService
{
    public static string SettingsFilePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Swift", "settings.json");

    public static AppSettings Load() => Load(SettingsFilePath);

    internal static AppSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new AppSettings();
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is not JsonException)
        {
            return new AppSettings();
        }
        catch (JsonException ex)
        {
            AppLog.Error("AppSettingsService", $"Failed to parse settings file: {ex.Message}", ex);
            return new AppSettings();
        }
    }

    // Fire-and-forget; failures are logged
    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
            File.WriteAllText(SettingsFilePath, json);
        }
        catch (Exception ex)
        {
            try { AppLog.Error("AppSettingsService", "Failed to save settings", ex); } catch { }
        }
    }
}

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true,
    Converters = [typeof(JsonStringEnumConverter<AppTheme>)])]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsJsonContext : JsonSerializerContext
{
}
