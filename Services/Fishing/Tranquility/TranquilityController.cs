using System;
using System.Collections.Generic;
using System.Linq;

namespace Client.Services.Fishing;

// Port of AHK TranquilityController. Scans the 4 rhythm lanes and presses each
// lane's key when a Note sits inside the hit window, with a per-key cooldown.
internal sealed class TranquilityController
{
    private const double HitYMin = 0.78;
    private const double HitYMax = 0.90;
    private const long KeyCooldownMs = 30;

    private readonly RobloxMemory _memory;
    private readonly FishingRuntimeContext _context;
    private readonly HashSet<ulong> _hitNotes = new();
    private readonly Dictionary<string, long> _lastKeySentAt = new();

    public TranquilityController(RobloxMemory memory, FishingRuntimeContext context)
    {
        _memory = memory;
        _context = context;
    }

    public void Reset()
    {
        NativeMouse.LeftUp();
        _hitNotes.Clear();
        _lastKeySentAt.Clear();
    }

    public void Update()
    {
        // It is a key-press game — make sure no mouse hold is active.
        NativeMouse.LeftUp();

        var root = _context.GetTranquilityRoot();
        if (root == 0)
        {
            return;
        }

        var container = _context.GetTranquilityLaneContainer(root);
        if (container == 0)
        {
            return;
        }

        var seen = new HashSet<ulong>();
        for (var index = 1; index <= 4; index++)
        {
            var lane = _context.GetTranquilityLane(container, index);
            if (lane == 0 || !_memory.IsVisible(lane, "FrameVisible"))
            {
                continue;
            }

            var key = _context.GetTranquilityLaneKey(root, lane, index);
            if (key.Length == 0)
            {
                continue;
            }

            foreach (var noteAddr in _memory.ReadChildren(lane))
            {
                if (!string.Equals(_memory.ReadName(noteAddr), "Note", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_memory.ReadClass(noteAddr), "ImageLabel", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                seen.Add(noteAddr);
                if (_hitNotes.Contains(noteAddr) || !_memory.IsVisible(noteAddr, "FrameVisible"))
                {
                    continue;
                }

                var sy = _memory.ReadFramePosition(noteAddr).YScale;
                if (!(sy > -5.0 && sy < 5.0))
                {
                    continue;
                }

                if (sy >= HitYMin && sy <= HitYMax)
                {
                    PressLaneKey(key, noteAddr);
                }
            }
        }

        var stale = _hitNotes.Where(note => !seen.Contains(note)).ToList();
        foreach (var note in stale)
        {
            _hitNotes.Remove(note);
        }
    }

    private void PressLaneKey(string key, ulong noteAddr)
    {
        var now = Environment.TickCount64;
        if (_lastKeySentAt.TryGetValue(key, out var lastSentAt) &&
            lastSentAt != 0 && now - lastSentAt < KeyCooldownMs)
        {
            return;
        }

        NativeKeyboard.PressKey(key[0]);
        _lastKeySentAt[key] = now;
        _hitNotes.Add(noteAddr);
    }
}
