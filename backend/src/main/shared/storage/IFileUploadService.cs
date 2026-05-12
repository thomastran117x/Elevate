namespace backend.main.shared.storage
{
    public interface IFileUploadService
    {
        Task<string> UploadImageAsync(IFormFile image, string folder);
        Task DeleteImageAsync(string imageUrl);

    }
}
