using backend.main.shared.storage;

namespace backend.tests.Integration.Infrastructure;

public sealed class FakeFileUploadService : IFileUploadService
{
    private readonly Dictionary<string, byte[]> _files = [];

    public Task<string> UploadImageAsync(IFormFile image, string folder)
    {
        var extension = Path.GetExtension(image.FileName);
        var path = $"/uploads/{folder}/{Guid.NewGuid():N}{extension}";
        _files[path] = [];
        return Task.FromResult($"https://localhost{path}");
    }

    public Task DeleteImageAsync(string imageUrl)
    {
        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out var absoluteUri))
            _files.Remove(absoluteUri.AbsolutePath);

        return Task.CompletedTask;
    }
}
