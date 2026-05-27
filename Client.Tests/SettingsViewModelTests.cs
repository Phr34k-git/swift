using Client.ViewModels;
using Xunit;

namespace Client.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void ClientBackCommand_ReturnsToMainSettingsView()
    {
        var viewModel = new SettingsViewModel();

        viewModel.NavigateToClientCommand.Execute(null);

        var clientViewModel = Assert.IsType<SettingsClientViewModel>(viewModel.CurrentSubView);
        Assert.False(viewModel.IsMainViewVisible);

        clientViewModel.BackCommand.Execute(null);

        Assert.Null(viewModel.CurrentSubView);
        Assert.True(viewModel.IsMainViewVisible);
    }
}
