using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Services;

internal static class ApiJson
{
    public static StringContent CreateContent<T>(T value, JsonTypeInfo<T> jsonTypeInfo)
    {
        var json = JsonSerializer.Serialize(value, jsonTypeInfo);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    public static async Task<T?> ReadAsync<T>(
        HttpContent content,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync(stream, jsonTypeInfo, cancellationToken);
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ExchangeCodeRequest))]
[JsonSerializable(typeof(CredentialsLoginRequest))]
[JsonSerializable(typeof(RefreshRequest))]
[JsonSerializable(typeof(LogoutRequest))]
[JsonSerializable(typeof(TokenPairResponse))]
[JsonSerializable(typeof(SessionStatusResponse))]
[JsonSerializable(typeof(UsernameUpdateBody))]
[JsonSerializable(typeof(PasswordUpdateBody))]
[JsonSerializable(typeof(AccountSnapshot))]
internal partial class ApiJsonContext : JsonSerializerContext
{
}

internal sealed record ExchangeCodeRequest(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("hwid_hash")] string HwidHash);

internal sealed record CredentialsLoginRequest(
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("hwid_hash")] string HwidHash);

internal sealed record RefreshRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("hwid_hash")] string HwidHash);

internal sealed record LogoutRequest(
    [property: JsonPropertyName("refresh_token")] string RefreshToken);

internal sealed record UsernameUpdateBody(
    [property: JsonPropertyName("username")] string Username);

internal sealed record PasswordUpdateBody(
    [property: JsonPropertyName("current_password")] string? CurrentPassword,
    [property: JsonPropertyName("new_password")] string NewPassword);

internal sealed class TokenPairResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("access_token_expires_at")]
    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }

    [JsonPropertyName("refresh_token_expires_at")]
    public DateTimeOffset RefreshTokenExpiresAt { get; init; }
}

internal sealed class SessionStatusResponse
{
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("offsets_version")]
    public string? OffsetsVersion { get; init; }
}
