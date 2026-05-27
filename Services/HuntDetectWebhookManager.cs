using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Client.ViewModels;

namespace Client.Services;

internal sealed class HuntDetectWebhookManager
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(12),
    };

    private static readonly IReadOnlySet<string> RareHunts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Olympian Devil",
        "Ancient Megalodon",
        "Phantom Megalodon",
        "Ancient Kraken",
        "Profane Leviathan",
        "Skeletal Leviathan",
        "Awakened Omnithal",
        "Ancient Goldwraith",
        "Colossal Ancient Dragon",
        "Colossal Blue Dragon",
        "Colossal Ethereal Dragon",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static int GetDiscordEmbedColor(string huntName)
    {
        return HuntDetectColors.GetDiscordEmbedColor(huntName);
    }

    public async Task SendDetectedAsync(
        string webhookUrl,
        string huntName,
        string fullMessage,
        DateTimeOffset detectedAt,
        string? serverInfo,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return;
        }

        var rare = RareHunts.Contains(huntName);
        var payload = BuildPayload(huntName, fullMessage, detectedAt, serverInfo, rare);
        var json = JsonSerializer.Serialize(payload, JsonOptions);

        await PostWithRetryAsync(webhookUrl, json, cancellationToken);
    }

    private static DiscordWebhookPayload BuildPayload(
        string huntName,
        string fullMessage,
        DateTimeOffset detectedAt,
        string? serverInfo,
        bool rare)
    {
        var iso = detectedAt.ToUniversalTime().ToString("O");
        var displayName = HuntDetectColors.GetDisplayName(huntName);
        var accentColor = GetDiscordEmbedColor(displayName);
        var roleMention = rare ? Environment.GetEnvironmentVariable("HUNT_DETECT_RARE_ROLE_MENTION") : null;
        var fields = new List<DiscordEmbedField>
        {
            new()
            {
                Name = "Full Message",
                Value = string.IsNullOrWhiteSpace(fullMessage) ? "Not available." : fullMessage,
                Inline = false
            },
        };

        if (!string.IsNullOrWhiteSpace(serverInfo))
        {
            fields.Add(new DiscordEmbedField
            {
                Name = "Server Info",
                Value = serverInfo,
                Inline = false
            });
        }

        return new DiscordWebhookPayload
        {
            Content = string.IsNullOrWhiteSpace(roleMention) ? null : roleMention,
            Embeds =
            [
                new DiscordEmbed
                {
                    Title = "Hunt Detected",
                    Description = displayName,
                    Color = accentColor,
                    Fields = fields,
                    Footer = new DiscordEmbedFooter
                    {
                        Text = "Fisch Hunt Detector"
                    },
                    Timestamp = iso
                }
            ]
        };
    }

    private static async Task PostWithRetryAsync(
        string webhookUrl,
        string json,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await HttpClient.PostAsync(webhookUrl, content, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if ((int)response.StatusCode is >= 400 and < 500 && (int)response.StatusCode != 429)
                {
                    AppLog.Info("HuntDetectWebhook", $"Webhook rejected permanently: {(int)response.StatusCode}.");
                    return;
                }

                last = new InvalidOperationException($"Webhook returned {(int)response.StatusCode}.");
            }
            catch (Exception ex)
            {
                last = ex;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt * attempt), cancellationToken);
        }

        if (last is not null)
        {
            AppLog.Error("HuntDetectWebhook", "Webhook send failed after retries.", last);
        }
    }
}

internal sealed class DiscordWebhookPayload
{
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("embeds")]
    public List<DiscordEmbed> Embeds { get; init; } = [];
}

internal sealed class DiscordEmbed
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("color")]
    public int Color { get; init; }

    [JsonPropertyName("fields")]
    public List<DiscordEmbedField> Fields { get; init; } = [];

    [JsonPropertyName("footer")]
    public DiscordEmbedFooter? Footer { get; init; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; init; }
}

internal sealed class DiscordEmbedField
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;

    [JsonPropertyName("inline")]
    public bool Inline { get; init; }
}

internal sealed class DiscordEmbedFooter
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}
