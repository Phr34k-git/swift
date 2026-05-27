using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Client.Services;

namespace Client.Services.Fishing;

internal sealed record AutoAnglerSettings(int ClickX, int ClickY);

internal sealed record AutoAnglerStepResult(bool Failed, string Status, string CurrentFish);
internal sealed record AutoAnglerQuestDiagnostic(string Fish, string Summary);

internal sealed class AutoAnglerRunner : IDisposable
{
    private const int CompletingDelayPaddingMs = 200;
    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private string _state = "START";
    private DateTimeOffset _nextStepAt = DateTimeOffset.MinValue;
    private string _currentFish = "None";
    private ulong _searchFrame;
    private ulong _itemContainer;
    private ulong _inventoryFrame;
    private string _lastLoggedFish = "None";

    public void Reset()
    {
        _state = "START";
        _nextStepAt = DateTimeOffset.MinValue;
        _currentFish = "None";
        _searchFrame = 0;
        _itemContainer = 0;
        _inventoryFrame = 0;
    }

    public AutoAnglerStepResult Step(AutoAnglerSettings settings)
    {
        if (settings.ClickX <= 0 || settings.ClickY <= 0)
        {
            return new AutoAnglerStepResult(true, "Set a click point before starting Auto Angler.", "None");
        }

        _memory.EnsureAttached();
        var liveFish = ReadCurrentQuestFish();
        _currentFish = string.Equals(liveFish, "None", StringComparison.OrdinalIgnoreCase) ? "None" : liveFish;
        var now = DateTimeOffset.UtcNow;
        if (now < _nextStepAt)
        {
            if (string.Equals(_state, "START", StringComparison.Ordinal))
            {
                var remaining = (int)Math.Ceiling((_nextStepAt - now).TotalSeconds);
                if (remaining < 0)
                {
                    remaining = 0;
                }

                return new AutoAnglerStepResult(false, $"COUNTDOWN:{remaining}", _currentFish);
            }

            return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);
        }

