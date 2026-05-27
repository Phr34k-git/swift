using Client.Services;
using Client.ViewModels;
using Xunit;

namespace Client.Tests;

public sealed class LockedOutViewModelTests
{
    [Fact]
    public void DefaultState_ShowsRevokedCopy()
    {
        var vm = new LockedOutViewModel();
        var expected = LockedOutCopy.Resolve(null);

        Assert.Equal(expected.Title, vm.Title);
        Assert.Equal(expected.Subtext, vm.Subtext);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("banned")]
    [InlineData("chargeback")]
    [InlineData("compromised")]
    [InlineData("hwid_mismatch")]
    [InlineData("unlicensed")]
    [InlineData("maintenance")]
    public void SetReason_KnownReason_UpdatesTitleAndSubtext(string reason)
    {
        var vm = new LockedOutViewModel();
        var expected = LockedOutCopy.Resolve(reason);

        vm.Reason = reason;

        Assert.Equal(expected.Title, vm.Title);
        Assert.Equal(expected.Subtext, vm.Subtext);
    }

    [Fact]
    public void SetReason_Maintenance_UsesMaintenanceImage()
    {
        var vm = new LockedOutViewModel();

        vm.Reason = "maintenance";

        Assert.Equal("avares://Client/Assets/maintenance.png", vm.ImageAssetUri);
    }

    [Fact]
    public void SetReason_UnknownReason_FallsBackToRevokedCopy()
    {
        var vm = new LockedOutViewModel();
        var revoked = LockedOutCopy.Resolve("revoked");

        vm.Reason = "whatever_new_reason";

        Assert.Equal(revoked.Title, vm.Title);
        Assert.Equal(revoked.Subtext, vm.Subtext);
    }

    [Fact]
    public void SetReason_Null_FallsBackToRevokedCopy()
    {
        var vm = new LockedOutViewModel();
        vm.Reason = "banned";

        vm.Reason = null;

        var revoked = LockedOutCopy.Resolve("revoked");
        Assert.Equal(revoked.Title, vm.Title);
        Assert.Equal(revoked.Subtext, vm.Subtext);
    }

    [Fact]
    public void SetReason_RaisesPropertyChangedForTitleAndSubtext()
    {
        var vm = new LockedOutViewModel();
        var changed = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.Reason = "banned";

        Assert.Contains("Reason", changed);
        Assert.Contains("Title", changed);
        Assert.Contains("Subtext", changed);
    }
}
