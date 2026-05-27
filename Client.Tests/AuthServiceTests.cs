using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Client.Services;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

public sealed class AuthServiceTests
{
    private const string TokenPairJson =
        """
        {
          "access_token": "access-token",
          "access_token_expires_at": "2035-01-01T00:00:00+00:00",
          "refresh_token": "refresh-token",
          "refresh_token_expires_at": "2035-01-01T00:00:00+00:00"
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public void ApiHttp_BaseUrl_UsesHttps()
    {
        Assert.StartsWith("https://", ApiHttp.BaseUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthService_UsesSwiftAuthPaths()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, TokenPairJson));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, TokenPairJson));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, TokenPairJson));
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var service = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("https://localhost") });

        Assert.EndsWith("/api/v1/swift/auth/discord", service.GetAuthUrl(), StringComparison.Ordinal);

        await service.ExchangeCodeAsync("code", new string('a', 64));
        await service.LoginAsync("user", "password", new string('a', 64));
        await service.RefreshAsync("refresh", new string('a', 64));
        await service.LogoutAsync("refresh");

        Assert.Equal(
            new[]
            {
                "/api/v1/swift/auth/exchange",
                "/api/v1/swift/auth/login",
                "/api/v1/swift/auth/refresh",
                "/api/v1/swift/auth/logout",
            },
            handler.RequestPaths);
    }
}
