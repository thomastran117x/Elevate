using System.Text.Json;

namespace backend.main.features.auth.captcha
{
    public sealed class CloudflareTurnstileCaptchaService : ICaptchaService
    {
        private const string SiteverifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
        private const int DefaultTimeoutSeconds = 5;

        private readonly HttpClient _http;
        private readonly ILogger<CloudflareTurnstileCaptchaService> _logger;
        private readonly string? _secret;
        private readonly CaptchaVerificationPolicy _policy;
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
            _policy = new CaptchaVerificationPolicy(config);

            _secret =
                config["Turnstile:Secret"]
                ?? config["CLOUDFLARE_TURNSTILE_SECRET"]
                ?? config["TURNSTILE_SECRET"];

            var timeoutSeconds = config.GetValue("Turnstile:TimeoutSeconds", DefaultTimeoutSeconds);
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        /// <inheritdoc />
        public async Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default)
        {
            if (_policy.IsBypassEnabled)
            {
                _logger.LogWarning(
                    "[Captcha] Turnstile captcha bypass is enabled for {Environment}.",
                    _policy.EnvironmentName
                );
                return true;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("[Captcha] Turnstile captcha token is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_secret))
            {
                _logger.LogError("[Captcha] Cloudflare Turnstile secret is not configured.");
                return false;
            }

            try
            {
                var formData = new Dictionary<string, string>
                {
                    ["secret"] = _secret!,
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
                        "[Captcha] Turnstile returned HTTP {Status}. Body: {Body}. Rejecting captcha.",
                        (int)resp.StatusCode,
                        body
                    );
                    return false;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<TurnstileSiteverifyResponse>(
                    stream,
                    JsonOpts,
                    cancellationToken
                );

                if (payload == null)
                {
                    _logger.LogError("[Captcha] Turnstile response deserialized to null. Rejecting captcha.");
                    return false;
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
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[Captcha] Turnstile response was malformed. Rejecting captcha.");
                return false;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[Captcha] Turnstile request timed out. Rejecting captcha.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Captcha] Turnstile verification request failed. Rejecting captcha.");
                return false;
            }
        }
    }
}

