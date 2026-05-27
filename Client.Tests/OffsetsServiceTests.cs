using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Client.Services;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

public sealed class OffsetsServiceTests
{
    private const string ValidPayload =
        """
        {
          "version": "v2026.05.18-9377ee10",
          "source": "https://imtheo.lol/Offsets",
          "offsets": {
            "TaskScheduler": { "Pointer": 130017672, "JobStart": 200 },
            "Instance":      { "Name": 24 }
          }
        }
        """;

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // Default helper: single-shot (no retry) so per-attempt specs don't need to
    // enqueue N copies of the same response.
    private static OffsetsService BuildService(FakeHttpMessageHandler handler) =>
        BuildService(handler, Array.Empty<TimeSpan>());

    private static OffsetsService BuildService(FakeHttpMessageHandler handler, IReadOnlyList<TimeSpan> retryDelays) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://localhost") }, retryDelays);

    [Fact]
    public async Task RefreshAsync_PopulatesOffsetsAndVersion()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, ValidPayload));
        var svc = BuildService(handler);

        await svc.RefreshAsync("access-token");

        Assert.True(svc.IsPopulated);
        Assert.Equal("v2026.05.18-9377ee10", svc.Version);
        Assert.True(svc.TryGetOffset("TaskScheduler.Pointer", out var pointer));
        Assert.Equal(130017672UL, pointer);
        Assert.True(svc.TryGetOffset("TaskScheduler.JobStart", out var jobStart));
        Assert.Equal(200UL, jobStart);
        // Bare field name is also resolvable (alias support for RobloxMemory).
        Assert.True(svc.TryGetOffset("Name", out var name));
        Assert.Equal(24UL, name);
    }

    [Fact]
    public async Task RefreshAsync_SendsBearerToOffsetsEndpoint()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, ValidPayload));
        var svc = BuildService(handler);

        await svc.RefreshAsync("access-token");

        Assert.Single(handler.RequestPaths);
        Assert.Equal("/api/v1/swift/offsets", handler.RequestPaths[0]);
    }

    [Fact]
    public void GetOffset_ThrowsWhenNotPopulated()
    {
        var svc = new OffsetsService(new HttpClient { BaseAddress = new Uri("https://x") });

        Assert.False(svc.IsPopulated);
        Assert.Null(svc.Version);
        Assert.False(svc.TryGetOffset("Anything", out _));
    }

    [Fact]
    public async Task RefreshAsync_PropagatesAuthApiExceptionOn401()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Unauthorized,
            "{\"reason\":\"expired\",\"detail\":\"Invalid or expired access token\"}"));
        var svc = BuildService(handler);

        var ex = await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Equal("expired", ex.Reason);
        Assert.False(svc.IsPopulated);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesLockoutReasonOn403()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Forbidden,
            "{\"reason\":\"unlicensed\",\"detail\":\"Purchase required\"}"));
        var svc = BuildService(handler);

        var ex = await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.Equal("unlicensed", ex.Reason);
        Assert.False(ex.IsTransient);
    }

    [Fact]
    public async Task RefreshAsync_PropagatesOffsetsUnavailableAs404()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.NotFound,
            "{\"reason\":\"offsets_unavailable\",\"detail\":\"No offsets published\"}"));
        var svc = BuildService(handler);

        var ex = await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.Equal("offsets_unavailable", ex.Reason);
    }

    [Fact]
    public async Task RefreshAsync_ConnectionFailure_ThrowsTransient()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        var svc = BuildService(handler);

        var ex = await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task RefreshAsync_EmptyOffsets_ThrowsInvalidData()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"version\":\"v1\",\"offsets\":{}}"));
        var svc = BuildService(handler);

        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => svc.RefreshAsync("token"));
        Assert.False(svc.IsPopulated);
    }

    [Fact]
    public async Task RefreshAsync_MissingVersion_ThrowsInvalidData()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.OK,
            "{\"offsets\":{\"X\":{\"Y\":1}}}"));
        var svc = BuildService(handler);

        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => svc.RefreshAsync("token"));
    }

    [Fact]
    public async Task RefreshAsync_RetriesTransientFailureUntilSuccess()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, ValidPayload));
        var svc = BuildService(handler, new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero });

        await svc.RefreshAsync("token");

        Assert.True(svc.IsPopulated);
        Assert.Equal(3, handler.RequestPaths.Count);
    }

    [Fact]
    public async Task RefreshAsync_GivesUpAfterExhaustingRetries()
    {
        var handler = new FakeHttpMessageHandler();
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        handler.EnqueueException(new HttpRequestException("Connection refused."));
        var svc = BuildService(handler, new[] { TimeSpan.Zero, TimeSpan.Zero });

        var ex = await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.True(ex.IsTransient);
        Assert.Equal(3, handler.RequestPaths.Count);
        Assert.False(svc.IsPopulated);
    }

    [Fact]
    public async Task RefreshAsync_DoesNotRetryNonTransientFailures()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(
            HttpStatusCode.Unauthorized,
            "{\"reason\":\"expired\",\"detail\":\"Invalid or expired access token\"}"));
        // Retry schedule is generous; non-transient should not consume any of it.
        var svc = BuildService(handler, new[] { TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero });

        await Assert.ThrowsAsync<AuthApiException>(() => svc.RefreshAsync("token"));

        Assert.Single(handler.RequestPaths);
    }

    [Fact]
    public async Task RefreshAsync_RetriesOn5xx()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.InternalServerError, "{}"));
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, ValidPayload));
        var svc = BuildService(handler, new[] { TimeSpan.Zero });

        await svc.RefreshAsync("token");

        Assert.True(svc.IsPopulated);
        Assert.Equal(2, handler.RequestPaths.Count);
    }

    [Fact]
    public async Task Clear_DropsCachedOffsets()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Enqueue(JsonResponse(HttpStatusCode.OK, ValidPayload));
        var svc = BuildService(handler);
        await svc.RefreshAsync("token");
        Assert.True(svc.IsPopulated);

        svc.Clear();

        Assert.False(svc.IsPopulated);
        Assert.Null(svc.Version);
        Assert.False(svc.TryGetOffset("TaskScheduler.Pointer", out _));
    }
}
