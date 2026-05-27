using System;
using System.Threading.Tasks;
using Client.Services;
using Client.Tests.Fakes;
using Client.ViewModels;
using Xunit;

namespace Client.Tests;

public sealed class LoginViewModelUpdateTests
{
    [Fact]
    public void Constructor_WithPreviewEnvironment_ShowsUpdateRestartPrompt()
    {
        var previous = Environment.GetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, "v1.0.1");

            var viewModel = Build();

            Assert.True(viewModel.IsUpdateAvailable);
            Assert.Equal("v1.0.1", viewModel.UpdateVersion);
            Assert.Equal("v1.0.1 is ready to install.", viewModel.UpdateStatusText);
            Assert.Equal("Restart to v1.0.1", viewModel.RestartUpdateButtonText);
            Assert.True(viewModel.RestartUpdateCommand.CanExecute(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void Constructor_HidesUpdateRestartPromptByDefault()
    {
        var previous = Environment.GetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, null);

            var viewModel = Build();

            Assert.False(viewModel.IsUpdateAvailable);
            Assert.Equal("Restart to update", viewModel.RestartUpdateButtonText);
            Assert.False(viewModel.RestartUpdateCommand.CanExecute(null));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ShellViewModel.PreviewUpdateVersionEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void RestartUpdateCommand_InvokesRestartCallback()
    {
        var restarted = false;
        var viewModel = Build(() =>
        {
            restarted = true;
            return Task.CompletedTask;
        });

        viewModel.ShowUpdateAvailable("v1.0.1");
        viewModel.RestartUpdateCommand.Execute(null);

        Assert.True(restarted);
        Assert.True(viewModel.IsRestartingUpdate);
        Assert.Equal("Restarting...", viewModel.UpdateStatusText);
    }

    private static LoginViewModel Build(Func<Task>? restartUpdateAsync = null)
    {
        var appState = new AppStateService(new FakeAuthService(), new FakeHwidProvider());
        return new LoginViewModel(appState, restartUpdateAsync);
    }
}
