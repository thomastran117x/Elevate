using System.Text.Json;

using backend.main.dtos.responses.external;
using backend.main.services.interfaces;

using Polly.CircuitBreaker;

namespace backend.main.services.implementations
{
    public sealed class GoogleCaptchaService : ICaptchaService
    {
        private const string VerifyPath = "recaptcha/api/siteverify";
        private const int DefaultTimeoutSeconds = 5;

        private readonly HttpClient _http;
        private readonly ILogger<GoogleCaptchaService> _logger;
        private readonly string? _captchaSecret;
        private readonly CaptchaVerificationPolicy _policy;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public GoogleCaptchaService(HttpClient http, ILogger<GoogleCaptchaService> logger, IConfiguration config)
        {
            _http = http;
            _logger = logger;
            _captchaSecret = config["GoogleCaptcha:Secret"] ?? config["GOOGLE_CAPTCHA_SECRET"];
            _policy = new CaptchaVerificationPolicy(config);
            var timeoutSeconds = config.GetValue("GoogleCaptcha:TimeoutSeconds", DefaultTimeoutSeconds);
            _http.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        public async Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default)
        {
            if (_policy.IsBypassEnabled)
            {
                _logger.LogWarning(
                    "[Captcha] Google captcha bypass is enabled for {Environment}.",
                    _policy.EnvironmentName
                );
                return true;
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("[Captcha] Google captcha token is missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(_captchaSecret))
            {
                _logger.LogError("[Captcha] Google captcha secret is not configured.");
                return false;
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
                        "[Captcha] Google returned HTTP {Status}. Body: {Body}. Rejecting captcha.",
                        (int)resp.StatusCode,
                        body
                    );
                    return false;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(cancellationToken);
                var payload = await JsonSerializer.DeserializeAsync<GoogleCaptchaResponse>(
                    stream,
                    JsonOpts,
                    cancellationToken
                );

                if (payload == null)
                {
                    _logger.LogError("[Captcha] Google response deserialized to null. Rejecting captcha.");
                    return false;
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
                _logger.LogError(ex, "[Captcha] Google captcha circuit is open. Rejecting captcha.");
                return false;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[Captcha] Google captcha response was malformed. Rejecting captcha.");
                return false;
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "[Captcha] Google captcha request timed out. Rejecting captcha.");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Captcha] Google captcha verification request failed. Rejecting captcha.");
                return false;
            }
        }
    }
}
