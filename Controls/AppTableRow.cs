using System.Collections.Generic;

namespace Client.Controls;

public sealed record AppTableRow(IReadOnlyList<object?> Cells, bool IsEnabled = true);
