using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;

namespace Client.Services;

public sealed class LocalOAuthCallbackException : Exception
{
    public LocalOAuthCallbackException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}

/// <summary>
/// Thrown when the loopback listener can't be bound. The message is intended
/// for the login screen; <see cref="Cause"/> distinguishes "port already in
/// use" (another Swift instance, or a leftover process holding 9999) from
/// "access denied" (Windows/antivirus blocking the bind).
/// </summary>
public sealed class LocalOAuthCallbackBindException : Exception
{
    public LocalOAuthCallbackBindException(LocalOAuthCallbackBindCause cause, string message, Exception? inner = null)
        : base(message, inner)
    {
        Cause = cause;
    }

    public LocalOAuthCallbackBindCause Cause { get; }
}

public enum LocalOAuthCallbackBindCause
{
    Unknown,
    AddressInUse,
    AccessDenied,
}

/// <summary>
/// Minimal loopback HTTP server for receiving the browser OAuth callback.
/// </summary>
public sealed class LocalOAuthCallbackServer : IDisposable
{
    private const string CallbackPath = "/callback";
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly Lazy<string> CallbackFontFaceCss = new(CreateCallbackFontFaceCss);
    private readonly IReadOnlyList<TcpListener> _listeners;
    private int _acceptCount;
    private int _validCallbackCount;

    private LocalOAuthCallbackServer(IReadOnlyList<TcpListener> listeners)
    {
        _listeners = listeners;
    }

    /// <summary>
    /// Starts listening on IPv4 and IPv6 loopback for the configured callback
    /// port. Throws <see cref="LocalOAuthCallbackBindException"/> if neither
    /// family can be bound — the caller surfaces a specific message to the
    /// login UI so the user can either close the conflicting process or use
    /// the manual code paste fallback.
    /// </summary>
    public static LocalOAuthCallbackServer Start(int port)
    {
        var listeners = new List<TcpListener>();
        var bindFailures = new List<SocketException>(2);

        TryStartListener(IPAddress.Loopback, port, listeners, bindFailures);
        TryStartListener(IPAddress.IPv6Loopback, port, listeners, bindFailures);

        if (listeners.Count == 0)
        {
            var (cause, message) = ClassifyBindFailures(port, bindFailures);
            AppLog.Error("LocalOAuthCallbackServer", $"Could not bind any loopback listeners on port {port}. cause={cause}");
            throw new LocalOAuthCallbackBindException(cause, message, bindFailures.FirstOrDefault());
        }

        AppLog.Info("LocalOAuthCallbackServer", $"Started {listeners.Count} listener(s) on port {port}.");
        return new LocalOAuthCallbackServer(listeners);
    }

    /// <summary>
    /// Waits for Discord's redirect and returns the one-time exchange code.
    /// Emits a heartbeat log every <see cref="HeartbeatInterval"/> so a stuck
    /// listener is observable in logs; on timeout the log includes the count
    /// of TCP accepts seen, which distinguishes "browser never arrived" from
    /// "browser arrived but the request didn't carry a code".
    /// </summary>
    public async Task<string> WaitForCodeAsync(TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        using var heartbeat = StartHeartbeat(timeout, cancellation.Token);
        var tasks = _listeners
            .Select(listener => AcceptCallbackAsync(listener, cancellation.Token))
            .ToList();

        try
        {
            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks);
                tasks.Remove(completed);

                try
                {
                    return await completed;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    AppLog.Error(
                        "LocalOAuthCallbackServer",
                        $"Timed out waiting for callback. accepts={_acceptCount}, valid_callbacks={_validCallbackCount}.");
                    throw new TimeoutException(BuildTimeoutMessage());
                }
                catch (LocalOAuthCallbackException)
                {
                    throw;
                }
                catch when (tasks.Count > 0)
                {
                }
            }

