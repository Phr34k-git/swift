using System;
using System.Collections.Generic;
using Client.Services.Fishing;

namespace Client.Tests.Fakes;

internal sealed class FakeOffsetsSource : IOffsetsSource
{
    private readonly Dictionary<string, ulong> _offsets;

    public FakeOffsetsSource(IDictionary<string, ulong>? offsets = null, string? version = "fake-version")
    {
        _offsets = offsets is null
            ? new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ulong>(offsets, StringComparer.OrdinalIgnoreCase);
        Version = version;
    }

    public string? Version { get; set; }

    public bool IsPopulated => _offsets.Count > 0;

    public bool TryGetOffset(string key, out ulong value) =>
        _offsets.TryGetValue(key, out value);
}
