using backend.main.application.bootstrap;
using backend.main.shared.utilities.logger;
using backend.worker.media_worker;

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
builder.Services.AddImageValidationPipeline(builder.Configuration);
builder.Services.AddSingleton(MediaWorkerOptions.FromEnvironment());
builder.Services.AddSingleton<IMediaWorkerDlqPublisher, KafkaMediaWorkerDlqPublisher>();
builder.Services.AddSingleton<IMediaValidationStatusPublisher, KafkaMediaValidationStatusPublisher>();
builder.Services.AddScoped<MediaWorkerMessageProcessor>();
builder.Services.AddHostedService<KafkaMediaWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
