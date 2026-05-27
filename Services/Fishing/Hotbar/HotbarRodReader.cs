using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Client.Services.Fishing;

internal sealed class HotbarRodReader : IDisposable
{
    private static readonly string[] KnownRodNames =
    [
        "Abyssal Specter Rod",
        "Acidgrinder",
        "Adventurer's Rod",
        "Anchor n' Chain",
        "Antler Rod",
        "Apollo's Sunshot",
        "Arctic Rod",
        "Astraeus Serenade",
        "Auric Rod",
        "Aurora Rod",
        "Avalanche Rod",
        "Axe of Rhoads",
        "Bat Whisperer Rod",
        "Bellona's Waraxe",
        "Blade Of Glorp",
        "Bone Blade",
        "Brick Built Rod",
        "Brick Rod",
        "Bunnybloom Caster",
        "Candy Cane Rod",
        "Carbon Rod",
        "Carrot Rod",
        "Celestial Rod",
        "Cerulean Fang Rod",
        "Challenger's Rod",
        "Champions Rod",
        "Chrysalis",
        "Cinder Block Rod",
        "Cinderstring",
        "Cornucopia Rod",
        "Cryolash",
        "Crystalized Rod",
        "Cupid's Bow",
        "Daybreaker Rod",
        "Depthseeker Rod",
        "Destiny Rod",
        "Dreambreaker",
        "Dusekkar Rod",
        "Duskwire",
        "Eardrum Rod",
        "Egg Rod",
        "Eidolon Rod",
        "Elder Mossripper",
        "Ethereal Prism Rod",
        "Evil Pitchfork",
        "Fabulous Rod",
        "Fast Rod",
        "Firework Rod",
        "Fischer's Rod",
        "Fischmas Rod",
        "Fixer's Rod",
        "Flimsy Rod",
        "Fortune Rod",
        "Friendly Rod",
        "Frost Warden Rod",
        "Frostfire Rod",
        "Fungal Rod",
        "Gardenkeeper Rod",
        "Great Dreamer Rod",
        "Great Rod of Oscar",
        "Haunted Rod",
        "Heaven's Rod",
        "Ice Warpers Rod",
        "Jack-o-Blazer",
        "Katana Rod",
        "Kings Rod",
        "Kraken Rod",
        "Krampus's Rod",
        "Leprechaun Line",
        "Leviathan's Fang Rod",
        "Long Rod",
        "Lucid Rod",
        "Lucky Rod",
        "Luminescent Oath",
        "Maelstrom Rod",
        "Magma Rod",
        "Magnet Rod",
        "Merlin's Staff",
        "Midas Rod",
        "MiguRod",
        "Mission Specialist's Rod",
        "Mythical Rod",
        "Necrotic Rod",
        "Nico's Yarncaster",
        "Noctone",
        "Nocturnal Rod",
        "No-Life Rod",
        "North Pole",
        "North-Star Rod",
        "Onirifalx",
        "Paleontologist's Rod",
        "Paper Fan Rod",
        "Phoenix Rod",
        "Pinion's Aria",
        "Plastic Rod",
        "Polaris Serenade",
        "Popsicle Rod",
        "Poseidon Rod",
        "Precision Rod",
        "Rainbow Cluster Rod",
        "Random Rod",
        "Rapid Rod",
        "Reinforced Rod",
        "Relic Rod",
        "Resourceful Rod",
        "Riptide Rod",
        "Rod Of The Cosmos",
        "Rod Of The Depths",
        "Rod Of The Eternal King",
        "Rod Of The Exalted One",
        "Rod Of The Forgotten Fang",
        "Rod Of The Zenith",
        "Rod Of Time",
        "Rose Rend",
        "Ruinous Oath",
        "Scarlet Ravager",
        "Scarlet Spincaster Rod",
        "Scurvy Rod",
        "Seasons Rod",
        "Seraphic Rod",
        "Shamrock Rod",
        "Silly Fun Happy Rod",
        "Smurf Rod",
        "SOULREAPER",
        "Spiritbinder",
        "Spooky Rod",
        "Steady Rod",
        "Stone Rod",
        "Summit Rod",
        "Sunken Rod",
        "Superstar Rod",
        "Sweet-Stinger",
        "Tempest Rod",
        "Test Rod",
        "Thalassar's Ruin",
        "The Boom Ball",
        "The Lost Rod",
        "Tidal Wave Rod",
        "Tidemourner",
        "Toxic Spire Rod",
        "Toxinburst Rod",
        "Training Rod",
        "Tranquility Rod",
        "Trident Rod",
        "Tryhard Rod",
        "Vinefang Rod",
        "Vineweaver Rod",
        "Volcanic Rod",
        "Voyager Rod",
        "Wicked Fang Rod",
        "Wildflower Rod",
        "Wind Elemental",
        "Wingripper",
        "Wisdom Rod",
        "Zeus Rod",
    ];

    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private ulong _hotbarGui;
    private ulong _cachedWorkspace;
    private ulong _cachedLocalPlayer;
    private ulong _cachedCharacter;
    private string _cachedPlayerName = string.Empty;
    private long _nextCharacterRefreshAt;

    public HotbarRodSnapshot GetSnapshot()
    {
        var rodName = GetHotbarRodName();
        var equippedToolName = GetEquippedToolName();
        var isEquipped = IsRodMatch(rodName, equippedToolName);
        return new HotbarRodSnapshot(
            string.IsNullOrWhiteSpace(rodName) ? "---" : rodName,
            string.IsNullOrWhiteSpace(equippedToolName) ? "---" : equippedToolName,
            isEquipped);
    }

