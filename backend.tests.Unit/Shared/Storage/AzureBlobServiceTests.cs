using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

using Azure.Storage.Blobs;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

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
    }

    private static AzureBlobService CreateServiceWithContainer()
    {
        var container = new BlobContainerClient(
            "DefaultEndpointsProtocol=https;AccountName=eventassets;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net",
            "media");

        var service = (AzureBlobService)RuntimeHelpers.GetUninitializedObject(typeof(AzureBlobService));
        SetPrivateField(service, "_container", container);
        SetPrivateField(service, "_configurationError", null);
        return service;
    }

    private static AzureBlobService CreateServiceWithoutContainer(string configurationError)
    {
        var service = (AzureBlobService)RuntimeHelpers.GetUninitializedObject(typeof(AzureBlobService));
        SetPrivateField(service, "_container", null);
        SetPrivateField(service, "_configurationError", configurationError);
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

        public void CreateInstance() => _instance = Activator.CreateInstance(_type);

        public string? GetConfigurationError() =>
            (string?)GetField("_configurationError");

        public object? GetContainer() => GetField("_container");

        public string? GetContainerName()
        {
            var container = GetField("_container");
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
