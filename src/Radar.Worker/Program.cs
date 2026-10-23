using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;
using Radar.Worker;

// Explicit one-shot commands run and exit without starting the monitor.
if (args is [GitHubImportCommand.Name, var repository])
{
    return await GitHubImportCommand.RunAsync(repository);
}

// Only one Worker may run per user session. A second instance exits immediately.
using var instanceMutex = new Mutex(initiallyOwned: true, @"Local\Radar.Worker", out var isFirstInstance);
if (!isFirstInstance)
{
    return 0;
}

var database = RadarDatabase.CreateDefault();
database.Initialize();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<GitHubMonitorOptions>(builder.Configuration.GetSection(GitHubMonitorOptions.SectionName));
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<SourceStore>();
builder.Services.AddSingleton<ContentItemStore>();
builder.Services.AddSingleton(_ => GitHubReleasesClient.CreateHttpClient());
builder.Services.AddSingleton<GitHubReleasesClient>();
builder.Services.AddSingleton<GitHubReleaseImporter>();
builder.Services.AddHostedService<GitHubReleaseMonitor>();

var host = builder.Build();
host.Run();
return 0;
