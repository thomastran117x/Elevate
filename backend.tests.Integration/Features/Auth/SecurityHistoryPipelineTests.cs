extern alias securityhistoryworker;

using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using backend.main.application.security;
using backend.main.features.auth.contracts.requests;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.history;
using backend.main.features.auth.mfa.recovery;
using backend.main.features.auth.notifications;
using backend.main.features.auth.token;
using backend.main.features.profile.contracts.requests;
using backend.main.infrastructure.database.core;
using backend.main.shared.providers;
using backend.main.shared.providers.messages;
using backend.main.shared.providers.messaging;
using backend.main.utilities;
using securityhistoryworker::backend.worker.security_history_worker;

using backend.tests.Integration.Infrastructure;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace backend.tests.Integration.Features.Auth;

/// <summary>
/// The account security history end to end: real auth journeys through the API, read back from the
/// security history outbox exactly as Debezium relays it to the worker, run through the worker's
/// processor and alert dispatcher against the same database, and read again through the endpoint.
/// </summary>
/// <remarks>
/// The test host does not start hosted services, so each test drains the API's queue into the
/// outbox itself, and Kafka Connect is not part of the test infrastructure, so the outbox payload
/// stands in for the relayed message. Its parse by the worker is what the relay delivers.
/// </remarks>
public sealed class SecurityHistoryPipelineTests
{
    private const string ClientAddress = "203.0.113.50";

    [Fact]
    public async Task Login_ShouldPublishOnlyTheSuccess_WithoutCredentialsOrContactDetails()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-login@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-login-device");

        var failed = await LoginAsync(app, "pipeline-login", "wrong passphrase for pipeline", "pipeline-login-device");
        failed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var session = await app.LoginApiAsync("pipeline-login", trustedDeviceToken: "pipeline-login-device");

        var outbox = await OutboxForAsync(app, user.Id);

        // The failed attempt is deliberately not part of the owner's history.
        var raw = outbox.Should().ContainSingle().Subject.Payload;
        foreach (var secret in new[]
                 {
                     AuthApiTestApp.DefaultPassword,
                     "wrong passphrase for pipeline",
                     "pipeline-login@example.com",
                     "pipeline-login\"",
                     "pipeline-login-device",
                     session.AccessToken,
                     session.RefreshToken!,
                     session.SessionBindingToken!,
                     ClientAddress,
                 })
        {
            raw.Should().NotContain(secret);
        }

