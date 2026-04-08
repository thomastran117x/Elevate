using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using backend.main.configurations.environment;
using backend.main.dtos.responses.events;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

namespace backend.main.services.implementation
{
    public class AzureBlobService : IAzureBlobService
    {
        private readonly BlobContainerClient _container;

        public AzureBlobService()
        {
            var connectionString = EnvironmentSetting.AzureStorageConnectionString
                ?? throw new InvalidOperationException("AZURE_STORAGE_CONNECTION_STRING is not configured.");
            var containerName = EnvironmentSetting.AzureStorageContainerName
                ?? throw new InvalidOperationException("AZURE_STORAGE_CONTAINER_NAME is not configured.");

            _container = new BlobContainerClient(connectionString, containerName);
        }

        public async Task<PresignedUploadResponse> GenerateUploadUrlAsync(string fileName, string contentType)
        {
            await _container.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var ext = Path.GetExtension(fileName);
            var blobName = $"events/{Guid.NewGuid()}{ext}";
            var blobClient = _container.GetBlobClient(blobName);

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = _container.Name,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = expiresAt
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Write | BlobSasPermissions.Create);

            var uploadUrl = blobClient.GenerateSasUri(sasBuilder);

            return new PresignedUploadResponse
            {
                UploadUrl = uploadUrl.ToString(),
                PublicUrl = blobClient.Uri.ToString(),
                ExpiresAt = expiresAt
            };
        }

        public async Task DeleteBlobAsync(string blobUrl)
        {
            try
            {
                var uri = new Uri(blobUrl);
                var blobPath = uri.AbsolutePath.TrimStart('/');
                // AbsolutePath is "/{container}/{blobName}" — strip the container prefix
                var containerPrefix = _container.Name + "/";
                if (blobPath.StartsWith(containerPrefix))
                    blobPath = blobPath[containerPrefix.Length..];

                var blobClient = _container.GetBlobClient(blobPath);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"[AzureBlobService] Best-effort blob deletion failed for: {blobUrl}");
            }
        }
    }
}
