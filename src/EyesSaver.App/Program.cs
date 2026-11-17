using Avalonia;

namespace EyesSaver.App;

internal static class Program
{
    private const string MutexName = @"Local\EyesSaver.SingleInstance";

    /// <summary>Set before Avalonia starts: what the first instance should do once it is up.</summary>
    public static AppCommand StartupCommand { get; private set; } = AppCommand.Show;

    public static InstanceChannel? Channel { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(arg => arg is "--help" or "-h" or "/?"))
        {
            Console.WriteLine(CommandLine.Usage);
            return 0;
        }

        if (args.Any(arg => arg is "--version" or "-v"))
        {
            Console.WriteLine($"Eyes Saver {AppInfo.Version}");
            return 0;
        }

        var command = CommandLine.Parse(args);

        // Already running: hand the command over and leave.
        if (InstanceChannel.TrySend(command))
        {
            return 0;
        }

        if (command == AppCommand.Quit)
        {
            return 0;
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is starting up right now; give it a moment to start listening.
            for (var attempt = 0; attempt < 30; attempt++)
            {
                Thread.Sleep(100);
                if (InstanceChannel.TrySend(command))
                {
                    return 0;
                }
            }

            return 1;
        }

        // Commands other than "show" start the app quietly in the tray.
        StartupCommand = command == AppCommand.Show ? AppCommand.Show : AppCommand.Tray;

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
        }
        finally
        {
            Channel?.Dispose();
        }
    }

    // Also used by the Avalonia previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        // Windows keeps Segoe UI. Linux desktops vary a lot, so use the bundled Inter there.
        if (OperatingSystem.IsLinux())
        {
            builder = builder.With(new Avalonia.Media.FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });
        }

        return builder;
    }
}