        var message = SecurityHistoryMessageParser.Parse(raw);
        message.UserId.Should().Be(user.Id);
        message.Kind.Should().Be(AccountSecurityEventKind.SignedIn);
        message.EventName.Should().Be("auth.sign_in.succeeded");
        message.Method.Should().Be("password");
        message.Network.Should().Be("203.0.113.0/24");
        message.CorrelationId.Should().NotBeNullOrWhiteSpace();
        message.InitiatedByAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Logout_ShouldRecordASignedOutEntry()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-logout@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-logout-device");
        var session = await app.LoginApiAsync("pipeline-logout", trustedDeviceToken: "pipeline-logout-device");

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/api/logout",
            new RefreshTokenRequest
            {
                RefreshToken = session.RefreshToken,
                SessionBindingToken = session.SessionBindingToken
            },
            session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));

        var message = (await MessagesForAsync(app, user.Id))
            .Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.SignedOut).Subject;
        message.Reason.Should().Be("logout");
        message.EventName.Should().Be("auth.session.revoked");
    }

    [Fact]
    public async Task SignIn_ShouldAppearInTheOwnersHistory_OnceTheWorkerHasStoredIt()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-history@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-history-device");
        var session = await app.LoginApiAsync("pipeline-history", trustedDeviceToken: "pipeline-history-device");
        var published = (await MessagesForAsync(app, user.Id))
            .Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.SignedIn).Subject;

        var outcome = await ProcessAsync(app, published);

        outcome.Should().Be(SecurityHistoryEntryOutcome.Inserted);
        var response = await app.GetWithBearerAsync("/api/auth/security-events?pageSize=10", session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));
        var page = (await app.ReadApiResponseAsync<HistoryPage>(response)).Data!;
        var entry = page.Items.Should().ContainSingle().Subject;
        entry.Kind.Should().Be("signed_in");
        entry.Method.Should().Be("password");
        entry.Network.Should().Be("203.0.113.0/24");
        entry.Reference.Should().Be(published.CorrelationId);

        // A redelivery changes nothing.
        (await ProcessAsync(app, published)).Should().Be(SecurityHistoryEntryOutcome.Duplicate);
    }

    [Fact]
    public async Task PasswordChange_ShouldStoreTheEntryAndEmailASecurityAlert()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-alert@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-alert-device");
        var session = await app.LoginApiAsync("pipeline-alert", trustedDeviceToken: "pipeline-alert-device");
        await app.CompleteSessionMfaByEmailAsync("pipeline-alert@example.com", session.AccessToken);
        await app.MarkNotificationBoundaryAsync();

        var response = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/profile/change-password",
            new ChangePasswordAuthenticatedRequest
            {
                CurrentPassword = AuthApiTestApp.DefaultPassword,
                NewPassword = "brisk willow anchor 456"
            },
            session.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));

        var changed = (await MessagesForAsync(app, user.Id))
            .Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.PasswordChanged).Subject;
        (await ProcessAsync(app, changed)).Should().Be(SecurityHistoryEntryOutcome.Inserted);
        // A redelivery before the dispatcher runs must neither send a second alert nor lose this one.
        (await ProcessAsync(app, changed)).Should().Be(SecurityHistoryEntryOutcome.Duplicate);
        (await DispatchAlertsAsync(app)).Should().BeGreaterThanOrEqualTo(1);

        var alert = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.SecurityAlert && m.Email == "pipeline-alert@example.com");
        alert.SecurityAlertKind.Should().Be("password_changed");
        alert.SecurityAlertNetwork.Should().Be("203.0.113.0/24");
        alert.Token.Should().BeNull();
        alert.Code.Should().BeNull();
        var stored = await app.QueryDbAsync(db => db.AccountSecurityEvents.AsNoTracking().SingleAsync(e =>
            e.UserId == user.Id && e.Kind == AccountSecurityEventKind.PasswordChanged));
        stored.AlertState.Should().Be(SecurityAlertState.Sent);
    }

    [Fact]
    public async Task RecoveryCodes_ShouldRecordGenerationAndUse_AndAlertOnUse_WithoutAnyCode()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-recovery@example.com");
        await app.SeedKnownDeviceAsync(user.Id, "pipeline-recovery-device");
        var session = await app.LoginApiAsync("pipeline-recovery", trustedDeviceToken: "pipeline-recovery-device");
        await app.GrantStepUpProofAsync(session.AccessToken);

        var generated = await app.SendWithBearerAndCsrfAsync(
            HttpMethod.Post, "/api/auth/mfa/recovery-codes/regenerate", null, session.AccessToken);
        generated.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(generated));
        var codes = (await app.ReadApiResponseAsync<RecoveryCodesResponse>(generated)).Data!.Codes;
        await app.MarkNotificationBoundaryAsync();

        var verified = await app.PostJsonWithBearerAndCsrfAsync(
            "/api/auth/mfa/step-up/verify",
            new SessionMfaVerifyRequest { Method = "recovery_code", Code = codes[0] },
            session.AccessToken);
        verified.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(verified));

        var outbox = await OutboxForAsync(app, user.Id);
        foreach (var code in codes)
        {
            RecoveryCodeFormat.TryNormalize(code, out var normalized).Should().BeTrue();
            outbox.Should().OnlyContain(row => !row.Payload.Contains(code) && !row.Payload.Contains(normalized));
        }

        var messages = outbox.Select(row => SecurityHistoryMessageParser.Parse(row.Payload)).ToList();
        messages.Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.RecoveryCodesGenerated);
        var used = messages.Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.RecoveryCodeUsed).Subject;
        (await ProcessAsync(app, used)).Should().Be(SecurityHistoryEntryOutcome.Inserted);
        (await DispatchAlertsAsync(app)).Should().BeGreaterThanOrEqualTo(1);

        var alert = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.SecurityAlert && m.Email == "pipeline-recovery@example.com");
        alert.SecurityAlertKind.Should().Be("recovery_code_used");
        alert.SecurityAlertNetwork.Should().Be("203.0.113.0/24");
        alert.Code.Should().BeNull();
        alert.Token.Should().BeNull();
    }

    [Fact]
    public async Task AdminDisable_ShouldBeRecordedAndAlerted_EvenWithoutAnySessionToRevoke()
    {
        await using var app = await CreateAppAsync();
        var admin = await app.SeedUserAsync("pipeline-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "pipeline-admin-device");
        var adminSession = await app.LoginApiAsync("pipeline-admin", trustedDeviceToken: "pipeline-admin-device");
        await app.GrantStepUpProofAsync(adminSession.AccessToken);
        // Never signed in, so disabling it revokes nothing.
        var dormant = await app.SeedUserAsync("pipeline-dormant@example.com");
        await app.MarkNotificationBoundaryAsync();

        var response = await app.SendWithBearerAndCsrfAsync(
            HttpMethod.Patch,
            $"/api/admin/users/{dormant.Id}/status",
            new { isDisabled = true, reason = "policy" },
            adminSession.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(response));

        // Disabling an account that is already disabled changes nothing the owner is told about.
        var repeat = await app.SendWithBearerAndCsrfAsync(
            HttpMethod.Patch,
            $"/api/admin/users/{dormant.Id}/status",
            new { isDisabled = true, reason = "policy, again" },
            adminSession.AccessToken);
        repeat.StatusCode.Should().Be(HttpStatusCode.OK, await app.DescribeFailureAsync(repeat));

        var disabled = (await MessagesForAsync(app, dormant.Id))
            .Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.AccountDisabled).Subject;
        disabled.EventName.Should().Be("auth.account.status_changed");
        disabled.InitiatedByAdmin.Should().BeTrue();
        // The request was the administrator's, so none of its context is attributed to the owner.
        disabled.Network.Should().BeNull();
        disabled.Client.Should().Be(SecurityHistoryClient.Unknown);
        (await ProcessAsync(app, disabled)).Should().Be(SecurityHistoryEntryOutcome.Inserted);
        await DispatchAlertsAsync(app);

        var alert = await app.WaitForEmailAsync(m =>
            m.Type == EmailMessageType.SecurityAlert && m.Email == "pipeline-dormant@example.com");
        alert.SecurityAlertKind.Should().Be("account_disabled");
    }

    [Fact]
    public async Task ConcurrentAdminDisables_ShouldRecordOneEntry()
    {
        await using var app = await CreateAppAsync();
        var admin = await app.SeedUserAsync("pipeline-race-admin@example.com", role: "Admin");
        await app.SeedKnownDeviceAsync(admin.Id, "pipeline-race-admin-device");
        var adminSession = await app.LoginApiAsync("pipeline-race-admin", trustedDeviceToken: "pipeline-race-admin-device");
        await app.GrantStepUpProofAsync(adminSession.AccessToken);
        var target = await app.SeedUserAsync("pipeline-race-target@example.com");

        // A double submit, or two admins at once: both requests read "enabled" before either writes.
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => app.SendWithBearerAndCsrfAsync(
            HttpMethod.Patch,
            $"/api/admin/users/{target.Id}/status",
            new { isDisabled = true, reason = "policy" },
            adminSession.AccessToken)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await MessagesForAsync(app, target.Id))
            .Should().ContainSingle(m => m.Kind == AccountSecurityEventKind.AccountDisabled);
    }

    [Fact]
    public async Task AnEntryForADeletedAccount_ShouldBeDropped()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-deleted@example.com");
        await app.QueryDbAsync(db => db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync());

        var outcome = await ProcessAsync(app, new SecurityHistoryMessage
        {
            MessageId = Guid.NewGuid(),
            UserId = user.Id,
            Kind = AccountSecurityEventKind.SignedOut,
            EventName = "auth.session.revoked",
            Reason = "logout",
            OccurredAt = DateTimeOffset.UtcNow,
        });

        outcome.Should().Be(SecurityHistoryEntryOutcome.AccountDeleted);
        (await app.QueryDbAsync(db => db.AccountSecurityEvents.CountAsync(e => e.UserId == user.Id))).Should().Be(0);
    }

    [Fact]
    public async Task RetentionRunner_ShouldDeleteOnlyExpiredEntries()
    {
        await using var app = await CreateAppAsync();
        var user = await app.SeedUserAsync("pipeline-retention@example.com");
        var now = DateTime.UtcNow;
        var expired = Guid.NewGuid();
        var current = Guid.NewGuid();
        await app.QueryDbAsync(db =>
        {
            db.AccountSecurityEvents.AddRange(
                Stored(user.Id, expired, now.AddDays(-91), now.AddDays(-1)),
                Stored(user.Id, current, now.AddDays(-1), now.AddDays(89)));
            return db.SaveChangesAsync();
        });

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var runner = new SecurityHistoryRetentionRunner(
                scope.ServiceProvider.GetRequiredService<AppDatabaseContext>(),
                WorkerOptions(),
                TimeProvider.System,
                new SecurityHistoryWorkerMetrics(app.Services.GetRequiredService<IMeterFactory>()));
            await runner.RunOnceAsync();
        }

        var remaining = await app.QueryDbAsync(db => db.AccountSecurityEvents
            .Where(e => e.UserId == user.Id)
            .Select(e => e.MessageId)
            .ToListAsync());
        remaining.Should().Equal(current);
    }

    /// <summary>
    /// Stores what the API has queued and returns the account's outbox rows, oldest first: what the
    /// security-history-outbox connector would relay.
    /// </summary>
    private static async Task<IReadOnlyList<SecurityHistoryOutbox>> OutboxForAsync(AuthApiTestApp app, int userId)
    {
        await app.Services.GetRequiredService<SecurityHistoryOutboxDrainer>().DrainAllAsync();
        var aggregateId = userId.ToString();
        return await app.QueryDbAsync(db => db.SecurityHistoryOutbox
            .AsNoTracking()
            .Where(row => row.AggregateId == aggregateId)
            .OrderBy(row => row.CreatedAt)
            .ToListAsync());
    }

    private static async Task<IReadOnlyList<SecurityHistoryMessage>> MessagesForAsync(AuthApiTestApp app, int userId) =>
        (await OutboxForAsync(app, userId)).Select(row => SecurityHistoryMessageParser.Parse(row.Payload)).ToList();

    private static async Task<int> DispatchAlertsAsync(AuthApiTestApp app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var runner = new SecurityHistoryAlertRunner(
            services.GetRequiredService<AppDatabaseContext>(),
            services.GetRequiredService<IAuthNotificationService>(),
            TimeProvider.System,
            WorkerOptions(),
            new SecurityHistoryWorkerMetrics(services.GetRequiredService<IMeterFactory>()));
        return await runner.RunOnceAsync();
    }

    private static async Task<SecurityHistoryEntryOutcome> ProcessAsync(AuthApiTestApp app, SecurityHistoryMessage message)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDatabaseContext>();
        var options = WorkerOptions();
        var processor = new SecurityHistoryMessageProcessor(
            new SecurityHistoryWriter(db, options, TimeProvider.System),
            new FailingDlqPublisher(),
            new SecurityHistoryWorkerMetrics(services.GetRequiredService<IMeterFactory>()));

        var envelope = new KafkaMessageEnvelope(
            options.Topic,
            0,
            0,
            null,
            JsonSerializer.Serialize(message, JsonOptions.Default),
            new Dictionary<string, string?>());
        return await processor.ProcessAsync(envelope);
    }

    private static SecurityHistoryWorkerOptions WorkerOptions() => new(
        BootstrapServers: "unused",
        Topic: "eventxperience-security-history",
        GroupId: "security-history-worker",
        DlqTopic: "eventxperience-security-history-dlq",
        RetentionDays: 90,
        PurgeBatchSize: 500,
        PurgeMaxBatches: 20,
        PurgeInterval: TimeSpan.FromHours(1),
        NotificationsEnabled: true,
        AlertKinds: SecurityHistoryWorkerOptions.DefaultAlertKinds,
        AlertMaxAge: SecurityHistoryWorkerOptions.DefaultAlertMaxAge);

    private static AccountSecurityEvent Stored(int userId, Guid messageId, DateTime occurredAt, DateTime expiresAt) => new()
    {
        MessageId = messageId,
        UserId = userId,
        Kind = AccountSecurityEventKind.SignedIn,
        EventName = "auth.sign_in.succeeded",
        OccurredAtUtc = occurredAt,
        RecordedAtUtc = occurredAt,
        ExpiresAtUtc = expiresAt,
    };

    /// <summary>Nothing in these journeys should be dead-lettered; one that is fails the test.</summary>
    private sealed class FailingDlqPublisher : ISecurityHistoryWorkerDlqPublisher
    {
        public Task PublishAsync(KafkaMessageEnvelope envelope, string error, CancellationToken cancellationToken = default) =>
            throw new Xunit.Sdk.XunitException($"Unexpected dead letter: {error}");
    }

    private sealed record HistoryPage(
        List<AccountSecurityEventResponse> Items,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages);

    private static Task<AuthApiTestApp> CreateAppAsync(Action<IServiceCollection>? serviceOverrides = null) =>
        AuthApiTestApp.CreateAsync(serviceOverrides: services =>
        {
            services.AddSingleton<IStartupFilter>(new ClientAddressStartupFilter(IPAddress.Parse(ClientAddress)));
            serviceOverrides?.Invoke(services);
        });

    private static async Task<HttpResponseMessage> LoginAsync(
        AuthApiTestApp app,
        string username,
        string password,
        string trustedDeviceToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest
            {
                Username = username,
                Password = password,
                Captcha = "captcha",
                Transport = SessionTransportResolver.ApiValue,
            })
        };
        request.Headers.Add(HttpUtility.TrustedDeviceHeaderName, trustedDeviceToken);
        request.Headers.Add(CsrfConfiguration.CsrfHeaderName, await app.GetCsrfTokenAsync());
        return await app.Client.SendAsync(request);
    }

    private sealed class ClientAddressStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = address;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
