using System.Text.Json;

using backend.main.dtos.responses.external;
using backend.main.services.interfaces;

using Polly.CircuitBreaker;

namespace backend.main.services.implementations
{
    public sealed class GoogleCaptchaService : ICaptchaService
    {
        private const string VerifyPath = "recaptcha/api/siteverify";

        private readonly HttpClient _http;
        private readonly ILogger<GoogleCaptchaService> _logger;
        private readonly string? _captchaSecret;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public GoogleCaptchaService(HttpClient http, ILogger<GoogleCaptchaService> logger, IConfiguration config)
        {
            _http = http;
            _logger = logger;
            _captchaSecret = config["GoogleCaptcha:Secret"] ?? config["GOOGLE_CAPTCHA_SECRET"];
        }

        public async Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_captchaSecret))
            {
                _logger.LogWarning("[Captcha] Google Captcha secret not configured. Skipping validation.");
                return true;
            }

            try
            {
                using var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["secret"] = _captchaSecret!,
                    ["response"] = token
                });

                using var resp = await _http.PostAsync(VerifyPath, form, cancellationToken);

                if (!resp.IsSuccessStatusCode)
                {
                    var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogError(
                        "[Captcha] Google returned HTTP {Status}. Body: {Body}. Allowing login (fail-open).",
                        (int)resp.StatusCode,
                        body
                    );
                    return true;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<GoogleCaptchaResponse>(stream, JsonOpts, cancellationToken);

                if (payload == null)
                {
                    _logger.LogError("[Captcha] Google response deserialized to null. Allowing login (fail-open).");
                    return true;
                }

                if (payload.Success != true)
                {
                    _logger.LogWarning(
                        "[Captcha] Captcha failed (explicit). Errors: {Errors}",
                        payload.ErrorCodes is not null ? string.Join(",", payload.ErrorCodes) : "none"
                    );
                    return false;
                }

                return true;
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(ex, "[Captcha] Circuit open. Skipping Google call. Allowing login (fail-open).");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Captcha] Verification request failed. Allowing login (fail-open).");
                return true;
            }
        }
    }
}
