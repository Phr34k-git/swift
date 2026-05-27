using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Client.Services;

namespace Client.Services.Fishing;

internal sealed class AquariumSequenceRunner : IDisposable
{
    private static readonly TimeSpan FeedClickInterval = TimeSpan.FromMilliseconds(50);
    private readonly RobloxMemory _memory = new(OffsetsSourceProvider.Current);
    private readonly HashSet<ulong> _seen = new();
    private ulong _playerGui;
    private ulong _aquariumButton;
    private ulong _feedAnchor;
    private AquariumSequencePhase _phase;
    private DateTimeOffset _nextActionAt = DateTimeOffset.MinValue;
    private DateTimeOffset _feedUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _nextScrollAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextFeedClickAt = DateTimeOffset.MinValue;
    private bool _didFastScrollUp;

    public void Reset()
    {
        AppLog.Fishing("AquariumRunner", $"Reset | wasPhase={_phase}");
        _phase = AquariumSequencePhase.Resolve;
        _nextActionAt = DateTimeOffset.MinValue;
        _feedUntil = DateTimeOffset.MinValue;
        _nextScrollAt = DateTimeOffset.MinValue;
        _nextFeedClickAt = DateTimeOffset.MinValue;
        _didFastScrollUp = false;
        _playerGui = 0;
        _aquariumButton = 0;
        _feedAnchor = 0;
    }

    public AquariumSequenceResult Step(TimeSpan clickDelay)
    {
        _memory.EnsureAttached();
        var now = DateTimeOffset.UtcNow;
        if (now < _nextActionAt)
        {
            return AquariumSequenceResult.Running;
        }

        AppLog.Fishing("AquariumRunner", $"Step phase={_phase} | clickDelay={clickDelay.TotalMilliseconds:0}ms playerGui=0x{_playerGui:X} button=0x{_aquariumButton:X} feedAnchor=0x{_feedAnchor:X}");

        switch (_phase)
        {
            case AquariumSequencePhase.Resolve:
                EnsureTargets();
                AppLog.Fishing("AquariumRunner", $"Resolve -> OpenAquarium | playerGui=0x{_playerGui:X} button=0x{_aquariumButton:X} feedAnchor=0x{_feedAnchor:X}");
                _phase = AquariumSequencePhase.OpenAquarium;
                return AquariumSequenceResult.Running;

            case AquariumSequencePhase.OpenAquarium:
                AppLog.Fishing("AquariumRunner", "OpenAquarium | clicking aquarium button");
                ClickCenter(_aquariumButton, true);
                _phase = AquariumSequencePhase.WaitAfterOpen;
                _nextActionAt = now + Max(clickDelay, TimeSpan.FromMilliseconds(500));
                return AquariumSequenceResult.Running;

            case AquariumSequencePhase.WaitAfterOpen:
                if (_feedAnchor == 0 && !RefreshFeedAnchor())
                {
                    AppLog.Fishing("AquariumRunner", "WaitAfterOpen | feed anchor not yet visible, polling");
                    _nextActionAt = now + TimeSpan.FromMilliseconds(100);
                    return AquariumSequenceResult.Running;
                }

                var bounds = ReadScreenBounds(_feedAnchor, false);
                var x = (int)Math.Round(bounds.X + bounds.Width * 0.25f);
                var y = (int)Math.Round(bounds.Y + bounds.Height * 0.75f);
                AppLog.Fishing("AquariumRunner", $"WaitAfterOpen -> Feeding | clicking feed anchor x={x} y={y}");
                NativeMouse.ClickAt(x, y);
                NativeMouse.MoveTo(x, y);
                _feedUntil = now + TimeSpan.FromMilliseconds(3650);
                _nextScrollAt = now;
                _nextFeedClickAt = now;
                _didFastScrollUp = false;
                _phase = AquariumSequencePhase.Feeding;
                _nextActionAt = now + clickDelay;
                return AquariumSequenceResult.Running;

            case AquariumSequencePhase.Feeding:
                if (now >= _feedUntil)
                {
                    AppLog.Fishing("AquariumRunner", "Feeding -> CloseAquarium (feed window elapsed)");
                    _phase = AquariumSequencePhase.CloseAquarium;
                    _nextActionAt = now;
                    return AquariumSequenceResult.Running;
                }

                if (!GetCursorPos(out var cursor))
                {
                    cursor = default;
                }

                if (now >= _nextFeedClickAt)
                {
                    NativeMouse.MoveTo(cursor.X, cursor.Y);
                    NativeMouse.LeftDown();
                    NativeMouse.LeftUp();
                    _nextFeedClickAt = now + FeedClickInterval;
                }

                if (now >= _nextScrollAt)
                {
                    if (!_didFastScrollUp)
                    {
                        AppLog.Fishing("AquariumRunner", "Feeding | initial 50x ScrollUp burst");
                        for (var i = 0; i < 50; i++)
                        {
                            NativeMouse.ScrollUp();
                        }

                        _didFastScrollUp = true;
                        _nextScrollAt = now + TimeSpan.FromMilliseconds(400);
                        _nextActionAt = Min(_nextFeedClickAt, _nextScrollAt);
                        return AquariumSequenceResult.Running;
                    }

                    NativeMouse.ScrollDown();
                    _nextScrollAt = now + Max(TimeSpan.FromMilliseconds(156), TimeSpan.FromMilliseconds(clickDelay.TotalMilliseconds * 0.715));
                }

                _nextActionAt = Min(_nextFeedClickAt, _nextScrollAt);
                return AquariumSequenceResult.Running;

            case AquariumSequencePhase.CloseAquarium:
                AppLog.Fishing("AquariumRunner", "CloseAquarium | clicking aquarium button to dismiss");
                ClickCenter(_aquariumButton, true);
                _phase = AquariumSequencePhase.CenterClick;
                _nextActionAt = now + clickDelay;
                return AquariumSequenceResult.Running;

            default:
                var client = GetClientScreenRectangle();
                var cx = client.X + client.Width / 2;
                var cy = client.Y + client.Height / 2;
                AppLog.Fishing("AquariumRunner", $"CenterClick | terminal click at screen({cx},{cy}) | THIS CLICK FIRES INTO THE GAME REGARDLESS OF REEL GUI STATE");
                NativeMouse.ClickAt(cx, cy);
                Reset();
                _nextActionAt = now + clickDelay;
                AppLog.Fishing("AquariumRunner", "Done");
                return AquariumSequenceResult.Done;
        }
    }

