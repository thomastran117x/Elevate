namespace backend.main.shared.http
{
    public interface IExternalApiClient
    {
        HttpClient HttpClient
        {
            get;
        }

        Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct = default
        );
        Task<HttpResponseMessage> PostAsync(
            string requestUri,
            HttpContent content,
            CancellationToken ct = default
        );
        Task<HttpResponseMessage> GetAsync(string requestUri, CancellationToken ct = default);
    }
}
