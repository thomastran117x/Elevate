using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

internal static partial class DevTasksCli
{
    private const string DefaultQuarantineContainerName = "event-assets-quarantine";

    /// <summary>
    /// Creates the two blob containers the API expects: the public one images are served from,
    /// and the private quarantine one presigned uploads land in.
    /// </summary>
    /// <remarks>
    /// The API no longer creates containers on the request path — that was a round trip per
    /// upload and needed a credential allowed to create containers. This runs once, with a
    /// connection string that has that right, so the app's own credential does not need it.
    /// <para>
    /// Existing containers are reported, never altered. The one exception is a quarantine
    /// container that allows anonymous reads: that defeats the point of quarantine, so the
    /// command fails and says how to fix it rather than guessing whether to change it.
    /// </para>
    /// </remarks>
    private static async Task<int> RunStorageProvisionAsync(string[] args)
    {
        string? connectionString = null;
        string? containerName = null;
        string? quarantineContainerName = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--connection-string":
                    connectionString = ReadRequiredValue(args, ref index, "connection string");
                    break;
                case "--container":
                    containerName = ReadRequiredValue(args, ref index, "container");
                    break;
                case "--quarantine-container":
                    quarantineContainerName = ReadRequiredValue(args, ref index, "quarantine container");
                    break;
                case var option when IsHelp(option):
                    WriteStorageProvisionHelp();
                    return 0;
                default:
                    return Fail($"Unknown storage-provision option '{args[index]}'.");
            }
        }

        var repoRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        var dotEnv = ReadDotEnv(Path.Combine(repoRoot, ".env"));

        connectionString ??= ReadSetting("AZURE_STORAGE_CONNECTION_STRING", dotEnv);
        containerName ??= ReadSetting("AZURE_STORAGE_CONTAINER_NAME", dotEnv);
        quarantineContainerName ??= ReadSetting("AZURE_STORAGE_QUARANTINE_CONTAINER_NAME", dotEnv)
            ?? DefaultQuarantineContainerName;

        if (string.IsNullOrWhiteSpace(connectionString))
            return Fail("No connection string: pass --connection-string or set AZURE_STORAGE_CONNECTION_STRING.");
        if (string.IsNullOrWhiteSpace(containerName))
            return Fail("No public container name: pass --container or set AZURE_STORAGE_CONTAINER_NAME.");
        if (string.Equals(containerName, quarantineContainerName, StringComparison.OrdinalIgnoreCase))
            return Fail("The public and quarantine containers must be different containers.");

        var service = new BlobServiceClient(connectionString);

        await EnsureContainerAsync(service, containerName, PublicAccessType.Blob, "public");
        var quarantineAccess = await EnsureContainerAsync(
            service, quarantineContainerName, PublicAccessType.None, "quarantine");

        // Not Fail(): this is a storage problem, not a usage error, so the help text would only
        // bury the one line that matters.
        if (quarantineAccess != PublicAccessType.None)
        {
            Console.Error.WriteLine(
                $"Quarantine container '{quarantineContainerName}' allows anonymous {quarantineAccess} access, " +
                "so unvalidated uploads are publicly readable. Set its access level to private, e.g. " +
                $"'az storage container set-permission --name {quarantineContainerName} --public-access off'.");
            return 1;
        }

        Console.WriteLine("Storage is provisioned.");
        return 0;
    }

    private static async Task<PublicAccessType> EnsureContainerAsync(
        BlobServiceClient service,
        string name,
        PublicAccessType access,
        string role)
    {
        var container = service.GetBlobContainerClient(name);

        try
        {
            await container.CreateAsync(access);
            Console.WriteLine($"Created {role} container '{name}' ({DescribeAccess(access)}).");
            return access;
        }
        catch (RequestFailedException ex) when (ex.ErrorCode == BlobErrorCode.ContainerAlreadyExists)
        {
            var existing = (await container.GetAccessPolicyAsync()).Value.BlobPublicAccess;
            Console.WriteLine(
                $"Found existing {role} container '{name}' ({DescribeAccess(existing)}); left unchanged.");

            if (existing != access)
            {
                Console.WriteLine(
                    $"  Note: expected {DescribeAccess(access)} for the {role} container.");
            }

            return existing;
        }
    }

    private static string DescribeAccess(PublicAccessType access) => access switch
    {
        PublicAccessType.None => "private",
        PublicAccessType.Blob => "anonymous read for blobs",
        PublicAccessType.BlobContainer => "anonymous read and list",
        _ => access.ToString()
    };

    private static string? ReadSetting(string key, IReadOnlyDictionary<string, string> dotEnv)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return dotEnv.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile)
            ? fromFile
            : null;
    }

    /// <summary>
    /// Reads KEY=VALUE lines from the repository's .env, the file the backend itself loads in
    /// development. Comments and blank lines are skipped; surrounding quotes are removed.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadDotEnv(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path))
            return values;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    private static void WriteStorageProvisionHelp()
    {
        Console.WriteLine("storage-provision options:");
        Console.WriteLine(
            "  --connection-string    Storage connection string with container-create rights. Default: AZURE_STORAGE_CONNECTION_STRING"
        );
        Console.WriteLine(
            "  --container            Public container name. Default: AZURE_STORAGE_CONTAINER_NAME"
        );
        Console.WriteLine(
            $"  --quarantine-container Private quarantine container name. Default: AZURE_STORAGE_QUARANTINE_CONTAINER_NAME or {DefaultQuarantineContainerName}"
        );
        Console.WriteLine("  Settings not passed as options are read from the environment, then from the repository's .env.");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  dotnet run --project tools/Event.DevTasks -- storage-provision");
        Console.WriteLine();
    }
}
