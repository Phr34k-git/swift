using System;

namespace Client.Services.Fishing;

internal static class AutomationInputGate
{
    private static readonly object Sync = new();
    private static string? _owner;
    private static int _ownerPriority;

    public static bool TryEnter(string owner, int priority = 0)
    {
        lock (Sync)
        {
            if (_owner is null || string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                _owner = owner;
                _ownerPriority = priority;
                return true;
            }

            return false;
        }
    }

    public static void Exit(string owner)
    {
        lock (Sync)
        {
            if (string.Equals(_owner, owner, StringComparison.Ordinal))
            {
                _owner = null;
                _ownerPriority = 0;
            }
        }
    }

    public static bool IsHeldByOther(string owner)
    {
        lock (Sync)
        {
            return _owner is not null && !string.Equals(_owner, owner, StringComparison.Ordinal);
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            _owner = null;
            _ownerPriority = 0;
        }
    }
}
