using System;
using System.Linq;
using Client.Services;

namespace Client.Diagnostics;

public static class HwidCommand
{
    public static bool TryHandle(string[] args)
    {
        if (!args.Any(arg => string.Equals(arg, "--hwid", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        try
        {
            var result = new HwidService().GetCurrent();

            Console.WriteLine("OpenMacro HWID");
            Console.WriteLine($"Hash: {result.Hash}");
            Console.WriteLine($"Components used: {string.Join(", ", result.ComponentsUsed)}");
            Console.WriteLine($"Machine GUID present: {FormatPresence(result.Components.MachineGuid)}");
            Console.WriteLine($"SMBIOS UUID present: {FormatPresence(result.Components.SmbiosUuid)}");
            Console.WriteLine($"CPU identifier present: {FormatPresence(result.Components.CpuIdentifier)}");
            Console.WriteLine($"Baseboard identifier present: {FormatPresence(result.Components.BaseboardIdentifier)}");
            Console.WriteLine($"System identifier present: {FormatPresence(result.Components.SystemIdentifier)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"HWID collection failed: {ex.Message}");
            Environment.ExitCode = 1;
            return true;
        }
    }

    private static string FormatPresence(string? value)
    {
        return HwidService.IsJunk(value) ? "no" : "yes";
    }
}
