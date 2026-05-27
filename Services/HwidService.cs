using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Client.Services;

public sealed class HwidService
{
    private const string CanonicalVersion = "v2";
    private const int MinimumSignalsRequired = 2;

    private static readonly HashSet<string> JunkValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "",
        "0",
        "00000000",
        "default string",
        "none",
        "not applicable",
        "not available",
        "not specified",
        "system serial number",
        "to be filled by o.e.m.",
        "to be filled by oem",
        "unknown",
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff"
    };

    public HwidResult GetCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("HWID collection is currently implemented for Windows only.");
        }

        var components = new HwidComponents(
            MachineGuid: SafeQuery(QueryMachineGuid),
            SmbiosUuid: SafeQuery(QuerySmbiosSystemUuid),
            CpuIdentifier: SafeQuery(QueryCpuIdentifier),
            BaseboardIdentifier: SafeQuery(QueryBaseboardIdentifier),
            SystemIdentifier: SafeQuery(QuerySystemIdentifier));

        var slots = BuildSlots(components);
        var nonJunkSlotCount = slots.Count(s => !string.IsNullOrEmpty(s.Value));

        if (nonJunkSlotCount < MinimumSignalsRequired)
        {
            throw new InvalidOperationException(
                $"HWID requires at least {MinimumSignalsRequired} stable hardware signals but only {nonJunkSlotCount} were available.");
        }

        var canonical = CanonicalVersion + "|" + string.Join("|", slots.Select(s => s.Key + "=" + s.Value));

        return new HwidResult(
            Hash: ComputeSha256Hex(canonical),
            Components: components,
            ComponentsUsed: slots.Where(s => !string.IsNullOrEmpty(s.Value)).Select(s => s.Key).ToArray());
    }

    public static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    public static bool IsJunk(string? value)
    {
        return JunkValues.Contains(Normalize(value));
    }

    public static string ComputeSha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IReadOnlyList<KeyValuePair<string, string>> BuildSlots(HwidComponents components)
    {
        return new[]
        {
            new KeyValuePair<string, string>("machine_guid", SlotValue(components.MachineGuid)),
            new KeyValuePair<string, string>("smbios_uuid", SlotValue(components.SmbiosUuid)),
            new KeyValuePair<string, string>("cpu", SlotValue(components.CpuIdentifier)),
            new KeyValuePair<string, string>("baseboard", SlotValue(components.BaseboardIdentifier)),
            new KeyValuePair<string, string>("system", SlotValue(components.SystemIdentifier))
        };
    }

    private static string SlotValue(string? raw)
    {
        var normalized = Normalize(raw);
        return JunkValues.Contains(normalized) ? string.Empty : normalized;
    }

    private static string? SafeQuery(Func<string?> query)
    {
        try
        {
            return query();
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? QueryMachineGuid()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return key?.GetValue("MachineGuid")?.ToString();
    }

    [SupportedOSPlatform("windows")]
    private static string? QueryCpuIdentifier()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        if (key is null)
        {
            return null;
        }

        var vendor = key.GetValue("VendorIdentifier")?.ToString();
        var identifier = key.GetValue("Identifier")?.ToString();
        var name = key.GetValue("ProcessorNameString")?.ToString();

        return CombineParts(vendor, identifier, name);
    }

    [SupportedOSPlatform("windows")]
    private static string? QueryBaseboardIdentifier()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        if (key is null)
        {
            return null;
        }

        var manufacturer = key.GetValue("BaseBoardManufacturer")?.ToString();
        var product = key.GetValue("BaseBoardProduct")?.ToString();

        return CombineParts(manufacturer, product);
    }

    [SupportedOSPlatform("windows")]
    private static string? QuerySystemIdentifier()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
        if (key is null)
        {
            return null;
        }

        var manufacturer = key.GetValue("SystemManufacturer")?.ToString();
        var product = key.GetValue("SystemProductName")?.ToString();
        var family = key.GetValue("SystemFamily")?.ToString();

        return CombineParts(manufacturer, product, family);
    }

    private static string? CombineParts(params string?[] parts)
    {
        var clean = parts
            .Select(p => p?.Trim())
            .Where(p => !string.IsNullOrEmpty(p) && !JunkValues.Contains(p!.ToLowerInvariant()))
            .ToArray();
        return clean.Length == 0 ? null : string.Join(" ", clean!);
    }

    [SupportedOSPlatform("windows")]
    private static string? QuerySmbiosSystemUuid()
    {
        const uint rsmb = 0x52534D42; // 'RSMB'

        var size = GetSystemFirmwareTable(rsmb, 0, IntPtr.Zero, 0);
        if (size == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var written = GetSystemFirmwareTable(rsmb, 0, buffer, size);
            if (written == 0 || written > size)
            {
                return null;
            }

            var raw = new byte[written];
            Marshal.Copy(buffer, raw, 0, (int)written);
            return ParseSmbiosSystemUuid(raw);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static string? ParseSmbiosSystemUuid(byte[] raw)
    {
        // RawSMBIOSData header: Used20CallingMethod(1) MajorVersion(1) MinorVersion(1) DmiRevision(1) Length(4)
        const int headerSize = 8;
        if (raw.Length <= headerSize)
        {
            return null;
        }

        var smbiosMajor = raw[1];
        var tableLength = BitConverter.ToUInt32(raw, 4);
        var tableStart = headerSize;
        var tableEnd = (int)Math.Min(raw.Length, tableStart + tableLength);

        var offset = tableStart;
        while (offset + 4 <= tableEnd)
        {
            var type = raw[offset];
            var structLen = raw[offset + 1];
            if (structLen < 4 || offset + structLen > tableEnd)
            {
                break;
            }

            if (type == 1 && structLen >= 0x19)
            {
                // System Information: UUID at offset 0x08..0x17 (16 bytes) within the structure.
                var uuidStart = offset + 0x08;
                var uuidBytes = new byte[16];
                Array.Copy(raw, uuidStart, uuidBytes, 0, 16);
                return FormatSmbiosUuid(uuidBytes, smbiosMajor);
            }

            // Skip formatted area, then walk the string set (terminated by double-null).
            var next = offset + structLen;
            while (next + 1 < tableEnd && !(raw[next] == 0 && raw[next + 1] == 0))
            {
                next++;
            }
            next += 2;

            if (next <= offset)
            {
                break;
            }

            offset = next;

            if (type == 127) // End-of-table
            {
                break;
            }
        }

        return null;
    }

    private static string? FormatSmbiosUuid(byte[] u, byte smbiosMajor)
    {
        if (u.Length != 16)
        {
            return null;
        }

        var allZero = true;
        var allFf = true;
        for (var i = 0; i < 16; i++)
        {
            if (u[i] != 0x00) allZero = false;
            if (u[i] != 0xFF) allFf = false;
        }
        if (allZero || allFf)
        {
            return null;
        }

        // SMBIOS >= 2.6 stores the first three UUID fields in little-endian on x86;
        // standard textual form requires them in big-endian. Older versions stored
        // them already in big-endian, but in practice virtually every machine in the
        // wild reports 2.6+, so a single consistent transform keeps the hash stable.
        if (smbiosMajor >= 2)
        {
            (u[0], u[3]) = (u[3], u[0]);
            (u[1], u[2]) = (u[2], u[1]);
            (u[4], u[5]) = (u[5], u[4]);
            (u[6], u[7]) = (u[7], u[6]);
        }

        return string.Format(
            "{0:x2}{1:x2}{2:x2}{3:x2}-{4:x2}{5:x2}-{6:x2}{7:x2}-{8:x2}{9:x2}-{10:x2}{11:x2}{12:x2}{13:x2}{14:x2}{15:x2}",
            u[0], u[1], u[2], u[3], u[4], u[5], u[6], u[7], u[8], u[9], u[10], u[11], u[12], u[13], u[14], u[15]);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetSystemFirmwareTable(
        uint firmwareTableProviderSignature,
        uint firmwareTableId,
        IntPtr firmwareTableBuffer,
        uint bufferSize);
}

public sealed record HwidComponents(
    string? MachineGuid,
    string? SmbiosUuid,
    string? CpuIdentifier,
    string? BaseboardIdentifier,
    string? SystemIdentifier);

public sealed record HwidResult(
    string Hash,
    HwidComponents Components,
    IReadOnlyList<string> ComponentsUsed);
