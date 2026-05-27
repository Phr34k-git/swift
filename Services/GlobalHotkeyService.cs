using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Client.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int DuplicateKeyupWindowMs = 250;
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook;
    private int _virtualKey;
    private long _lastPressAt;

    public GlobalHotkeyService()
    {
        _proc = HookCallback;
        _hook = SetHook(_proc);
    }

    public event Action? Pressed;

    public void SetHotkey(Key key)
    {
        _virtualKey = ToVirtualKey(key);
        // Treat the physical press that just bound this key as already-consumed:
        // the matching KeyUp arrives after SetHotkey and would otherwise toggle the macro.
        _lastPressAt = Environment.TickCount64;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _virtualKey != 0 &&
            (wParam == new IntPtr(WmKeyUp) || wParam == new IntPtr(WmSysKeyUp)))
        {
            var key = Marshal.ReadInt32(lParam);
            if (key == _virtualKey)
            {
                var now = Environment.TickCount64;
                if (now - _lastPressAt < DuplicateKeyupWindowMs)
                {
                    AppLog.Info("Hotkey", $"Suppressed duplicate press. vk={_virtualKey} dt={now - _lastPressAt}ms");
                    return CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                _lastPressAt = now;
                AppLog.Info("Hotkey", $"Accepted press. vk={_virtualKey} msg=0x{wParam.ToInt64():X}");
                Pressed?.Invoke();
            }
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using var currentProcess = Process.GetCurrentProcess();
        using var currentModule = currentProcess.MainModule;
        return SetWindowsHookEx(WhKeyboardLl, proc, GetModuleHandle(currentModule?.ModuleName), 0);
    }

    private static int ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + (key - Key.A);
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return 0x30 + (key - Key.D0);
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return 0x60 + (key - Key.NumPad0);
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return 0x70 + (key - Key.F1);
        }

        return key switch
        {
            Key.Space => 0x20,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Up => 0x26,
            Key.Down => 0x28,
            Key.Left => 0x25,
            Key.Right => 0x27,
            _ => 0,
        };
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

