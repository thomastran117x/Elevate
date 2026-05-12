namespace backend.main.application.bootstrap
{
    public static class StartupConfig
    {
        public static string ConfigureServerUrls(this WebApplicationBuilder builder)
        {
            builder.Configuration.AddEnvironmentVariables();

            var port =
                Environment.GetEnvironmentVariable("PORT") ??
                Environment.GetEnvironmentVariable("ASPNETCORE_PORT") ??
                "8090";

            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            return port;
        }
    }
}
