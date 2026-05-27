using System;
using System.Net;
using Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class AuthApiExceptionTests
{
    // All cases below use null reason — matches the existing behaviour except for 403.
    // 403 + null reason is now transient (proxy-level block with no API annotation).
    [Theory]
    [InlineData(null,  true)]
    [InlineData(429,   true)]
    [InlineData(500,   true)]
    [InlineData(502,   true)]
    [InlineData(503,   true)]
    [InlineData(401,   false)]
    [InlineData(403,   true)]   // null reason → Cloudflare-style block → transient
    [InlineData(400,   false)]
    [InlineData(409,   false)]
    [InlineData(422,   false)]
    public void IsTransient_NullReason_ReturnsExpected(int? statusCode, bool expected)
    {
        HttpStatusCode? code = statusCode.HasValue ? (HttpStatusCode)statusCode.Value : null;
        var ex = new AuthApiException(code, null, "test");
        Assert.Equal(expected, ex.IsTransient);
    }

    // A 403 that carries a reason string is an API decision (banned, revoked, etc.)
    // and must remain non-transient regardless of the reason value.
    [Theory]
    [InlineData("banned")]
    [InlineData("revoked")]
    [InlineData("hwid_mismatch")]
    [InlineData("some_future_reason")]
    public void IsTransient_403WithReason_IsAlwaysFalse(string reason)
    {
        var ex = new AuthApiException(HttpStatusCode.Forbidden, reason, "test");
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public void Transient_Factory_SetsNullStatusAndPreservesInner()
    {
        var inner = new Exception("inner");
        var ex = AuthApiException.Transient("msg", inner);

        Assert.Null(ex.StatusCode);
        Assert.True(ex.IsTransient);
        Assert.Same(inner, ex.InnerException);
        Assert.Equal("msg", ex.Message);
    }
}
