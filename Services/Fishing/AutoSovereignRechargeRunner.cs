using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace Client.Services.Fishing;

internal sealed record AutoSovereignRechargeStepResult(
    bool Failed,
    string Status,
    double? CurrentPowerPercent);

internal sealed class AutoSovereignRechargeRunner : IDisposable
{
    private const string GateOwner = "AUTO_SOVEREIGN_RECHARGE";
    private const int InventoryToggleDelayMs = 200;
    private const int SearchFocusDelayMs = 100;
    private const int SearchResultDelayMs = 700;
    private const int PostTypeSettleDelayMs = 250;
    private const int PostEnchantDelayMs = 350;
    private const int RetryDelayMs = 500;
    private const int MissingRelicRetryDelayMs = 600;
    private const string RelicName = "Enchant Relic";
    private const string InventorySearchText = "mutation:no Enchant Relic";
    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private string _state = "IDLE";
    private bool _recharging;
    private bool _searchPrimed;
    private bool _relicSelectedForUse;
    private DateTimeOffset _nextStepAt = DateTimeOffset.MinValue;
    private ulong _searchFrame;
    private ulong _itemContainer;
    private ulong _inventoryFrame;
    private ulong _enchantButton;
    private readonly HashSet<ulong> _seen = new();

    public void Reset()
    {
        AutomationInputGate.Exit(GateOwner);
        _state = "IDLE";
        _recharging = false;
        _searchPrimed = false;
        _relicSelectedForUse = false;
        AutoSovereignRechargeSettings.RuntimeBusy = false;
        _nextStepAt = DateTimeOffset.MinValue;
        _searchFrame = 0;
        _itemContainer = 0;
        _inventoryFrame = 0;
        _enchantButton = 0;
    }

