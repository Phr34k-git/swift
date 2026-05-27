using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Client.Services.Fishing;

internal sealed class WorldStatusReader : IDisposable
{
    private static readonly TimeSpan SurgeMaxDuration = TimeSpan.FromMinutes(20);
    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private readonly HashSet<string> _seenChatEntries = new(StringComparer.Ordinal);
    private bool _chatPrimed;
    private DateTimeOffset _lastDebugLogAt = DateTimeOffset.MinValue;
    private bool _shinySurgeActive;
    private bool _sparklingSurgeActive;
    private bool _mutationSurgeActive;
    private DateTimeOffset? _shinySurgeStartedAt;
    private DateTimeOffset? _sparklingSurgeStartedAt;
    private DateTimeOffset? _mutationSurgeStartedAt;

    public void Dispose()
    {
        _memory.Dispose();
    }

    public bool TryRead(out string weather, out string cycle, out bool shinySurge, out bool sparklingSurge, out bool mutationSurge)
    {
        weather = string.Empty;
        cycle = string.Empty;
        shinySurge = false;
        sparklingSurge = false;
        mutationSurge = false;

        try
        {
            _memory.EnsureAttached();

            var eventText = GetWorldStatusText("2_event");
            var weatherText = GetWorldStatusText("3_weather");
            var cycleText = GetWorldStatusText("4_cycle");
            var allStatuses = GetAllWorldStatusTexts();
            var allStatusText = string.Join(" ", allStatuses);
            var combined = $"{eventText} {weatherText} {allStatusText}";

            UpdateSurgesFromChat();
            shinySurge = _shinySurgeActive;
            sparklingSurge = _sparklingSurgeActive;
            mutationSurge = _mutationSurgeActive;

            weather = ResolveWeather(combined);
            cycle = ResolveCycle(cycleText, allStatusText);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateSurgesFromChat()
    {
        var now = DateTimeOffset.UtcNow;
        var entries = ReadChatEntries();
        if (!_chatPrimed)
        {
            foreach (var entry in entries)
            {
                _seenChatEntries.Add($"{entry.Address}:{Normalize(entry.Text)}");
            }
            _chatPrimed = true;
            return;
        }

        foreach (var entry in entries)
        {
            var entryKey = $"{entry.Address}:{Normalize(entry.Text)}";
            if (!_seenChatEntries.Add(entryKey))
            {
                continue;
            }

            var text = Normalize(entry.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (ContainsPhrase(text, "There is currently a Shiny surge"))
            {
                _shinySurgeActive = true;
                _sparklingSurgeActive = false;
                _mutationSurgeActive = false;
                _shinySurgeStartedAt = now;
                _sparklingSurgeStartedAt = null;
                _mutationSurgeStartedAt = null;
                AppLog.Info("WorldStatusReader", $"surge chat active: shiny | '{text}'");
            }
            else if (ContainsPhrase(text, "Shiny Surge is now over"))
            {
                _shinySurgeActive = false;
                _shinySurgeStartedAt = null;
                AppLog.Info("WorldStatusReader", $"surge chat gone: shiny | '{text}'");
            }

            if (ContainsPhrase(text, "Today is the Day of the Luminous") ||
                ContainsPhrase(text, "Tonight is the Night of the Luminous"))
            {
                _shinySurgeActive = false;
                _sparklingSurgeActive = true;
                _mutationSurgeActive = false;
                _shinySurgeStartedAt = null;
                _sparklingSurgeStartedAt = now;
                _mutationSurgeStartedAt = null;
                AppLog.Info("WorldStatusReader", $"surge chat active: sparkling | '{text}'");
            }
            else if (ContainsPhrase(text, "Night of the Luminous is now over"))
            {
                _sparklingSurgeActive = false;
                _sparklingSurgeStartedAt = null;
                AppLog.Info("WorldStatusReader", $"surge chat gone: sparkling | '{text}'");
            }

            if (ContainsPhrase(text, "There is currently a Mutation surge"))
            {
                _shinySurgeActive = false;
                _sparklingSurgeActive = false;
                _mutationSurgeActive = true;
                _shinySurgeStartedAt = null;
                _sparklingSurgeStartedAt = null;
                _mutationSurgeStartedAt = now;
                AppLog.Info("WorldStatusReader", $"surge chat active: mutation | '{text}'");
            }
            else if (ContainsPhrase(text, "Mutation Surge is now over"))
            {
                _mutationSurgeActive = false;
                _mutationSurgeStartedAt = null;
                AppLog.Info("WorldStatusReader", $"surge chat gone: mutation | '{text}'");
            }
        }

        if (_shinySurgeActive && _shinySurgeStartedAt is { } shinySince && now - shinySince >= SurgeMaxDuration)
        {
            _shinySurgeActive = false;
            _shinySurgeStartedAt = null;
        }

        if (_sparklingSurgeActive && _sparklingSurgeStartedAt is { } sparklingSince && now - sparklingSince >= SurgeMaxDuration)
        {
            _sparklingSurgeActive = false;
            _sparklingSurgeStartedAt = null;
        }

        if (_mutationSurgeActive && _mutationSurgeStartedAt is { } mutationSince && now - mutationSince >= SurgeMaxDuration)
        {
            _mutationSurgeActive = false;
            _mutationSurgeStartedAt = null;
        }

        if (now - _lastDebugLogAt >= TimeSpan.FromSeconds(2))
        {
            _lastDebugLogAt = now;
            AppLog.Info(
                "WorldStatusReader",
                $"surge state: shiny={_shinySurgeActive}, sparkling={_sparklingSurgeActive}, mutation={_mutationSurgeActive}, seen={_seenChatEntries.Count}");
        }
    }

    private List<ChatEntry> ReadChatEntries()
    {
        var results = new List<ChatEntry>();
        var dataModel = _memory.GetDataModel();
        if (dataModel == 0)
        {
            return results;
        }

        CollectTextChatServiceMessages(results, dataModel);
        CollectExperienceChatUiBodyText(results, dataModel);
        return results;
    }

    private void CollectTextChatServiceMessages(List<ChatEntry> results, ulong dataModel)
    {
        var textChatService = _memory.FindDescendantByClass(dataModel, "TextChatService");
        if (textChatService == 0)
        {
            return;
        }

        var stack = new Stack<ulong>();
        var seen = new HashSet<ulong>();
        stack.Push(textChatService);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var className = _memory.ReadClass(current);
            if (className.Contains("TextChatMessage", StringComparison.OrdinalIgnoreCase))
            {
                var text = ReadTextChatMessageText(current);
                if (text.Length > 0)
                {
                    results.Add(new ChatEntry(current, text));
                }
            }

            foreach (var child in _memory.ReadChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private void CollectExperienceChatUiBodyText(List<ChatEntry> results, ulong dataModel)
    {
        var coreGui = _memory.FindDescendantByClass(dataModel, "CoreGui");
        if (coreGui == 0)
        {
            return;
        }

        var experienceChat = _memory.FindDescendantByName(coreGui, "ExperienceChat");
        if (experienceChat == 0)
        {
            return;
        }

        var stack = new Stack<ulong>();
        var seen = new HashSet<ulong>();
        stack.Push(experienceChat);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var name = _memory.ReadName(current);
            var className = _memory.ReadClass(current);
            if (string.Equals(name, "BodyText", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(className, "TextLabel", StringComparison.OrdinalIgnoreCase))
            {
                var text = Normalize(_memory.ReadGuiText(current));
                if (text.Length > 0)
                {
                    results.Add(new ChatEntry(current, text));
                }
            }

            foreach (var child in _memory.ReadChildren(current))
            {
                stack.Push(child);
            }
        }
    }

    private string ReadTextChatMessageText(ulong address)
    {
        if (address == 0)
        {
            return string.Empty;
        }

        var text = Normalize(_memory.ReadGuiText(address));
        if (text.Length > 0)
        {
            return text;
        }

        try
        {
            var textOffset = _memory.GetOffset("Text");
            var indirect = Normalize(_memory.ReadString(_memory.ReadPtr(address + textOffset)));
            if (indirect.Length > 0)
            {
                return indirect;
            }
        }
        catch
        {
            // Best-effort chat parsing for status UI.
        }

        return string.Empty;
    }

    private string GetWorldStatusText(string statusName)
    {
        var status = ResolveWorldStatus(statusName);
        if (status == 0 || !_memory.IsVisible(status, "FrameVisible") || _memory.ReadGuiBounds(status, true) is null)
        {
            return string.Empty;
        }

        var label = _memory.FindChildByName(status, "label");
        if (label == 0)
        {
            return string.Empty;
        }

        return Normalize(_memory.ReadGuiText(label));
    }

    private ulong ResolveWorldStatus(string statusName)
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var hud = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "hud");
        var safezone = hud == 0 ? 0 : _memory.FindChildByName(hud, "safezone");
        var worldStatuses = safezone == 0 ? 0 : _memory.FindChildByName(safezone, "worldstatuses");
        return worldStatuses == 0 ? 0 : _memory.FindChildByName(worldStatuses, statusName);
    }

    private IReadOnlyList<string> GetAllWorldStatusTexts()
    {
        var results = new List<string>();
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return results;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var hud = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "hud");
        var safezone = hud == 0 ? 0 : _memory.FindChildByName(hud, "safezone");
        var worldStatuses = safezone == 0 ? 0 : _memory.FindChildByName(safezone, "worldstatuses");
        if (worldStatuses == 0)
        {
            return results;
        }

        foreach (var status in _memory.ReadChildren(worldStatuses))
        {
            if (!_memory.IsVisible(status, "FrameVisible") || _memory.ReadGuiBounds(status, true) is null)
            {
                continue;
            }

            var label = _memory.FindChildByName(status, "label");
            if (label == 0)
            {
                continue;
            }

            var text = Normalize(_memory.ReadGuiText(label));
            if (!string.IsNullOrWhiteSpace(text))
            {
                results.Add(text);
            }
        }

        return results;
    }

    private static string ResolveWeather(string combined)
    {
        if (Contains(combined, "aurora"))
        {
            return "Aurora Borealis";
        }

        if (Contains(combined, "starfall"))
        {
            return "Starfall";
        }

        if (Contains(combined, "eclipse"))
        {
            return "Eclipse";
        }

        if (Contains(combined, "rainbow"))
        {
            return "Rainbow";
        }

        if (Contains(combined, "rain"))
        {
            return "Rain";
        }

        if (Contains(combined, "wind"))
        {
            return "Windy";
        }

        if (Contains(combined, "fog"))
        {
            return "Foggy";
        }

        if (Contains(combined, "clear"))
        {
            return "Clear";
        }

        return string.Empty;
    }

    private static string ResolveCycle(string cycleText, string allStatusText)
    {
        if (ContainsWholeWord(cycleText, "night"))
        {
            return "Night";
        }

        if (ContainsWholeWord(cycleText, "day"))
        {
            return "Day";
        }

        var scrubbed = allStatusText.Replace("Night of the Luminous", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (ContainsWholeWord(scrubbed, "night"))
        {
            return "Night";
        }

        if (ContainsWholeWord(scrubbed, "day"))
        {
            return "Day";
        }

        return string.Empty;
    }

    private static bool Contains(string text, string needle)
    {
        return text.Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsWholeWord(string text, string word)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase);
    }

    private static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var pattern = $@"\b{string.Join(@"\s+", Array.ConvertAll(parts, Regex.Escape))}\b";
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
    }

    private static bool AnyStatusMatches(
        IReadOnlyList<string> statuses,
        string phrase,
        bool requireActiveSignal = false,
        bool requirePlusMarker = false)
    {
        foreach (var status in statuses)
        {
            if (!IsNamedStatus(status, phrase))
            {
                continue;
            }

            if (requirePlusMarker && !HasPlusMarkerForPhrase(status, phrase))
            {
                continue;
            }

            if (requireActiveSignal && !HasActiveStatusSignal(status))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool HasActiveStatusSignal(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        return Regex.IsMatch(normalized, @"\d", RegexOptions.CultureInvariant) ||
            normalized.Contains('%', StringComparison.Ordinal) ||
            normalized.Contains('+', StringComparison.Ordinal) ||
            ContainsPhrase(normalized, "increased") ||
            ContainsPhrase(normalized, "boost");
    }

    private static bool HasPlusMarkerForPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var parts = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        var phrasePattern = string.Join(@"\s+", Array.ConvertAll(parts, Regex.Escape));
        return Regex.IsMatch(
            text,
            $@"\+\s*{phrasePattern}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsNamedStatus(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var haystack = text.Trim().ToLowerInvariant();
        var needle = phrase.Trim().ToLowerInvariant();
        if (!haystack.StartsWith(needle, StringComparison.Ordinal))
        {
            return false;
        }

        if (haystack.Length == needle.Length)
        {
            return true;
        }

        var next = haystack[needle.Length];
        return next == ' ' || next == ':' || next == '-' || next == '(';
    }

    private static string Normalize(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim();
    }

    private readonly record struct ChatEntry(ulong Address, string Text);
}
