using System.Runtime.InteropServices;
using System.Windows;
namespace PoeAdaptive.Builder;
public partial class App : Application
{
    [DllImport("kernel32.dll")] private static extern bool AttachConsole(uint process);
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--build"))
        {
            AttachConsole(uint.MaxValue);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shutdown(await Build.CommandLine.Run(e.Args));
        }
        else new MainWindow().Show();
    }
}
