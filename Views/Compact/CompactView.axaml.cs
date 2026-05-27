using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Client.Views;

public partial class CompactView : UserControl
{
    public CompactView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
