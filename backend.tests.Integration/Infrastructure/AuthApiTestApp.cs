using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.oauth;
using backend.main.features.auth.token;
using backend.main.features.auth.device;
using backend.main.features.clubs.staff;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.payment;
using backend.main.features.profile;
using backend.main.infrastructure.database.core;
using backend.main.utilities;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Infrastructure;

public sealed class AuthApiTestApp : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TestWebApplicationFactory _factory;

    public HttpClient Client { get; }
    public InMemoryCacheService Cache => _factory.Cache;
    public CapturingPublisher Publisher => _factory.Publisher;
    public FakeOAuthService OAuth => _factory.OAuth;
    public FakeAzureBlobService BlobStorage => _factory.BlobStorage;

    private AuthApiTestApp(TestWebApplicationFactory factory, HttpClient client)
    {
        _factory = factory;
        Client = client;
    }

    public static Task<AuthApiTestApp> CreateAsync(Action<IServiceCollection>? serviceOverrides = null)
    {
        var factory = new TestWebApplicationFactory(serviceOverrides);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost")
        });
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36");

        return Task.FromResult(new AuthApiTestApp(factory, client));
    }

    public async Task<User> SeedUserAsync(
        string email,
        string password = "Password123!",
        string role = "Participant",
        bool disabled = false,
        string? googleId = null,
        string? microsoftId = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        var user = new User
        {
            Email = email,
            Password = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 4),
            Usertype = role,
            IsDisabled = disabled,
            GoogleID = googleId,
            MicrosoftID = microsoftId
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public async Task SeedKnownDeviceAsync(
        int userId,
        string trustedDeviceToken,
        string deviceType = "Desktop",
        string clientName = "Chrome")
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        db.Devices.Add(new Device
        {
            UserId = userId,
            DeviceTokenHash = ComputeHash(trustedDeviceToken),
            DeviceType = deviceType,
            ClientName = clientName,
            IpAddress = "127.0.0.1"
        });
        await db.SaveChangesAsync();
    }

    public async Task<User?> FindUserByEmailAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        return await db.Users.SingleOrDefaultAsync(user => user.Email == email);
    }

    public async Task AddClubStaffAsync(int clubId, int userId, int grantedByUserId, ClubStaffRole role = ClubStaffRole.Manager)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.ClubStaff.Add(new ClubStaff
        {
            ClubId = clubId,
            UserId = userId,
            GrantedByUserId = grantedByUserId,
            Role = role
        });

        await db.SaveChangesAsync();
    }

    public async Task AddAcceptedInvitationAsync(int eventId, int userId, string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.EventInvitations.Add(new EventInvitation
        {
            EventId = eventId,
            RecipientUserId = userId,
            RecipientEmail = email,
            RecipientEmailNormalized = email.Trim().ToLowerInvariant(),
            SourceType = EventInvitationSource.DirectUser,
            LifecycleStatus = EventInvitationLifecycleStatus.Accepted,
            DeliveryStatus = EventInvitationDeliveryStatus.Sent,
            AcceptedAtUtc = DateTime.UtcNow,
            AcceptedByUserId = userId
        });

        await db.SaveChangesAsync();
    }

    public async Task AddRegistrationAsync(int eventId, int userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.EventRegistrations.Add(new EventRegistration
        {
            EventId = eventId,
            UserId = userId,
            Status = RegistrationStatus.Active
        });

        var ev = await db.Events.FirstAsync(existing => existing.Id == eventId);
        ev.RegistrationCount += 1;
        ev.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task SetEventStartTimeToPast(int eventId, int minutesAgo = 10)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        ev.StartTime = DateTime.UtcNow.AddMinutes(-minutesAgo);
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetEventEndTimeToPast(int eventId, int minutesAgo = 5)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        ev.StartTime = DateTime.UtcNow.AddHours(-2);
        ev.EndTime = DateTime.UtcNow.AddMinutes(-minutesAgo);
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetMaxParticipants(int eventId, int max)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();
        var ev = await db.Events.FirstAsync(e => e.Id == eventId);
        ev.maxParticipants = max;
        ev.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task AddPaymentAsync(int eventId, int userId, PaymentStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabaseContext>();

        db.Payments.Add(new Payment
        {
            EventId = eventId,
            UserId = userId,
            Amount = 1000,
            Currency = "usd",
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
    }

    public async Task DeletePendingOAuthSignupAsync(string signupToken)
    {
        await Cache.DeleteKeyAsync($"oauth:pending:{signupToken}");
    }

    public async Task<HttpResponseMessage> PostJsonWithCsrfAsync(string path, object payload)
    {
        var token = await GetCsrfTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, token);
        return await Client.SendAsync(request);
    }

    public async Task<string> GetCsrfTokenAsync()
    {
        var response = await Client.GetAsync("/api/auth/csrf");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ApiEnvelope<CsrfTokenPayload>>(JsonOptions);
        return body!.Data!.Token;
    }

    public async Task<ApiEnvelope<T>> ReadApiResponseAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(JsonOptions);
        payload.Should().NotBeNull();
        return payload!;
    }

    public async Task<AuthenticatedSessionResponse> SignUpAndVerifyByTokenAsync(
        string email,
        string password = "Password123!",
        string role = "Participant",
        string? transport = null)
    {
        var signupResponse = await PostJsonWithCsrfAsync("/api/auth/signup", new SignUpRequest
        {
            Email = email,
            Password = password,
            Usertype = role,
            Captcha = "captcha"
        });
        signupResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var verifyEmail = Publisher.EmailMessages
            .Last(message => message.Type == backend.main.shared.providers.messages.EmailMessageType.VerifyEmail
                && message.Email == email);

        var verifyResponse = await PostJsonWithCsrfAsync("/api/auth/verify", new VerificationTokenRequest
        {
            Token = verifyEmail.Token,
            Transport = transport
        });
        verifyResponse.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var apiResponse = await ReadApiResponseAsync<AuthenticatedSessionResponse>(verifyResponse);
        apiResponse.Data.Should().NotBeNull();
        return apiResponse.Data!;
    }

    public async Task<AuthenticatedSessionResponse> LoginApiAsync(
        string email,
        string password = "Password123!",
        string trustedDeviceToken = "known-device")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Email = email,
                Password = password,
                Captcha = "captcha",
                Transport = SessionTransportResolver.ApiValue
            })
        };
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, trustedDeviceToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await GetCsrfTokenAsync());

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var apiResponse = await ReadApiResponseAsync<AuthenticatedSessionResponse>(response);
        apiResponse.Data.Should().NotBeNull();
        return apiResponse.Data!;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        _factory.Dispose();
        await Task.CompletedTask;
    }

    public static string? ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            return null;

        foreach (var value in values)
        {
            if (!value.StartsWith($"{cookieName}=", StringComparison.OrdinalIgnoreCase))
                continue;

            var start = cookieName.Length + 1;
            var end = value.IndexOf(';');
            return end >= 0 ? value[start..end] : value[start..];
        }

        return null;
    }

    private static string ComputeHash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private sealed class CsrfTokenPayload
    {
        public required string Token { get; init; }
    }

    public sealed class ApiEnvelope<T>
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public T? Data { get; init; }
    }
}
