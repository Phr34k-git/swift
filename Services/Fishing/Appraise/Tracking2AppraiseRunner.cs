using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Client.Services.Fishing;

internal enum AppraiseRunMode
{
    Gamepass,
    Normal,
}

internal sealed record AppraiseSettings(
    IReadOnlyList<string> BaseMutations,
    bool RequireShiny,
    bool RequireSparkling,
    bool RequireTiny,
    bool RequireSmall,
    bool RequireBig,
    bool RequireGiant,
    int ClickX,
    int ClickY,
    double GamepassSpeed,
    AppraiseRunMode Mode);

internal sealed record AppraiseStepResult(bool Completed, bool Failed, string Status);

internal sealed class Tracking2AppraiseRunner : IDisposable
{
    private const int FixedDelayMs = 70;

    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private string _state = "IDLE";
    private long _lastClickAt;
    private long _waitStartedAt;
    private ulong _playerGui;
    private ulong _enchantButton;
    private DateTimeOffset _nextActionAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextEnterAt = DateTimeOffset.MinValue;

    public void Reset()
    {
        _state = "IDLE";
        _lastClickAt = 0;
        _waitStartedAt = 0;
        _playerGui = 0;
        _enchantButton = 0;
        _nextActionAt = DateTimeOffset.MinValue;
        _nextEnterAt = DateTimeOffset.MinValue;
    }

    public AppraiseStepResult Step(AppraiseSettings settings)
    {
        if (!HasAnyTarget(settings))
        {
            return new AppraiseStepResult(false, true, "Choose at least one appraise target.");
        }

        if (settings.Mode == AppraiseRunMode.Normal && (settings.ClickX <= 0 || settings.ClickY <= 0))
        {
            return new AppraiseStepResult(false, true, "Set a click point before appraising.");
        }

        _memory.EnsureAttached();
        if (settings.Mode == AppraiseRunMode.Gamepass)
        {
            return StepGamepass(settings);
        }

        if (_state == "IDLE")
        {
            if (HasDesiredMutation(settings))
            {
                return new AppraiseStepResult(true, false, $"Found {DescribeMatchedTargets(settings)}.");
            }

            _state = "CLICK_FIRST";
        }

        var now = Environment.TickCount64;
        switch (_state)
        {
            case "CLICK_FIRST":
                TriggerRoll(settings);
                _lastClickAt = now;
                _state = "CLICK_SECOND";
                return new AppraiseStepResult(false, false, "Clicking 1/2.");

            case "CLICK_SECOND":
                if (now - _lastClickAt < FixedDelayMs)
                {
                    return new AppraiseStepResult(false, false, "Clicking 2/2.");
                }

                TriggerRoll(settings);
                _lastClickAt = now;
                _waitStartedAt = now;
                _state = "WAIT_RESULT";
                return new AppraiseStepResult(false, false, "Clicking 2/2.");

            case "WAIT_RESULT":
                if (now - _waitStartedAt < FixedDelayMs)
                {
                    return new AppraiseStepResult(false, false, "Checking result.");
                }

                if (HasDesiredMutation(settings))
                {
                    Reset();
                    return new AppraiseStepResult(true, false, $"Found {DescribeMatchedTargets(settings)}.");
                }

                _waitStartedAt = now;
                _state = "WAIT_RETRY";
                return new AppraiseStepResult(false, false, $"Still looking for {DescribeTargets(settings)}.");

            case "WAIT_RETRY":
                if (now - _waitStartedAt < FixedDelayMs)
                {
                    return new AppraiseStepResult(false, false, "Retrying.");
                }

                _state = "CLICK_FIRST";
                return new AppraiseStepResult(false, false, "Retrying.");

            default:
                Reset();
                return new AppraiseStepResult(false, false, "Resolving fish info...");
        }
    }

    public void Dispose()
    {
        _memory.Dispose();
    }

