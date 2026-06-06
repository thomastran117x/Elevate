namespace backend.main.shared.storage
{
    [Obsolete("Use IAzureBlobService instead.")]
    public interface IFileUploadService
    {
        Task<string> UploadImageAsync(IFormFile image, string folder);
        Task DeleteImageAsync(string imageUrl);

    }
}
