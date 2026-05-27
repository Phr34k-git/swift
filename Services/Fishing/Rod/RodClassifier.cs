using System;
using System.Text.RegularExpressions;

namespace Client.Services.Fishing;

internal static class RodClassifier
{
    public static RodKind Classify(string displayText)
    {
        var text = Normalize(displayText);
        if (text.Length == 0)
        {
            return RodKind.Default;
        }

        if (text.Contains("bellona", StringComparison.Ordinal) &&
            text.Contains("waraxe", StringComparison.Ordinal))
        {
            return RodKind.BellonaWaraxe;
        }

        if (text.Contains("masterline", StringComparison.Ordinal))
        {
            return RodKind.MasterlineRod;
        }

        if (text.Contains("tranquility", StringComparison.Ordinal))
        {
            return RodKind.Tranquility;
        }

        if (text.Contains("pinion", StringComparison.Ordinal))
        {
            return RodKind.Pinion;
        }

        if (text.Contains("dreambreaker", StringComparison.Ordinal))
        {
            return RodKind.Dreambreaker;
        }

        if (text.Contains("requiem", StringComparison.Ordinal))
        {
            return RodKind.Requiem;
        }

        if (text.Contains("splitbranch", StringComparison.Ordinal) &&
            text.Contains("twig", StringComparison.Ordinal))
        {
            return RodKind.SplitbranchTwig;
        }

        if (text.Contains("migu", StringComparison.Ordinal))
        {
            return RodKind.MiguRod;
        }

        return RodKind.Default;
    }

    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stripped = Regex.Replace(text, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        return stripped.ToLowerInvariant().Trim();
    }
}
