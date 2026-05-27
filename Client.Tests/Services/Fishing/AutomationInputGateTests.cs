using Client.Services.Fishing;
using Xunit;

namespace Client.Tests.Services.Fishing;

public sealed class AutomationInputGateTests : IDisposable
{
    public void Dispose()
    {
        AutomationInputGate.Reset();
    }

    [Fact]
    public void TryEnter_WithPriorityKeepsExistingOwnerUntilReleased()
    {
        Assert.True(AutomationInputGate.TryEnter("AUTO_AQUARIUM", priority: 100));

        Assert.False(AutomationInputGate.TryEnter("AUTO_TOTEM", priority: 50));
        Assert.True(AutomationInputGate.TryEnter("AUTO_AQUARIUM", priority: 100));

        AutomationInputGate.Exit("AUTO_AQUARIUM");

        Assert.True(AutomationInputGate.TryEnter("AUTO_TOTEM", priority: 50));
    }
}
