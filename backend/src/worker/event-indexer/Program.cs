using backend.main.application.bootstrap;
using backend.main.shared.utilities.logger;
using backend.worker.event_indexer;

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
builder.Services.AddEventSearchInfrastructure(builder.Configuration);
builder.Services.AddSingleton(EventIndexerOptions.FromEnvironment());
builder.Services.AddSingleton<IEventIndexerDlqPublisher, KafkaEventIndexerDlqPublisher>();
builder.Services.AddScoped<EventIndexerMessageProcessor>();
builder.Services.AddHostedService<EventSearchIndexBootstrapService>();
builder.Services.AddHostedService<KafkaEventIndexerWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