        switch (_state)
        {
            case "START":
                NativeKeyboard.PressE(_memory.WindowHandle);
                _state = "CLICK_POINT_A";
                _nextStepAt = now.AddSeconds(2).AddMilliseconds(CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "CLICK_POINT_A":
                NativeMouse.ClickAt(settings.ClickX, settings.ClickY);
                _state = "READ_FISH";
                _nextStepAt = now.AddSeconds(1).AddMilliseconds(CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "READ_FISH":
                _currentFish = liveFish;
                NativeKeyboard.PressG(_memory.WindowHandle);
                _state = "SEARCH_FISH";
                _nextStepAt = now.AddMilliseconds(200 + CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "SEARCH_FISH":
                if (!EnsureInventoryTargets())
                {
                    return new AutoAnglerStepResult(true, "Inventory search targets not found.", _currentFish);
                }

                ClickGuiCenter(_searchFrame);
                Thread.Sleep(100);
                NativeKeyboard.PressCtrlA(_memory.WindowHandle);
                NativeKeyboard.PressBackspace(_memory.WindowHandle);
                if (!string.Equals(_currentFish, "None", StringComparison.OrdinalIgnoreCase))
                {
                    NativeKeyboard.TypeText(_currentFish, _memory.WindowHandle);
                }

                _state = "CLICK_SEARCH_RESULT";
                _nextStepAt = now.AddMilliseconds(500 + CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "CLICK_SEARCH_RESULT":
                if (!string.Equals(_currentFish, "None", StringComparison.OrdinalIgnoreCase))
                {
                    var target = FindInventoryItemTarget(_itemContainer, _currentFish);
                    if (target == 0)
                    {
                        return new AutoAnglerStepResult(true, $"Could not find '{_currentFish}' in inventory list.", _currentFish);
                    }

                    ClickGuiCenter(target);
                }

                NativeKeyboard.PressG(_memory.WindowHandle);
                _state = "PRESS_E_AGAIN";
                _nextStepAt = now.AddMilliseconds(200 + CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "PRESS_E_AGAIN":
                NativeKeyboard.PressE(_memory.WindowHandle);
                _state = "CLICK_POINT_B";
                _nextStepAt = now.AddSeconds(1).AddMilliseconds(CompletingDelayPaddingMs);
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);

            case "CLICK_POINT_B":
                NativeMouse.ClickAt(settings.ClickX, settings.ClickY);
                _state = "START";
                _nextStepAt = now.AddSeconds(124);
                return new AutoAnglerStepResult(false, "COUNTDOWN:124", _currentFish);

            default:
                Reset();
                return new AutoAnglerStepResult(false, "COMPLETING", _currentFish);
        }
    }

    public string ReadCurrentQuestFish()
    {
        _memory.EnsureAttached();
        var quest = ResolveAnglerQuestFolder();
        if (quest == 0)
        {
            return "None";
        }

        // Preferred schema: quest contains BoolValue children where the active fish is false.
        var boolChildren = new List<ulong>();
        var tracking = true;
        foreach (var child in _memory.ReadChildren(quest))
        {
            if (string.Equals(_memory.ReadClass(child), "BoolValue", StringComparison.OrdinalIgnoreCase))
            {
                boolChildren.Add(child);
                var name = _memory.ReadName(child).Trim();
                if (string.Equals(name, "Tracking", StringComparison.OrdinalIgnoreCase))
                {
                    tracking = ReadBoolValue(child);
                }
            }
        }

        // Quest inactive: do not keep stale fish display.
        if (!tracking)
        {
            return "None";
        }

        if (boolChildren.Count > 0)
        {
            foreach (var child in boolChildren)
            {
                var name = _memory.ReadName(child).Trim();
                if (IsMetaQuestBoolName(name))
                {
                    continue;
                }

                if (!ReadBoolValue(child))
                {
                    return ResolveFishName(child);
                }
            }
        }

        var questValueText = NormalizeQuestValue(TryReadValueText(quest));
        if (questValueText.Length > 0 &&
            !string.Equals(questValueText, "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(questValueText, "false", StringComparison.OrdinalIgnoreCase))
        {
            return questValueText;
        }

        foreach (var child in _memory.ReadChildren(quest))
        {
            var cls = _memory.ReadClass(child);
            if (!cls.Contains("Value", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ReadBoolValue(child))
            {
                var fishName = _memory.ReadName(child).Trim();
                return fishName.Length == 0 ? "None" : fishName;
            }

            // Some builds expose the selected fish as value text on child nodes.
            var childValue = NormalizeQuestValue(TryReadValueText(child));
            if (childValue.Length > 0 &&
                !string.Equals(childValue, "false", StringComparison.OrdinalIgnoreCase))
            {
                return childValue;
            }
        }

        return "None";
    }

    public AutoAnglerQuestDiagnostic ReadCurrentQuestFishDiagnostic()
    {
        _memory.EnsureAttached();
        var quest = ResolveAnglerQuestFolder();
        if (quest == 0)
        {
            return new AutoAnglerQuestDiagnostic("None", "quest=not-found");
        }

        var fish = ReadCurrentQuestFish();
        var entries = new List<string>();
        foreach (var child in _memory.ReadChildren(quest))
        {
            if (!string.Equals(_memory.ReadClass(child), "BoolValue", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = _memory.ReadName(child).Trim();
            var value = ReadBoolValue(child);
            entries.Add($"{name}:{(value ? "true" : "false")}");
        }

        var summary = $"quest=0x{quest:X} bools=[{string.Join(", ", entries)}]";
        AppLog.Info("AutoAnglerDiag", $"{summary} -> fish={fish}");
        return new AutoAnglerQuestDiagnostic(fish, summary);
    }

    public void Dispose()
    {
        _memory.Dispose();
    }

    private bool EnsureInventoryTargets()
    {
        _inventoryFrame = ResolveInventoryFrame();
        if (_inventoryFrame == 0)
        {
            return false;
        }

        _searchFrame = ResolveInventorySearchFrame(_inventoryFrame);
        _itemContainer = ResolveInventoryItemContainer(_inventoryFrame);
        return _searchFrame != 0 && _itemContainer != 0;
    }

    private ulong ResolveAnglerQuestFolder()
    {
        var direct = ResolveAnglerQuestFolderForLocalPlayer();
        if (direct != 0)
        {
            return direct;
        }

        var workspace = _memory.FindWorkspace();
        if (workspace == 0)
        {
            return 0;
        }

        var playerStats = _memory.FindChildByName(workspace, "PlayerStats");
        if (playerStats == 0)
        {
            return 0;
        }

        foreach (var questNode in Traverse(playerStats, 10))
        {
            var cls = _memory.ReadClass(questNode);
            if (!cls.Contains("Value", StringComparison.OrdinalIgnoreCase) &&
                !cls.Contains("Folder", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = _memory.ReadName(questNode);
            if (!IsAnglerQuestName(name))
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(questNode))
            {
                if (string.Equals(_memory.ReadClass(child), "BoolValue", StringComparison.OrdinalIgnoreCase))
                {
                    return questNode;
                }
            }
        }

        return 0;
    }

    private ulong ResolveAnglerQuestFolderForLocalPlayer()
    {
        var workspace = _memory.FindWorkspace();
        if (workspace == 0)
        {
            return 0;
        }

        var playerStats = _memory.FindChildByName(workspace, "PlayerStats");
        if (playerStats == 0)
        {
            return 0;
        }

        // Match the schema proven by the standalone reader:
        // Workspace/PlayerStats/Model:roblox_user_xxx/Part:T/Folder:roblox_user_xxx/Folder:Quests/StringValue:Angler Quest_...
        var statsModel = 0UL;
        foreach (var child in _memory.ReadChildren(playerStats))
        {
            if (!string.Equals(_memory.ReadClass(child), "Model", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = _memory.ReadName(child);
            if (name.StartsWith("roblox_user_", StringComparison.OrdinalIgnoreCase))
            {
                statsModel = child;
                break;
            }
        }

        if (statsModel == 0)
        {
            return 0;
        }

        var statsModelName = _memory.ReadName(statsModel);
        if (string.IsNullOrWhiteSpace(statsModelName))
        {
            return 0;
        }

        var partT = _memory.FindChildByName(statsModel, "T");
        if (partT == 0)
        {
            return 0;
        }

        var userFolder = _memory.FindChildByName(partT, statsModelName);
        if (userFolder == 0)
        {
            // Fallback if the duplicate-named folder is absent on some builds.
            userFolder = partT;
        }

        var questsFolder = _memory.FindChildByName(userFolder, "Quests");
        if (questsFolder == 0)
        {
            foreach (var node in Traverse(userFolder, 6))
            {
                if (string.Equals(_memory.ReadName(node), "Quests", StringComparison.OrdinalIgnoreCase))
                {
                    questsFolder = node;
                    break;
                }
            }
        }

        if (questsFolder == 0)
        {
            return 0;
        }

        foreach (var questNode in _memory.ReadChildren(questsFolder))
        {
            var name = _memory.ReadName(questNode);
            if (!IsAnglerQuestName(name))
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(questNode))
            {
                if (string.Equals(_memory.ReadClass(child), "BoolValue", StringComparison.OrdinalIgnoreCase))
                {
                    return questNode;
                }
            }
        }

        // One more deep fallback under quests for non-direct quest containers.
        foreach (var questNode in Traverse(questsFolder, 4))
        {
            if (!IsAnglerQuestName(_memory.ReadName(questNode)))
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(questNode))
            {
                if (string.Equals(_memory.ReadClass(child), "BoolValue", StringComparison.OrdinalIgnoreCase))
                {
                    return questNode;
                }
            }
        }

        return 0;
    }

    private bool ReadBoolValue(ulong boolValueInstance)
    {
        // Primary method validated in standalone probe:
        // BoolValue payload is a canonical byte at Misc.Value (0xD0 in current offsets).
        if (TryReadCanonicalBoolAtOffset(boolValueInstance, "Misc.Value", out var miscValue))
        {
            return miscValue;
        }

        // Fallbacks for cross-build resiliency.
        foreach (var key in new[] { "StatsItem.Value", "Value" })
        {
            if (TryReadCanonicalBoolAtOffset(boolValueInstance, key, out var value))
            {
                return value;
            }
        }

        return false;
    }

    private bool TryReadCanonicalBoolAtOffset(ulong instance, string key, out bool value)
    {
        value = false;
        ulong offset;
        try
        {
            offset = _memory.GetOffset(key);
        }
        catch
        {
            return false;
        }

        var rawByte = _memory.ReadByte(instance + offset);
        if (rawByte is 0 or 1)
        {
            value = rawByte == 1;
            return true;
        }

        var rawInt = _memory.ReadInt32(instance + offset);
        if (rawInt is 0 or 1)
        {
            value = rawInt == 1;
            return true;
        }

        return false;
    }

    private string TryReadValueText(ulong instance)
    {
        foreach (var key in new[] { "StatsItem.Value", "Misc.Value", "Value" })
        {
            ulong offset;
            try
            {
                offset = _memory.GetOffset(key);
            }
            catch
            {
                continue;
            }

            var direct = _memory.ReadString(instance + offset);
            if (!string.IsNullOrWhiteSpace(direct))
            {
                return direct.Trim();
            }

            var ptr = _memory.ReadPtr(instance + offset);
            var indirect = _memory.ReadString(ptr);
            if (!string.IsNullOrWhiteSpace(indirect))
            {
                return indirect.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeQuestValue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text.Trim();
        var newline = value.IndexOf('\n');
        if (newline >= 0)
        {
            value = value[..newline].Trim();
        }

        return value;
    }

    private static bool IsAnglerQuestName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return name.Contains("Angler Quest", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AnglerQuest", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMetaQuestBoolName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return string.Equals(name, "AutoComplete", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "Tracking", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("_Goal", StringComparison.OrdinalIgnoreCase);
    }

    private ulong ResolveInventoryFrame()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "backpack");
        return backpack == 0 ? 0 : _memory.FindChildByName(backpack, "inventory");
    }

    private ulong ResolveInventorySearchFrame(ulong inventory)
    {
        var byPath = FindByPath(inventory, "Search", "Frame", "topbar", "search");
        if (byPath != 0)
        {
            return byPath;
        }

        return FindByPath(inventory, "Search", string.Empty, "topbar");
    }

    private ulong ResolveInventoryItemContainer(ulong inventory)
    {
        var byName = _memory.FindChildByName(inventory, "itemContainer");
        if (byName != 0)
        {
            return byName;
        }

        return FindByPath(inventory, "itemContainer", "Frame");
    }

    private ulong FindInventoryItemTarget(ulong itemContainer, string name)
    {
        var needle = NormalizeLoose(name);
        foreach (var node in Traverse(itemContainer, 32))
        {
            var cls = _memory.ReadClass(node);
            if (!cls.Contains("Text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = NormalizeLoose(_memory.ReadGuiText(node));
            if (text.Length == 0)
            {
                continue;
            }

            if (!text.Equals(needle, StringComparison.OrdinalIgnoreCase) &&
                !text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var clickable = FindClickableAncestor(node, itemContainer);
            if (clickable != 0)
            {
                return clickable;
            }
        }

        return 0;
    }

    private ulong FindClickableAncestor(ulong node, ulong stopRoot)
    {
        var current = node;
        for (var i = 0; i < 10 && RobloxMemory.IsValidAddress(current); i++)
        {
            var cls = _memory.ReadClass(current);
            if (cls.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
                cls.Contains("Frame", StringComparison.OrdinalIgnoreCase))
            {
                if (_memory.ReadGuiBounds(current, false) is { Width: > 10, Height: > 10 })
                {
                    return current;
                }
            }

            if (current == stopRoot)
            {
                break;
            }

            var parent = _memory.ReadPtr(current + _memory.GetOffset("Parent"));
            if (!RobloxMemory.IsValidAddress(parent) || parent == current)
            {
                break;
            }

            current = parent;
        }

        return 0;
    }

    private ulong FindByPath(ulong root, string name, string classNeedle, params string[] segments)
    {
        foreach (var item in Traverse(root, 256))
        {
            var nameMatches = string.IsNullOrWhiteSpace(name) || string.Equals(_memory.ReadName(item), name, StringComparison.OrdinalIgnoreCase);
            if (!nameMatches || !ClassMatches(_memory.ReadClass(item), classNeedle))
            {
                continue;
            }

            if (PathContains(item, segments))
            {
                return item;
            }
        }

        return 0;
    }

    private IEnumerable<ulong> Traverse(ulong root, int maxDepth)
    {
        var seen = new HashSet<ulong>();
        var queue = new Queue<(ulong Address, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (address, depth) = queue.Dequeue();
            if (!seen.Add(address))
            {
                continue;
            }

            yield return address;
            if (depth >= maxDepth)
            {
                continue;
            }

            foreach (var child in _memory.ReadChildren(address))
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private bool PathContains(ulong item, IReadOnlyList<string> segments)
    {
        var path = BuildPath(item).ToLowerInvariant();
        foreach (var segment in segments)
        {
            if (!path.Contains((segment ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private string BuildPath(ulong instance)
    {
        var parts = new List<string>();
        var current = instance;
        for (var i = 0; i < 64 && RobloxMemory.IsValidAddress(current); i++)
        {
            var name = _memory.ReadName(current);
            if (!string.IsNullOrWhiteSpace(name))
            {
                parts.Add(name);
            }

            var parent = _memory.ReadPtr(current + _memory.GetOffset("Parent"));
            if (!RobloxMemory.IsValidAddress(parent) || parent == current)
            {
                break;
            }

            current = parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private void ClickGuiCenter(ulong address)
    {
        var bounds = _memory.ReadGuiBounds(address, false);
        if (bounds is null)
        {
            return;
        }

        var origin = GetClientScreenOrigin();
        var x = (int)Math.Round(origin.X + bounds.Value.X + bounds.Value.Width * 0.5f);
        var y = (int)Math.Round(origin.Y + bounds.Value.Y + bounds.Value.Height * 0.5f);
        NativeMouse.ClickAt(x, y);
    }

    private WinPoint GetClientScreenOrigin()
    {
        var point = new WinPoint(0, 0);
        ClientToScreen(_memory.WindowHandle, ref point);
        return point;
    }

    private static bool ClassMatches(string className, string classNeedle)
    {
        if (string.IsNullOrWhiteSpace(classNeedle))
        {
            return true;
        }

        return classNeedle.Contains("Button", StringComparison.OrdinalIgnoreCase)
            ? className.Contains("Button", StringComparison.OrdinalIgnoreCase)
            : string.Equals(className, classNeedle, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLoose(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().ToLowerInvariant();
    }

    private string ResolveFishName(ulong boolValueInstance)
    {
        var fishName = _memory.ReadName(boolValueInstance).Trim();
        if (fishName.Length == 0)
        {
            return "None";
        }

        if (!string.Equals(_lastLoggedFish, fishName, StringComparison.OrdinalIgnoreCase))
        {
            _lastLoggedFish = fishName;
            AppLog.Info("AutoAngler", $"Resolved current fish: {fishName}");
        }

        return fishName;
    }


    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint
    {
        public int X;
        public int Y;

        public WinPoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
