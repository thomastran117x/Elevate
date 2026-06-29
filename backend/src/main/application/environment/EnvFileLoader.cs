using backend.main.shared.utilities.logger;

namespace backend.main.application.environment
{
    internal static class EnvFileLoader
    {
        public static bool LoadNearestAndParentEnvFiles(string baseDirectory)
        {
            var envPaths = FindEnvFiles(baseDirectory);
            if (envPaths.Count == 0)
                return false;

            var protectedKeys = new HashSet<string>(
                Environment.GetEnvironmentVariables()
                    .Keys
                    .OfType<string>()
                    .Where(key => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key))),
                StringComparer.OrdinalIgnoreCase);

            foreach (var envPath in envPaths)
            {
                try
                {
                    LoadFile(envPath, protectedKeys);
                    Logger.Info($".env file loaded from: {envPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, $"Failed to load .env file at {envPath}");
                }
            }

            return true;
        }

        internal static IReadOnlyList<string> FindEnvFiles(string baseDirectory)
        {
            var files = new List<string>();
            var dir = new DirectoryInfo(baseDirectory);

            while (dir != null)
            {
                var envPath = Path.Combine(dir.FullName, ".env");
                if (File.Exists(envPath))
                    files.Add(envPath);

                dir = dir.Parent;
            }

            files.Reverse();
            return files;
        }

        internal static void LoadFile(string path, ISet<string>? protectedKeys = null)
        {
            foreach (var line in File.ReadLines(path))
            {
                if (!TryParseAssignment(line, out var key, out var value))
                    continue;

                if (protectedKeys?.Contains(key) == true)
                    continue;

                if (string.IsNullOrWhiteSpace(value))
                    continue;

                Environment.SetEnvironmentVariable(key, value);
            }
        }

        internal static bool TryParseAssignment(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.Trim();
            if (trimmed.StartsWith('#'))
                return false;

            if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed["export ".Length..].TrimStart();

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
                return false;

            key = trimmed[..equalsIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            value = NormalizeValue(trimmed[(equalsIndex + 1)..]);
            return true;
        }

        internal static string NormalizeValue(string rawValue)
        {
            var value = rawValue.Trim();
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    return value[1..^1];
            }

            var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
            if (commentIndex >= 0)
                value = value[..commentIndex].TrimEnd();

            return value;
        }
    }
}
