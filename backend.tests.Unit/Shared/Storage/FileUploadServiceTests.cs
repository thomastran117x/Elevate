using System.Text;

using backend.main.shared.storage;
using backend.main.shared.utilities.logger;

using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

using Moq;

namespace backend.tests.Unit.Shared.Storage;

public class FileUploadServiceTests
{
    [Fact]
    public async Task UploadImageAsync_ShouldPersistFileUnderSanitizedFolder_AndReturnAbsoluteUrl()
    {
        var webRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(webRoot, out _);
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("avatar-bytes"));
            var image = new FormFile(stream, 0, stream.Length, "image", "avatar.png");

            var url = await service.UploadImageAsync(image, "..\\avatars");

            url.Should().StartWith("https://event.test/uploads/avatars/");
            url.Should().EndWith(".png");

            var uploadsFolder = Path.Combine(webRoot, "uploads", "avatars");
            Directory.Exists(uploadsFolder).Should().BeTrue();

            var files = Directory.GetFiles(uploadsFolder);
            files.Should().ContainSingle();
            var persistedContent = await File.ReadAllTextAsync(files[0]);
            persistedContent.Should().Be("avatar-bytes");
        }
        finally
        {
            DeleteDirectoryIfExists(webRoot);
        }
    }

    [Fact]
    public async Task UploadImageAsync_ShouldThrow_ForNullOrEmptyImage()
    {
        var webRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(webRoot, out _);
            await using var emptyStream = new MemoryStream();
            var emptyFile = new FormFile(emptyStream, 0, 0, "image", "empty.png");

            var nullAction = async () => await service.UploadImageAsync(null!, "avatars");
            var emptyAction = async () => await service.UploadImageAsync(emptyFile, "avatars");

            await nullAction.Should().ThrowAsync<ArgumentException>().WithMessage("Image is null or empty");
            await emptyAction.Should().ThrowAsync<ArgumentException>().WithMessage("Image is null or empty");
        }
        finally
        {
            DeleteDirectoryIfExists(webRoot);
        }
    }

    [Fact]
    public async Task DeleteImageAsync_ShouldDeleteExistingFile()
    {
        var webRoot = CreateTempDirectory();

        try
        {
            var uploadsFolder = Path.Combine(webRoot, "uploads", "avatars");
            Directory.CreateDirectory(uploadsFolder);
            var filePath = Path.Combine(uploadsFolder, "photo.png");
            await File.WriteAllTextAsync(filePath, "to-delete");

            var service = CreateService(webRoot, out _);

            await service.DeleteImageAsync("https://event.test/uploads/avatars/photo.png");

            File.Exists(filePath).Should().BeFalse();
        }
        finally
        {
            DeleteDirectoryIfExists(webRoot);
        }
    }

    [Fact]
    public async Task DeleteImageAsync_ShouldSwallowMalformedUrls_AndLogWarning()
    {
        var webRoot = CreateTempDirectory();

        try
        {
            var service = CreateService(webRoot, out _);
            var logger = new TestLogger();
            Logger.SetInstance(logger);

            await service.DeleteImageAsync("not-a-valid-url");

            logger.WarnMessages.Should().ContainSingle(message => message.Contains("Failed to delete image: not-a-valid-url"));
        }
        finally
        {
            Logger.SetInstance(new TestLogger());
            DeleteDirectoryIfExists(webRoot);
        }
    }

    private static FileUploadService CreateService(string webRootPath, out DefaultHttpContext httpContext)
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(item => item.WebRootPath).Returns(webRootPath);

        httpContext = new DefaultHttpContext();
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("event.test");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(item => item.HttpContext).Returns(httpContext);

        return new FileUploadService(env.Object, accessor.Object);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "event-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectoryIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    private sealed class TestLogger : ICustomLogger
    {
        public List<string> WarnMessages { get; } = [];

        public void Debug(string message) { }

        public void Info(string message) { }

        public void Warn(string message) => WarnMessages.Add(message);

        public void Error(string message) { }

        public void Warn(Exception ex, string? message = null) =>
            WarnMessages.Add(message ?? ex.Message);

        public void Error(Exception ex, string? message = null) { }

        public void Log(LogLevel level, string message)
        {
            if (level == LogLevel.Warn)
                WarnMessages.Add(message);
        }

        public void Log(LogLevel level, Exception ex, string? message = null)
        {
            if (level == LogLevel.Warn)
                WarnMessages.Add(message ?? ex.Message);
        }
    }
}
