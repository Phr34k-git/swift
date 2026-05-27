using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Client.Services.Fishing;

namespace Client.ViewModels;

internal static class ColoredAppraiseText
{
    private static readonly IReadOnlyList<string> KnownNames = AppraiseColors.GetKnownMutationNames()
        .OrderByDescending(item => item.Length)
        .ToList();

    private static IBrush GetTextPrimary()
    {
        if (Application.Current?.Resources.TryGetResource("TextPrimary", null, out var res) == true && res is IBrush brush)
            return brush;
        return Brushes.White;
    }

    public static IReadOnlyList<ColoredTextLineViewModel> BuildLines(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text)
            ? "---"
            : text.Replace("\r", string.Empty, StringComparison.Ordinal);

        return normalized.Split('\n')
            .Select(line => new ColoredTextLineViewModel(BuildSegments(line.Length == 0 ? " " : line)))
            .ToList();
    }

    private static IReadOnlyList<ColoredTextSegmentViewModel> BuildSegments(string text)
    {
        var result = new List<ColoredTextSegmentViewModel>();
        var plainStart = 0;
        var index = 0;

        while (index < text.Length)
        {
            var match = FindMatchAt(text, index);
            if (match is null)
            {
                index++;
                continue;
            }

            if (index > plainStart)
            {
                result.Add(new ColoredTextSegmentViewModel(text[plainStart..index], GetTextPrimary()));
            }

            result.Add(new ColoredTextSegmentViewModel(text.Substring(index, match.Length), AppraiseColors.GetAppraiseBrush(match)));
            index += match.Length;
            plainStart = index;
        }

        if (plainStart < text.Length)
        {
            result.Add(new ColoredTextSegmentViewModel(text[plainStart..], GetTextPrimary()));
        }

        if (result.Count == 0)
        {
            result.Add(new ColoredTextSegmentViewModel(text, GetTextPrimary()));
        }

        return result;
    }

    private static string? FindMatchAt(string text, int index)
    {
        foreach (var name in KnownNames)
        {
            if (index + name.Length > text.Length ||
                !string.Equals(text.Substring(index, name.Length), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (HasTextBoundary(text, index - 1) && HasTextBoundary(text, index + name.Length))
            {
                return name;
            }
        }

        return null;
    }

    private static bool HasTextBoundary(string text, int index)
    {
        if (index < 0 || index >= text.Length)
        {
            return true;
        }

        var ch = text[index];
        return !char.IsLetterOrDigit(ch) && ch != '\'';
    }
}
