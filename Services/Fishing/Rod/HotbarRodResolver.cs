using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Client.Services.Fishing;

// Reads the equipped rod's hotbar display text using a caller-supplied
// RobloxMemory (the caller is responsible for EnsureAttached). Lets both
// HotbarRodReader and the fishing tracker resolve the rod without each
// owning a separate process attachment.
internal static class HotbarRodResolver
{
    public static string GetHotbarRodDisplayText(RobloxMemory memory)
    {
        var hotbar = GetHotbarGui(memory);
        if (hotbar == 0)
        {
            return string.Empty;
        }

        var slots = new List<ulong>();
        foreach (var slot in memory.ReadChildren(hotbar))
        {
            if (!string.Equals(memory.ReadClass(slot), "ImageButton", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(memory.ReadName(slot), "ItemTemplate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            slots.Add(slot);
        }

        if (slots.Count == 0)
        {
            return string.Empty;
        }

        var selectedIndex = Math.Clamp(HotbarSlotSettings.RodSlot, 1, 9) - 1;
        if (selectedIndex >= slots.Count)
        {
            selectedIndex = slots.Count - 1;
        }

        var selectedText = ReadSlotText(memory, slots[selectedIndex]);
        if (!string.IsNullOrWhiteSpace(selectedText))
        {
            return selectedText;
        }

        var fallback = slots
            .Select(slot => ReadSlotText(memory, slot))
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        return fallback ?? string.Empty;
    }

    public static string NormalizeRodDisplayText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, "[ \t]+", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, "\n+", "\n", RegexOptions.CultureInvariant);
        return normalized.Trim();
    }

    private static ulong GetHotbarGui(RobloxMemory memory)
    {
        var localPlayer = memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : memory.FindChildByName(playerGui, "backpack");
        return backpack == 0 ? 0 : memory.FindChildByName(backpack, "hotbar");
    }

    private static string ReadSlotText(RobloxMemory memory, ulong slot)
    {
        var nameInstance = memory.FindChildByName(slot, "ItemName");
        if (nameInstance == 0)
        {
            return string.Empty;
        }

        return NormalizeRodDisplayText(memory.ReadGuiText(nameInstance));
    }
}
