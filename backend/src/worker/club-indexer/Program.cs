using backend.main.application.bootstrap;
using backend.main.shared.utilities.logger;
using backend.worker.club_indexer;

Logger.Configure(options =>
{
    options.EnableFileLogging = true;
    options.MinFileLevel = LogLevel.Warn;
    options.LogDirectory = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "logs")
    );
});

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(Logger.GetOptions());
builder.Services.AddSingleton<ICustomLogger, FileLogger>();
builder.Services.AddClubSearchInfrastructure(builder.Configuration);
builder.Services.AddSingleton(ClubIndexerOptions.FromEnvironment());
builder.Services.AddSingleton<IClubIndexerDlqPublisher, KafkaClubIndexerDlqPublisher>();
builder.Services.AddScoped<ClubIndexerMessageProcessor>();
builder.Services.AddHostedService<ClubSearchIndexBootstrapService>();
builder.Services.AddHostedService<KafkaClubIndexerWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
