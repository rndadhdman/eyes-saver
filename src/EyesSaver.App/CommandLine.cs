using System.IO.Pipes;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using Avalonia.Threading;

namespace EyesSaver.App;

internal static class AppInfo
{
    /// <summary>Reverse-DNS id used for the Flatpak, the .desktop file and the icon name.</summary>
    public const string LinuxAppId = "io.github.eyessaver.EyesSaver";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev";
}

internal enum AppCommand
{
    Show,
    Tray,
    Toggle,
    Pause,
    Resume,
    Stronger,
    Weaker,
    Quit
}

internal static class CommandLine
{
    /// <summary>Used by the start-at-sign-in entry so the app starts quietly in the tray.</summary>
    public const string TrayArgument = "--tray";

    public const string Usage =
        """
        Usage: eyessaver [option]

          (no option)   Start Eyes Saver, or open its window if it is running
          --tray        Start without opening the window
          --toggle      Pause or resume the tint
          --pause       Pause the tint
          --resume      Resume the tint
          --stronger    Make the tint 5% stronger
          --weaker      Make the tint 5% weaker
          --quit        Close the running Eyes Saver
          --version     Print the version
        """;

    public static AppCommand Parse(IEnumerable<string> args)
    {
        foreach (var arg in args.Select(arg => arg.Trim().ToLowerInvariant()))
        {
            switch (arg)
            {
                case TrayArgument or "--minimized" or "/tray":
                    return AppCommand.Tray;
                case "--toggle":
                    return AppCommand.Toggle;
                case "--pause":
                    return AppCommand.Pause;
                case "--resume":
                    return AppCommand.Resume;
                case "--stronger":
                    return AppCommand.Stronger;
                case "--weaker":
                    return AppCommand.Weaker;
                case "--quit" or "--exit":
                    return AppCommand.Quit;
            }
        }

        return AppCommand.Show;
    }
}

/// <summary>
/// One Eyes Saver per user. The first instance listens (a named pipe on Windows, a Unix domain
/// socket on Linux); later launches send their command there and exit. This is also how desktop
/// shortcuts on Wayland reach the app: eyessaver --toggle.
/// </summary>
internal sealed class InstanceChannel : IDisposable
{
    private static readonly string PipeName = $"EyesSaver.Instance.{Environment.UserName}";

    private readonly Socket? _socket;
    private readonly CancellationTokenSource _stop = new();

    private InstanceChannel(Socket? socket, Func<AppCommand, string> handler)
    {
        _socket = socket;
        var thread = new Thread(() => Serve(handler)) { IsBackground = true, Name = "Eyes Saver instance channel" };
        thread.Start();
    }

    /// <summary>The Linux socket path.</summary>
    public static string SocketPath { get; } = ResolveSocketPath();

    public static bool TrySend(AppCommand command)
    {
        try
        {
            using var stream = Connect();
            if (stream is null)
            {
                return false;
            }

            var request = Encoding.UTF8.GetBytes(command + "\n");
            stream.Write(request);
            stream.Flush();
            var buffer = new byte[64];
            _ = stream.Read(buffer);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Starts listening. Call only while holding the single-instance mutex.</summary>
    public static InstanceChannel? Listen(Func<AppCommand, string> handler)
    {
        if (OperatingSystem.IsWindows())
        {
            return new InstanceChannel(null, handler);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SocketPath)!);

            // Left behind by an instance that did not exit cleanly; we hold the mutex, so it is stale.
            File.Delete(SocketPath);

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Bind(new UnixDomainSocketEndPoint(SocketPath));
            socket.Listen(8);
            return new InstanceChannel(socket, handler);
        }
        catch (Exception ex) when (ex is SocketException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_socket is not null)
        {
            try
            {
                _socket.Dispose();
                File.Delete(SocketPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static Stream? Connect()
    {
        if (OperatingSystem.IsWindows())
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            try
            {
                pipe.Connect(300);
                return pipe;
            }
            catch (TimeoutException)
            {
                pipe.Dispose();
                return null;
            }
        }

        if (!File.Exists(SocketPath))
        {
            return null;
        }

        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified)
        {
            ReceiveTimeout = 5000,
            SendTimeout = 2000
        };
        try
        {
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (SocketException)
        {
            socket.Dispose();
            return null;
        }
    }

    private void Serve(Func<AppCommand, string> handler)
    {
        while (!_stop.IsCancellationRequested)
        {
            Stream client;
            try
            {
                if (_socket is null)
                {
                    var pipe = new NamedPipeServerStream(
                        PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    pipe.WaitForConnectionAsync(_stop.Token).GetAwaiter().GetResult();
                    client = pipe;
                }
                else
                {
                    client = new NetworkStream(_socket.Accept(), ownsSocket: true);
                }
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                Thread.Sleep(200);
                continue;
            }

            using (client)
            {
                try
                {
                    var buffer = new byte[256];
                    var length = client.Read(buffer);
                    var text = Encoding.UTF8.GetString(buffer, 0, length).Trim();
                    var reply = Enum.TryParse<AppCommand>(text, ignoreCase: true, out var command)
                        ? Dispatcher.UIThread.InvokeAsync(() => handler(command)).GetAwaiter().GetResult()
                        : "unknown";
                    client.Write(Encoding.UTF8.GetBytes(reply + "\n"));
                    client.Flush();
                }
                catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or TaskCanceledException)
                {
                }
            }
        }
    }

    private static string ResolveSocketPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return string.Empty;
        }

        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var flatpak = Environment.GetEnvironmentVariable("FLATPAK_ID");
        if (!string.IsNullOrEmpty(runtime) && Directory.Exists(runtime))
        {
            // $XDG_RUNTIME_DIR/app/<id> is shared by every instance of a Flatpak app.
            return string.IsNullOrEmpty(flatpak)
                ? Path.Combine(runtime, "eyessaver.sock")
                : Path.Combine(runtime, "app", flatpak, "eyessaver.sock");
        }

        return Path.Combine(Path.GetTempPath(), $"eyessaver-{Environment.UserName}.sock");
    }
}
