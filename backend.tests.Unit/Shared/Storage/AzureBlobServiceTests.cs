using System.Reflection;
using System.Runtime.CompilerServices;

using Azure.Storage.Blobs;

using backend.main.shared.exceptions.http;
using backend.main.shared.storage;

using FluentAssertions;

namespace backend.tests.Unit.Shared.Storage;

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
}
