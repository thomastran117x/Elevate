using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

using backend.main.application.environment;
using backend.main.features.events.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.storage.imaging;
using backend.main.shared.utilities.logger;

using Microsoft.Extensions.Options;

namespace backend.main.shared.storage
{
    public class AzureBlobService : IAzureBlobService
    {
        private static readonly Dictionary<string, string[]> AllowedImageTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["image/jpeg"] = new[] { ".jpg", ".jpeg" },
                ["image/jpg"] = new[] { ".jpg", ".jpeg" },
                ["image/png"] = new[] { ".png" },
                [WebpMedia.ContentType] = new[] { WebpMedia.FileExtension },
                ["image/gif"] = new[] { ".gif" }
            };
        private static readonly Dictionary<string, string> CanonicalContentTypesByExtension =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [".jpg"] = "image/jpeg",
                [".jpeg"] = "image/jpeg",
                [".png"] = "image/png",
                [WebpMedia.FileExtension] = WebpMedia.ContentType,
                [".gif"] = "image/gif"
            };

        private readonly BlobContainerClient? _container;
        private readonly BlobContainerClient? _quarantine;
        private readonly string? _configurationError;
        private readonly ImageUploadOptions _imageUploadOptions;

        public long MaxImageBytes => _imageUploadOptions.MaxBytes;

        public AzureBlobService(IOptions<ImageUploadOptions> imageUploadOptions)
        {
            _imageUploadOptions = imageUploadOptions.Value;

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
            _quarantine = new BlobContainerClient(
                connectionString,
                EnvironmentSetting.AzureStorageQuarantineContainerName);
        }

        public async Task<string> UploadProcessedImageAsync(
            ProcessedImage image,
            string blobPathPrefix,
            CancellationToken cancellationToken = default)
        {
            if (image == null || image.Content.Length == 0)
                throw new ArgumentException("Image is null or empty");

            // This container is anonymously readable, so the content type stamped here is what
            // the world is served. It and the extension come from the processed output — always
            // WebP — not from the caller's file name or declared type.
            var container = GetRequiredContainer();

            var normalizedPrefix = NormalizeBlobPathPrefix(blobPathPrefix, "uploads");
            var blobName = $"{normalizedPrefix}/{Guid.NewGuid():N}{image.FileExtension}";
            var blobClient = container.GetBlobClient(blobName);

            await UploadProcessedAsync(blobClient, image, cancellationToken);

            return blobClient.Uri.ToString();
        }

        public async Task UploadProcessedImageToAsync(
            ProcessedImage image,
            string publicUrl,
            CancellationToken cancellationToken = default)
        {
            if (image == null || image.Content.Length == 0)
                throw new ArgumentException("Image is null or empty");

            var container = GetRequiredContainer();
            if (!TryGetBlobPath(container, publicUrl, out var blobPath))
                throw new ArgumentException("The target URL is not in the public container.", nameof(publicUrl));

            // Overwrites: the name was reserved by this service when the upload was issued, so
            // the only earlier write it can meet is this same promotion, retried.
            await UploadProcessedAsync(container.GetBlobClient(blobPath), image, cancellationToken);
        }

        public async Task<PresignedUploadResponse> GenerateUploadUrlAsync(
            string blobPathPrefix,
            string fileName,
            string contentType)
        {
            // No CreateIfNotExists: both containers are provisioned out of band (the DevTasks
            // storage-provision command, or infrastructure), so the request path neither pays a
            // round trip for it nor needs a credential allowed to create containers.
            var container = GetRequiredContainer();

            var normalizedContentType = ResolveImageContentType(fileName, contentType);
            var extension = ValidateAndNormalizeImageExtension(fileName, normalizedContentType);
            var normalizedPrefix = NormalizeBlobPathPrefix(blobPathPrefix, "events");
            var blobName = $"{normalizedPrefix}/{Guid.NewGuid():N}{extension}";
            var blobClient = container.GetBlobClient(blobName);

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var sasBuilder = BuildUploadSas(container.Name, blobName, expiresAt, normalizedContentType);

            var uploadUrl = blobClient.GenerateSasUri(sasBuilder);

            return new PresignedUploadResponse
            {
                UploadUrl = uploadUrl.ToString(),
                PublicUrl = blobClient.Uri.ToString(),
                ExpiresAt = expiresAt
            };
        }

        public Task<QuarantineUpload> GenerateQuarantineUploadUrlAsync(
            string blobPathPrefix,
            string fileName,
            string contentType)
        {
            var container = GetRequiredContainer();
            var quarantine = GetRequiredQuarantine();

            var normalizedContentType = ResolveImageContentType(fileName, contentType);
            var extension = ValidateAndNormalizeImageExtension(fileName, normalizedContentType);
            var normalizedPrefix = NormalizeBlobPathPrefix(blobPathPrefix, "events");
            var id = Guid.NewGuid().ToString("N");

            // One id names both blobs. The quarantine copy keeps the uploader's extension; the
            // public one is reserved with the processor's, because whatever format arrives, what
            // is published is WebP.
            var quarantineBlobPath = $"{normalizedPrefix}/{id}{extension}";
            var publicBlobClient = container.GetBlobClient($"{normalizedPrefix}/{id}{WebpMedia.FileExtension}");

            var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
            var sasBuilder = BuildUploadSas(quarantine.Name, quarantineBlobPath, expiresAt, normalizedContentType);
            var uploadUrl = quarantine.GetBlobClient(quarantineBlobPath).GenerateSasUri(sasBuilder);

            return Task.FromResult(new QuarantineUpload(
                uploadUrl.ToString(),
                quarantineBlobPath,
                publicBlobClient.Uri.ToString(),
                normalizedContentType,
                expiresAt));
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

        public async IAsyncEnumerable<BlobListItem> ListBlobsAsync(
            string blobPathPrefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var container = _container;
            if (container == null)
                yield break;

            var normalizedPrefix = NormalizeBlobPathPrefix(blobPathPrefix, string.Empty);
            var listPrefix = string.IsNullOrEmpty(normalizedPrefix) ? null : normalizedPrefix + "/";

            await foreach (var blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, listPrefix, cancellationToken))
            {
                var url = container.GetBlobClient(blob.Name).Uri.ToString();
                yield return new BlobListItem(url, blob.Properties.LastModified);
            }
        }

        public async Task<BlobInspection?> InspectBlobAsync(
            string blobUrl,
            int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetManagedBlobPath(blobUrl, out var blobPath))
                return null;

            var container = _container;
            if (container == null)
                return null;

            return await InspectAsync(container.GetBlobClient(blobPath), prefixByteCount, cancellationToken);
        }

        private static async Task<BlobInspection?> InspectAsync(
            BlobClient blobClient,
            int prefixByteCount,
            CancellationToken cancellationToken)
        {
            try
            {
                var properties = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

                // Only the header crosses the wire. Downloading the blob to find out how big it
                // is would defeat the point of having a size cap at all.
                var requestedBytes = Math.Max(prefixByteCount, 0);
                var header = Array.Empty<byte>();

                if (requestedBytes > 0 && properties.Value.ContentLength > 0)
                {
                    var download = await blobClient.DownloadStreamingAsync(
                        new BlobDownloadOptions { Range = new HttpRange(0, requestedBytes) },
                        cancellationToken);

                    await using var content = download.Value.Content;
                    var buffer = new byte[requestedBytes];
                    var read = await content.ReadAtLeastAsync(
                        buffer, requestedBytes, throwOnEndOfStream: false, cancellationToken);
                    header = buffer[..read];
                }

                return new BlobInspection(
                    properties.Value.ContentLength,
                    properties.Value.ContentType,
                    header);
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                // The upload never completed, or the blob is already gone. Either way there is
                // nothing to attach; a transient fault is deliberately not caught here, so it
                // can never be mistaken for a valid image.
                return null;
            }
        }

        public async Task<BlobInspection?> InspectQuarantineBlobAsync(
            string quarantineBlobPath,
            int prefixByteCount = ImageSignatureInspector.HeaderByteCount,
            CancellationToken cancellationToken = default)
        {
            if (_quarantine == null || string.IsNullOrWhiteSpace(quarantineBlobPath))
                return null;

            return await InspectAsync(
                _quarantine.GetBlobClient(quarantineBlobPath), prefixByteCount, cancellationToken);
        }

        public async Task<Stream?> OpenQuarantineBlobReadAsync(
            string quarantineBlobPath,
            CancellationToken cancellationToken = default)
        {
            var quarantine = GetRequiredQuarantine();

            try
            {
                var download = await quarantine.GetBlobClient(quarantineBlobPath)
                    .DownloadStreamingAsync(cancellationToken: cancellationToken);
                return download.Value.Content;
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                return null;
            }
        }

        public async Task DeleteQuarantineBlobAsync(string quarantineBlobPath)
        {
            try
            {
                if (_quarantine == null || string.IsNullOrWhiteSpace(quarantineBlobPath))
                    return;

                await _quarantine.GetBlobClient(quarantineBlobPath).DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"[AzureBlobService] Best-effort quarantine deletion failed for: {quarantineBlobPath}");
            }
        }

        public async IAsyncEnumerable<QuarantineBlobItem> ListQuarantineBlobsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var quarantine = _quarantine;
            if (quarantine == null)
                yield break;

            await foreach (var blob in quarantine.GetBlobsAsync(BlobTraits.None, BlobStates.None, null, cancellationToken))
                yield return new QuarantineBlobItem(blob.Name, blob.Properties.LastModified);
        }

        public async Task NormalizeBlobHeadersAsync(
            string blobUrl,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            if (!TryGetManagedBlobPath(blobUrl, out var blobPath))
                return;

            var container = _container;
            if (container == null)
                return;

            try
            {
                // Set Blob Properties sets the whole header group together and clears any member
                // the request leaves out, which is the point: sending only the content type drops
                // any Content-Disposition, Content-Encoding, Content-Language or Cache-Control the
                // uploader set on its PUT. The container is anonymously readable, so these headers
                // are what the public is served. The stored Content-MD5 is cleared with them;
                // nothing reads it.
                await container.GetBlobClient(blobPath).SetHttpHeadersAsync(
                    new BlobHttpHeaders { ContentType = contentType },
                    cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == StatusCodes.Status404NotFound)
            {
                // Swept or deleted between the inspection and here; there is nothing to stamp.
            }
        }

        private static async Task UploadProcessedAsync(
            BlobClient blobClient,
            ProcessedImage image,
            CancellationToken cancellationToken)
        {
            await blobClient.UploadAsync(
                BinaryData.FromBytes(image.Content),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = image.ContentType
                    }
                },
                cancellationToken);
        }

        private BlobContainerClient GetRequiredQuarantine()
        {
            if (_quarantine != null)
                return _quarantine;

            throw new InvalidOperationException(
                _configurationError ?? "Azure Blob Storage is not configured."
            );
        }

        private BlobContainerClient GetRequiredContainer()
        {
            if (_container != null)
                return _container;

            throw new InvalidOperationException(
                _configurationError ?? "Azure Blob Storage is not configured."
            );
        }

        /// <summary>
        /// Builds the write-once SAS a client uploads through.
        /// </summary>
        /// <remarks>
        /// Create without Write is deliberate. Per <c>Put Blob</c>'s authorization rules, creating
        /// a new block blob accepts either permission but overwriting an existing one requires
        /// Write, so granting only Create makes the URL usable exactly once. The SAS outlives the
        /// attach by the rest of its window; with Write it could be replayed afterwards to swap
        /// the inspected bytes for something oversized or not an image at all.
        /// <para>
        /// <c>ContentType</c> is the SAS <c>rsct</c> response override, which applies only to
        /// reads made through this SAS — not to the anonymously readable public URL. The stored
        /// content type comes from the client's own PUT headers, so it is not trusted here and is
        /// restamped from the bytes when the blob is attached.
        /// </para>
        /// </remarks>
        private static BlobSasBuilder BuildUploadSas(
            string containerName,
            string blobName,
            DateTimeOffset expiresAt,
            string contentType)
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b",
                ExpiresOn = expiresAt,
                ContentType = contentType
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Create);

            return sasBuilder;
        }

        private static string ResolveImageContentType(string fileName, string? contentType)
        {
            var normalizedContentType = (contentType ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedContentType) &&
                normalizedContentType != "application/octet-stream")
            {
                return normalizedContentType;
            }

            var extension = Path.GetExtension(Path.GetFileName(fileName?.Trim() ?? string.Empty)).ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(extension) &&
                CanonicalContentTypesByExtension.TryGetValue(extension, out var inferredContentType))
            {
                return inferredContentType;
            }

            return normalizedContentType;
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

        private static string NormalizeBlobPathPrefix(string blobPathPrefix, string fallbackPrefix)
        {
            var segments = (blobPathPrefix ?? string.Empty)
                .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            var safeSegments = new List<string>();
            foreach (var segment in segments)
            {
                if (segment == ".")
                    continue;

                if (segment == "..")
                {
                    if (safeSegments.Count > 0)
                        safeSegments.RemoveAt(safeSegments.Count - 1);

                    continue;
                }

                safeSegments.Add(segment);
            }

            return safeSegments.Count == 0
                ? fallbackPrefix
                : string.Join('/', safeSegments);
        }

        private bool TryGetManagedBlobPath(string blobUrl, out string blobPath)
        {
            blobPath = string.Empty;

            return _container != null && TryGetBlobPath(_container, blobUrl, out blobPath);
        }

        private static bool TryGetBlobPath(BlobContainerClient container, string blobUrl, out string blobPath)
        {
            blobPath = string.Empty;

            if (!Uri.TryCreate(blobUrl, UriKind.Absolute, out var blobUri))
                return false;

            if (!string.Equals(blobUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            var containerUri = container.Uri;
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
