using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Client.Services.Fishing;

internal static class NativeKeyboard
{
    private const int FocusSettleDelayMs = 25;
    private const int KeyHoldDelayMs = 35;
    private const int ModifierSettleDelayMs = 12;
    private const int DefaultTextKeyDelayMs = 25;
    private const byte VkReturn = 0x0D;
    private const byte VkBack = 0x08;
    private const byte VkControl = 0x11;
    private const byte VkShift = 0x10;
    private const byte VkMenu = 0x12;
    private const byte VkA = 0x41;
    private const byte VkE = 0x45;
    private const byte VkG = 0x47;
    private const byte VkBackslash = 0xDC;
    private const uint KeyeventfKeyup = 0x0002;
    private const uint KeyeventfScancode = 0x0008;
    private const uint InputKeyboard = 1;
    private const uint MapvkVkToVsc = 0;

    public static void PressEnter()
    {
        PressKey(VkReturn, IntPtr.Zero);
    }

    public static void PressEnter(IntPtr targetWindow)
    {
        PressKey(VkReturn, targetWindow);
    }

    public static void PressKey(char key)
    {
        var scan = VkKeyScan(key);
        if (scan == -1)
        {
            return;
        }

        PressKey((byte)(scan & 0xFF), IntPtr.Zero);
    }

    public static void PressE()
    {
        PressKey(VkE, IntPtr.Zero);
    }

    public static void PressE(IntPtr targetWindow)
    {
        PressKey(VkE, targetWindow);
    }

    public static void PressBackslash()
    {
        PressKey(VkBackslash, IntPtr.Zero);
    }

    public static void PressG()
    {
        PressKey(VkG, IntPtr.Zero);
    }

    public static void PressG(IntPtr targetWindow)
    {
        PressKey(VkG, targetWindow);
    }

    public static void PressShift()
    {
        PressKey(VkShift, IntPtr.Zero);
    }

    public static void PressShift(IntPtr targetWindow)
    {
        PressKey(VkShift, targetWindow);
    }

    public static void PressBackspace(IntPtr targetWindow)
    {
        PressKey(VkBack, targetWindow);
    }

    public static void PressCtrlA(IntPtr targetWindow)
    {
        PressModifiedKey(VkA, targetWindow, control: true, shift: false, alt: false);
    }

    public static void PressAltA(IntPtr targetWindow)
    {
        PressModifiedKey(VkA, targetWindow, control: false, shift: false, alt: true);
    }

    public static void TypeText(string text, IntPtr targetWindow)
    {
        TypeText(text, targetWindow, DefaultTextKeyDelayMs);
    }

    public static void TypeText(string text, IntPtr targetWindow, int keyDelayMs)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var delayMs = Math.Max(0, keyDelayMs);
        FocusTarget(targetWindow);
        foreach (var ch in text)
        {
            var key = VkKeyScan(ch);
            if (key == -1)
            {
                continue;
            }

            var vk = (byte)(key & 0xFF);
            var shift = (key & 0x0100) != 0;
            var control = (key & 0x0200) != 0;
            var alt = (key & 0x0400) != 0;
            PressModifiedKey(vk, IntPtr.Zero, control, shift, alt);
            if (delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    public static void PressDigit(int digit)
    {
        PressDigit(digit, IntPtr.Zero);
    }

    public static void PressDigit(int digit, IntPtr targetWindow)
    {
        if (digit < 1 || digit > 9)
        {
            return;
        }

        var vk = (byte)(0x30 + digit);
        PressKey(vk, targetWindow);
    }

    public static void PressEnterSpam(int count)
    {
        PressEnterSpam(count, IntPtr.Zero);
    }

    public static void PressEnterSpam(int count, IntPtr targetWindow)
    {
        var total = Math.Max(1, count);
        for (var i = 0; i < total; i++)
        {
            PressKey(VkReturn, targetWindow);
            Thread.Sleep(18);
        }
    }

    public static void PressEAndEnterSpam(int count)
    {
        PressEAndEnterSpam(count, IntPtr.Zero);
    }

    public static void PressESpam(int count)
    {
        PressESpam(count, IntPtr.Zero);
    }

    public static void PressESpam(int count, IntPtr targetWindow)
    {
        var total = Math.Max(1, count);
        for (var i = 0; i < total; i++)
        {
            PressKey(VkE, targetWindow);
            Thread.Sleep(18);
        }
    }

    public static void PressEAndEnterSpam(int count, IntPtr targetWindow)
    {
        var total = Math.Max(1, count);
        for (var i = 0; i < total; i++)
        {
            PressKey(VkE, targetWindow);
            Thread.Sleep(15);
            PressKey(VkReturn, targetWindow);
            Thread.Sleep(18);
        }
    }

    private static void PressKey(byte virtualKey, IntPtr targetWindow)
    {
        FocusTarget(targetWindow);
        if (!SendInputKey(virtualKey, false, true) && !SendInputKey(virtualKey, false, false))
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        }

        Thread.Sleep(KeyHoldDelayMs);
        if (!SendInputKey(virtualKey, true, true) && !SendInputKey(virtualKey, true, false))
        {
            keybd_event(virtualKey, 0, KeyeventfKeyup, UIntPtr.Zero);
        }
        Thread.Sleep(ModifierSettleDelayMs);
    }

    private static void PressModifiedKey(byte virtualKey, IntPtr targetWindow, bool control, bool shift, bool alt)
    {
        FocusTarget(targetWindow);
        if (control)
        {
            KeyDown(VkControl);
            Thread.Sleep(ModifierSettleDelayMs);
        }

        if (shift)
        {
            KeyDown(VkShift);
            Thread.Sleep(ModifierSettleDelayMs);
        }

        if (alt)
        {
            KeyDown(VkMenu);
            Thread.Sleep(ModifierSettleDelayMs);
        }

        PressKey(virtualKey, IntPtr.Zero);

        if (alt)
        {
            KeyUp(VkMenu);
        }

        if (shift)
        {
            KeyUp(VkShift);
        }

        if (control)
        {
            KeyUp(VkControl);
        }
    }

    private static void KeyDown(byte virtualKey)
    {
        if (!SendInputKey(virtualKey, false, true) && !SendInputKey(virtualKey, false, false))
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        }
    }

    private static void KeyUp(byte virtualKey)
    {
        if (!SendInputKey(virtualKey, true, true) && !SendInputKey(virtualKey, true, false))
        {
            keybd_event(virtualKey, 0, KeyeventfKeyup, UIntPtr.Zero);
        }
    }

    private static void FocusTarget(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return;
        }

        SetForegroundWindow(targetWindow);
        Thread.Sleep(FocusSettleDelayMs);
    }

    private static bool SendInputKey(byte virtualKey, bool keyUp, bool scanCode)
    {
        var flags = keyUp ? KeyeventfKeyup : 0;
        ushort scan = 0;
        if (scanCode)
        {
            scan = unchecked((ushort)MapVirtualKey(virtualKey, MapvkVkToVsc));
            if (scan == 0)
            {
                return false;
            }

            flags |= KeyeventfScancode;
        }

        var input = new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = scanCode ? (ushort)0 : virtualKey,
                    ScanCode = scan,
                    Flags = flags,
                },
            },
        };

        return SendInput(1, [input], Marshal.SizeOf<Input>()) == 1;
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char character);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
