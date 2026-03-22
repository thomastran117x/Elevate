using System.Text.Json;

using backend.main.dtos.responses.external;
using backend.main.exceptions.http;
using backend.main.services.interfaces;

namespace backend.main.services.implementations
{
    public sealed class CloudflareTurnstileCaptchaService : ICaptchaService
    {
        private const string SiteverifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
        private const int DefaultTimeoutSeconds = 5;

        private readonly HttpClient _http;
        private readonly ILogger<CloudflareTurnstileCaptchaService> _logger;
        private readonly string _secret;
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public CloudflareTurnstileCaptchaService(
            HttpClient http,
            ILogger<CloudflareTurnstileCaptchaService> logger,
            IConfiguration config,
            IHttpContextAccessor? httpContextAccessor = null)
        {
            _http = http;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;

            var secret = config["Turnstile:Secret"] ?? config["CLOUDFLARE_TURNSTILE_SECRET"] ?? config["TURNSTILE_SECRET"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                throw new NotAvailableException(
                    "Cloudflare Turnstile secret is not configured. Set one of: Turnstile:Secret, CLOUDFLARE_TURNSTILE_SECRET, or TURNSTILE_SECRET.");
            }
            _secret = secret;

            var timeoutSeconds = config.GetValue("Turnstile:TimeoutSeconds", DefaultTimeoutSeconds);
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <inheritdoc />
        public async Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default)
        {
            try
            {
                var formData = new Dictionary<string, string>
                {
                    ["secret"] = _secret,
                    ["response"] = token
                };

                var remoteIp = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
                if (!string.IsNullOrWhiteSpace(remoteIp))
                    formData["remoteip"] = remoteIp;

                using var form = new FormUrlEncodedContent(formData);

                using var resp = await _http.PostAsync(SiteverifyUrl, form, cancellationToken);

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "[Captcha] Turnstile returned HTTP {Status}. Body: {Body}. Allowing login (fail-open).",
                        (int)resp.StatusCode,
                        body
                    );
                    return true;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<TurnstileSiteverifyResponse>(stream, JsonOpts, cancellationToken);

                if (payload == null)
                {
                    _logger.LogError("[Captcha] Turnstile response deserialized to null. Allowing login (fail-open).");
                    return true;
                }

                if (payload.Success != true)
                {
                    _logger.LogWarning(
                        "[Captcha] Turnstile verification failed. ErrorCodes: {ErrorCodes}",
                        payload.ErrorCodes is not null ? string.Join(", ", payload.ErrorCodes) : "none"
                    );
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Captcha] Turnstile verification request failed. Allowing login (fail-open).");
                return true;
            }
        }
    }
}
