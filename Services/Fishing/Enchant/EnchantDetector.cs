using System;
using System.Text;

namespace Client.Services.Fishing;

internal sealed class EnchantDetector : IDisposable
{
    private readonly HotbarRodReader _rodReader = new();

    public EnchantSnapshot Read()
    {
        var displayText = _rodReader.GetHotbarRodDisplayText();
        var enchant = FindEnchant(displayText);
        return new EnchantSnapshot(
            RodText: string.IsNullOrWhiteSpace(displayText) ? string.Empty : displayText,
            Enchant: enchant,
            SourceText: displayText);
    }

    public void Dispose()
    {
        _rodReader.Dispose();
    }

    private static string FindEnchant(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (var enchant in EnchantCatalog.All)
        {
            if (text.Contains(enchant, StringComparison.OrdinalIgnoreCase))
            {
                return enchant;
            }

            var normalizedText = NormalizeForMatch(text);
            var normalizedEnchant = NormalizeForMatch(enchant);
            if (normalizedEnchant.Length > 0 && normalizedText.Contains(normalizedEnchant, StringComparison.Ordinal))
            {
                return enchant;
            }
        }

        return string.Empty;
    }

    private static string NormalizeForMatch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}

internal sealed record EnchantSnapshot(string RodText, string Enchant, string SourceText);