    public AutoSovereignRechargeStepResult Step(double minPercent, double maxPercent)
    {
        _memory.EnsureAttached();
        if (_memory.WindowHandle == IntPtr.Zero)
        {
            Reset();
            return new AutoSovereignRechargeStepResult(false, "Waiting for Roblox window focus.", ReadPowerPercent());
        }

        var normalizedMin = Math.Clamp(minPercent, 0, 100);
        var normalizedMax = Math.Clamp(maxPercent, normalizedMin, 100);
        var currentPower = ReadPowerPercent();
        var now = DateTimeOffset.UtcNow;

        if (!_recharging)
        {
            AutoSovereignRechargeSettings.RuntimeBusy = false;
            if (currentPower is null)
            {
                return new AutoSovereignRechargeStepResult(false, "Waiting for power read.", null);
            }

            if (currentPower.Value >= normalizedMin)
            {
                return new AutoSovereignRechargeStepResult(
                    false,
                    $"Power healthy: {currentPower.Value:0.0}% (min {normalizedMin:0.0}%).",
                    currentPower);
            }

            _recharging = true;
            _state = "OPEN_INVENTORY";
            _searchPrimed = false;
            _relicSelectedForUse = false;
            _nextStepAt = DateTimeOffset.MinValue;
            AutoSovereignRechargeSettings.RuntimeBusy = true;
        }

        if (!AutomationInputGate.TryEnter(GateOwner))
        {
            return new AutoSovereignRechargeStepResult(false, "Waiting for other automation.", currentPower);
        }

        if (currentPower is not null && currentPower.Value >= normalizedMax)
        {
            if (_state != "IDLE")
            {
                NativeKeyboard.PressG(_memory.WindowHandle);
            }
            AutomationInputGate.Exit(GateOwner);
            _recharging = false;
            _state = "IDLE";
            AutoSovereignRechargeSettings.RuntimeBusy = false;
            return new AutoSovereignRechargeStepResult(
                false,
                $"Power recharged: {currentPower.Value:0.0}% (max {normalizedMax:0.0}%).",
                currentPower);
        }

        if (now < _nextStepAt)
        {
            return new AutoSovereignRechargeStepResult(
                false,
                $"Recharging... {FormatPower(currentPower)}",
                currentPower);
        }

        try
        {
            switch (_state)
            {
                case "OPEN_INVENTORY":
                    if (_memory.WindowHandle == IntPtr.Zero)
                    {
                        _nextStepAt = now.AddMilliseconds(RetryDelayMs);
                        return new AutoSovereignRechargeStepResult(true, "Roblox window unavailable.", currentPower);
                    }

                    NativeKeyboard.PressG(_memory.WindowHandle);
                    _state = "PREPARE_SEARCH";
                    _nextStepAt = now.AddMilliseconds(InventoryToggleDelayMs);
                    return new AutoSovereignRechargeStepResult(false, "Opening inventory.", currentPower);

                case "PREPARE_SEARCH":
                    if (!EnsureInventoryTargets())
                    {
                        _nextStepAt = now.AddMilliseconds(RetryDelayMs);
                        return new AutoSovereignRechargeStepResult(true, "Inventory search targets not found.", currentPower);
                    }

                    if (!_searchPrimed)
                    {
                        ClickGuiCenter(_searchFrame);
                        Thread.Sleep(SearchFocusDelayMs);
                        ClearInventorySearchInput();
                        NativeKeyboard.TypeText(InventorySearchText, _memory.WindowHandle, 35);
                        _nextStepAt = DateTimeOffset.UtcNow.AddMilliseconds(PostTypeSettleDelayMs);
                        _searchPrimed = true;
                        _state = "WAIT_SEARCH_SETTLE";
                        return new AutoSovereignRechargeStepResult(false, "Typing inventory search.", currentPower);
                    }

                    _state = "CLICK_RELIC";
                    _nextStepAt = now.AddMilliseconds(SearchResultDelayMs);
                    return new AutoSovereignRechargeStepResult(false, "Searching Enchant Relic.", currentPower);

                case "WAIT_SEARCH_SETTLE":
                    _state = "CLICK_RELIC";
                    _nextStepAt = now.AddMilliseconds(SearchResultDelayMs);
                    return new AutoSovereignRechargeStepResult(false, "Searching Enchant Relic.", currentPower);

                case "CLICK_RELIC":
                {
                    if (!TrySelectInventoryItem(_itemContainer, RelicName))
                    {
                        _searchPrimed = false;
                        _relicSelectedForUse = false;
                        _state = "PREPARE_SEARCH";
                        _nextStepAt = now.AddMilliseconds(MissingRelicRetryDelayMs);
                        return new AutoSovereignRechargeStepResult(true, "Could not find Enchant Relic in inventory.", currentPower);
                    }

                    _relicSelectedForUse = true;
                    _state = "CLICK_ENCHANT";
                    _nextStepAt = now.AddMilliseconds(InventoryToggleDelayMs);
                    return new AutoSovereignRechargeStepResult(false, "Selecting Enchant Relic.", currentPower);
                }

                case "CLICK_ENCHANT":
                    if (!_relicSelectedForUse)
                    {
                        _state = "CLICK_RELIC";
                        _nextStepAt = now.AddMilliseconds(RetryDelayMs);
                        return new AutoSovereignRechargeStepResult(true, "Relic not selected; retrying.", currentPower);
                    }

                    if (!TryClickEnchantButton())
                    {
                        _state = "CLICK_ENCHANT";
                        _nextStepAt = now.AddMilliseconds(RetryDelayMs);
                        return new AutoSovereignRechargeStepResult(true, "Enchant button not ready; retrying.", currentPower);
                    }

                    _relicSelectedForUse = false;
                    _state = "CONFIRM_ENCHANT";
                    _nextStepAt = now.AddMilliseconds(InventoryToggleDelayMs);
                    return new AutoSovereignRechargeStepResult(false, "Using Enchant Relic.", currentPower);

                case "CONFIRM_ENCHANT":
                    NativeKeyboard.PressEnter(_memory.WindowHandle);
                    _state = "CLICK_RELIC";
                    _nextStepAt = now.AddMilliseconds(PostEnchantDelayMs);
                    return new AutoSovereignRechargeStepResult(false, $"Recharge cycle complete. {FormatPower(currentPower)}", currentPower);

                default:
                    Reset();
                    return new AutoSovereignRechargeStepResult(false, "Idle.", currentPower);
            }
        }
        catch
        {
            AutomationInputGate.Exit(GateOwner);
            AutoSovereignRechargeSettings.RuntimeBusy = false;
            _recharging = false;
            _state = "IDLE";
            throw;
        }
    }