    public string GetHotbarRodName()
    {
        var displayText = GetHotbarRodDisplayText();
        var pureRodName = ExtractPureRodName(displayText);
        return string.IsNullOrWhiteSpace(pureRodName) ? displayText : pureRodName;
    }

    public string GetHotbarRodDisplayText()
    {
        _memory.EnsureAttached();
        return HotbarRodResolver.GetHotbarRodDisplayText(_memory);
    }

    public IReadOnlyList<string> GetMasterlineOverlayRodNames()
    {
        _memory.EnsureAttached();
        var runtime = new FishingRuntimeContext(_memory);
        return runtime.GetMasterlineOverlayRodNames();
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

    private string GetEquippedToolName()
    {
        _memory.EnsureAttached();
        var now = Environment.TickCount64;
        EnsureCharacterPointers(now);
        if (_cachedCharacter == 0)
        {
            return string.Empty;
        }

        foreach (var child in _memory.ReadChildren(_cachedCharacter))
        {
            if (string.Equals(_memory.ReadClass(child), "Tool", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizeRodDisplayText(_memory.ReadName(child));
            }
        }

        // Fast path had no tool; periodically refresh character path to avoid stale pointer.
        if (now >= _nextCharacterRefreshAt)
        {
            _cachedCharacter = 0;
            EnsureCharacterPointers(now);
            if (_cachedCharacter != 0)
            {
                foreach (var child in _memory.ReadChildren(_cachedCharacter))
                {
                    if (string.Equals(_memory.ReadClass(child), "Tool", StringComparison.OrdinalIgnoreCase))
                    {
                        return NormalizeRodDisplayText(_memory.ReadName(child));
                    }
                }
            }
        }

        return string.Empty;
    }

    private void EnsureCharacterPointers(long now)
    {
        var needsRefresh = _cachedCharacter == 0 || now >= _nextCharacterRefreshAt;
        if (!needsRefresh)
        {
            return;
        }

        _cachedWorkspace = _memory.FindWorkspace();
        _cachedLocalPlayer = _memory.GetLocalPlayer();
        if (_cachedWorkspace == 0 || _cachedLocalPlayer == 0)
        {
            _cachedCharacter = 0;
            _cachedPlayerName = string.Empty;
            _nextCharacterRefreshAt = now + 500;
            return;
        }

        _cachedPlayerName = _memory.ReadName(_cachedLocalPlayer);
        if (string.IsNullOrWhiteSpace(_cachedPlayerName))
        {
            _cachedCharacter = 0;
            _nextCharacterRefreshAt = now + 500;
            return;
        }

        _cachedCharacter = _memory.FindChildByName(_cachedWorkspace, _cachedPlayerName);
        _nextCharacterRefreshAt = now + (_cachedCharacter == 0 ? 500 : 2000);
    }
    private static string ExtractPureRodName(string text)
    {
        var cleanText = HotbarRodResolver.NormalizeRodDisplayText(text);
        if (string.IsNullOrWhiteSpace(cleanText))
        {
            return string.Empty;
        }

        foreach (var rodName in KnownRodNames)
        {
            if (cleanText.Contains(rodName, StringComparison.OrdinalIgnoreCase))
            {
                return rodName;
            }
        }

        foreach (var line in cleanText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (string.Equals(trimmed, "Pinion's Aria", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(trimmed, @"\brod\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return trimmed;
            }
        }

        return string.Empty;
    }

    private static bool IsRodMatch(string selectedRodName, string equippedToolName)
    {
        if (string.IsNullOrWhiteSpace(selectedRodName) || string.IsNullOrWhiteSpace(equippedToolName))
        {
            return false;
        }

        var selected = NormalizeLoose(selectedRodName);
        var equipped = NormalizeLoose(equippedToolName);
        if (selected.Length == 0 || equipped.Length == 0)
        {
            return false;
        }

        if (string.Equals(selected, equipped, StringComparison.OrdinalIgnoreCase) ||
            selected.Contains(equipped, StringComparison.OrdinalIgnoreCase) ||
            equipped.Contains(selected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var selectedPure = NormalizeLoose(ExtractPureRodName(selectedRodName));
        var equippedPure = NormalizeLoose(ExtractPureRodName(equippedToolName));
        if (selectedPure.Length == 0 || equippedPure.Length == 0)
        {
            return false;
        }

        return string.Equals(selectedPure, equippedPure, StringComparison.OrdinalIgnoreCase) ||
            selectedPure.Contains(equippedPure, StringComparison.OrdinalIgnoreCase) ||
            equippedPure.Contains(selectedPure, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPinionRodText(string text)
    {
        return NormalizeRodDisplayText(text).Contains("pinion", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRodDisplayText(string text)
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

    private static string NormalizeLoose(string text)
    {
        return NormalizeRodDisplayText(text).ToLowerInvariant();
    }

    private string ReadSlotText(ulong slot)
    {
        var nameInstance = _memory.FindChildByName(slot, "ItemName");
        if (nameInstance == 0)
        {
            return string.Empty;
        }

        return NormalizeRodDisplayText(_memory.ReadGuiText(nameInstance));
    }
}

internal sealed record HotbarRodSnapshot(string RodName, string EquippedToolName, bool IsEquipped);
