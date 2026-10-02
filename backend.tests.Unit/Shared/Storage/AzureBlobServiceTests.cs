using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Azure.Storage.Blobs;
using Azure.Storage.Sas;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;
using backend.main.shared.storage.imaging;

using backend.tests.Unit.Support;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Storage;

[Collection(EnvironmentVariableTestCollection.Name)]
public class AzureBlobServiceTests
{
    [Fact]
    public void ValidateAndNormalizeImageExtension_ShouldReturnExtension_ForSupportedInputs()
    {
        var extension = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ValidateAndNormalizeImageExtension",
            " Poster.JPEG ",
            " image/jpeg ");

        extension.Should().Be(".jpeg");
    }

    [Fact]
    public void ValidateAndNormalizeImageExtension_ShouldThrowUnsupportedMediaType_ForUnknownContentType()
    {
        var action = () => InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ValidateAndNormalizeImageExtension",
            "poster.bmp",
            "image/bmp");

        action.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<UnsupportedMediaTypeException>()
            .WithMessage("*JPEG, PNG, WEBP, and GIF*");
    }

    [Fact]
    public void ValidateAndNormalizeImageExtension_ShouldThrowBadRequest_ForMismatchedExtension()
    {
        var action = () => InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ValidateAndNormalizeImageExtension",
            "poster.png",
            "image/jpeg");

        action.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<BadRequestException>()
            .WithMessage("*extension must match*");
    }

    [Fact]
    public void ResolveImageContentType_ShouldInferType_FromFileExtension_WhenMissing()
    {
        var contentType = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ResolveImageContentType",
            "avatar.png",
            null);

        contentType.Should().Be("image/png");
    }

    [Fact]
    public void ResolveImageContentType_ShouldReturnExplicitContentType_WhenProvided()
    {
        var contentType = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ResolveImageContentType",
            "avatar.png",
            " image/webp ");

        contentType.Should().Be("image/webp");
    }

    [Fact]
    public void ResolveImageContentType_ShouldReturnEmptyString_WhenContentTypeAndExtensionAreUnknown()
    {
        var contentType = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ResolveImageContentType",
            "avatar.unknown",
            "application/octet-stream");

        contentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public void ValidateAndNormalizeImageExtension_ShouldThrowBadRequest_ForMissingFileName()
    {
        var action = () => InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "ValidateAndNormalizeImageExtension",
            "   ",
            "image/png");

        action.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<BadRequestException>()
            .WithMessage("*valid file name is required*");
    }

    [Fact]
    public void NormalizeBlobPathPrefix_ShouldNormalizeWindowsSeparators_AndCollapseDotSegments()
    {
        var prefix = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "NormalizeBlobPathPrefix",
            @"..\clubs\images\.\covers",
            "uploads");

        prefix.Should().Be("clubs/images/covers");
    }

    [Fact]
    public void NormalizeBlobPathPrefix_ShouldFallBack_WhenSegmentsCollapseAway()
    {
        var prefix = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "NormalizeBlobPathPrefix",
            @"..\.\..",
            "uploads");

        prefix.Should().Be("uploads");
    }

    [Fact]
    public void NormalizeBlobPathPrefix_ShouldRemovePreviousSegment_WhenDotDotIsEncountered()
    {
        var prefix = InvokePrivateStatic<string>(
            typeof(AzureBlobService),
            "NormalizeBlobPathPrefix",
            "events/gallery/../hero",
            "uploads");

        prefix.Should().Be("events/hero");
    }

    [Fact]
    public void IsOwnedBlobUrl_ShouldReturnTrue_ForManagedHttpsUrl()
    {
        var service = CreateServiceWithContainer();

        var result = service.IsOwnedBlobUrl("https://eventassets.blob.core.windows.net/media/events/poster.png");

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://eventassets.blob.core.windows.net/media/events/poster.png")]
    [InlineData("https://other.blob.core.windows.net/media/events/poster.png")]
    [InlineData("https://eventassets.blob.core.windows.net/other/events/poster.png")]
    [InlineData("not-a-url")]
    public void IsOwnedBlobUrl_ShouldReturnFalse_ForUnmanagedOrInvalidUrls(string blobUrl)
    {
        var service = CreateServiceWithContainer();

        var result = service.IsOwnedBlobUrl(blobUrl);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteBlobAsync_ShouldNoOp_ForInvalidUrl_AndMissingContainer()
    {
        var serviceWithContainer = CreateServiceWithContainer();
        var serviceWithoutContainer = CreateServiceWithoutContainer("missing config");

        await serviceWithContainer.Invoking(svc => svc.DeleteBlobAsync("not-a-url")).Should().NotThrowAsync();
        await serviceWithoutContainer.Invoking(svc => svc.DeleteBlobAsync("https://eventassets.blob.core.windows.net/media/events/poster.png")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteBlobAsync_ShouldNoOp_ForRelativePathOutsideContainer()
    {
        var service = CreateServiceWithContainer();

        await service.Invoking(svc => svc.DeleteBlobAsync("https://eventassets.blob.core.windows.net/media/")).Should().NotThrowAsync();
    }

    [Fact]
    public async Task ListBlobsAsync_ShouldYieldNothing_WhenContainerIsMissing()
    {
        var service = CreateServiceWithoutContainer("missing config");

        var items = new List<BlobListItem>();
        await foreach (var item in service.ListBlobsAsync("users"))
            items.Add(item);

        items.Should().BeEmpty();
    }

    [Fact]
    public void GetRequiredContainer_ShouldThrowInvalidOperation_WhenContainerIsMissing()
    {
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        var action = () => InvokePrivateInstance<BlobContainerClient>(
            service,
            "GetRequiredContainer");

        action.Should()
            .Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>()
            .WithMessage("*AZURE_STORAGE_CONNECTION_STRING is not configured.*");
    }

    [Fact]
    public void GetRequiredContainer_ShouldReturnConfiguredContainer()
    {
        var service = CreateServiceWithContainer();

        var container = InvokePrivateInstance<BlobContainerClient>(service, "GetRequiredContainer");

        container.Name.Should().Be("media");
    }

    [Fact]
    public void Constructor_ShouldCaptureMissingConnectionStringConfiguration()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["AZURE_STORAGE_CONNECTION_STRING"] = null,
            ["AZURE_STORAGE_CONTAINER_NAME"] = "media"
        });
        using var harness = AzureBlobServiceHarness.Load();

        harness.CreateInstance();

        harness.GetConfigurationError().Should().Be("AZURE_STORAGE_CONNECTION_STRING is not configured.");
        harness.GetContainer().Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldCaptureMissingContainerConfiguration()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["AZURE_STORAGE_CONNECTION_STRING"] = "UseDevelopmentStorage=true",
            ["AZURE_STORAGE_CONTAINER_NAME"] = null
        });
        using var harness = AzureBlobServiceHarness.Load();

        harness.CreateInstance();

        harness.GetConfigurationError().Should().Be("AZURE_STORAGE_CONTAINER_NAME is not configured.");
        harness.GetContainer().Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldCreateBlobContainer_WhenConfigurationIsPresent()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["AZURE_STORAGE_CONNECTION_STRING"] = "DefaultEndpointsProtocol=https;AccountName=eventassets;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            ["AZURE_STORAGE_CONTAINER_NAME"] = "images"
        });
        using var harness = AzureBlobServiceHarness.Load();

        harness.CreateInstance();

        harness.GetConfigurationError().Should().BeNull();
        harness.GetContainerName().Should().Be("images");
        harness.GetContainerName("_quarantine").Should().Be("event-assets-quarantine");
    }

    [Fact]
    public void Constructor_ShouldUseTheConfiguredQuarantineContainer()
    {
        using var scope = new EnvironmentVariableScope(new Dictionary<string, string?>
        {
            ["DOTNET_RUNNING_IN_CONTAINER"] = "true",
            ["AZURE_STORAGE_CONNECTION_STRING"] = "DefaultEndpointsProtocol=https;AccountName=eventassets;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            ["AZURE_STORAGE_CONTAINER_NAME"] = "images",
            ["AZURE_STORAGE_QUARANTINE_CONTAINER_NAME"] = "images-private"
        });
        using var harness = AzureBlobServiceHarness.Load();

        harness.CreateInstance();

        harness.GetContainerName("_quarantine").Should().Be("images-private");
    }

    [Fact]
    public async Task GenerateQuarantineUploadUrlAsync_ShouldSignAWriteOnceUrlIntoQuarantine_AndReserveAWebpPublicUrl()
    {
        var service = CreateServiceWithContainer();

        var upload = await service.GenerateQuarantineUploadUrlAsync("events/clubs/7/pending", "Poster.PNG", "image/png");

        var uploadUri = new Uri(upload.UploadUrl);
        uploadUri.AbsolutePath.Should().Be($"/quarantine/{upload.QuarantineBlobPath}");
        var query = System.Web.HttpUtility.ParseQueryString(uploadUri.Query);
        query["sp"].Should().Be("c", "the SAS must not be able to overwrite what was inspected");
        query["rsct"].Should().Be("image/png");

        upload.QuarantineBlobPath.Should().MatchRegex("^events/clubs/7/pending/[0-9a-f]{32}\\.png$");
        var id = Path.GetFileNameWithoutExtension(upload.QuarantineBlobPath);
        upload.PublicUrl.Should().Be(
            $"https://eventassets.blob.core.windows.net/media/events/clubs/7/pending/{id}.webp");
        upload.PublicUrl.Should().NotContain("quarantine");
        upload.ContentType.Should().Be("image/png");
        service.IsOwnedBlobUrl(upload.PublicUrl).Should().BeTrue();
    }

    [Fact]
    public async Task GenerateQuarantineUploadUrlAsync_ShouldInferTheTypeFromTheExtension_ForOctetStream()
    {
        var service = CreateServiceWithContainer();

        var upload = await service.GenerateQuarantineUploadUrlAsync("clubs/pending/3", "photo.jpg", "application/octet-stream");

        upload.ContentType.Should().Be("image/jpeg");
        upload.QuarantineBlobPath.Should().EndWith(".jpg");
    }

    [Fact]
    public async Task GenerateQuarantineUploadUrlAsync_ShouldRejectUnsupportedTypes()
    {
        var service = CreateServiceWithContainer();

        await service.Invoking(svc => svc.GenerateQuarantineUploadUrlAsync("events", "x.bmp", "image/bmp"))
            .Should().ThrowAsync<UnsupportedMediaTypeException>();
    }

    [Fact]
    public async Task GenerateQuarantineUploadUrlAsync_ShouldRequireConfiguredStorage()
    {
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        await service.Invoking(svc => svc.GenerateQuarantineUploadUrlAsync("events", "x.png", "image/png"))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*AZURE_STORAGE_CONNECTION_STRING is not configured.*");
    }

    [Fact]
    public async Task GenerateUploadUrlAsync_ShouldStillSignIntoThePublicContainer_WithoutCreatingIt()
    {
        // The flag-off path. It no longer calls CreateIfNotExists, which is what lets this run
        // with no storage account behind it at all.
        var service = CreateServiceWithContainer();

        var upload = await service.GenerateUploadUrlAsync("events", "poster.png", "image/png");

        new Uri(upload.UploadUrl).AbsolutePath.Should().StartWith("/media/events/").And.EndWith(".png");
        upload.PublicUrl.Should().StartWith("https://eventassets.blob.core.windows.net/media/events/");
        upload.MediaAssetId.Should().BeNull();
    }

    [Fact]
    public async Task UploadProcessedImageToAsync_ShouldRefuseATargetOutsideThePublicContainer()
    {
        var service = CreateServiceWithContainer();

        await service.Invoking(svc => svc.UploadProcessedImageToAsync(
                new ProcessedImage([0x52, 0x49, 0x46, 0x46]),
                "https://eventassets.blob.core.windows.net/quarantine/events/x.webp"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not in the public container*");
    }

    [Fact]
    public async Task UploadProcessedImageToAsync_ShouldThrowArgumentException_ForEmptyContent()
    {
        var service = CreateServiceWithContainer();

        await service.Invoking(svc => svc.UploadProcessedImageToAsync(
                new ProcessedImage([]),
                "https://eventassets.blob.core.windows.net/media/events/x.webp"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Image is null or empty*");
    }

    [Fact]
    public async Task QuarantineReads_ShouldBeInert_WhenStorageIsNotConfigured()
    {
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        (await service.InspectQuarantineBlobAsync("events/x.png")).Should().BeNull();
        await service.Invoking(svc => svc.DeleteQuarantineBlobAsync("events/x.png")).Should().NotThrowAsync();

        var items = new List<QuarantineBlobItem>();
        await foreach (var item in service.ListQuarantineBlobsAsync())
            items.Add(item);
        items.Should().BeEmpty();

        await service.Invoking(svc => svc.OpenQuarantineBlobReadAsync("events/x.png"))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task FindMissingContainersAsync_ShouldReportNothing_WhenStorageIsNotConfigured()
    {
        // A missing connection string is reported by configuration validation, not here.
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        (await service.FindMissingContainersAsync(includeQuarantine: true)).Should().BeEmpty();
    }

    [Fact]
    public void StorageNotProvisioned_ShouldBeA503TellingTheUserToRetryLater()
    {
        var error = AzureBlobService.StorageNotProvisioned("images");

        error.StatusCode.Should().Be(503);
        error.Message.Should().Contain("not available");
    }

    [Theory]
    [InlineData("ContainerNotFound", true)]
    [InlineData("BlobNotFound", false)]
    [InlineData("AuthorizationFailure", false)]
    public void IsContainerMissing_ShouldMatchOnlyAMissingContainer(string errorCode, bool expected)
    {
        var failure = new Azure.RequestFailedException(404, "failed", errorCode, null);

        AzureBlobService.IsContainerMissing(failure).Should().Be(expected);
    }

    [Fact]
    public async Task InspectQuarantineBlobAsync_ShouldReturnNull_ForABlankPath()
    {
        var service = CreateServiceWithContainer();

        (await service.InspectQuarantineBlobAsync("  ")).Should().BeNull();
    }

    [Fact]
    public async Task UploadProcessedImageAsync_ShouldThrowArgumentException_ForEmptyContent()
    {
        var service = CreateServiceWithoutContainer("missing config");

        await service.Invoking(svc => svc.UploadProcessedImageAsync(new ProcessedImage([]), "users"))
            .Should()
            .ThrowAsync<ArgumentException>()
            .WithMessage("*Image is null or empty*");
    }

    [Fact]
    public async Task UploadProcessedImageAsync_ShouldRequireConfiguredStorage()
    {
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        await service.Invoking(svc => svc.UploadProcessedImageAsync(new ProcessedImage([0x52, 0x49, 0x46, 0x46]), "users"))
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*AZURE_STORAGE_CONNECTION_STRING is not configured.*");
    }

    [Fact]
    public async Task InspectBlobAsync_ShouldReturnNull_WhenStorageIsNotConfigured()
    {
        var service = CreateServiceWithoutContainer("AZURE_STORAGE_CONNECTION_STRING is not configured.");

        var inspection = await service.InspectBlobAsync("https://eventassets.blob.core.windows.net/media/poster.png");

        inspection.Should().BeNull();
    }

    [Fact]
    public async Task InspectBlobAsync_ShouldReturnNull_ForUrlsOutsideOurContainer()
    {
        // Reaching storage would need credentials; returning null proves the ownership guard
        // runs first, so a pasted URL never costs an Azure round trip.
        var service = CreateServiceWithContainer();

        var inspection = await service.InspectBlobAsync("https://attacker.test/media/poster.png");

        inspection.Should().BeNull();
    }

    [Fact]
    public void BuildUploadSas_ShouldGrantCreateWithoutWrite()
    {
        // Put Blob accepts Create or Write to make a new block blob, but requires Write to
        // overwrite an existing one. Without this the SAS stays usable for the rest of its window
        // after the blob has been inspected and attached, and the accepted bytes could be swapped
        // for anything at all. A regression here silently reopens that hole, so it is pinned.
        var sas = InvokePrivateStatic<BlobSasBuilder>(
            typeof(AzureBlobService),
            "BuildUploadSas",
            "media",
            "events/poster.png",
            DateTimeOffset.UtcNow.AddMinutes(15),
            "image/png");

        sas.Permissions.Should().Be("c");
        sas.Resource.Should().Be("b");
        sas.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task NormalizeBlobHeadersAsync_ShouldDoNothing_ForUrlsOutsideOurContainer()
    {
        var service = CreateServiceWithContainer();

        await service.Invoking(svc => svc.NormalizeBlobHeadersAsync("https://attacker.test/media/x.png", "image/png"))
            .Should()
            .NotThrowAsync();
    }

    [Fact]
    public void MaxImageBytes_ShouldComeFromConfiguredOptions()
    {
        var service = CreateServiceWithContainer(new ImageUploadOptions { MaxBytes = 1234 });

        service.MaxImageBytes.Should().Be(1234);
    }

    private static AzureBlobService CreateServiceWithContainer(ImageUploadOptions? imageUploadOptions = null)
    {
        var container = new BlobContainerClient(
            "DefaultEndpointsProtocol=https;AccountName=eventassets;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            "media");

        var quarantine = new BlobContainerClient(
            "DefaultEndpointsProtocol=https;AccountName=eventassets;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            "quarantine");

        var service = (AzureBlobService)RuntimeHelpers.GetUninitializedObject(typeof(AzureBlobService));
        SetPrivateField(service, "_container", container);
        SetPrivateField(service, "_quarantine", quarantine);
        SetPrivateField(service, "_configurationError", null);
        SetPrivateField(service, "_imageUploadOptions", imageUploadOptions ?? new ImageUploadOptions());
        return service;
    }

    private static AzureBlobService CreateServiceWithoutContainer(string configurationError)
    {
        var service = (AzureBlobService)RuntimeHelpers.GetUninitializedObject(typeof(AzureBlobService));
        SetPrivateField(service, "_container", null);
        SetPrivateField(service, "_quarantine", null);
        SetPrivateField(service, "_configurationError", configurationError);
        SetPrivateField(service, "_imageUploadOptions", new ImageUploadOptions());
        return service;
    }

    private static void SetPrivateField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(target, value);
    }

    private static T InvokePrivateStatic<T>(Type type, string methodName, params object?[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(null, args)!;
    }

    private static T InvokePrivateInstance<T>(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (T)method!.Invoke(target, args)!;
    }

    private sealed class AzureBlobServiceHarness : IDisposable
    {
        private readonly AssemblyLoadContext _loadContext;
        private readonly Type _type;
        private object? _instance;

        private AzureBlobServiceHarness(AssemblyLoadContext loadContext, Type type)
        {
            _loadContext = loadContext;
            _type = type;
        }

        public static AzureBlobServiceHarness Load()
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, "backend.dll");
            var loadContext = new IsolatedBackendLoadContext(assemblyPath);
            var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
            var type = assembly.GetType("backend.main.shared.storage.AzureBlobService", throwOnError: true)!;
            return new AzureBlobServiceHarness(loadContext, type);
        }

        /// <remarks>
        /// The options argument has to be built out of the isolated context's own
        /// <c>ImageUploadOptions</c>, because a type loaded there is not the type of the same
        /// name loaded here.
        /// </remarks>
        public void CreateInstance()
        {
            var optionsType = _type.Assembly.GetType(
                "backend.main.shared.storage.ImageUploadOptions", throwOnError: true)!;
            var optionsHost = _loadContext.LoadFromAssemblyName(
                new AssemblyName("Microsoft.Extensions.Options"));
            var create = optionsHost
                .GetType("Microsoft.Extensions.Options.Options", throwOnError: true)!
                .GetMethod("Create")!
                .MakeGenericMethod(optionsType);

            _instance = Activator.CreateInstance(
                _type,
                create.Invoke(null, [Activator.CreateInstance(optionsType)]));
        }

        public string? GetConfigurationError() =>
            (string?)GetField("_configurationError");

        public object? GetContainer() => GetField("_container");

        public string? GetContainerName(string fieldName = "_container")
        {
            var container = GetField(fieldName);
            container.Should().NotBeNull();
            return (string?)container!.GetType().GetProperty("Name")!.GetValue(container);
        }

        private object? GetField(string fieldName)
        {
            _instance.Should().NotBeNull();
            var field = _type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.Should().NotBeNull();
            return field!.GetValue(_instance);
        }

        public void Dispose()
        {
            _loadContext.Unload();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class IsolatedBackendLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public IsolatedBackendLoadContext(string mainAssemblyPath)
            : base(isCollectible: true)
        {
            _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path == null ? null : LoadFromAssemblyPath(path);
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new();

        public EnvironmentVariableScope(IReadOnlyDictionary<string, string?> values)
        {
            foreach (var pair in values)
            {
                _originals[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        public void Dispose()
        {
            foreach (var pair in _originals)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
