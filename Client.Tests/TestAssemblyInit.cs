using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Client.Services.Fishing;
using Client.Tests.Fakes;

namespace Client.Tests;

internal static class TestAssemblyInit
{
    [ModuleInitializer]
    public static void RegisterDefaults()
    {
        // Existing tracker/runner constructors resolve OffsetsSourceProvider.Current at
        // construction time. Tests that don't care about offsets get a populated fake
        // source so they don't have to thread one through. Tests that DO care about
        // offset wiring (OffsetsServiceTests, RobloxMemoryOffsetsTests) construct their
        // own RobloxMemory directly with the source they want.
        OffsetsSourceProvider.Register(new FakeOffsetsSource(
            new Dictionary<string, ulong>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["FakeDataModel.Pointer"]       = 0,
                ["FakeDataModel.RealDataModel"] = 0,
                ["Player.LocalPlayer"]          = 0,
                ["Instance.ChildrenStart"]      = 0,
                ["Instance.ChildrenEnd"]        = 0,
                ["Instance.Name"]               = 0,
                ["Instance.Parent"]             = 0,
                ["Instance.ClassDescriptor"]    = 0,
                ["Instance.ClassName"]          = 0,
                ["Misc.StringLength"]           = 0,
            },
            version: "test-fake-v1"));
    }
}
