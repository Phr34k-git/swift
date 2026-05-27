using System;

namespace Client.Services.Fishing;

/// <summary>
/// Process-wide registry for the active <see cref="IOffsetsSource"/>. Registered
/// once at startup (by composition root) and resolved by every callsite that
/// builds a <see cref="RobloxMemory"/>. Keeps the diff against the 11 existing
/// runners small without introducing a DI container.
/// </summary>
internal static class OffsetsSourceProvider
{
    private static IOffsetsSource? _current;

    public static void Register(IOffsetsSource source)
    {
        _current = source ?? throw new ArgumentNullException(nameof(source));
    }

    public static IOffsetsSource Current =>
        _current ?? throw new InvalidOperationException(
            "OffsetsSourceProvider has no registered source. " +
            "Ensure AppStateService is constructed before any RobloxMemory.");

    // Test-only reset hook. Internal so Client.Tests can call it.
    internal static void Reset() => _current = null;
}
