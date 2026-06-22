using System.Net;
using System.Net.Http.Headers;
using System.Text;

using backend.main.shared.providers.messages;

namespace backend.worker.sms_worker;

public sealed class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _httpClient;
    private readonly SmsWorkerOptions _options;
    private readonly AuthenticationHeaderValue _authorizationHeader;

    public TwilioSmsSender(HttpClient httpClient, SmsWorkerOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _authorizationHeader = BuildAuthorizationHeader(options);
    }

    public async Task SendAsync(SmsMfaMessage message, CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
            throw new InvalidOperationException("SMS worker is missing Twilio configuration.");

        ValidateMessage(message);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{_options.AccountSid}/Messages.json"
        )
        {
            Content = BuildContent(message)
        };

        request.Headers.Authorization = _authorizationHeader;

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        var messageText = $"Twilio SMS delivery failed with status {(int)response.StatusCode}: {detail}";

        if (IsTransient(response.StatusCode))
            throw new TransientSmsDeliveryException(messageText);

        throw new InvalidOperationException(messageText);
    }

    private HttpContent BuildContent(SmsMfaMessage message)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = message.PhoneNumber,
            ["Body"] = BuildBody(message)
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            fields["MessagingServiceSid"] = _options.MessagingServiceSid!;
        else
            fields["From"] = _options.FromPhoneNumber!;

        return new FormUrlEncodedContent(fields);
    }

    private static string BuildBody(SmsMfaMessage message)
    {
        var purpose = string.IsNullOrWhiteSpace(message.Purpose)
            ? "verification"
            : message.Purpose.Trim().ToLowerInvariant();

        return $"Your EventXperience {purpose} code is {message.Code}. It expires at {message.ExpiresAtUtc:O}.";
    }

    private static AuthenticationHeaderValue BuildAuthorizationHeader(SmsWorkerOptions options)
    {
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}")
        );

        return new AuthenticationHeaderValue("Basic", credentials);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status == 429 || status >= 500;
    }

    private static void ValidateMessage(SmsMfaMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.PhoneNumber))
            throw new InvalidOperationException("SMS payload requires a recipient phone number.");

        if (string.IsNullOrWhiteSpace(message.Code))
            throw new InvalidOperationException("SMS payload requires a verification code.");

        if (string.IsNullOrWhiteSpace(message.Challenge))
            throw new InvalidOperationException("SMS payload requires a verification challenge.");
    }
}
