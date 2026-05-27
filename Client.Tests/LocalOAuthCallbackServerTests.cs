using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class LocalOAuthCallbackServerTests
{
    [Fact]
    public async Task WaitForCodeAsync_RendersMissingRolePageAndThrowsCallbackError()
    {
        var port = GetFreePort();
        using var server = LocalOAuthCallbackServer.Start(port);
        var waitTask = server.WaitForCodeAsync(TimeSpan.FromSeconds(5));

        var response = await SendRequestAsync(port, "/callback?error=missing_required_role");
        var ex = await Assert.ThrowsAsync<LocalOAuthCallbackException>(() => waitTask);

        Assert.Equal("missing_required_role", ex.ErrorCode);
        Assert.Contains("HTTP/1.1 400 Bad Request", response, StringComparison.Ordinal);
        Assert.Contains("Role Missing", response, StringComparison.Ordinal);
        Assert.Contains("OpenMacro", response, StringComparison.Ordinal);
        Assert.Contains("Back to Swift once your Discord role is active.", response, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid_state", "Session Drifted")]
    [InlineData("expired_state", "Took Too Long")]
    [InlineData("access_denied", "No Worries")]
    [InlineData("missing_code", "Not Signed In")]
    [InlineData("discord_exchange_failed", "Discord Stumbled")]
    [InlineData("discord_user_failed", "Couldn\u0027t Read Discord")]
    public async Task WaitForCodeAsync_RendersKnownErrorPages(string errorCode, string expectedTitle)
    {
        var port = GetFreePort();
        using var server = LocalOAuthCallbackServer.Start(port);
        var waitTask = server.WaitForCodeAsync(TimeSpan.FromSeconds(5));

        var response = await SendRequestAsync(port, $"/callback?error={errorCode}");
        var ex = await Assert.ThrowsAsync<LocalOAuthCallbackException>(() => waitTask);

        Assert.Equal(errorCode, ex.ErrorCode);
        Assert.Contains("HTTP/1.1 400 Bad Request", response, StringComparison.Ordinal);
        Assert.Contains(expectedTitle, response, StringComparison.Ordinal);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<string> SendRequestAsync(int port, string target)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var stream = client.GetStream();
        var request = Encoding.ASCII.GetBytes(
            $"GET {target} HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(request);
        await stream.FlushAsync();

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return Encoding.UTF8.GetString(memory.ToArray());
    }
}
