using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace EyesSaver.App;

public sealed class App : Application
{
    internal AppController? Controller { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
            Controller = new AppController(desktop, showSettings: Program.StartupCommand == AppCommand.Show);
            Program.Channel = InstanceChannel.Listen(Controller.HandleCommand);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
