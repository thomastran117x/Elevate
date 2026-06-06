using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using backend.main.shared.storage;

using FluentAssertions;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

public class FileUploadServiceTests
{
    [Fact]
    public async Task UploadImageAsync_ShouldPersistFile_AndReturnPublicUrl()
    {
        var webRoot = CreateTempDirectory();
        try
        {
            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(webRoot);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.test");

            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

            var service = new FileUploadService(env.Object, accessor.Object);
            var file = CreateFormFile("poster.png", "image-content");

            var url = await service.UploadImageAsync(file, @"..\clubs\gallery");

            url.Should().StartWith("https://example.test/uploads/gallery/");

            var uploads = Directory.GetFiles(Path.Combine(webRoot, "uploads", "gallery"));
            uploads.Should().ContainSingle();
            var persisted = await File.ReadAllTextAsync(uploads[0]);
            persisted.Should().Be("image-content");
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UploadImageAsync_ShouldCreateUploadsFolder_WhenMissing()
    {
        var webRoot = CreateTempDirectory();
        try
        {
            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(webRoot);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Scheme = "http";
            httpContext.Request.Host = new HostString("localhost:5000");

            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(httpContext);

            var service = new FileUploadService(env.Object, accessor.Object);

            await service.UploadImageAsync(CreateFormFile("avatar.jpg", "x"), "avatars");

            Directory.Exists(Path.Combine(webRoot, "uploads", "avatars")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UploadImageAsync_ShouldThrow_ForNullOrEmptyImage()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.WebRootPath).Returns(CreateTempDirectory());

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

        var service = new FileUploadService(env.Object, accessor.Object);

        await service.Invoking(s => s.UploadImageAsync(null!, "uploads"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("Image is null or empty");

        await service.Invoking(s => s.UploadImageAsync(CreateFormFile("empty.png", string.Empty), "uploads"))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("Image is null or empty");
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("./../")]
    public async Task UploadImageAsync_ShouldThrow_ForInvalidFolder(string folder)
    {
        var env = new Mock<IWebHostEnvironment>();
        var webRoot = CreateTempDirectory();
        env.SetupGet(e => e.WebRootPath).Returns(webRoot);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

        var service = new FileUploadService(env.Object, accessor.Object);

        await service.Invoking(s => s.UploadImageAsync(CreateFormFile("poster.png", "image"), folder))
            .Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Folder is invalid*");

        Directory.Delete(webRoot, recursive: true);
    }

    [Fact]
    public async Task DeleteImageAsync_ShouldDeleteExistingManagedFile()
    {
        var webRoot = CreateTempDirectory();
        try
        {
            var uploads = Path.Combine(webRoot, "uploads", "events");
            Directory.CreateDirectory(uploads);
            var path = Path.Combine(uploads, "poster.png");
            await File.WriteAllTextAsync(path, "content");

            var env = new Mock<IWebHostEnvironment>();
            env.SetupGet(e => e.WebRootPath).Returns(webRoot);

            var accessor = new Mock<IHttpContextAccessor>();
            accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

            var service = new FileUploadService(env.Object, accessor.Object);

            await service.DeleteImageAsync("https://example.test/uploads/events/poster.png");

            File.Exists(path).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(webRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteImageAsync_ShouldNoOp_ForBlankOrMissingFile()
    {
        var env = new Mock<IWebHostEnvironment>();
        var webRoot = CreateTempDirectory();
        env.SetupGet(e => e.WebRootPath).Returns(webRoot);

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());

        var service = new FileUploadService(env.Object, accessor.Object);

        await service.Invoking(s => s.DeleteImageAsync(null)).Should().NotThrowAsync();
        await service.Invoking(s => s.DeleteImageAsync("https://example.test/uploads/events/missing.png")).Should().NotThrowAsync();

        Directory.Delete(webRoot, recursive: true);
    }

    private static FormFile CreateFormFile(string fileName, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "event-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
