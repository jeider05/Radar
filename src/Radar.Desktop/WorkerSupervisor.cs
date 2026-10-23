using System.Diagnostics;
using System.IO;

namespace Radar.Desktop;

/// <summary>
/// Keeps a single Radar.Worker process running while Desktop is open.
/// Attaches to an already running Worker or starts one, and restarts it with an increasing delay when it exits.
/// Desktop never stops the Worker: disposing this class only stops supervision.
/// </summary>
internal sealed class WorkerSupervisor : IDisposable
{
    private const string WorkerProcessName = "Radar.Worker";
    private static readonly TimeSpan InitialRestartDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRestartDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StableRunTime = TimeSpan.FromMinutes(1);

    private readonly string _workerPath = Path.Combine(AppContext.BaseDirectory, "Worker", "Radar.Worker.exe");
    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _gate = new();
    private Process? _worker;
    private DateTime _watchingSince;
    private TimeSpan _nextRestartDelay = InitialRestartDelay;

    public void Start() => EnsureWorkerRunning();

    private void EnsureWorkerRunning()
    {
        lock (_gate)
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var worker = FindRunningWorker() ?? StartWorker();
                Watch(worker);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Trace.WriteLine($"[WorkerSupervisor] Could not start Worker at '{_workerPath}': {ex.Message}");
                ScheduleRestart();
            }
        }
    }

    private static Process? FindRunningWorker()
    {
        var running = Process.GetProcessesByName(WorkerProcessName);
        foreach (var extra in running.Skip(1))
        {
            extra.Dispose();
        }

        if (running.Length > 0)
        {
            Trace.WriteLine($"[WorkerSupervisor] Attached to running Worker (PID {running[0].Id}).");
        }

        return running.FirstOrDefault();
    }

    private Process StartWorker()
    {
        var startInfo = new ProcessStartInfo(_workerPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_workerPath)!,
        };

        var worker = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Process.Start returned no process.");
        Trace.WriteLine($"[WorkerSupervisor] Started Worker (PID {worker.Id}).");
        return worker;
    }

    private void Watch(Process worker)
    {
        _worker = worker;
        _watchingSince = DateTime.UtcNow;
        worker.Exited += OnWorkerExited;
        worker.EnableRaisingEvents = true;
    }

    private void OnWorkerExited(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (sender is Process exited)
            {
                exited.Exited -= OnWorkerExited;
                Trace.WriteLine($"[WorkerSupervisor] Worker (PID {exited.Id}) exited.");
                exited.Dispose();
            }

            _worker = null;

            if (DateTime.UtcNow - _watchingSince >= StableRunTime)
            {
                _nextRestartDelay = InitialRestartDelay;
            }

            ScheduleRestart();
        }
    }

    // Must be called while holding _gate.
    private void ScheduleRestart()
    {
        if (_stopping.IsCancellationRequested)
        {
            return;
        }

        var delay = _nextRestartDelay;
        _nextRestartDelay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRestartDelay.Ticks));
        Trace.WriteLine($"[WorkerSupervisor] Restarting Worker in {delay.TotalSeconds:0} s.");
        _ = RestartAfterAsync(delay);
    }

    private async Task RestartAfterAsync(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _stopping.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        EnsureWorkerRunning();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopping.Cancel();

            if (_worker is not null)
            {
                // Releases our handle only; the Worker process keeps running.
                _worker.Exited -= OnWorkerExited;
                _worker.Dispose();
                _worker = null;
            }
        }

        _stopping.Dispose();
    }
}
