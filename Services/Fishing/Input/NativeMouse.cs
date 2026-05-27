using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Client.Services.Fishing;

internal static class NativeMouse
{
    private const int ReliableClickJitterPixels = 4;
    private const uint MouseEventMove = 0x0001;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint MouseEventRightDown = 0x0008;
    private const uint MouseEventRightUp = 0x0010;
    private const uint MouseEventWheel = 0x0800;
    private const int VkLButton = 0x01;
    private const int VkRButton = 0x02;

    public static void MoveTo(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void ClickAt(int x, int y)
    {
        MoveToWithJitter(x, y);
        LeftDown();
        Thread.Sleep(45);
        MoveTo(x, y);
        LeftUp();
    }

    public static void ClickAtSingle(int x, int y)
    {
        MoveToWithJitter(x, y);
        LeftDown();
        Thread.Sleep(35);
        MoveTo(x, y);
        LeftUp();
    }

    private static void MoveToWithJitter(int x, int y)
    {
        MoveTo(x, y);
        Thread.Sleep(8);
        MoveTo(x, y - ReliableClickJitterPixels);
        Thread.Sleep(8);
        MoveTo(x - ReliableClickJitterPixels, y);
        Thread.Sleep(8);
        MoveTo(x, y + ReliableClickJitterPixels);
        Thread.Sleep(8);
        MoveTo(x, y);
        Thread.Sleep(12);
        mouse_event(MouseEventMove, 1, 0, 0, UIntPtr.Zero);
        Thread.Sleep(8);
        mouse_event(MouseEventMove, unchecked((uint)-1), 0, 0, UIntPtr.Zero);
        Thread.Sleep(8);
        MoveTo(x, y);
        Thread.Sleep(8);
    }

    public static void ScrollDown()
    {
        mouse_event(MouseEventWheel, 0, 0, unchecked((uint)-120), UIntPtr.Zero);
    }

    public static void ScrollDownShort()
    {
        mouse_event(MouseEventWheel, 0, 0, unchecked((uint)-60), UIntPtr.Zero);
    }

    public static void ScrollUp()
    {
        mouse_event(MouseEventWheel, 0, 0, 120, UIntPtr.Zero);
    }

    public static void LeftDown()
    {
        mouse_event(MouseEventLeftDown, 0, 0, 0, UIntPtr.Zero);
    }

    public static void LeftUp()
    {
        mouse_event(MouseEventLeftUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static void RightDown()
    {
        mouse_event(MouseEventRightDown, 0, 0, 0, UIntPtr.Zero);
    }

    public static void RightUp()
    {
        mouse_event(MouseEventRightUp, 0, 0, 0, UIntPtr.Zero);
    }

    public static (int X, int Y) GetCursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    public static bool IsLeftButtonDown()
    {
        return (GetAsyncKeyState(VkLButton) & 0x8000) != 0;
    }

    public static bool IsRightButtonDown()
    {
        return (GetAsyncKeyState(VkRButton) & 0x8000) != 0;
    }

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out WinPoint lpPoint);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private struct WinPoint
    {
        public int X;
        public int Y;
    }
}

