using BsDiff;

namespace Launcher;

internal static class Patcher
{
    internal static void Apply(string oldExePath, string patchPath, string newExePath)
    {
        using var oldStream = File.OpenRead(oldExePath);
        using var newStream = File.Create(newExePath);
        BinaryPatch.Apply(oldStream, () => File.OpenRead(patchPath), newStream);
    }
}
