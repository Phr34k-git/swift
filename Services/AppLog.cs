using System;
using System.IO;
using System.Threading;

namespace Client.Services;

internal static class AppLog
{
    private static readonly object SyncRoot = new();
    private static readonly object FishingSyncRoot = new();
    private static long _fishingSeq;

    public static string LogPath { get; } = ResolveLogPath("client.log");

    public static string FishingLogPath { get; } = ResolveLogPath("fishing.log");

    public static void Info(string area, string message)
    {
        Write(LogPath, SyncRoot, "INFO", area, message, null);
    }

    public static void Error(string area, string message, Exception? exception = null)
    {
        Write(LogPath, SyncRoot, "ERROR", area, message, exception);
    }

    // Dedicated execution-flow trace channel for the fishing automation
    // pipeline. Routed to a separate file so the verbose flow log does not
    // drown the main client.log. Each line is sequence-numbered so ordering
    // across threads is recoverable even when timestamps collide.
    public static void Fishing(string area, string message)
    {
        WriteFishing("TRACE", area, message, null);
    }

    public static void FishingError(string area, string message, Exception? exception = null)
    {
        WriteFishing("ERROR", area, message, exception);
    }

    private static void WriteFishing(string level, string area, string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(FishingLogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var seq = Interlocked.Increment(ref _fishingSeq);
            var threadId = Environment.CurrentManagedThreadId;
            var line = $"{DateTimeOffset.Now:O} #{seq:D8} t{threadId:D3} [{level}] {area}: {OneLine(message)}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {OneLine(exception.Message)} | {OneLine(exception.StackTrace ?? string.Empty)}";
            }

            lock (FishingSyncRoot)
            {
                File.AppendAllText(FishingLogPath, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static void Write(string path, object sync, string level, string area, string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:O} [{level}] {area}: {OneLine(message)}";
            if (exception is not null)
            {
                line += $" | {exception.GetType().Name}: {OneLine(exception.Message)}";
            }

            lock (sync)
            {
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    private static string ResolveLogPath(string fileName)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "Swift", "logs", fileName);
        }

        return Path.Combine(AppContext.BaseDirectory, fileName);
    }

    private static string OneLine(string value)
    {
        return value.Replace('\r', ' ').Replace('\n', ' ');
    }
}
