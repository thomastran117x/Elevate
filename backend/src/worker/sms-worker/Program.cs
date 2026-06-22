using backend.main.shared.utilities.logger;
using backend.worker.sms_worker;

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
builder.Services.AddSingleton(SmsWorkerOptions.FromEnvironment());
builder.Services.AddSingleton<ISmsWorkerDlqPublisher, KafkaSmsWorkerDlqPublisher>();
builder.Services.AddHttpClient<ISmsSender, TwilioSmsSender>();
builder.Services.AddScoped<SmsMfaMessageProcessor>();
builder.Services.AddHostedService<KafkaSmsWorker>();

using var host = builder.Build();

Logger.SetInstance(host.Services.GetRequiredService<ICustomLogger>());

await host.RunAsync();