    public string ReadHeldFishText()
    {
        _memory.EnsureAttached();
        var infoAddr = ResolveFishInfoInfo();
        if (infoAddr == 0)
        {
            return "---";
        }

        foreach (var name in new[] { "FishName", "Name", "Title", "Fish" })
        {
            var node = _memory.FindChildByName(infoAddr, name);
            if (node == 0)
            {
                continue;
            }

            var text = NormalizeFishText(_memory.ReadGuiText(node));
            if (text.Length > 0)
            {
                return text;
            }
        }

        foreach (var child in _memory.ReadChildren(infoAddr))
        {
            var className = _memory.ReadClass(child);
            if (!className.Contains("Text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = NormalizeFishText(_memory.ReadGuiText(child));
            if (text.Length > 0)
            {
                return text;
            }
        }

        var subvaluesAddr = _memory.FindChildByName(infoAddr, "Subvalues");
        if (subvaluesAddr != 0)
        {
            var fallback = NormalizeFishText(CollectSubvaluesText(subvaluesAddr));
            if (fallback.Length > 0)
            {
                return fallback;
            }
        }

        return "---";
    }

    private bool HasDesiredMutation(AppraiseSettings settings)
    {
        var subvaluesAddr = ResolveFishInfoSubvalues();
        if (subvaluesAddr == 0)
        {
            // Fish info can transiently disappear while UI updates during rapid rolling.
            // Treat as "not matched yet" instead of failing the appraise loop.
            return false;
        }

        var haystack = NormalizeText(CollectSubvaluesText(subvaluesAddr));
        if (!HasAllRequiredTraits(haystack, settings))
        {
            return false;
        }

        var baseMutations = GetSelectedBaseMutations(settings);
        if (baseMutations.Count == 0)
        {
            return true;
        }

        foreach (var mutation in baseMutations)
        {
            var normalized = NormalizeText(mutation);
            if (normalized.Length > 0 && haystack.Contains(normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private ulong ResolveFishInfoSubvalues()
    {
        var info = ResolveFishInfoInfo();
        if (info == 0)
        {
            return 0;
        }

        return _memory.FindChildByName(info, "Subvalues");
    }

    private ulong ResolveFishInfoInfo()
    {
        var workspace = _memory.FindWorkspace();
        if (workspace == 0)
        {
            return 0;
        }

        var localPlayer = _memory.GetLocalPlayer();
        if (localPlayer == 0)
        {
            return 0;
        }

        var playerName = _memory.ReadName(localPlayer);
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return 0;
        }

        var character = _memory.FindChildByName(workspace, playerName);
        if (character == 0)
        {
            return 0;
        }

        var fishInfo = _memory.FindChildByName(character, "fishinfo");
        if (fishInfo == 0)
        {
            return 0;
        }

        var info = _memory.FindChildByName(fishInfo, "Info");
        if (info == 0)
        {
            return 0;
        }

        return info;
    }

    private string CollectSubvaluesText(ulong subvaluesAddr)
    {
        var textParts = new List<string>();
        AppendNodeText(textParts, subvaluesAddr);
        foreach (var child in _memory.ReadChildren(subvaluesAddr))
        {
            AppendNodeText(textParts, child);
            foreach (var descendant in _memory.ReadChildren(child))
            {
                AppendNodeText(textParts, descendant);
            }
        }

        return string.Join(" ", textParts);
    }

    private void AppendNodeText(List<string> textParts, ulong instanceAddr)
    {
        try
        {
            var className = _memory.ReadClass(instanceAddr);
            if (!className.Contains("Text", StringComparison.OrdinalIgnoreCase) &&
                !className.Contains("Value", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var text = _memory.ReadGuiText(instanceAddr).Trim();
            if (text.Length > 0)
            {
                textParts.Add(text);
            }
        }
        catch
        {
            // ignored
        }
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant);
        return normalized.Trim().ToLowerInvariant();
    }

    private static string NormalizeFishText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Replace("\r", "\n", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, "\\s+", " ", RegexOptions.CultureInvariant);
        return normalized.Trim();
    }

    private static void ReliableClick(AppraiseSettings settings)
    {
        var x = settings.ClickX;
        var y = settings.ClickY;
        NativeMouse.MoveTo(x, y);
        System.Threading.Thread.Sleep(15);
        NativeMouse.MoveTo(x + 3, y);
        System.Threading.Thread.Sleep(15);
        NativeMouse.MoveTo(x - 3, y);
        System.Threading.Thread.Sleep(15);
        NativeMouse.MoveTo(x, y + 3);
        System.Threading.Thread.Sleep(15);
        NativeMouse.MoveTo(x, y - 3);
        System.Threading.Thread.Sleep(15);
        NativeMouse.MoveTo(x, y);
        NativeMouse.ClickAt(x, y);
    }

    private static void TriggerRoll(AppraiseSettings settings)
    {
        if (settings.Mode == AppraiseRunMode.Normal)
        {
            ReliableClick(settings);
            return;
        }

        NativeKeyboard.PressEAndEnterSpam(1);
    }

    private AppraiseStepResult StepGamepass(AppraiseSettings settings)
    {
        if (HasDesiredMutation(settings))
        {
            return new AppraiseStepResult(true, false, $"Found {DescribeMatchedTargets(settings)}.");
        }

        var now = DateTimeOffset.UtcNow;
        var speed = ClampSpeed(settings.GamepassSpeed);
        var actionDelay = ResolveGamepassActionDelay(speed);
        var spamDelay = ResolveGamepassSpamDelay(speed);
        var clickEnterBurst = ResolveGamepassClickEnterBurst(speed);
        var rollingEnterBurst = ResolveGamepassRollingEnterBurst(speed);

        SpamEnterWhileRolling(now, spamDelay, rollingEnterBurst);
        if (now >= _nextActionAt)
        {
            EnsureEnchantButtonTarget();
            ClickAppraiseFromEnchantOffset();
            NativeKeyboard.PressEnterSpam(clickEnterBurst, _memory.WindowHandle);
            _nextActionAt = now + actionDelay;
        }

        if (HasDesiredMutation(settings))
        {
            return new AppraiseStepResult(true, false, $"Found {DescribeMatchedTargets(settings)}.");
        }

        return new AppraiseStepResult(false, false, $"Still looking for {DescribeTargets(settings)}.");
    }

    private static bool HasAnyTarget(AppraiseSettings settings)
    {
        return GetSelectedBaseMutations(settings).Count > 0
            || settings.RequireShiny
            || settings.RequireSparkling
            || settings.RequireTiny
            || settings.RequireSmall
            || settings.RequireBig
            || settings.RequireGiant;
    }

    private static bool HasAllRequiredTraits(string haystack, AppraiseSettings settings)
    {
        var requireAnySize = settings.RequireTiny || settings.RequireSmall || settings.RequireBig || settings.RequireGiant;
        if (requireAnySize)
        {
            var hasAnySelectedSize =
                (settings.RequireTiny && haystack.Contains("tiny", StringComparison.Ordinal)) ||
                (settings.RequireSmall && haystack.Contains("small", StringComparison.Ordinal)) ||
                (settings.RequireBig && haystack.Contains("big", StringComparison.Ordinal)) ||
                (settings.RequireGiant && haystack.Contains("giant", StringComparison.Ordinal));
            if (!hasAnySelectedSize)
            {
                return false;
            }
        }

        if (settings.RequireShiny && !haystack.Contains("shiny", StringComparison.Ordinal))
        {
            return false;
        }

        if (settings.RequireSparkling && !haystack.Contains("sparkling", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string DescribeTargets(AppraiseSettings settings)
    {
        var targets = new List<string>();
        var baseMutations = GetSelectedBaseMutations(settings);
        if (baseMutations.Count > 0)
        {
            targets.AddRange(baseMutations);
        }

        if (settings.RequireShiny)
        {
            targets.Add("Shiny");
        }

        if (settings.RequireSparkling)
        {
            targets.Add("Sparkling");
        }

        if (settings.RequireBig)
        {
            targets.Add("Big");
        }

        if (settings.RequireGiant)
        {
            targets.Add("Giant");
        }

        if (settings.RequireTiny)
        {
            targets.Add("Tiny");
        }

        if (settings.RequireSmall)
        {
            targets.Add("Small");
        }

        return targets.Count == 0 ? "target" : string.Join(" + ", targets);
    }

    private string DescribeMatchedTargets(AppraiseSettings settings)
    {
        var subvaluesAddr = ResolveFishInfoSubvalues();
        if (subvaluesAddr == 0)
        {
            return DescribeTargets(settings);
        }

        var haystack = NormalizeText(CollectSubvaluesText(subvaluesAddr));
        var matched = new List<string>();

        var baseMutations = GetSelectedBaseMutations(settings);
        if (baseMutations.Count > 0)
        {
            foreach (var mutation in baseMutations)
            {
                var normalized = NormalizeText(mutation);
                if (normalized.Length > 0 && haystack.Contains(normalized, StringComparison.Ordinal))
                {
                    matched.Add(mutation);
                }
            }
        }

        if (settings.RequireShiny && haystack.Contains("shiny", StringComparison.Ordinal))
        {
            matched.Add("Shiny");
        }

        if (settings.RequireSparkling && haystack.Contains("sparkling", StringComparison.Ordinal))
        {
            matched.Add("Sparkling");
        }

        if (settings.RequireTiny && haystack.Contains("tiny", StringComparison.Ordinal))
        {
            matched.Add("Tiny");
        }

        if (settings.RequireSmall && haystack.Contains("small", StringComparison.Ordinal))
        {
            matched.Add("Small");
        }

        if (settings.RequireBig && haystack.Contains("big", StringComparison.Ordinal))
        {
            matched.Add("Big");
        }

        if (settings.RequireGiant && haystack.Contains("giant", StringComparison.Ordinal))
        {
            matched.Add("Giant");
        }

        return matched.Count == 0 ? DescribeTargets(settings) : string.Join(" + ", matched);
    }

    private void SpamEnterWhileRolling(DateTimeOffset now, TimeSpan spamDelay, int enterBurst)
    {
        if (now < _nextEnterAt)
        {
            return;
        }

        NativeKeyboard.PressEnterSpam(enterBurst, _memory.WindowHandle);
        _nextEnterAt = now + spamDelay;
    }

    private static double ClampSpeed(double speed)
    {
        if (!double.IsFinite(speed))
        {
            return 0.5;
        }

        return Math.Clamp(speed, 0.0, 1.0);
    }

    private static TimeSpan ResolveGamepassActionDelay(double speed)
    {
        // Slow (left) -> larger delay, Fast (right) -> smaller delay.
        var ms = Lerp(550, 35, speed);
        return TimeSpan.FromMilliseconds(ms);
    }

    private static TimeSpan ResolveGamepassSpamDelay(double speed)
    {
        var ms = Lerp(180, 8, speed);
        return TimeSpan.FromMilliseconds(ms);
    }

    private static int ResolveGamepassClickEnterBurst(double speed)
    {
        // Fast mode should not be blocked by long key burst loops.
        return (int)Math.Round(Lerp(8, 1, speed));
    }

    private static int ResolveGamepassRollingEnterBurst(double speed)
    {
        return (int)Math.Round(Lerp(6, 1, speed));
    }

    private static double Lerp(double slow, double fast, double t)
    {
        return slow + ((fast - slow) * t);
    }

    private static List<string> GetSelectedBaseMutations(AppraiseSettings settings)
    {
        var selected = new List<string>();
        if (settings.BaseMutations is null)
        {
            return selected;
        }

        foreach (var value in settings.BaseMutations)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ||
                selected.Exists(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            selected.Add(value);
        }

        return selected;
    }

    private void EnsureEnchantButtonTarget()
    {
        _memory.EnsureAttached();
        if (_enchantButton != 0 && RobloxMemory.IsValidAddress(_enchantButton))
        {
            return;
        }

        _playerGui = _memory.FindPlayerGui();
        if (_playerGui == 0)
        {
            throw new InvalidOperationException("PlayerGui not found.");
        }

        _enchantButton = FindByPath(_playerGui, "Enchant", "Button", "backpack", "inventory", "topbuttons", "textbutton", "enchant");
        if (_enchantButton == 0)
        {
            _enchantButton = FindByPath(_playerGui, string.Empty, "Button", "backpack", "inventory", "topbuttons", "enchant");
        }

        if (_enchantButton == 0)
        {
            _enchantButton = FindByNameClass(_playerGui, "Enchant", "Button");
        }

        if (_enchantButton == 0)
        {
            throw new InvalidOperationException("Enchant button not found.");
        }
    }

    private void ClickAppraiseFromEnchantOffset()
    {
        var bounds = ReadScreenBounds(_enchantButton, false);
        var clickX = (int)Math.Round(bounds.X + bounds.Width * 0.5f) - 150;
        var clickY = (int)Math.Round(bounds.Y + bounds.Height * 0.5f);
        NativeMouse.ClickAt(clickX, clickY);
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

    private GuiBounds ReadScreenBounds(ulong address, bool visibleRequired)
    {
        var bounds = _memory.ReadGuiBounds(address, visibleRequired)
            ?? throw new InvalidOperationException("Could not read enchant button bounds.");
        var origin = GetClientScreenOrigin();
        return new GuiBounds(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
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


    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint point);

    private readonly record struct GuiBounds(float X, float Y, float Width, float Height);

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
