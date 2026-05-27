using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Tests.Fakes;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<Task<HttpResponseMessage>>> _responses = new();

    public List<string> RequestPaths { get; } = new();

    public void Enqueue(HttpResponseMessage response) =>
        _responses.Enqueue(() => Task.FromResult(response));

    public void EnqueueException(Exception ex) =>
        _responses.Enqueue(() => Task.FromException<HttpResponseMessage>(ex));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestPaths.Add(request.RequestUri?.PathAndQuery ?? string.Empty);

        if (_responses.Count == 0)
            throw new InvalidOperationException("No HTTP response queued in FakeHttpMessageHandler.");

        return _responses.Dequeue()();
    }
}
