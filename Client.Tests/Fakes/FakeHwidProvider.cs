using Client.Services;

namespace Client.Tests.Fakes;

internal sealed class FakeHwidProvider : IHwidProvider
{
    public string GetHash() =>
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
}
