using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Client.Services;

public sealed class UserSettingsStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public UserSettingsStore()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = AppContext.BaseDirectory;
        }

        _path = Path.Combine(baseDir, "Swift", "settings.json");
    }

    public UserSettingsSnapshot Load()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return new UserSettingsSnapshot();
                }

                var json = File.ReadAllText(_path);
                var parsed = JsonSerializer.Deserialize(json, UserSettingsJsonContext.Default.UserSettingsSnapshot);
                return parsed ?? new UserSettingsSnapshot();
            }
            catch
            {
                return new UserSettingsSnapshot();
            }
        }
    }

    public void Save(UserSettingsSnapshot snapshot)
    {
        lock (_sync)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, UserSettingsJsonContext.Default.UserSettingsSnapshot);
            File.WriteAllText(_path, json);
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UserSettingsSnapshot))]
[JsonSerializable(typeof(FishingSettingsSnapshot))]
[JsonSerializable(typeof(AutoTotemSettingsSnapshot))]
[JsonSerializable(typeof(AutoSovereignSettingsSnapshot))]
[JsonSerializable(typeof(GeneralSettingsSnapshot))]
[JsonSerializable(typeof(CustomThemeSnapshot))]
internal partial class UserSettingsJsonContext : JsonSerializerContext
{
}

public sealed class UserSettingsSnapshot
{
    public int Version { get; set; } = 1;

    public FishingSettingsSnapshot Fishing { get; set; } = new();

    public AutoTotemSettingsSnapshot AutoTotem { get; set; } = new();

    public AutoSovereignSettingsSnapshot AutoSovereign { get; set; } = new();

    public HuntDetectSettingsSnapshot HuntDetect { get; set; } = new();

    public GeneralSettingsSnapshot General { get; set; } = new();

    public string? Theme { get; set; }

    public CustomThemeSnapshot? CustomTheme { get; set; }
}

public sealed class CustomThemeSnapshot
{
    public string? Background { get; set; }
    public string? Surface { get; set; }
    public string? Border { get; set; }
    public string? Accent { get; set; }
    public string? TextPrimary { get; set; }
}

public sealed class FishingSettingsSnapshot
{
    public string? TrackerMode { get; set; }

    public string? CastingMode { get; set; }

    public bool AutoAquariumEnabled { get; set; }

    public double AutoAquariumCycleDelayMinutes { get; set; } = 65;
}

public sealed class AutoTotemSettingsSnapshot
{
    public bool Enabled { get; set; }

    public string? TotemName { get; set; }

    public bool UseShinyTotem { get; set; }

    public bool UseSparklingTotem { get; set; }

    public bool UseMutationTotem { get; set; }

    public bool StayDay { get; set; }

    public bool StayNight { get; set; }
}

public sealed class AutoSovereignSettingsSnapshot
{
    public bool Enabled { get; set; }

    public double MinimumPercent { get; set; } = 95;

    public double MaximumPercent { get; set; } = 99;
}

public sealed class HuntDetectSettingsSnapshot
{
    public bool Enabled { get; set; }

    public bool UseHuntColors { get; set; }

    public string DiscordWebhook { get; set; } = string.Empty;

    public string[] SelectedTargets { get; set; } = Array.Empty<string>();
}

public sealed class GeneralSettingsSnapshot
{
    public int RodSlot { get; set; } = 1;

    public string? StartStopHotkey { get; set; }
}
