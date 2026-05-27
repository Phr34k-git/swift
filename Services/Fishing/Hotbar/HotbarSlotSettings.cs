namespace Client.Services.Fishing;

internal static class HotbarSlotSettings
{
    private static int _rodSlot = 1;

    public static int RodSlot
    {
        get => _rodSlot;
        set
        {
            if (value < 1)
            {
                _rodSlot = 1;
                return;
            }

            if (value > 9)
            {
                _rodSlot = 9;
                return;
            }

            _rodSlot = value;
        }
    }
}
