using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Client.Services.Fishing;

internal sealed class AutoEnchantRunner : IDisposable
{
    private static readonly TimeSpan TargetRefreshInterval = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan MinimumActionGap = TimeSpan.FromMilliseconds(350);

    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private readonly EnchantDetector _detector = new();
    private readonly HashSet<ulong> _seen = new();
    private ulong _playerGui;
    private ulong _enchantButton;
    private ulong _confirmButton;
    private DateTimeOffset _nextActionAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextTargetRefreshAt = DateTimeOffset.MinValue;
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;

    public void Reset()
    {
        _nextActionAt = DateTimeOffset.MinValue;
        _nextTargetRefreshAt = DateTimeOffset.MinValue;
        _lastActionAt = DateTimeOffset.MinValue;
        _playerGui = 0;
        _enchantButton = 0;
        _confirmButton = 0;
    }

    public EnchantRunnerResult Step(string targetEnchant, EnchantRollMode mode, double cycleMs)
    {
        if (string.IsNullOrWhiteSpace(targetEnchant))
        {
            return new EnchantRunnerResult(false, "Select a target enchant.", new EnchantSnapshot(string.Empty, string.Empty, string.Empty));
        }

        _memory.EnsureAttached();
        var actionDelay = ResolveActionDelay(cycleMs);
        var snapshot = ReadSnapshotOrEmpty();
        if (string.Equals(snapshot.Enchant, targetEnchant, StringComparison.OrdinalIgnoreCase))
        {
            return new EnchantRunnerResult(true, $"Found: {snapshot.Enchant}.", snapshot);
        }

        var now = DateTimeOffset.UtcNow;
        RefreshTargetsIfDue(now, mode);
        if (now < _nextActionAt)
        {
            return new EnchantRunnerResult(false, "Rolling.", snapshot);
        }

        if (now - _lastActionAt < MinimumActionGap)
        {
            return new EnchantRunnerResult(false, "Rolling.", snapshot);
        }

        // Reserve a provisional cycle window before sending input, so transient
        // exceptions cannot cause an immediate duplicate retry.
        _nextActionAt = now + actionDelay;
        _lastActionAt = now;

        if (mode == EnchantRollMode.Gamepass)
        {
            EnsureTargets();
            ClickCenter(_enchantButton, false);
            Thread.Sleep(200);
            NativeKeyboard.PressEnter(_memory.WindowHandle);
        }
        else
        {
            EnsureConfirmTarget();
            ClickCenter(_confirmButton, false);
            Thread.Sleep(200);
            NativeKeyboard.PressE(_memory.WindowHandle);
        }

        // Anchor the next cycle after input completes so we do not start another
        // click immediately after Enter/E on shorter cycle values.
        _nextActionAt = DateTimeOffset.UtcNow + actionDelay;
        return new EnchantRunnerResult(false, "Rolling.", snapshot);
    }

    public void Dispose()
    {
        _detector.Dispose();
        _memory.Dispose();
    }

    private void EnsureTargets()
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

    private EnchantSnapshot ReadSnapshotOrEmpty()
    {
        try
        {
            return _detector.Read();
        }
        catch
        {
            return new EnchantSnapshot(string.Empty, string.Empty, string.Empty);
        }
    }

    private static TimeSpan ResolveActionDelay(double cycleMs)
    {
        var ms = double.IsFinite(cycleMs) ? cycleMs : 300;
        ms = Math.Clamp(ms, 30, 120000);
        return TimeSpan.FromMilliseconds(ms);
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

    private void ClickCenter(ulong address, bool visibleRequired)
    {
        var bounds = ReadScreenBounds(address, visibleRequired);
        NativeMouse.ClickAt((int)Math.Round(bounds.X + bounds.Width * 0.5f), (int)Math.Round(bounds.Y + bounds.Height * 0.5f));
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


    private void RefreshTargetsIfDue(DateTimeOffset now, EnchantRollMode mode)
    {
        if (now < _nextTargetRefreshAt)
        {
            return;
        }

        if (mode == EnchantRollMode.Gamepass)
        {
            _enchantButton = 0;
            EnsureTargets();
        }
        else
        {
            _confirmButton = 0;
            EnsureConfirmTarget();
        }

        _nextTargetRefreshAt = now + TargetRefreshInterval;
    }

    private void EnsureConfirmTarget()
    {
        _memory.EnsureAttached();
        if (_confirmButton != 0 && RobloxMemory.IsValidAddress(_confirmButton))
        {
            return;
        }

        _playerGui = _memory.FindPlayerGui();
        if (_playerGui == 0)
        {
            throw new InvalidOperationException("PlayerGui not found.");
        }

        _confirmButton = FindByPath(_playerGui, "enchantButton", "ImageButton", "hud", "safezone", "enchantconfirm", "enchantbutton");
        if (_confirmButton == 0)
        {
            _confirmButton = FindByPath(_playerGui, "enchantButton", "ImageButton", "screengui", "hud", "safezone", "enchantconfirm", "enchantbutton");
        }
        if (_confirmButton == 0)
        {
            _confirmButton = FindByNameClass(_playerGui, "enchantButton", "ImageButton");
        }
        if (_confirmButton == 0)
        {
            _confirmButton = FindByPath(_playerGui, "confirm", "TextButton", "over", "prompt", "confirm");
        }
        if (_confirmButton == 0)
        {
            _confirmButton = FindByNameClass(_playerGui, "confirm", "TextButton");
        }

        if (_confirmButton == 0)
        {
            throw new InvalidOperationException("confirm button not found.");
        }
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

internal readonly record struct EnchantRunnerResult(bool Completed, string Status, EnchantSnapshot Snapshot);

internal enum EnchantRollMode
{
    Gamepass,
    Normal,
}
