using System.Windows;

namespace Radar.Desktop;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly WorkerSupervisor _workerSupervisor = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _workerSupervisor.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stops supervising only; the Worker keeps running after Desktop closes.
        _workerSupervisor.Dispose();
        base.OnExit(e);
    }
}
