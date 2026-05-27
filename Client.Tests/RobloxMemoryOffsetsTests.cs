using System;
using System.Collections.Generic;
using Client.Services.Fishing;
using Client.Tests.Fakes;
using Xunit;

namespace Client.Tests;

public sealed class RobloxMemoryOffsetsTests
{
    [Fact]
    public void GetOffset_ResolvesNamespacedKey()
    {
        var source = new FakeOffsetsSource(new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["FakeDataModel.Pointer"] = 0xDEAD,
        });
        using var memory = new RobloxMemory(source);

        Assert.Equal(0xDEADUL, memory.GetOffset("FakeDataModel.Pointer"));
    }

    [Fact]
    public void GetOffset_ResolvesAliasThroughSource()
    {
        var source = new FakeOffsetsSource(new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["FakeDataModel.Pointer"] = 0xBEEF,
        });
        using var memory = new RobloxMemory(source);

        Assert.Equal(0xBEEFUL, memory.GetOffset("FakeDataModelPointer"));
    }

    [Fact]
    public void GetOffset_ThrowsWhenKeyMissing()
    {
        var source = new FakeOffsetsSource(new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaskScheduler.Pointer"] = 1,
        });
        using var memory = new RobloxMemory(source);

        Assert.Throws<KeyNotFoundException>(() => memory.GetOffset("Does.Not.Exist"));
    }

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RobloxMemory(null!));
    }

    [Fact]
    public void EnsureAttached_EmptySource_ThrowsBeforeTouchingRoblox()
    {
        var source = new FakeOffsetsSource();
        using var memory = new RobloxMemory(source);

        var ex = Assert.Throws<InvalidOperationException>(() => memory.EnsureAttached());
        Assert.Contains("Offsets unavailable", ex.Message, StringComparison.Ordinal);
    }
}
