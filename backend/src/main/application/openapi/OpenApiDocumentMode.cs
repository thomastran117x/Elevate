namespace backend.main.application.openapi
{
    /// <summary>
    /// Centralizes OpenAPI document-generation and export mode checks.
    /// </summary>
    public static class OpenApiDocumentMode
    {
        public const string DocumentName = "v1";
        public const string ExportEnvironmentVariable = "OPENAPI_EXPORT";
        public const string IncludePrefixEnvironmentVariable = "OPENAPI_INCLUDE_PREFIX";
        public const string DefaultJsonRoute = "/openapi.json";
        public const string DefaultYamlRoute = "/openapi.yaml";
        public const string JsonRoutePattern = "/openapi/{documentName}.json";
        public const string YamlRoutePattern = "/openapi/{documentName}.yaml";

        public static bool IsExportMode =>
            bool.TryParse(Environment.GetEnvironmentVariable(ExportEnvironmentVariable), out var enabled)
            && enabled;

        public static bool ShouldSkipStartupSideEffects => IsExportMode;
    }
}
