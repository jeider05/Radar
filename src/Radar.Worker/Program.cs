using Radar.Infrastructure.Contents;
using Radar.Infrastructure.GitHub;
using Radar.Infrastructure.Persistence;
using Radar.Infrastructure.Sources;
using Radar.Infrastructure.YouTube;
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
builder.Services.Configure<YouTubeMonitorOptions>(builder.Configuration.GetSection(YouTubeMonitorOptions.SectionName));
builder.Services.AddSingleton(database);
builder.Services.AddSingleton<SourceStore>();
builder.Services.AddSingleton<ContentItemStore>();

// Each client gets its own HttpClient with its own settings.
builder.Services.AddSingleton(_ => new GitHubReleasesClient(GitHubReleasesClient.CreateHttpClient()));
builder.Services.AddSingleton<GitHubReleaseImporter>();
builder.Services.AddHostedService<GitHubReleaseMonitor>();

builder.Services.AddSingleton(_ => new YouTubeClient(YouTubeClient.CreateHttpClient()));
builder.Services.AddSingleton<YouTubeVideoImporter>();
builder.Services.AddHostedService<YouTubeChannelMonitor>();

var host = builder.Build();
host.Run();
return 0;