            throw new InvalidOperationException("The local OAuth callback server stopped before receiving a code.");
        }
        finally
        {
            cancellation.Cancel();
            StopListeners();
        }
    }

    private string BuildTimeoutMessage()
    {
        if (_acceptCount == 0)
        {
            return "Timed out waiting for the browser login callback. " +
                "The browser never reached the local listener — close any browser tab that's stuck, " +
                "or use the \"Have a code? Paste it\" option to finish signing in.";
        }

        return "Timed out waiting for the browser login callback. " +
            $"Saw {_acceptCount} connection(s) but no valid login code. " +
            "Use the \"Have a code? Paste it\" option to finish signing in.";
    }

    private IDisposable StartHeartbeat(TimeSpan totalTimeout, CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(async () =>
        {
            var deadline = DateTimeOffset.UtcNow + totalTimeout;
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(HeartbeatInterval, cts.Token);
                    var remaining = deadline - DateTimeOffset.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    AppLog.Info(
                        "LocalOAuthCallbackServer",
                        $"Waiting for browser callback. remaining={remaining.TotalSeconds:0}s, " +
                        $"accepts={_acceptCount}, valid_callbacks={_validCallbackCount}.");
                }
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
        return cts;
    }

    public void Dispose()
    {
        StopListeners();
    }

    private static void TryStartListener(
        IPAddress address,
        int port,
        ICollection<TcpListener> listeners,
        ICollection<SocketException> bindFailures)
    {
        try
        {
            var listener = new TcpListener(address, port);
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                listener.Server.DualMode = false;
            }

            listener.Start();
            listeners.Add(listener);
        }
        catch (SocketException ex)
        {
            bindFailures.Add(ex);
            AppLog.Info("LocalOAuthCallbackServer", $"Bind to {address}:{port} failed. code={ex.SocketErrorCode}");
        }
    }

    private static (LocalOAuthCallbackBindCause Cause, string Message) ClassifyBindFailures(
        int port,
        IReadOnlyList<SocketException> failures)
    {
        // Prefer the most actionable diagnosis — if EITHER family saw "address
        // in use", that's the one the user can fix (close the other instance).
        var hasInUse = failures.Any(f => f.SocketErrorCode is SocketError.AddressAlreadyInUse);
        var hasDenied = failures.Any(f => f.SocketErrorCode is SocketError.AccessDenied);

        if (hasInUse)
        {
            return (
                LocalOAuthCallbackBindCause.AddressInUse,
                $"Port {port} is already in use — another Swift instance may be running. " +
                "Close it and try again, or use the \"Have a code? Paste it\" option below.");
        }

        if (hasDenied)
        {
            return (
                LocalOAuthCallbackBindCause.AccessDenied,
                "Windows or your antivirus blocked Swift from listening for the Discord callback. " +
                "Try running Swift as administrator, or use the \"Have a code? Paste it\" option below.");
        }

        return (
            LocalOAuthCallbackBindCause.Unknown,
            "Could not start the local Discord callback listener. " +
            "Use the \"Have a code? Paste it\" option below to finish signing in.");
    }

    private async Task<string> AcceptCallbackAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        Interlocked.Increment(ref _acceptCount);
        await using var stream = client.GetStream();

        var requestTarget = await ReadRequestTargetAsync(stream, cancellationToken);
        AppLog.Info("LocalOAuthCallbackServer", $"Received callback request. codePresent={RequestHasCode(requestTarget)}.");
        var code = TryReadCallbackCode(requestTarget);
        var callbackError = TryReadCallbackError(requestTarget);

        if (callbackError is not null)
        {
            var errorPage = ResolveErrorPage(callbackError);
            AppLog.Error("LocalOAuthCallbackServer", $"Callback included error={callbackError}.");
            await WriteCallbackResponseAsync(stream, errorPage, cancellationToken);
            throw new LocalOAuthCallbackException(callbackError, errorPage.AppMessage);
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            AppLog.Error("LocalOAuthCallbackServer", "Callback did not include a code.");
            await WriteCallbackResponseAsync(stream, ResolveErrorPage("missing_code"), cancellationToken);
            throw new InvalidOperationException("OAuth callback did not include a code.");
        }

        Interlocked.Increment(ref _validCallbackCount);
        await WriteCallbackResponseAsync(stream, CallbackPage.Success, cancellationToken);
        AppLog.Info("LocalOAuthCallbackServer", $"Callback code accepted. length={code.Length}.");
        return code;
    }

    private static async Task<string> ReadRequestTargetAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 1024,
            leaveOpen: true);

        var requestLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return string.Empty;
        }

        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
        {
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    private static string? TryReadCallbackCode(string requestTarget)
    {
        if (!Uri.TryCreate($"http://localhost{requestTarget}", UriKind.Absolute, out var uri) ||
            !string.Equals(uri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            return null;
        }

        return ReadQueryValue(uri.Query, "code");
    }

    private static string? TryReadCallbackError(string requestTarget)
    {
        if (!Uri.TryCreate($"http://localhost{requestTarget}", UriKind.Absolute, out var uri) ||
            !string.Equals(uri.AbsolutePath, CallbackPath, StringComparison.Ordinal))
        {
            return null;
        }

        return ReadQueryValue(uri.Query, "error");
    }

    private static string? ReadQueryValue(string query, string name)
    {
        var trimmedQuery = query.TrimStart('?');
        if (string.IsNullOrEmpty(trimmedQuery))
        {
            return null;
        }

        foreach (var part in trimmedQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace("+", " "));
            if (!string.Equals(key, name, StringComparison.Ordinal))
            {
                continue;
            }

            return pair.Length == 2
                ? Uri.UnescapeDataString(pair[1].Replace("+", " "))
                : string.Empty;
        }

        return null;
    }

    private static bool RequestHasCode(string requestTarget)
    {
        return Uri.TryCreate($"http://localhost{requestTarget}", UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(ReadQueryValue(uri.Query, "code"));
    }

    private static async Task WriteCallbackResponseAsync(
        Stream stream,
        CallbackPage page,
        CancellationToken cancellationToken)
    {
        var fontFaceCss = CallbackFontFaceCss.Value;
        var body = $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>OpenMacro</title>
                <style>
                    {{fontFaceCss}}

                    html {
                        background: #f7f7f4;
                        color: #171717;
                        font: 16px/1.5 'Satoshi', system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                    }

                    body {
                        min-height: calc(100dvh - 48px);
                        margin: 24px;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        background: #f7f7f4;
                    }

                    main {
                        max-width: 640px;
                        text-align: center;
                    }

                    .brand {
                        margin: 0 0 18px;
                        color: #6e6b62;
                        font-size: 14px;
                        font-weight: 600;
                    }

                    h1 {
                        margin: 0;
                        font-size: clamp(48px, 9vw, 96px);
                        line-height: .92;
                        font-weight: 700;
                        letter-spacing: 0;
                    }

                    p {
                        margin: 22px 0 0;
                        color: #57544c;
                        font-size: 18px;
                    }
                </style>
            </head>
            <body>
                <main>
                    <p class="brand">OpenMacro</p>
                    <h1>{{page.Title}}</h1>
                    <p>{{page.Detail}}</p>
                </main>
            </body>
            </html>
            """;
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var headerBytes = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {page.Status}\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Cache-Control: no-store\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(headerBytes, cancellationToken);
        await stream.WriteAsync(bodyBytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static CallbackPage ResolveErrorPage(string errorCode) =>
        errorCode switch
        {
            "invalid_state" => new(
                "400 Bad Request",
                "Session Drifted",
                "Start the Discord sign-in again from Swift.",
                "Your login session changed. Try again from Swift."),

            "expired_state" => new(
                "400 Bad Request",
                "Took Too Long",
                "Start again from Swift and finish the Discord step in one go.",
                "Your login session expired. Try again from Swift."),

            "access_denied" => new(
                "400 Bad Request",
                "No Worries",
                "Swift is still waiting when you want to try again.",
                "Discord sign-in was cancelled."),

            "discord_exchange_failed" => new(
                "400 Bad Request",
                "Discord Stumbled",
                "Try again from Swift. If it keeps happening, Discord may be slow.",
                "Discord did not finish the sign-in. Try again from Swift."),

            "discord_user_failed" => new(
                "400 Bad Request",
                "Couldn't Read Discord",
                "Try again from Swift. If it keeps happening, reconnect Discord.",
                "Swift could not read your Discord profile. Try again."),

            "missing_required_role" => new(
                "400 Bad Request",
                "Role Missing",
                "Back to Swift once your Discord role is active.",
                "Your Discord account is missing the required role."),

            _ => new(
                "400 Bad Request",
                "Not Signed In",
                "Try again from Swift.",
                "Login did not complete. Try again from Swift."),
        };

    private sealed record CallbackPage(
        string Status,
        string Title,
        string Detail,
        string AppMessage)
    {
        public static CallbackPage Success { get; } = new(
            "200 OK",
            "You're in.",
            "Back to Swift.",
            string.Empty);
    }

    private static string CreateCallbackFontFaceCss()
    {
        try
        {
            return $$"""
                    @font-face {
                        font-family: 'Satoshi';
                        src: url('{{CreateFontDataUri("Satoshi-Regular.otf")}}') format('opentype');
                        font-weight: 400;
                        font-style: normal;
                        font-display: swap;
                    }

                    @font-face {
                        font-family: 'Satoshi';
                        src: url('{{CreateFontDataUri("Satoshi-Bold.otf")}}') format('opentype');
                        font-weight: 700;
                        font-style: normal;
                        font-display: swap;
                    }
                """;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CreateFontDataUri(string fileName)
    {
        var uri = new Uri($"avares://Client/Assets/Fonts/Satoshi/OTF/{fileName}");
        using var stream = AssetLoader.Open(uri);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return $"data:font/otf;base64,{Convert.ToBase64String(memory.ToArray())}";
    }

    private void StopListeners()
    {
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }
    }
}
