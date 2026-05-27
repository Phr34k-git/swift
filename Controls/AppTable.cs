using System;
using System.Collections.Generic;
using Avalonia.Animation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Client.Controls;

public sealed class AppTable : UserControl
{
    public static readonly StyledProperty<IReadOnlyList<AppTableColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<AppTable, IReadOnlyList<AppTableColumn>?>(nameof(Columns));

    public static readonly StyledProperty<IReadOnlyList<AppTableRow>?> RowsProperty =
        AvaloniaProperty.Register<AppTable, IReadOnlyList<AppTableRow>?>(nameof(Rows));

    public static readonly StyledProperty<string> EmptyTitleProperty =
        AvaloniaProperty.Register<AppTable, string>(nameof(EmptyTitle), "No rows yet");

    public static readonly StyledProperty<string> EmptyDescriptionProperty =
        AvaloniaProperty.Register<AppTable, string>(nameof(EmptyDescription), "Entries will appear here when they are available.");

    public IReadOnlyList<AppTableColumn>? Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public IReadOnlyList<AppTableRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public string EmptyTitle
    {
        get => GetValue(EmptyTitleProperty);
        set => SetValue(EmptyTitleProperty, value);
    }

    public string EmptyDescription
    {
        get => GetValue(EmptyDescriptionProperty);
        set => SetValue(EmptyDescriptionProperty, value);
    }

    private const double ScrollTrackHeight = 6;
    private const double ThumbHeight = 4;
    private const double ThumbMinWidth = 24;

    private ScrollViewer? scrollViewer;
    private Border? scrollTrack;
    private Border? scrollThumb;
    private bool isDraggingScrollThumb;
    private bool keepScrollThumbVisibleUntilPointerExit;
    private double dragStartPointerX;
    private double dragStartOffsetX;

    public AppTable()
    {
        Background = Brushes.Transparent;

        AddHandler(PointerEnteredEvent, OnPointerEntered, RoutingStrategies.Direct, handledEventsToo: true);
        AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Direct, handledEventsToo: true);

        BuildTable();
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        keepScrollThumbVisibleUntilPointerExit = false;
        UpdateThumbVisibility();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (isDraggingScrollThumb)
        {
            return;
        }

