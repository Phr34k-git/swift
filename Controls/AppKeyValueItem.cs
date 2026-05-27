using System.Collections.Generic;
using Client.ViewModels;

namespace Client.Controls;

public sealed record AppKeyValueItem(string Entry, string Value, string ValueForegroundResourceKey = "TextPrimary", IReadOnlyList<ColoredTextLineViewModel>? ColoredLines = null);
