using backend.main.features.auth.notifications;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.utilities.logger;
using backend.worker.security_history_worker;

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
builder.Services.AddSingleton(SecurityHistoryWorkerOptions.FromConfiguration(builder.Configuration));
// The API owns the schema and its migrations; the worker only connects.
builder.Services.AddAppDatabase(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMetrics();
builder.Services.AddSingleton<SecurityHistoryWorkerMetrics>();
builder.Services.AddSingleton<IPublisher, Publisher>();
builder.Services.AddScoped<IAuthNotificationService, AuthNotificationService>();
builder.Services.AddSingleton<ISecurityHistoryWorkerDlqPublisher, KafkaSecurityHistoryWorkerDlqPublisher>();
builder.Services.AddScoped<SecurityHistoryWriter>();
builder.Services.AddScoped<SecurityHistoryAlertSender>();
builder.Services.AddScoped<SecurityHistoryMessageProcessor>();
builder.Services.AddScoped<SecurityHistoryRetentionRunner>();
builder.Services.AddHostedService<KafkaSecurityHistoryWorker>();
builder.Services.AddHostedService<SecurityHistoryRetentionService>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
