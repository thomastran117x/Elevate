namespace backend.main.shared.providers
{
    public sealed class ExternalApiClient : IExternalApiClient
    {
        public HttpClient HttpClient
        {
            get;
        }

        public ExternalApiClient(HttpClient httpClient) => HttpClient = httpClient;

        public Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken ct = default
        ) => HttpClient.SendAsync(request, ct);

        public Task<HttpResponseMessage> PostAsync(
            string requestUri,
            HttpContent content,
            CancellationToken ct = default
        ) => HttpClient.PostAsync(requestUri, content, ct);

        public Task<HttpResponseMessage> GetAsync(
            string requestUri,
            CancellationToken ct = default
        ) => HttpClient.GetAsync(requestUri, ct);
    }
}
