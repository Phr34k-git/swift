using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Client.Services.Fishing;

internal sealed class HotbarTotemReader : IDisposable
{
    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private ulong _hotbarGui;

    public IReadOnlyList<string> GetTotems()
    {
        _memory.EnsureAttached();
        var hotbar = GetHotbarGui();
        if (hotbar == 0)
        {
            return Array.Empty<string>();
        }

        var totems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in _memory.ReadChildren(hotbar))
        {
            if (!string.Equals(_memory.ReadClass(slot), "ImageButton", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(_memory.ReadName(slot), "ItemTemplate", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = ReadSlotText(slot);
            if (string.IsNullOrWhiteSpace(text) ||
                !text.Contains("Totem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(text))
            {
                totems.Add(text);
            }
        }

        return totems.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Dispose()
    {
        _memory.Dispose();
    }

    private ulong GetHotbarGui()
    {
        if (_hotbarGui != 0 && string.Equals(_memory.ReadName(_hotbarGui), "hotbar", StringComparison.OrdinalIgnoreCase))
        {
            return _hotbarGui;
        }

        _hotbarGui = 0;
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "backpack");
        _hotbarGui = backpack == 0 ? 0 : _memory.FindChildByName(backpack, "hotbar");
        return _hotbarGui;
    }

    private string ReadSlotText(ulong slot)
    {
        var nameInstance = _memory.FindChildByName(slot, "ItemName");
        if (nameInstance == 0)
        {
            return string.Empty;
        }

        return Normalize(_memory.ReadGuiText(nameInstance));
    }

    private static string Normalize(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }

}