        keepScrollThumbVisibleUntilPointerExit = false;
        UpdateThumbVisibility();
    }

    private void UpdateThumbVisibility()
    {
        if (scrollThumb is null || scrollViewer is null)
        {
            return;
        }

        var hasOverflow = scrollViewer.Extent.Width > scrollViewer.Viewport.Width + 0.5;
        scrollThumb.IsVisible = hasOverflow;
        scrollThumb.Opacity = (IsPointerOver || isDraggingScrollThumb || keepScrollThumbVisibleUntilPointerExit) && hasOverflow ? 1 : 0;
    }

    private void UpdateThumbGeometry()
    {
        if (scrollViewer is null || scrollThumb is null || scrollTrack is null)
        {
            return;
        }

        var trackWidth = scrollTrack.Bounds.Width;
        if (trackWidth <= 0)
        {
            return;
        }

        var viewport = scrollViewer.Viewport.Width;
        var extent = scrollViewer.Extent.Width;
        var offset = scrollViewer.Offset.X;

        if (extent <= viewport + 0.5)
        {
            scrollThumb.IsVisible = false;
            return;
        }

        var thumbWidth = Math.Max(ThumbMinWidth, viewport / extent * trackWidth);
        thumbWidth = Math.Min(thumbWidth, trackWidth);

        var scrollRange = extent - viewport;
        var availableTravel = trackWidth - thumbWidth;
        var thumbOffset = scrollRange > 0 ? offset / scrollRange * availableTravel : 0;

        scrollThumb.Width = thumbWidth;
        scrollThumb.Margin = new Thickness(thumbOffset, 0, 0, 0);

        UpdateThumbVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ColumnsProperty ||
            change.Property == RowsProperty ||
            change.Property == EmptyTitleProperty ||
            change.Property == EmptyDescriptionProperty)
        {
            BuildTable();
        }
    }

    private void BuildTable()
    {
        var columns = Columns ?? [];
        var rows = Rows ?? [];

        var tableGrid = new Grid
        {
            RowDefinitions = new RowDefinitions(),
            ColumnDefinitions = new ColumnDefinitions(),
            Background = GetBrush("TableGap", "#0E0E0E"),
            RowSpacing = 1,
            ColumnSpacing = 1,
            ClipToBounds = true,
        };

        if (columns.Count == 0)
        {
            var inferredColumnCount = 1;
            foreach (var row in rows)
            {
                if (row.Cells.Count > inferredColumnCount)
                {
                    inferredColumnCount = row.Cells.Count;
                }
            }

            for (var i = 0; i < inferredColumnCount; i++)
            {
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            }
        }
        else
        {
            foreach (var column in columns)
            {
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition(column.Width));
            }
        }

        var rowIndex = 0;
        if (columns.Count > 0)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition(new GridLength(36)));
            AddHeaderRow(tableGrid, columns, rowIndex++);
        }

        if (rows.Count == 0)
        {
            tableGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddEmptyState(tableGrid, rowIndex, columns.Count);
        }
        else
        {
            var lastDataRowIndex = rowIndex + rows.Count - 1;
            foreach (var row in rows)
            {
                tableGrid.RowDefinitions.Add(new RowDefinition(new GridLength(38)));
                AddDataRow(tableGrid, row, columns.Count, rowIndex, rowIndex == lastDataRowIndex);
                rowIndex++;
            }
        }

        scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
            Content = tableGrid,
        };
        scrollViewer.ScrollChanged += (_, _) => UpdateThumbGeometry();
        scrollViewer.SizeChanged += (_, _) => UpdateThumbGeometry();

        scrollThumb = new Border
        {
            Height = ThumbHeight,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = GetBrush("TextSecondary", "#777777"),
            CornerRadius = new CornerRadius(2),
            IsVisible = false,
            Opacity = 0,
            IsHitTestVisible = true,
            Transitions = new Transitions
            {
                new DoubleTransition
                {
                    Property = Visual.OpacityProperty,
                    Duration = TimeSpan.FromMilliseconds(110),
                },
            },
        };

        scrollTrack = new Border
        {
            Height = ScrollTrackHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = Brushes.Transparent,
            Margin = new Thickness(4, 0, 4, 2),
            IsHitTestVisible = true,
            Child = scrollThumb,
        };
        scrollTrack.SizeChanged += (_, _) => UpdateThumbGeometry();
        scrollTrack.PointerPressed += OnScrollTrackPointerPressed;
        scrollTrack.PointerMoved += OnScrollTrackPointerMoved;
        scrollTrack.PointerReleased += OnScrollTrackPointerReleased;
        scrollTrack.PointerCaptureLost += OnScrollTrackPointerCaptureLost;

        var layout = new Grid();
        layout.Children.Add(scrollViewer);
        layout.Children.Add(scrollTrack);

        Content = new Border
        {
            Background = GetBrush("TableGap", "#0E0E0E"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Child = layout,
        };
    }

    private void OnScrollTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (scrollTrack is null || scrollViewer is null ||
            !e.GetCurrentPoint(scrollTrack).Properties.IsLeftButtonPressed ||
            !TryGetScrollbarMetrics(out _, out var thumbWidth, out var scrollRange, out var availableTravel))
        {
            return;
        }

        var pointerX = e.GetPosition(scrollTrack).X;
        var thumbOffset = GetThumbOffset(scrollRange, availableTravel);

        if (pointerX < thumbOffset || pointerX > thumbOffset + thumbWidth)
        {
            ScrollToThumbOffset(pointerX - thumbWidth / 2, scrollRange, availableTravel);
        }

        dragStartPointerX = pointerX;
        dragStartOffsetX = scrollViewer.Offset.X;
        isDraggingScrollThumb = true;
        UpdateThumbVisibility();

        e.Pointer.Capture(scrollTrack);
        e.Handled = true;
    }

    private void OnScrollTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isDraggingScrollThumb || scrollTrack is null ||
            !TryGetScrollbarMetrics(out _, out _, out var scrollRange, out var availableTravel))
        {
            return;
        }

        if (!e.GetCurrentPoint(scrollTrack).Properties.IsLeftButtonPressed)
        {
            StopDraggingScrollThumb(e.Pointer);
            return;
        }

        var delta = e.GetPosition(scrollTrack).X - dragStartPointerX;
        var scrollDelta = availableTravel > 0 ? delta / availableTravel * scrollRange : 0;
        ScrollToHorizontalOffset(dragStartOffsetX + scrollDelta, scrollRange);
        e.Handled = true;
    }

    private void OnScrollTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        keepScrollThumbVisibleUntilPointerExit = IsPointInsideTable(e.GetPosition(this));
        StopDraggingScrollThumb(e.Pointer);
        e.Handled = true;
    }

    private void OnScrollTrackPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        isDraggingScrollThumb = false;
        UpdateThumbVisibility();
    }

    private bool IsPointInsideTable(Point point)
    {
        return point.X >= 0 &&
               point.Y >= 0 &&
               point.X <= Bounds.Width &&
               point.Y <= Bounds.Height;
    }

    private void StopDraggingScrollThumb(IPointer pointer)
    {
        if (!isDraggingScrollThumb)
        {
            return;
        }

        isDraggingScrollThumb = false;
        pointer.Capture(null);
        UpdateThumbVisibility();
    }

    private bool TryGetScrollbarMetrics(out double trackWidth, out double thumbWidth, out double scrollRange, out double availableTravel)
    {
        trackWidth = 0;
        thumbWidth = 0;
        scrollRange = 0;
        availableTravel = 0;

        if (scrollViewer is null || scrollTrack is null)
        {
            return false;
        }

        trackWidth = scrollTrack.Bounds.Width;
        var viewport = scrollViewer.Viewport.Width;
        var extent = scrollViewer.Extent.Width;

        if (trackWidth <= 0 || extent <= viewport + 0.5)
        {
            return false;
        }

        thumbWidth = Math.Min(Math.Max(ThumbMinWidth, viewport / extent * trackWidth), trackWidth);
        scrollRange = extent - viewport;
        availableTravel = trackWidth - thumbWidth;

        return scrollRange > 0 && availableTravel > 0;
    }

    private double GetThumbOffset(double scrollRange, double availableTravel)
    {
        if (scrollViewer is null || scrollRange <= 0)
        {
            return 0;
        }

        return scrollViewer.Offset.X / scrollRange * availableTravel;
    }

    private void ScrollToThumbOffset(double thumbOffset, double scrollRange, double availableTravel)
    {
        var clampedThumbOffset = Math.Clamp(thumbOffset, 0, availableTravel);
        ScrollToHorizontalOffset(clampedThumbOffset / availableTravel * scrollRange, scrollRange);
    }

    private void ScrollToHorizontalOffset(double offset, double scrollRange)
    {
        if (scrollViewer is null)
        {
            return;
        }

        scrollViewer.Offset = new Vector(Math.Clamp(offset, 0, scrollRange), scrollViewer.Offset.Y);
        UpdateThumbGeometry();
    }

    private void AddHeaderRow(Grid tableGrid, IReadOnlyList<AppTableColumn> columns, int rowIndex)
    {
        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            var cell = CreateCellBorder(rowIndex, columnIndex, columns.Count, isLastRow: Rows is null || Rows.Count == 0);
            cell.Background = GetBrush("TableHeaderBackground", "#1B1B1B");
            cell.Child = new TextBlock
            {
                Text = columns[columnIndex].Header,
                FontFamily = GetFontFamily("UIFont"),
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = GetBrush("TextSecondary", "#8A8A8A"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, columnIndex);
            tableGrid.Children.Add(cell);
        }
    }

    private void AddDataRow(Grid tableGrid, AppTableRow row, int columnCount, int rowIndex, bool isLastRow)
    {
        var effectiveColumnCount = columnCount == 0 ? tableGrid.ColumnDefinitions.Count : columnCount;

        for (var columnIndex = 0; columnIndex < effectiveColumnCount; columnIndex++)
        {
            var cell = CreateCellBorder(rowIndex, columnIndex, effectiveColumnCount, isLastRow);
            cell.Background = GetBrush("TableRowBackground", "#141414");
            cell.Opacity = row.IsEnabled ? 1 : 0.56;
            cell.Child = CreateCellContent(columnIndex < row.Cells.Count ? row.Cells[columnIndex] : null, row.IsEnabled);

            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, columnIndex);
            tableGrid.Children.Add(cell);
        }
    }

    private Control CreateCellContent(object? content, bool isEnabled)
    {
        if (content is Control control)
        {
            return control;
        }

        if (content is AppTableCell cell)
        {
            return CreateTextCell(cell.Text, isEnabled, cell.ForegroundResourceKey);
        }

        return CreateTextCell(content?.ToString() ?? string.Empty, isEnabled, "TextPrimary");
    }

    private TextBlock CreateTextCell(string text, bool isEnabled, string foregroundResourceKey)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = GetFontFamily("UIFont"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = isEnabled ? GetBrush(foregroundResourceKey, "#F0F0F0") : GetBrush("TextSecondary", "#777777"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void AddEmptyState(Grid tableGrid, int rowIndex, int columnCount)
    {
        var content = new StackPanel
        {
            Spacing = 4,
            Margin = new Thickness(16, 18),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        content.Children.Add(new TextBlock
        {
            Text = EmptyTitle,
            FontFamily = GetFontFamily("UIFont"),
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = GetBrush("TextPrimary", "#F0F0F0"),
        });
        content.Children.Add(new TextBlock
        {
            Text = EmptyDescription,
            FontFamily = GetFontFamily("UIFont"),
            FontSize = 12,
            Foreground = GetBrush("TextSecondary", "#777777"),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
        });

        var border = new Border
        {
            Background = GetBrush("TableEmptyBackground", "#141414"),
            Child = content,
        };

        Grid.SetRow(border, rowIndex);
        Grid.SetColumn(border, 0);
        Grid.SetColumnSpan(border, columnCount == 0 ? 1 : columnCount);
        tableGrid.Children.Add(border);
    }

    private Border CreateCellBorder(int rowIndex, int columnIndex, int columnCount, bool isLastRow)
    {
        return new Border
        {
            Padding = new Thickness(columnIndex == 0 ? 14 : 12, 0, 12, 0),
        };
    }

    private IBrush GetBrush(string key, string fallback)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.Parse(fallback));
    }

    private FontFamily GetFontFamily(string key)
    {
        if (this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is FontFamily fontFamily)
        {
            return fontFamily;
        }

        return FontFamily.Default;
    }
}
