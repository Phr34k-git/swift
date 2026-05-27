using System;
using System.Collections.Generic;
using System.Diagnostics;
using Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class BrowserLauncherTests
{
    [Fact]
    public void TryOpenFallsBackWhenDefaultUrlHandlerFails()
    {
        var attempts = new List<string>();

        var opened = BrowserLauncher.TryOpen(
            "https://openmacro.net/api/v1/swift/auth/discord",
            startInfo =>
            {
                attempts.Add(startInfo.FileName);
                if (attempts.Count == 1)
                {
                    throw new InvalidOperationException("Application not found");
                }

                return null;
            },
            out var error);

        Assert.True(opened);
        Assert.Equal(
            ["https://openmacro.net/api/v1/swift/auth/discord", "rundll32.exe"],
            attempts);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void TryOpenReturnsErrorWhenAllLaunchAttemptsFail()
    {
        var opened = BrowserLauncher.TryOpen(
            "https://openmacro.net/api/v1/swift/auth/discord",
            _ => throw new InvalidOperationException("blocked"),
            out var error);

        Assert.False(opened);
        Assert.Contains("rundll32.exe", error);
        Assert.Contains("explorer.exe", error);
    }

    [Fact]
    public void BuildLaunchAttemptsUsesWindowsFallbackCommands()
    {
        var attempts = BrowserLauncher.BuildLaunchAttempts("https://example.com");

        Assert.Equal("https://example.com", attempts[0].FileName);
        Assert.True(attempts[0].UseShellExecute);
        Assert.Equal("rundll32.exe", attempts[1].FileName);
        Assert.Equal(["url.dll,FileProtocolHandler", "https://example.com"], attempts[1].ArgumentList);
        Assert.Equal("msedge.exe", attempts[2].FileName);
        Assert.Equal(["https://example.com"], attempts[2].ArgumentList);
        Assert.Equal("explorer.exe", attempts[3].FileName);
        Assert.Equal(["https://example.com"], attempts[3].ArgumentList);
    }
}
