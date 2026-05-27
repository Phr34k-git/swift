using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class Tracking1HotbarTotemTests
{
    // A totem placed in the hotbar must be located by slot position so Auto
    // Totem can click its slot button instead of falling through to inventory.
    [Fact]
    public void ResolveHotbarSlotIndex_FindsTotemInFirstSlot()
    {
        var slots = new[] { "Windset Totem", "Fischer's Rod", "Bait" };

        Assert.Equal(0, Tracking1FishingTracker.ResolveHotbarSlotIndex(slots, "Windset Totem"));
    }

    [Fact]
    public void ResolveHotbarSlotIndex_ReturnsZeroBasedPosition()
    {
        var slots = new[] { "Fischer's Rod", "Bait", "Shiny Totem" };

        Assert.Equal(2, Tracking1FishingTracker.ResolveHotbarSlotIndex(slots, "Shiny Totem"));
    }

    // Rich-text markup and quantity suffixes on the slot label must not block
    // the match.
    [Fact]
    public void ResolveHotbarSlotIndex_IgnoresMarkupAndQuantity()
    {
        var slots = new[] { "<font color=\"#fff\">Tempest Totem</font> x4" };

        Assert.Equal(0, Tracking1FishingTracker.ResolveHotbarSlotIndex(slots, "Tempest Totem"));
    }

    // Preserves the inventory fallback: a totem absent from the hotbar yields -1
    // so TryUseHotbarItem reports "not in hotbar" and the inventory search runs.
    [Fact]
    public void ResolveHotbarSlotIndex_ReturnsMinusOneWhenAbsent()
    {
        var slots = new[] { "Fischer's Rod", "Bait" };

        Assert.Equal(-1, Tracking1FishingTracker.ResolveHotbarSlotIndex(slots, "Windset Totem"));
    }

    // Clicking the slot button works for any slot, so totems past the 9th are
    // still located (no artificial keybind-digit cap).
    [Fact]
    public void ResolveHotbarSlotIndex_FindsTotemBeyondNinthSlot()
    {
        var slots = new string[11];
        for (var i = 0; i < 10; i++)
        {
            slots[i] = $"Filler {i}";
        }

        slots[10] = "Aurora Totem";

        Assert.Equal(10, Tracking1FishingTracker.ResolveHotbarSlotIndex(slots, "Aurora Totem"));
    }
}
