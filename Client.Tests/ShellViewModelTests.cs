using System;
using System.Net.Http;
using Client.Models;
using Client.Services;
using Client.Tests.Fakes;
using Client.ViewModels;
using Xunit;

namespace Client.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void Constructor_HidesUpdateBannerByDefault()
    {
        var viewModel = Build();

        Assert.False(viewModel.IsUpdateAvailable);
        Assert.Equal(string.Empty, viewModel.UpdateVersion);
        Assert.Equal("Restart to update", viewModel.RestartUpdateButtonText);
        Assert.False(viewModel.RestartUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void ShowUpdateAvailable_SetsNoticeableRestartCopy()
    {
        var viewModel = Build();

        viewModel.ShowUpdateAvailable("v0.0.4");

        Assert.True(viewModel.IsUpdateAvailable);
        Assert.Equal("v0.0.4", viewModel.UpdateVersion);
        Assert.Equal("v0.0.4 is ready to install.", viewModel.UpdateStatusText);
        Assert.Equal("Restart to v0.0.4", viewModel.RestartUpdateButtonText);
        Assert.True(viewModel.RestartUpdateCommand.CanExecute(null));
    }

    [Fact]
    public void Constructor_WithPreviewEnvironment_ShowsUpdateBanner()
    {
        var previous = Environment.GetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, "v0.0.4");

            var viewModel = Build();

            Assert.True(viewModel.IsUpdateAvailable);
            Assert.Equal("v0.0.4", viewModel.UpdateVersion);
            Assert.Equal("Restart to v0.0.4", viewModel.RestartUpdateButtonText);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, previous);
        }
    }

    private static ShellViewModel Build()
    {
        var auth = new FakeAuthService();
        var appState = new AppStateService(auth, new FakeHwidProvider());
        var accountApi = new AccountApiClient(appState, new HttpClient());

        return new ShellViewModel(appState, accountApi);
    }
}
