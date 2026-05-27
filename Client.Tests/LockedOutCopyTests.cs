using Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class LockedOutCopyTests
{
    [Theory]
    [InlineData("revoked",       "Access Revoked",      "Your access has been revoked. Contact support if you believe this is a mistake.")]
    [InlineData("banned",        "Account Banned",      "You've been permanently banned from OpenMacro. Contact support if you believe this is a mistake.")]
    [InlineData("hwid_mismatch", "Device Not Recognized", "This device doesn't match the one on file for your account. Log in from your usual machine, or — if this is your usual machine — contact support to re-register it.")]
    [InlineData("chargeback",    "Nice Try, accountant", "Your access has been suspended due to a disputed payment. Reverse the chargeback to regain access.")]
    [InlineData("compromised",   "Account Compromised", "Your account was compromised, so we've locked it down. Contact support to recover your account.")]
    [InlineData("unlicensed",    "Unlicensed Access",   "Your account isn't linked to any purchases.")]
    public void Resolve_KnownReason_ReturnsCopy(string reason, string expectedTitle, string expectedSubtext)
    {
        var entry = LockedOutCopy.Resolve(reason);

        Assert.Equal(expectedTitle, entry.Title);
        Assert.Equal(expectedSubtext, entry.Subtext);
    }

    [Fact]
    public void Resolve_NullReason_FallsBackToRevoked()
    {
        var entry = LockedOutCopy.Resolve(null);
        var revoked = LockedOutCopy.Resolve("revoked");

        Assert.Equal(revoked.Title, entry.Title);
        Assert.Equal(revoked.Subtext, entry.Subtext);
    }

    [Fact]
    public void Resolve_UnknownReason_FallsBackToRevoked()
    {
        var entry = LockedOutCopy.Resolve("some_future_reason");
        var revoked = LockedOutCopy.Resolve("revoked");

        Assert.Equal(revoked.Title, entry.Title);
        Assert.Equal(revoked.Subtext, entry.Subtext);
    }

    [Fact]
    public void Resolve_RevokedExplicit_MatchesFallback()
    {
        var explicit_ = LockedOutCopy.Resolve("revoked");
        var fallback = LockedOutCopy.Resolve(null);

        Assert.Equal(explicit_.Title, fallback.Title);
        Assert.Equal(explicit_.Subtext, fallback.Subtext);
    }
}
