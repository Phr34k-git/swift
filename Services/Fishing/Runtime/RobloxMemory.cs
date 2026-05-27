using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Client.Services.Fishing;

internal sealed class RobloxMemory : IDisposable
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private readonly IOffsetsSource _offsets;
    private IntPtr _handle;
    private Process? _process;
    private ulong _baseAddress;
    private static readonly IReadOnlyDictionary<string, string> OffsetAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FakeDataModelPointer"] = "FakeDataModel.Pointer",
            ["FakeDataModelToDataModel"] = "FakeDataModel.RealDataModel",
            ["LocalPlayer"] = "Player.LocalPlayer",
            ["Children"] = "Instance.ChildrenStart",
            ["ChildrenEnd"] = "Instance.ChildrenEnd",
            ["Name"] = "Instance.Name",
            ["Parent"] = "Instance.Parent",
            ["ClassDescriptor"] = "Instance.ClassDescriptor",
            ["ClassDescriptorToClassName"] = "Instance.ClassName",
            ["StringLength"] = "Misc.StringLength",
            ["FramePositionX"] = "GuiObject.Position",
            ["FrameSizeX"] = "GuiObject.Size",
            ["ScreenGuiEnabled"] = "GuiObject.ScreenGui_Enabled",
            ["FrameVisible"] = "GuiObject.Visible",
            ["Text"] = "GuiObject.Text",
            ["TextLabelText"] = "GuiObject.Text",
            ["ContentText"] = "GuiObject.Text",
            ["AbsolutePosition"] = "GuiBase2D.AbsolutePosition",
            ["AbsoluteSize"] = "GuiBase2D.AbsoluteSize",
        };

    public RobloxMemory(IOffsetsSource offsets)
    {
        _offsets = offsets ?? throw new ArgumentNullException(nameof(offsets));
    }

    public IntPtr WindowHandle => _process?.MainWindowHandle ?? IntPtr.Zero;

    public void EnsureAttached()
    {
        if (_process is { HasExited: false } && _handle != IntPtr.Zero && _offsets.IsPopulated)
        {
            return;
        }

        Detach();
        if (!_offsets.IsPopulated)
        {
            throw new InvalidOperationException(
                "Offsets unavailable; sign in and stay connected to receive offsets.");
        }
        _process = FindRobloxProcess();
        _baseAddress = unchecked((ulong)_process.MainModule!.BaseAddress.ToInt64());
        _handle = OpenProcess(ProcessQueryInformation | ProcessQueryLimitedInformation | ProcessVmRead, false, _process.Id);
        if (_handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Could not open Roblox for memory reads. Run the client as x64 and, if needed, as administrator.");
        }
    }

    public ulong GetOffset(string key)
    {
        if (_offsets.TryGetOffset(key, out var offset))
        {
            return offset;
        }

        if (OffsetAliases.TryGetValue(key, out var alias) && _offsets.TryGetOffset(alias, out offset))
        {
            return offset;
        }

        throw new KeyNotFoundException($"Missing Roblox offset: {key}");
    }

    public ulong GetDataModel()
    {
        EnsureAttached();
        var fakeDataModel = ReadPtr(_baseAddress + GetOffset("FakeDataModelPointer"));
        var dataModel = ReadPtr(fakeDataModel + GetOffset("FakeDataModelToDataModel"));
        return IsValidAddress(dataModel) ? dataModel : 0;
    }

    public ulong GetLocalPlayer()
    {
        var players = FindDescendantByClass(GetDataModel(), "Players");
        if (players == 0)
        {
            return 0;
        }

        var localPlayer = ReadPtr(players + GetOffset("LocalPlayer"));
        return IsValidAddress(localPlayer) ? localPlayer : 0;
    }

    public ulong FindPlayerGui()
    {
        var localPlayer = GetLocalPlayer();
        return localPlayer == 0 ? 0 : FindChildByClass(localPlayer, "PlayerGui");
    }

    public ulong FindWorkspace()
    {
        var dataModel = GetDataModel();
        foreach (var child in ReadChildren(dataModel))
        {
            if (string.Equals(ReadName(child), "Workspace", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ReadClass(child), "Workspace", StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return 0;
    }

    public ulong FindChildByName(ulong parent, string name)
    {
        foreach (var child in ReadChildren(parent))
        {
            if (string.Equals(ReadName(child), name, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return 0;
    }

    public ulong FindChildByClass(ulong parent, string className)
    {
        foreach (var child in ReadChildren(parent))
        {
            if (string.Equals(ReadClass(child), className, StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }
        }

        return 0;
    }

    public ulong FindDescendantByClass(ulong root, string className)
    {
        return FindDescendant(root, item => string.Equals(ReadClass(item), className, StringComparison.OrdinalIgnoreCase));
    }

    public ulong FindDescendantByName(ulong root, string name)
    {
        return FindDescendant(root, item => string.Equals(ReadName(item), name, StringComparison.OrdinalIgnoreCase));
    }

    public ulong FindDescendantFrameByName(ulong root, string name)
    {
        return FindDescendant(root, item =>
            string.Equals(ReadName(item), name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ReadClass(item), "Frame", StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<ulong> ReadChildren(ulong instance)
    {
        if (!IsValidAddress(instance))
        {
            return Array.Empty<ulong>();
        }

        var listPtr = ReadPtr(instance + GetOffset("Children"));
        var arrayStart = ReadPtr(listPtr);
        var arrayEnd = ReadPtr(listPtr + GetOffset("ChildrenEnd"));
        if (!IsVectorRange(arrayStart, arrayEnd))
        {
            return Array.Empty<ulong>();
        }

        var children = new List<ulong>();
        for (var entry = arrayStart; entry < arrayEnd && children.Count < 2000; entry += 0x10)
        {
            var child = ReadPtr(entry);
            if (IsValidAddress(child))
            {
                children.Add(child);
            }
        }

        return children;
    }

    public string ReadName(ulong instance)
    {
        return ReadString(ReadPtr(instance + GetOffset("Name")));
    }

    public string ReadClass(ulong instance)
    {
        var descriptor = ReadPtr(instance + GetOffset("ClassDescriptor"));
        return ReadString(ReadPtr(descriptor + GetOffset("ClassDescriptorToClassName")));
    }

    public string ReadGuiText(ulong instance)
    {
        foreach (var key in new[] { "Text", "TextLabelText", "ContentText" })
        {
            if (!TryGetOffset(key, out var offset))
            {
                continue;
            }

            var indirect = ReadString(ReadPtr(instance + offset));
            if (!string.IsNullOrEmpty(indirect))
            {
                return indirect;
            }

            var direct = ReadString(instance + offset);
            if (!string.IsNullOrEmpty(direct))
            {
                return direct;
            }
        }

        return string.Empty;
    }

    public UDim ReadFramePosition(ulong frame)
    {
        var offset = GetOffset("FramePositionX");
        return new UDim(
            ReadFloat(frame + offset),
            ReadInt32(frame + offset + 0x4),
            ReadFloat(frame + offset + 0x8),
            ReadInt32(frame + offset + 0xC));
    }

    public UDim ReadFrameSize(ulong frame)
    {
        var offset = GetOffset("FrameSizeX");
        return new UDim(
            ReadFloat(frame + offset),
            ReadInt32(frame + offset + 0x4),
            ReadFloat(frame + offset + 0x8),
            ReadInt32(frame + offset + 0xC));
    }

    public bool IsVisible(ulong frame, string offsetKey)
    {
        return !TryGetOffset(offsetKey, out var offset) || ReadByte(frame + offset) != 0;
    }

    private bool TryGetOffset(string key, out ulong offset)
    {
        if (_offsets.TryGetOffset(key, out offset)) return true;
        if (OffsetAliases.TryGetValue(key, out var alias)) return _offsets.TryGetOffset(alias, out offset);
        offset = 0;
        return false;
    }

    public GuiBounds? ReadGuiBounds(ulong instance, bool visibleRequired)
    {
        if (!IsValidAddress(instance))
        {
            return null;
        }

        if (visibleRequired && !IsVisible(instance, "FrameVisible"))
        {
            return null;
        }

        var positionOffset = GetOffset("AbsolutePosition");
        var sizeOffset = GetOffset("AbsoluteSize");
        var x = ReadFloat(instance + positionOffset);
        var y = ReadFloat(instance + positionOffset + 0x4);
        var width = ReadFloat(instance + sizeOffset);
        var height = ReadFloat(instance + sizeOffset + 0x4);
        ApplyWindowDpiScale(ref x, ref y, ref width, ref height);
        if (!IsReasonableFloat(x) || !IsReasonableFloat(y) || !IsReasonableFloat(width) || !IsReasonableFloat(height) ||
            width <= 1 || height <= 1)
        {
            return null;
        }

        return new GuiBounds(x, y, width, height);
    }

    public ulong ReadPtr(ulong address)
    {
        var bytes = ReadBytes(address, IntPtr.Size);
        if (bytes is null)
        {
            return 0;
        }

        return IntPtr.Size == 8
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public int ReadInt32(ulong address)
    {
        var bytes = ReadBytes(address, 4);
        return bytes is null ? 0 : BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public byte ReadByte(ulong address)
    {
        var bytes = ReadBytes(address, 1);
        return bytes is null ? (byte)0 : bytes[0];
    }

    public float ReadFloat(ulong address)
    {
        var bytes = ReadBytes(address, 4);
        return bytes is null ? 0 : BitConverter.ToSingle(bytes);
    }

    public string ReadString(ulong address)
    {
        if (!IsValidAddress(address))
        {
            return string.Empty;
        }

        var length = ReadInt32(address + GetOffset("StringLength"));
        if (length <= 0 || length > 1000)
        {
            return string.Empty;
        }

        var dataAddress = length > 15 ? ReadPtr(address) : address;
        var bytes = ReadBytes(dataAddress, length);
        return bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    public byte[]? ReadBytes(ulong address, int count)
    {
        if (!IsValidAddress(address) || count <= 0 || _handle == IntPtr.Zero)
        {
            return null;
        }

        var buffer = new byte[count];
        return ReadProcessMemory(_handle, new IntPtr(unchecked((long)address)), buffer, count, out var read) &&
            read.ToInt64() == count
            ? buffer
            : null;
    }

    public void Dispose()
    {
        Detach();
    }

    private ulong FindDescendant(ulong root, Func<ulong, bool> predicate)
    {
        if (!IsValidAddress(root))
        {
            return 0;
        }

        var queue = new Queue<(ulong Address, int Depth)>();
        var seen = new HashSet<ulong>();
        queue.Enqueue((root, 0));
        var inspected = 0;
        while (queue.Count > 0 && inspected++ < 60000)
        {
            var (address, depth) = queue.Dequeue();
            if (!seen.Add(address))
            {
                continue;
            }

            if (predicate(address))
            {
                return address;
            }

            if (depth >= 128)
            {
                continue;
            }

            foreach (var child in ReadChildren(address))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return 0;
    }

    private void Detach()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }

        _process = null;
        _baseAddress = 0;
    }

    private static Process FindRobloxProcess()
    {
        var process = Process.GetProcessesByName("RobloxPlayerBeta")
            .OrderByDescending(SafeStartTimeTicks)
            .FirstOrDefault();
        process ??= Process.GetProcesses()
            .Where(item => item.ProcessName.Contains("Roblox", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(SafeStartTimeTicks)
            .FirstOrDefault();

        return process ?? throw new InvalidOperationException("No running Roblox process was found.");
    }

    private static long SafeStartTimeTicks(Process process)
    {
        try
        {
            return process.StartTime.Ticks;
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsVectorRange(ulong start, ulong end)
    {
        return IsValidAddress(start) && IsValidAddress(end) && end >= start && end - start <= 1048576 && (end - start) % 0x10 == 0;
    }

    public static bool IsValidAddress(ulong address)
    {
        return address >= 0x10000 && address <= 0x00007FFFFFFFFFFF;
    }

    private static bool IsReasonableFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < 100000;
    }

    private void ApplyWindowDpiScale(ref float x, ref float y, ref float width, ref float height)
    {
        var hwnd = WindowHandle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        uint dpi;
        try
        {
            dpi = GetDpiForWindow(hwnd);
        }
        catch
        {
            return;
        }

        if (dpi <= 96)
        {
            return;
        }

        var scale = dpi / 96f;
        x *= scale;
        y *= scale;
        width *= scale;
        height *= scale;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr baseAddress, byte[] buffer, int size, out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}

internal readonly record struct UDim(float XScale, int XOffset, float YScale, int YOffset);

internal readonly record struct GuiBounds(float X, float Y, float Width, float Height);
