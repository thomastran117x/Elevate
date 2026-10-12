using System.Reflection;

using Microsoft.Extensions.Options;

namespace backend.main.features.auth.password
{
    public interface IPasswordBlocklist
    {
        /// <summary>
        /// Whether <paramref name="skeleton"/>, already reduced by <see cref="PasswordText.Skeleton"/>,
        /// matches a blocked password.
        /// </summary>
        bool Contains(string skeleton);
    }

    /// <summary>
    /// Common and breached passwords held in memory, from the bundled list and an optional local
    /// file. Checked entirely in process: nothing about a candidate password leaves the server.
    /// </summary>
    public sealed class PasswordBlocklist : IPasswordBlocklist
    {
        internal const string EmbeddedResourceName = "PasswordBlocklist.common.txt";

        private readonly Lazy<HashSet<string>> _entries;

        public PasswordBlocklist(IOptions<PasswordPolicyOptions> options)
        {
            var settings = options.Value;
            _entries = new Lazy<HashSet<string>>(
                () => Load(settings.UseEmbeddedBlocklist, settings.BlocklistPath),
                LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public bool Contains(string skeleton) =>
            skeleton.Length > 0 && _entries.Value.Contains(skeleton);

        internal int Count => _entries.Value.Count;

        private static HashSet<string> Load(bool useEmbedded, string? path)
        {
            var entries = new HashSet<string>(StringComparer.Ordinal);

            if (useEmbedded)
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName)
                    ?? throw new InvalidOperationException(
                        $"Embedded resource '{EmbeddedResourceName}' is missing. Check the EmbeddedResource LogicalName in backend.csproj.");
                using var reader = new StreamReader(stream);
                AddLines(entries, reader);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                using var reader = new StreamReader(path);
                AddLines(entries, reader);
            }

            return entries;
        }

        private static void AddLines(HashSet<string> entries, TextReader reader)
        {
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                    continue;

                var skeleton = PasswordText.Skeleton(line);
                if (skeleton.Length > 0)
                    entries.Add(skeleton);
            }
        }
    }
}
