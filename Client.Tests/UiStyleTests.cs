using System;
using System.IO;
using Client;
using Client.Services;
using Xunit;

namespace Client.Tests;

public sealed class UiStyleTests
{
    [Fact]
    public void AppButtonStyle_CentersContentByDefault()
    {
        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));

        Assert.Contains("<Style Selector=\"Button\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"HorizontalContentAlignment\" Value=\"Center\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"VerticalContentAlignment\" Value=\"Center\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppIcon_IsConfiguredForExecutableAndWindow()
    {
        var project = File.ReadAllText(FindRepoFile("Client.csproj"));
        var mainWindow = File.ReadAllText(FindRepoFile("MainWindow.axaml"));
        var iconPath = FindRepoFile(Path.Combine("Assets", "OpenMacro-Logo.ico"));

        Assert.Contains("<ApplicationIcon>Assets\\OpenMacro-Logo.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<AvaloniaResource Include=\"Assets\\OpenMacro-Logo.ico\" />", project, StringComparison.Ordinal);
        Assert.Contains("Icon=\"/Assets/OpenMacro-Logo.ico\"", mainWindow, StringComparison.Ordinal);
        Assert.True(new FileInfo(iconPath).Length > 0, "App icon asset should not be empty.");
    }

    [Fact]
    public void TreasureRefPort_UsesAotSafeMarshalSizeOf()
    {
        var source = File.ReadAllText(FindRepoFile(Path.Combine("Services", "Fishing", "Treasure", "TreasureRefPort.cs")));

        Assert.DoesNotContain("Marshal.SizeOf(typeof(", source, StringComparison.Ordinal);
        Assert.Contains("Marshal.SizeOf<Input>()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientFacingBranding_UsesSwiftName()
    {
        var sourceFiles = new[]
        {
            "MainWindow.axaml",
            Path.Combine("Views", "Account", "LoginView.axaml"),
            Path.Combine("Views", "Shared", "BlankView.axaml"),
            Path.Combine("Services", "LocalOAuthCallbackServer.cs"),
        };

        foreach (var sourceFile in sourceFiles)
        {
            var source = File.ReadAllText(FindRepoFile(sourceFile));

            Assert.DoesNotContain("XTernal", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Xternal", source, StringComparison.Ordinal);
            Assert.Contains("Swift", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ShellView_HasCompactTopUpdateRestartBanner()
    {
        var shellXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Shell", "ShellView.axaml")));

        Assert.Contains("Grid.Column=\"1\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Name=\"UpdateBannerRoot\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"40\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsUpdateAvailable}\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Update ready\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding UpdateStatusText}\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"update-restart\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"{Binding RestartUpdateButtonText}\"", shellXaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RestartUpdateCommand}\"", shellXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"UpdateSidebarRoot\"", shellXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableAppTable_DefinesNativeGridTableApi()
    {
        var tableSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTable.cs")));
        var cellSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTableCell.cs")));
        var columnSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTableColumn.cs")));
        var rowSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTableRow.cs")));

        Assert.Contains("public sealed class AppTable : UserControl", tableSource, StringComparison.Ordinal);
        Assert.Contains("ColumnsProperty", tableSource, StringComparison.Ordinal);
        Assert.Contains("RowsProperty", tableSource, StringComparison.Ordinal);
        Assert.Contains("EmptyTitleProperty", tableSource, StringComparison.Ordinal);
        Assert.Contains("BuildTable()", tableSource, StringComparison.Ordinal);
        Assert.Contains("AppTableCell", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedRowProperty", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnRowPointerPressed", tableSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record AppTableCell", cellSource, StringComparison.Ordinal);
        Assert.Contains("ForegroundResourceKey", cellSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record AppTableColumn", columnSource, StringComparison.Ordinal);
        Assert.Contains("GridLength Width", columnSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record AppTableRow", rowSource, StringComparison.Ordinal);
        Assert.Contains("IReadOnlyList<object?> Cells", rowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTable_UsesGlobalThemeResourcesForNativeStyling()
    {
        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));

        Assert.Contains("x:Key=\"TableSurface\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableRowBackground\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableGap\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableHeaderBackground\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"TableRowDisabled\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TableRowHover\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"TableRowSelected\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"#14201D\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Color=\"#18312B\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Style Selector=\"controls|AppTable\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsView_ShowsClientNavButton()
    {
        var settingsXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Settings", "SettingsView.axaml")));
        var settingsViewModel = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "Settings", "SettingsViewModel.cs")));

        Assert.Contains("NavigateToClientCommand", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Client\"", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FishTableColumns", settingsXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FishTableRows", settingsXaml, StringComparison.Ordinal);
        Assert.Contains("NavigateToClientCommand", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("IsMainViewVisible", settingsViewModel, StringComparison.Ordinal);
        Assert.Contains("CurrentSubView", settingsViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeColors_PalettesContainAllAppAxamlBrushKeys()
    {
        // Every SolidColorBrush key declared in App.axaml must exist in ThemeColors.Dark
        // so ThemeService.Apply can mutate it.
        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));

        var expectedKeys = new[]
        {
            "Background", "Surface", "Border", "Accent", "AccentHover", "AccentPressed",
            "AccentForeground", "TextPrimary", "TextSecondary", "Success", "Error",
            "InputHoverSurface", "InputHoverBorder", "InputFocusSurface", "InputFocusBorder",
            "TableSurface", "TableRowBackground", "TableGap", "TableHeaderBackground",
            "TableEmptyBackground", "TableRowDisabled",
            "SidebarItemForeground", "SidebarItemHoverForeground", "SidebarItemHoverBackground",
            "SidebarItemSelectedForeground", "SidebarItemSelectedBackground",
            "ButtonSecondaryBackground", "ButtonSecondaryForeground", "ButtonSecondaryBorder",
            "ButtonSecondaryHoverBackground", "ButtonSecondaryHoverBorder", "ButtonSecondaryPressedBackground",
            "CheckboxBackground", "CheckboxBorder", "CheckboxHoverBackground", "CheckboxHoverBorder",
            "CheckboxCheckedBackground", "CheckboxCheckedBorder", "CheckboxCheckForeground",
            "CheckboxLabelForeground", "CheckboxLabelHoverForeground",
            "UpdateBannerBackground", "ThemeCardHoverBackground",
            "ToggleHoverBackground", "ToggleHoverBorder", "ToggleCheckedBackground", "ToggleCheckedBorder",
        };

        foreach (var key in expectedKeys)
        {
            Assert.True(
                ThemeColors.Dark.ContainsKey(key),
                $"ThemeColors.Dark is missing key: {key}");
            Assert.True(
                ThemeColors.Light.ContainsKey(key),
                $"ThemeColors.Light is missing key: {key}");
            var slatePalette = typeof(ThemeColors).GetProperty("Slate")?.GetValue(null) as System.Collections.Generic.IReadOnlyDictionary<string, Avalonia.Media.Color>;
            Assert.NotNull(slatePalette);
            Assert.True(
                slatePalette.ContainsKey(key),
                $"ThemeColors.Slate is missing key: {key}");
            Assert.Contains($"x:Key=\"{key}\"", appXaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AppSettingsService_SettingsFilePath_IsUnderLocalAppDataSwift()
    {
        var path = AppSettingsService.SettingsFilePath;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(localAppData, path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Swift", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("settings.json", path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppSettingsService_Load_ReturnsDefaultWhenFileAbsent()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        // File intentionally does not exist
        var result = Client.Services.AppSettingsService.Load(missingPath);

        Assert.NotNull(result);
        Assert.Equal(AppTheme.Dark, result.Theme);
    }

    [Fact]
    public void SettingsClientView_HasThemeSelectorCards()
    {
        var clientViewXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Settings", "SettingsClientView.axaml")));
        var clientViewModel = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "Settings", "SettingsClientViewModel.cs")));

        Assert.Contains("BackCommand", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Back\"", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("SelectDarkCommand", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("SelectLightCommand", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("SelectSlateCommand", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("IsDarkSelected", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("IsLightSelected", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("IsSlateSelected", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Dark\"", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Light\"", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Slate\"", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("BoolToBorderBrushConverter", clientViewXaml, StringComparison.Ordinal);
        Assert.Contains("BackCommand", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("SelectDarkCommand", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("SelectLightCommand", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("SelectSlateCommand", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("IsDarkSelected", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("IsLightSelected", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("IsSlateSelected", clientViewModel, StringComparison.Ordinal);
        Assert.Contains("ThemeService.Apply", clientViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeService_SynchronizesAvaloniaThemeVariant()
    {
        var themeServiceSource = File.ReadAllText(FindRepoFile(Path.Combine("Services", "ThemeService.cs")));

        Assert.Contains("using Avalonia.Styling;", themeServiceSource, StringComparison.Ordinal);
        Assert.Contains("RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark", themeServiceSource, StringComparison.Ordinal);
        Assert.Contains("AppTheme.Slate => ThemeColors.Slate", themeServiceSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_IncludesSlate()
    {
        var appThemeSource = File.ReadAllText(FindRepoFile(Path.Combine("Services", "AppTheme.cs")));

        Assert.Contains("Slate", appThemeSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTable_RendersRowsAsOpaqueCellsInsteadOfTransparentPane()
    {
        var tableSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTable.cs")));

        Assert.Contains("GetBrush(\"TableRowBackground\"", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("cell.Background = Brushes.Transparent", tableSource, StringComparison.Ordinal);
        Assert.Contains("RowSpacing = 1", tableSource, StringComparison.Ordinal);
        Assert.Contains("ColumnSpacing = 1", tableSource, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(0)", tableSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTable_ShowsHorizontalScrollbarOnlyWhileHovered()
    {
        var tableSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTable.cs")));

        Assert.Contains("private ScrollViewer? scrollViewer;", tableSource, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public AppTable()\r\n    {\r\n        PointerEntered +=", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("public AppTable()\r\n    {\r\n        PointerExited +=", tableSource, StringComparison.Ordinal);
        Assert.Contains("UpdateThumbVisibility()", tableSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTable_OverlaysCustomScrollbarWithoutReservedBottomRow()
    {
        var tableSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTable.cs")));

        Assert.DoesNotContain("RowDefinitions = new RowDefinitions(\"*,Auto\")", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetRow(trackRow, 1)", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("var trackRow = new Border", tableSource, StringComparison.Ordinal);
        Assert.Contains("VerticalAlignment = VerticalAlignment.Bottom", tableSource, StringComparison.Ordinal);
        Assert.Contains("layout.Children.Add(scrollViewer);", tableSource, StringComparison.Ordinal);
        Assert.Contains("layout.Children.Add(scrollTrack);", tableSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTable_CustomScrollbarAcceptsPointerDragInput()
    {
        var tableSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppTable.cs")));

        Assert.Contains("using Avalonia.Animation;", tableSource, StringComparison.Ordinal);
        Assert.Contains("private bool isDraggingScrollThumb;", tableSource, StringComparison.Ordinal);
        Assert.Contains("private bool keepScrollThumbVisibleUntilPointerExit;", tableSource, StringComparison.Ordinal);
        Assert.Contains("new DoubleTransition", tableSource, StringComparison.Ordinal);
        Assert.Contains("Property = Visual.OpacityProperty", tableSource, StringComparison.Ordinal);
        Assert.Contains("Duration = TimeSpan.FromMilliseconds(110)", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollThumb.IsVisible = hasOverflow;", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollThumb.Opacity = (IsPointerOver || isDraggingScrollThumb || keepScrollThumbVisibleUntilPointerExit) && hasOverflow ? 1 : 0;", tableSource, StringComparison.Ordinal);
        Assert.Contains("keepScrollThumbVisibleUntilPointerExit = IsPointInsideTable(e.GetPosition(this));", tableSource, StringComparison.Ordinal);
        Assert.Contains("keepScrollThumbVisibleUntilPointerExit = false;", tableSource, StringComparison.Ordinal);
        Assert.DoesNotContain("OnPointerExited(object? sender, PointerEventArgs e)\r\n    {\r\n        if (isDraggingScrollThumb)\r\n        {\r\n            return;\r\n        }\r\n\r\n        if (scrollThumb is not null)\r\n        {\r\n            scrollThumb.IsVisible = false;\r\n        }\r\n    }", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollTrack.PointerPressed += OnScrollTrackPointerPressed;", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollTrack.PointerMoved += OnScrollTrackPointerMoved;", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollTrack.PointerReleased += OnScrollTrackPointerReleased;", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollTrack.PointerCaptureLost += OnScrollTrackPointerCaptureLost;", tableSource, StringComparison.Ordinal);
        Assert.Contains("IsHitTestVisible = true", tableSource, StringComparison.Ordinal);
        Assert.Contains("e.Pointer.Capture(scrollTrack)", tableSource, StringComparison.Ordinal);
        Assert.Contains("scrollViewer.Offset = new Vector", tableSource, StringComparison.Ordinal);
    }

    [Fact]
    public void ReusableAppKeyValueList_DefinesNativeKeyValueApi()
    {
        var listSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppKeyValueList.cs")));
        var itemSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppKeyValueItem.cs")));

        Assert.Contains("public sealed class AppKeyValueList : UserControl", listSource, StringComparison.Ordinal);
        Assert.Contains("ItemsProperty", listSource, StringComparison.Ordinal);
        Assert.Contains("AvaloniaProperty.Register<AppKeyValueList, IReadOnlyList<AppKeyValueItem>?>", listSource, StringComparison.Ordinal);
        Assert.Contains("BuildRows()", listSource, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions = new ColumnDefinitions(\"*,Auto\")", listSource, StringComparison.Ordinal);
        Assert.Contains("TextAlignment = TextAlignment.Right", listSource, StringComparison.Ordinal);
        Assert.Contains("public sealed record AppKeyValueItem", itemSource, StringComparison.Ordinal);
        Assert.Contains("string Entry", itemSource, StringComparison.Ordinal);
        Assert.Contains("string Value", itemSource, StringComparison.Ordinal);
        Assert.Contains("ValueForegroundResourceKey", itemSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppKeyValueList_UsesGlobalThemeResourcesForNativeStyling()
    {
        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));
        var listSource = File.ReadAllText(FindRepoFile(Path.Combine("Controls", "AppKeyValueList.cs")));

        Assert.Contains("Style Selector=\"controls|AppKeyValueList\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"KeyValueListBackground\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"KeyValueListDivider\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("GetBrush(\"TableRowBackground\"", listSource, StringComparison.Ordinal);
        Assert.Contains("GetBrush(\"TableGap\"", listSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetBrush(\"KeyValueListBackground\"", listSource, StringComparison.Ordinal);
        Assert.DoesNotContain("GetBrush(\"KeyValueListDivider\"", listSource, StringComparison.Ordinal);
        Assert.Contains("CornerRadius = new CornerRadius(6)", listSource, StringComparison.Ordinal);
        Assert.Contains("RowSpacing = 1", listSource, StringComparison.Ordinal);
    }

    [Fact]
    public void FishingView_ShowsStatusAppKeyValueList()
    {
        var fishingXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Fishing", "FishingView.axaml")));
        var fishingViewModel = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "Fishing", "FishingViewModel.cs")));

        Assert.Contains("xmlns:controls=\"using:Client.Controls\"", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("<controls:AppKeyValueList", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("Items=\"{Binding StatusItems}\"", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"430,12,220\"", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("StatusItems", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("StatsItems", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Phase\", Phase)", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Progress\", ProgressText)", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Input\", HoldingText)", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(RodHeaderText, EquippedRodText, ColoredLines: EquippedRodLines)", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Caught\", _status.ReelStats.Caught.ToString())", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Lost\", _status.ReelStats.Lost.ToString())", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Success Rate\", $\"{_status.ReelStats.SuccessRatePercent:0.0}%\")", fishingViewModel, StringComparison.Ordinal);
        Assert.Contains("new AppKeyValueItem(\"Fish skipped\", \"---\")", fishingViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("new AppKeyValueItem(\"Total counted\"", fishingViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"Phase\"", fishingXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<TextBlock Text=\"Progress\"", fishingXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FishingView_UsesThemeTextForModeLabels()
    {
        var fishingXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Fishing", "FishingView.axaml")));

        Assert.Contains("Text=\"Tracking Method\"", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Casting\"", fishingXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Foreground=\"White\"", fishingXaml, StringComparison.Ordinal);
        Assert.Contains("Foreground=\"{StaticResource TextPrimary}\"", fishingXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FishingAddonsExpandButtons_UseThemeResources()
    {
        var addonsXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Fishing", "FishingAddonsView.axaml")));

        Assert.DoesNotContain("#161B20", addonsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#D6E8FF", addonsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#31455A", addonsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#22303D", addonsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#4B6886", addonsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Button.addon-expand:pointerover /template/ ContentPresenter", addonsXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{StaticResource Accent}\"", addonsXaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"{StaticResource AccentForeground}\"", addonsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellView_HasNoHardcodedHexColors()
    {
        var shellXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Shell", "ShellView.axaml")));

        Assert.DoesNotContain("#1D1F1F", shellXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0F1D1C", shellXaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SidebarItemHoverBackground", shellXaml, StringComparison.Ordinal);
        Assert.Contains("UpdateBannerBackground", shellXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsView_HasNoHardcodedHexColorsOrWhiteLiterals()
    {
        var settingsXaml = File.ReadAllText(FindRepoFile(Path.Combine("Views", "Settings", "SettingsView.axaml")));

        Assert.DoesNotContain("#141415", settingsXaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Value=\"White\"", settingsXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellNavigationItemViewModel_HasNoHardcodedBrushes()
    {
        var vmSource = File.ReadAllText(FindRepoFile(Path.Combine("ViewModels", "Shell", "ShellNavigationItemViewModel.cs")));

        Assert.DoesNotContain("private static readonly IBrush SelectedBackground", vmSource, StringComparison.Ordinal);
        Assert.DoesNotContain("private static readonly IBrush IdleForeground", vmSource, StringComparison.Ordinal);
        Assert.Contains("ThemeService.ThemeChanged", vmSource, StringComparison.Ordinal);
        Assert.Contains("SidebarItemSelectedBackground", vmSource, StringComparison.Ordinal);
        Assert.Contains("SidebarItemSelectedForeground", vmSource, StringComparison.Ordinal);
        Assert.Contains("SidebarItemForeground", vmSource, StringComparison.Ordinal);
        Assert.Contains("IDisposable", vmSource, StringComparison.Ordinal);
    }

    [Fact]
    public void AppAxaml_DefinesGlobalThemeAwareCheckboxStates()
    {
        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));

        Assert.Contains("<Style Selector=\"CheckBox\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("CheckboxLabelForeground", appXaml, StringComparison.Ordinal);
        Assert.Contains("CheckBox:pointerover", appXaml, StringComparison.Ordinal);
        Assert.Contains("CheckboxLabelHoverForeground", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"CheckBoxBox\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Name=\"CheckBoxGlyph\"", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SecondaryButtons_UseSharedThemeResources()
    {
        var files = new[]
        {
            Path.Combine("Views", "Account", "LoginView.axaml"),
            Path.Combine("Views", "Account", "AccountView.axaml"),
            Path.Combine("Views", "Fishing", "GeneralView.axaml"),
            Path.Combine("Views", "Fishing", "FishingView.axaml"),
            Path.Combine("Views", "Fishing", "Angler", "AutoAnglerView.axaml"),
            Path.Combine("Views", "Fishing", "Appraise", "AppraiseView.axaml"),
            Path.Combine("Views", "AutoTotemView.axaml"),
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(FindRepoFile(file));
            Assert.Contains("ButtonSecondaryForeground", source, StringComparison.Ordinal);
            Assert.Contains("ButtonSecondaryHoverBackground", source, StringComparison.Ordinal);
        }

        var appXaml = File.ReadAllText(FindRepoFile("App.axaml"));
        Assert.Contains("ButtonSecondaryForeground", appXaml, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} from {AppContext.BaseDirectory}.");
    }
}
