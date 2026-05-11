using backend.main.services.interfaces;
using backend.main.shared.utilities.logger;

namespace backend.main.services.implementation
{
    public class FileUploadService : IFileUploadService
    {
        private readonly IWebHostEnvironment _env;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FileUploadService(IWebHostEnvironment env, IHttpContextAccessor httpContextAccessor)
        {
            _env = env;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<string> UploadImageAsync(IFormFile image, string folder)
        {
            if (image == null || image.Length == 0)
                throw new ArgumentException("Image is null or empty");

            var safeFolder = Path.GetFileName(folder);

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", safeFolder);
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var uniqueName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
            var path = Path.Combine(uploadsFolder, uniqueName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await image.CopyToAsync(stream);
            }

            var request = _httpContextAccessor.HttpContext!.Request;
            var fileUrl = $"{request.Scheme}://{request.Host}/uploads/{safeFolder}/{uniqueName}";
            return fileUrl;
        }

        public Task DeleteImageAsync(string? imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
                return Task.CompletedTask;

            try
            {
                var uploadsRoot = _env.WebRootPath;
                var uri = new Uri(imageUrl);
                var relativePath = uri.AbsolutePath.TrimStart('/');
                var fullPath = Path.Combine(uploadsRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }
            }
            catch (Exception)
            {
                Logger.Warn($"Failed to delete image: {imageUrl}");
            }

            return Task.CompletedTask;
        }
    }
}
