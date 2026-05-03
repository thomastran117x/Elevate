using backend.main.configurations.application;
using backend.main.utilities.implementation;
using backend.main.utilities.interfaces;
using backend.worker.event_indexer;

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
builder.Services.AddSearchInfrastructure(builder.Configuration);
builder.Services.AddSingleton(EventIndexerOptions.FromEnvironment());
builder.Services.AddSingleton(ClubPostIndexerOptions.FromEnvironment());
builder.Services.AddSingleton(EmailWorkerOptions.FromEnvironment());
builder.Services.AddSingleton<IEventIndexerDlqPublisher, KafkaEventIndexerDlqPublisher>();
builder.Services.AddSingleton<IClubPostIndexerDlqPublisher, KafkaClubPostIndexerDlqPublisher>();
builder.Services.AddSingleton<IEmailWorkerDlqPublisher, KafkaEmailWorkerDlqPublisher>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<EventIndexerMessageProcessor>();
builder.Services.AddScoped<ClubPostIndexerMessageProcessor>();
builder.Services.AddScoped<EmailMessageProcessor>();
builder.Services.AddHostedService<EventSearchIndexBootstrapService>();
builder.Services.AddHostedService<ClubPostSearchIndexBootstrapService>();
builder.Services.AddHostedService<KafkaEventIndexerWorker>();
builder.Services.AddHostedService<KafkaClubPostIndexerWorker>();
builder.Services.AddHostedService<KafkaEmailWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
