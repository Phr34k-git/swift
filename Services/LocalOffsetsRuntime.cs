using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Client.Services.Fishing;

namespace Client.Services;

// Loads offsets from local offsets.hpp so this build can run without API/auth.
internal sealed class LocalOffsetsRuntime : IOffsetsRuntime
{
    private readonly Dictionary<string, ulong> _offsets = new(StringComparer.OrdinalIgnoreCase);

    public LocalOffsetsRuntime()
    {
        LoadFromLocalOffsetsFile();
    }

    public string? Version { get; private set; } = "local";

    public bool IsPopulated => _offsets.Count > 0;

    public bool TryGetOffset(string key, out ulong value) => _offsets.TryGetValue(key, out value);

    public Task RefreshAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        _ = accessToken;
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public void Clear()
    {
        // Keep local offsets loaded in this no-security copy.
    }

    private void LoadFromLocalOffsetsFile()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "offsets.hpp"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "offsets.hpp"),
            Path.Combine(Environment.CurrentDirectory, "offsets.hpp"),
        };

        string? file = null;
        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (File.Exists(full))
            {
                file = full;
                break;
            }
        }

        if (file is null)
        {
            throw new FileNotFoundException("offsets.hpp not found for local offsets mode.");
        }

        var namespaceRegex = new Regex(@"^\s*namespace\s+([A-Za-z0-9_]+)\s*\{", RegexOptions.Compiled);
        var valueRegex = new Regex(@"^\s*inline\s+constexpr\s+uintptr_t\s+([A-Za-z0-9_]+)\s*=\s*0x([0-9A-Fa-f]+);", RegexOptions.Compiled);
        var versionRegex = new Regex(@"ClientVersion\s*=\s*""([^""]+)""", RegexOptions.Compiled);
        var stack = new Stack<string>();

        foreach (var line in File.ReadLines(file))
        {
            var versionMatch = versionRegex.Match(line);
            if (versionMatch.Success)
            {
                Version = versionMatch.Groups[1].Value;
            }

            var nsMatch = namespaceRegex.Match(line);
            if (nsMatch.Success)
            {
                var ns = nsMatch.Groups[1].Value;
                if (!string.Equals(ns, "Offsets", StringComparison.OrdinalIgnoreCase))
                {
                    stack.Push(ns);
                }
                continue;
            }

            if (line.Contains('}') && stack.Count > 0)
            {
                stack.Pop();
            }

            var valMatch = valueRegex.Match(line);
            if (!valMatch.Success || stack.Count == 0)
            {
                continue;
            }

            var name = valMatch.Groups[1].Value;
            var hex = valMatch.Groups[2].Value;
            if (!ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
            {
                continue;
            }

            var nsName = stack.Peek();
            _offsets[$"{nsName}.{name}"] = value;
            _offsets.TryAdd(name, value);
        }

        if (_offsets.Count == 0)
        {
            throw new InvalidDataException("No offsets parsed from offsets.hpp.");
        }
    }
}