    public void Dispose()
    {
        _memory.Dispose();
    }

    private void EnsureTargets()
    {
        if (_aquariumButton != 0)
        {
            return;
        }

        _playerGui = _memory.FindPlayerGui();
        if (_playerGui == 0)
        {
            throw new InvalidOperationException("Could not find LocalPlayer.PlayerGui.");
        }

        _aquariumButton = FindPersonalAquariumButton(_playerGui);
        if (_aquariumButton == 0)
        {
            throw new InvalidOperationException("PersonalAquarium button was not found.");
        }

        _feedAnchor = FindFeedAnchor(_playerGui);
    }

    private ulong FindPersonalAquariumButton(ulong root)
    {
        // Prefer exact HUD topbar button path first.
        var exactHud = FindByPath(root, "PersonalAquarium", "TextButton", "hud", "safezone", "topbar", "personalaquarium");
        if (exactHud != 0)
        {
            return exactHud;
        }

        // Some layouts include an extra ScreenGui segment in path text.
        var exactScreenGui = FindByPath(root, "PersonalAquarium", "TextButton", "screengui", "hud", "safezone", "topbar", "personalaquarium");
        if (exactScreenGui != 0)
        {
            return exactScreenGui;
        }

        // Fallback to previous broad matching behavior.
        return FindByNameClass(root, "PersonalAquarium", "Button");
    }

    private bool RefreshFeedAnchor()
    {
        _feedAnchor = FindFeedAnchor(_playerGui);
        return _feedAnchor != 0;
    }

    private ulong FindFeedAnchor(ulong root)
    {
        return FindByPath(root, "FoodList", "Frame", "safezone", "personalaquarium", "more", "fishfood", "foodlist") is var exact && exact != 0
            ? exact
            : FindByPath(root, "FoodList", string.Empty, "safezone", "personalaquarium", "more", "fishfood", "foodlist") is var loose && loose != 0
                ? loose
                : FindByPath(root, string.Empty, "Frame", "safezone", "personalaquarium", "more", "fishfood") is var frame && frame != 0
                    ? frame
                    : FindAnyByPath(root, "safezone", "personalaquarium", "more", "fishfood");
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

    private ulong FindAnyByPath(ulong root, params string[] segments)
    {
        foreach (var item in Traverse(root, 256))
        {
            if (PathContains(item, segments) && _memory.ReadGuiBounds(item, false) is not null)
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
        for (var i = 0; i < 14 && RobloxMemory.IsValidAddress(current); i++)
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
            ?? throw new InvalidOperationException("Could not read GUI bounds.");
        var origin = GetClientScreenOrigin();
        return new GuiBounds(origin.X + bounds.X, origin.Y + bounds.Y, bounds.Width, bounds.Height);
    }

    private ClientRectangle GetClientScreenRectangle()
    {
        if (!GetClientRect(_memory.WindowHandle, out var rect))
        {
            return new ClientRectangle(0, 0, 0, 0);
        }

        var origin = GetClientScreenOrigin();
        return new ClientRectangle(origin.X, origin.Y, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
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

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
    {
        return left >= right ? left : right;
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
    {
        return left <= right ? left : right;
    }


    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref WinPoint point);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out WinRect rect);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WinPoint point);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct WinRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly record struct ClientRectangle(int X, int Y, int Width, int Height);
}

internal enum AquariumSequencePhase
{
    Resolve,
    OpenAquarium,
    WaitAfterOpen,
    Feeding,
    CloseAquarium,
    CenterClick,
}

internal readonly record struct AquariumSequenceResult(bool Completed)
{
    public static readonly AquariumSequenceResult Running = new(false);
    public static readonly AquariumSequenceResult Done = new(true);
}
