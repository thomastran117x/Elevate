using backend.main.application.bootstrap;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;
using backend.worker.clubpost_indexer;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

Logger.Configure(options =>
{
    options.EnableFileLogging = true;
    options.MinFileLevel = backend.main.utilities.interfaces.LogLevel.Warn;
    options.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs")
    );
});

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(Logger.GetOptions());
builder.Services.AddSingleton<ICustomLogger, FileLogger>();
builder.Services.AddClubPostSearchInfrastructure(builder.Configuration);
builder.Services.AddSingleton(ClubPostIndexerOptions.FromEnvironment());
builder.Services.AddSingleton<IClubPostIndexerDlqPublisher, KafkaClubPostIndexerDlqPublisher>();
builder.Services.AddScoped<ClubPostIndexerMessageProcessor>();
builder.Services.AddHostedService<ClubPostSearchIndexBootstrapService>();
builder.Services.AddHostedService<KafkaClubPostIndexerWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
