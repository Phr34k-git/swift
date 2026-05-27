using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Client.Services;

/// <summary>
/// Shared API constants and JSON error parsing for OpenMacro HTTP clients.
/// </summary>
internal static class ApiHttp
{
    public const string BaseUrl = "https://openmacro.net";

    public static readonly HttpClient SharedClient = new()
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(15),
        DefaultRequestHeaders = { { "User-Agent", "OpenMacro-Swift/1.0" } }
    };

    public static async Task<(string? Reason, string? Detail)> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;

            var reason = TryReadString(root, "reason");
            var detail = TryReadString(root, "detail");

            if (root.TryGetProperty("detail", out var detailElement) &&
                detailElement.ValueKind == JsonValueKind.Object)
            {
                reason ??= TryReadString(detailElement, "reason");
                detail ??= TryReadString(detailElement, "message");
                detail ??= TryReadString(detailElement, "detail");
            }

            return (reason, detail);
        }
        catch
        {
            return (null, null);
        }
    }

    public static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }
}
