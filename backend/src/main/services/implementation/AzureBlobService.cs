using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using backend.main.application.environment;
using backend.main.shared.exceptions.http;
using backend.main.dtos.responses.events;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

namespace backend.main.services.implementation
{
    public class AzureBlobService : IAzureBlobService
    {
        private static readonly Dictionary<string, string[]> AllowedImageTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
                ["image/jpg"] = new[] { ".jpg", ".jpeg" },
                ["image/png"] = new[] { ".png" },
                ["image/webp"] = new[] { ".webp" },
                ["image/gif"] = new[] { ".gif" }
            };

        private readonly BlobContainerClient? _container;
        private readonly string? _configurationError;

        public AzureBlobService()
        {
            var connectionString = EnvironmentSetting.AzureStorageConnectionString;
            var containerName = EnvironmentSetting.AzureStorageContainerName;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                _configurationError = "AZURE_STORAGE_CONNECTION_STRING is not configured.";
                return;
            }

            if (string.IsNullOrWhiteSpace(containerName))
            {
                _configurationError = "AZURE_STORAGE_CONTAINER_NAME is not configured.";
                return;
            }

            _container = new BlobContainerClient(connectionString, containerName);
        }

        public async Task<PresignedUploadResponse> GenerateUploadUrlAsync(
            string blobPathPrefix,
            string fileName,
            string contentType)
        {
            var container = GetRequiredContainer();

            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var extension = ValidateAndNormalizeImageExtension(fileName, contentType);
            var normalizedPrefix = string.IsNullOrWhiteSpace(blobPathPrefix)
                ? "events"
                : blobPathPrefix.Trim().Trim('/');
            var blobName = $"{normalizedPrefix}/{Guid.NewGuid():N}{extension}";
            var blobClient = container.GetBlobClient(blobName);

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = container.Name,
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

        public bool IsOwnedBlobUrl(string blobUrl) =>
            TryGetManagedBlobPath(blobUrl, out _);

        public async Task DeleteBlobAsync(string blobUrl)
        {
            try
            {
                if (!TryGetManagedBlobPath(blobUrl, out var blobPath))
                    return;

                var container = _container;
                if (container == null)
                    return;

                var blobClient = container.GetBlobClient(blobPath);
                await blobClient.DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"[AzureBlobService] Best-effort blob deletion failed for: {blobUrl}");
            }
        }

        private BlobContainerClient GetRequiredContainer()
        {
            if (_container != null)
                return _container;

            throw new InvalidOperationException(
                _configurationError ?? "Azure Blob Storage is not configured."
            );
        }

        private static string ValidateAndNormalizeImageExtension(string fileName, string contentType)
        {
            var safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
            if (string.IsNullOrWhiteSpace(safeFileName))
                throw new BadRequestException("A valid file name is required.");

            var normalizedContentType = (contentType ?? string.Empty).Trim().ToLowerInvariant();
            if (!AllowedImageTypes.TryGetValue(normalizedContentType, out var allowedExtensions))
            {
                throw new UnsupportedMediaTypeException(
                    "Only JPEG, PNG, WEBP, and GIF images are supported.");
            }

            var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension) || !allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                throw new BadRequestException(
                    "The file extension must match the supplied image content type.");
            }

            return extension;
        }

        private bool TryGetManagedBlobPath(string blobUrl, out string blobPath)
        {
            blobPath = string.Empty;

            if (_container == null)
                return false;

            if (!Uri.TryCreate(blobUrl, UriKind.Absolute, out var blobUri))
                return false;

            if (!string.Equals(blobUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var containerUri = _container.Uri;
            if (!string.Equals(blobUri.Host, containerUri.Host, StringComparison.OrdinalIgnoreCase))
                return false;

            var containerPath = containerUri.AbsolutePath.TrimEnd('/');
            var requiredPrefix = containerPath + "/";
            if (!blobUri.AbsolutePath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
                return false;

            blobPath = blobUri.AbsolutePath[requiredPrefix.Length..];
            return !string.IsNullOrWhiteSpace(blobPath);
        }
    }
}