    public double? ReadCurrentPowerPercent()
    {
        try
        {
            _memory.EnsureAttached();
            return ReadPowerPercent();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _memory.Dispose();
    }

    private double? ReadPowerPercent()
    {
        var label = ResolvePowerLabel();
        if (label == 0)
        {
            return null;
        }

        var text = _memory.ReadGuiText(label);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = Regex.Match(text, @"([0-9]+(?:\.[0-9]+)?)\s*%", RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        return double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, 0, 100)
            : null;
    }

    private ulong ResolvePowerLabel()
    {
        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerGui = _memory.FindChildByClass(localPlayer, "PlayerGui");
        var backpack = playerGui == 0 ? 0 : _memory.FindChildByName(playerGui, "backpack");
        var hotbar = backpack == 0 ? 0 : _memory.FindChildByName(backpack, "hotbar");
        var folder = hotbar == 0 ? 0 : _memory.FindChildByName(hotbar, "Folder");
        var frameA = folder == 0 ? 0 : _memory.FindChildByName(folder, "Frame");
        var frameB = frameA == 0 ? 0 : _memory.FindChildByName(frameA, "Frame");
        var powerbar = frameB == 0 ? 0 : _memory.FindChildByName(frameB, "powerbar");
        var bar = powerbar == 0 ? 0 : _memory.FindChildByName(powerbar, "bar");
        var powerLabel = bar == 0 ? 0 : _memory.FindChildByName(bar, "powerLabel");
        if (powerLabel != 0)
        {
            return powerLabel;
        }

        return hotbar == 0 ? 0 : FindByPath(hotbar, "powerLabel", "TextLabel", "powerbar", "bar");
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

    private ulong FindInventoryItemTarget(ulong itemContainer, string itemName)
    {
        var needle = NormalizeLoose(itemName);
        ulong partialMatch = 0;
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

            var exact = IsInventoryItemExactMatch(text, needle);
            var contains = text.Contains(needle, StringComparison.OrdinalIgnoreCase);
            if (!exact && !contains)
            {
                continue;
            }

            var clickable = FindClickableAncestor(node, itemContainer);
            if (clickable == 0)
            {
                continue;
            }

            if (exact)
            {
                return clickable;
            }

            if (partialMatch == 0)
            {
                partialMatch = clickable;
            }
        }

        return partialMatch;
    }

    private bool TrySelectInventoryItem(ulong itemContainer, string itemName)
    {
        var target = FindInventoryItemTarget(itemContainer, itemName);
        if (target == 0)
        {
            return false;
        }

        ClickGuiCenter(target);
        return true;
    }

    private ulong FindClickableAncestor(ulong node, ulong stopRoot)
    {
        ulong frameFallback = 0;
        var current = node;
        for (var i = 0; i < 10 && RobloxMemory.IsValidAddress(current); i++)
        {
            var cls = _memory.ReadClass(current);
            if (cls.Contains("Button", StringComparison.OrdinalIgnoreCase))
            {
                if (_memory.ReadGuiBounds(current, false) is { Width: > 10, Height: > 10 })
                {
                    return current;
                }
            }
            else if (cls.Contains("Frame", StringComparison.OrdinalIgnoreCase) &&
                _memory.ReadGuiBounds(current, false) is { Width: > 10, Height: > 10 } bounds)
            {
                // Keep a tight frame fallback only when it looks like an item row,
                // not a large container that can cause wrong-item clicks.
                if (bounds.Width <= 500 && bounds.Height <= 110)
                {
                    frameFallback = current;
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

        return frameFallback;
    }

    private void EnsureEnchantTarget()
    {
        _memory.EnsureAttached();
        if (_enchantButton != 0 &&
            RobloxMemory.IsValidAddress(_enchantButton) &&
            _memory.ReadGuiBounds(_enchantButton, false) is not null)
        {
            return;
        }

        var playerGui = _memory.FindPlayerGui();
        if (playerGui == 0)
        {
            throw new InvalidOperationException("PlayerGui not found.");
        }

        _enchantButton = FindByPath(playerGui, "Enchant", "Button", "backpack", "inventory", "topbuttons", "textbutton", "enchant");
        if (_enchantButton == 0)
        {
            _enchantButton = FindByPath(playerGui, string.Empty, "Button", "backpack", "inventory", "topbuttons", "enchant");
        }

        if (_enchantButton == 0)
        {
            _enchantButton = FindByNameClass(playerGui, "Enchant", "Button");
        }

        if (_enchantButton == 0)
        {
            throw new InvalidOperationException("Enchant button not found.");
        }
    }

    private bool TryClickEnchantButton()
    {
        try
        {
            EnsureEnchantTarget();
            ClickGuiCenter(_enchantButton);
            return true;
        }
        catch
        {
            // Button pointers can become stale as inventory UI refreshes.
            // Force reacquire on next attempt instead of failing the full sequence.
            _enchantButton = 0;
            return false;
        }
    }

    private ulong FindByNameClass(ulong root, string name, string classNeedle)
    {
        foreach (var item in Traverse(root, 256))
        {
            if (string.Equals(_memory.ReadName(item), name, StringComparison.OrdinalIgnoreCase) &&
                ClassMatches(_memory.ReadClass(item), classNeedle))
            {
                return item;
            }
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
        _seen.Clear();
        var queue = new Queue<(ulong Address, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (address, depth) = queue.Dequeue();
            if (!_seen.Add(address))
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
        var bounds = _memory.ReadGuiBounds(address, false)
            ?? throw new InvalidOperationException("Could not read UI bounds.");
        var origin = GetClientScreenOrigin();
        var x = (int)Math.Round(origin.X + bounds.X + bounds.Width * 0.5f);
        var y = (int)Math.Round(origin.Y + bounds.Y + bounds.Height * 0.5f);
        NativeMouse.ClickAt(x, y);
    }

    private void ClearInventorySearchInput()
    {
        // Focus can intermittently drift on some systems; run two short clear
        // passes before typing to reduce stale-filter misses.
        for (var i = 0; i < 2; i++)
        {
            NativeKeyboard.PressCtrlA(_memory.WindowHandle);
            Thread.Sleep(80);
            NativeKeyboard.PressBackspace(_memory.WindowHandle);
            Thread.Sleep(80);
        }
    }

    private WinPoint GetClientScreenOrigin()
    {
        var point = new WinPoint(0, 0);
        ClientToScreen(_memory.WindowHandle, ref point);
        return point;
    }

    private static string NormalizeLoose(string text)
    {
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim().ToLowerInvariant();
    }

    private static bool IsInventoryItemExactMatch(string normalizedText, string normalizedNeedle)
    {
        if (normalizedText.Equals(normalizedNeedle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Common inventory label variants: "Enchant Relic x3", "Enchant Relic (3)".
        return normalizedText.StartsWith(normalizedNeedle + " x", StringComparison.OrdinalIgnoreCase) ||
            normalizedText.StartsWith(normalizedNeedle + " (", StringComparison.OrdinalIgnoreCase);
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

    private static string FormatPower(double? value)
    {
        return value is null ? "Power unknown." : $"Power {value.Value:0.0}%.";
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
