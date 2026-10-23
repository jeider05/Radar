using Radar.Worker;

// Only one Worker may run per user session. A second instance exits immediately.
using var instanceMutex = new Mutex(initiallyOwned: true, @"Local\Radar.Worker", out var isFirstInstance);
if (!isFirstInstance)
{
    return;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
