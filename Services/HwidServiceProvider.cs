namespace Client.Services;

internal sealed class HwidServiceProvider : IHwidProvider
{
    private readonly HwidService _hwidService = new();

    public string GetHash() => _hwidService.GetCurrent().Hash;
}
