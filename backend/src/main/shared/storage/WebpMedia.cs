namespace backend.main.shared.storage
{
    /// <summary>
    /// The one spelling of WebP shared by everything that stores or recognises it.
    /// </summary>
    /// <remarks>
    /// The signature table, the blob service's type maps and the processed output all have to agree
    /// on these: every stored avatar is WebP, so a disagreement would mean the format check accepts
    /// a file the storage layer then serves as something else.
    /// </remarks>
    public static class WebpMedia
    {
        public const string ContentType = "image/webp";

        public const string FileExtension = ".webp";
    }
}
