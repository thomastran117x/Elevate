using backend.main.shared.utilities.logger;
using backend.worker.email_worker;

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
builder.Services.AddSingleton(EmailWorkerOptions.FromEnvironment());
builder.Services.AddSingleton<IEmailWorkerDlqPublisher, KafkaEmailWorkerDlqPublisher>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<EmailMessageProcessor>();
builder.Services.AddHostedService<KafkaEmailWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
