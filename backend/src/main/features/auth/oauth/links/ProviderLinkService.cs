using System.Security.Cryptography;
using System.Text;

using backend.main.features.auth.events;
using backend.main.features.cache;
using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

using Microsoft.AspNetCore.WebUtilities;

namespace backend.main.features.auth.oauth.links
{
    public sealed class ProviderLinkService : IProviderLinkService
    {
        private readonly IUserIdentityLinkRepository _links;
        private readonly IAuthUserRepository _users;
        private readonly IProviderLinkStateStore _states;
        private readonly IReadOnlyDictionary<string, IProviderLinkFlow> _flows;
        private readonly IRefreshAheadCache _refreshCache;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private readonly TimeProvider _timeProvider;

        public ProviderLinkService(
            IUserIdentityLinkRepository links,
            IAuthUserRepository users,
            IProviderLinkStateStore states,
            IEnumerable<IProviderLinkFlow> flows,
            IRefreshAheadCache refreshCache,
            IAuthSecurityEventEmitter securityEvents,
            TimeProvider timeProvider)
        {
            _links = links;
            _users = users;
            _states = states;
            _flows = flows.ToDictionary(flow => flow.Provider, StringComparer.Ordinal);
            _refreshCache = refreshCache;
            _securityEvents = securityEvents;
            _timeProvider = timeProvider;
        }

        public async Task<ProviderLinkOverview> GetOverviewAsync(int userId)
        {
            var links = await _links.ListForUserAsync(userId);
            var hasPassword = (await _users.GetAuthByIdAsync(userId))?.Password != null;

            var providers = IdentityProviders.Linkable
                .Select(provider =>
                {
                    var link = links.FirstOrDefault(l => l.Provider == provider);
                    var available = _flows.TryGetValue(provider, out var flow) && flow.IsAvailable;
                    return new ProviderLinkStatus(
                        provider,
                        available,
                        link,
                        CanUnlink: link != null && SignInMethods.RemainAfterRemoving(hasPassword, links.Count));
                })
                .ToList();

            return new ProviderLinkOverview(hasPassword, providers);
        }

        public async Task<ProviderLinkStart> StartAsync(ProviderLinkSession session, string provider)
        {
            var flow = RequireAvailableFlow(provider);

            // Refused up front rather than after the provider round trip: the account must unlink
            // its current identity for this provider before it can link another.
            var links = await _links.ListForUserAsync(session.UserId);
            if (links.Any(l => l.Provider == flow.Provider))
                throw ProviderLinkConflictException.AlreadyConnected();

            var nonce = RandomToken();
            var codeVerifier = RandomToken();
            var state = await _states.CreateLinkStateAsync(new ProviderLinkState
            {
                Provider = flow.Provider,
                UserId = session.UserId,
                SessionId = session.SessionId,
                AuthVersion = session.AuthVersion,
                Nonce = nonce,
                CodeVerifier = codeVerifier,
                RedirectUri = flow.RedirectUri,
                CreatedAtUtc = UtcNow,
            });

            return new ProviderLinkStart(
                flow.BuildAuthorizationUrl(state, nonce, CodeChallenge(codeVerifier)),
                state,
                flow.BrowserRedeemsCode ? codeVerifier : null);
        }

        public async Task<UserIdentityLink> CompleteAsync(
            ProviderLinkSession session,
            string provider,
            string state,
            string? code,
            string? idToken)
        {
            var flow = RequireAvailableFlow(provider);

            // Consumed before anything is checked, so a state is spent whether this attempt
            // succeeds or not and can never be presented twice.
            var linkState = await _states.ConsumeLinkStateAsync(state)
                ?? throw new ProviderLinkExpiredException();

            if (linkState.CreatedAtUtc + ProviderLinkStateLifetime.Lifetime < UtcNow)
                throw new ProviderLinkExpiredException();

            // Bound to the session that started it: a state carried to another account, another
            // session, or past a credential change (AuthVersion) links nothing.
            if (linkState.UserId != session.UserId
                || !string.Equals(linkState.SessionId, session.SessionId, StringComparison.Ordinal)
                || linkState.AuthVersion != session.AuthVersion
                || linkState.Provider != flow.Provider)
            {
                throw new ProviderLinkMismatchException();
            }

            OAuthUser identity;
            try
            {
                identity = await flow.ResolveIdentityAsync(linkState, code, idToken);
            }
            // A refused token or code is not the caller's session failing, so it must not reach
            // the client as a 401 its interceptors would answer by refreshing or signing out.
            catch (UnauthorizedException)
            {
                throw new BadRequestException($"{ProviderLabel(flow.Provider)} did not confirm the account. Try again.");
            }

            if (identity.Provider != flow.Provider)
                throw new BadRequestException("The provider response did not match the link request.");

            return await LinkAsync(session.UserId, identity, AuthSecurityFlow.ProviderLink);
        }

        public async Task<UserIdentityLink> ConfirmEmailLinkAsync(int userId, string linkToken)
        {
            var pending = await _states.ConsumePendingEmailLinkAsync(linkToken)
                ?? throw new ProviderLinkExpiredException();

            if (pending.CreatedAtUtc + ProviderLinkStateLifetime.Lifetime < UtcNow)
                throw new ProviderLinkExpiredException();

            // The sign-in that matched by email named one account; confirming from another would
            // let whoever holds the provider identity attach it to an account of their choosing.
            if (pending.UserId != userId)
                throw new ProviderLinkMismatchException();

            var identity = new OAuthUser(
                pending.Subject,
                pending.Email,
                pending.Name ?? pending.Email,
                pending.Provider,
                emailVerified: true);

            return await LinkAsync(userId, identity, AuthSecurityFlow.ProviderLinkConfirmation);
        }

        public async Task UnlinkAsync(int userId, string provider)
        {
            if (!IdentityProviders.TryParseLinkable(provider, out var name))
                throw new ResourceNotFoundException("That sign-in provider is not supported.");

            switch (await _links.RemoveIfAnotherMethodRemainsAsync(userId, name))
            {
                case IdentityLinkRemoveResult.NotLinked:
                    throw new ResourceNotFoundException($"{ProviderLabel(name)} is not linked to your account.");
                case IdentityLinkRemoveResult.LastSignInMethod:
                    throw ProviderLinkConflictException.LastSignInMethod();
            }

            await AfterLinksChangedAsync(userId);
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.ProviderUnlinked,
                Outcome = AuthSecurityOutcome.Succeeded,
                Method = SecurityMethod(name),
                Flow = AuthSecurityFlow.ProviderUnlink,
                UserId = userId,
            });
        }

        private async Task<UserIdentityLink> LinkAsync(int userId, OAuthUser identity, AuthSecurityFlow flow)
        {
            var existing = await _links.FindBySubjectAsync(identity.Provider, identity.Id);
            if (existing != null)
            {
                if (existing.UserId == userId)
                    return existing;

                throw RefuseLink(userId, identity.Provider, AuthSecurityReason.ProviderBoundToOtherAccount);
            }

            var now = UtcNow;
            var link = new UserIdentityLink
            {
                UserId = userId,
                Provider = identity.Provider,
                Subject = identity.Id,
                Email = string.IsNullOrWhiteSpace(identity.Email) ? null : identity.Email,
                DisplayName = string.IsNullOrWhiteSpace(identity.Name) ? null : identity.Name,
                LinkedAtUtc = now,
                LastUsedAtUtc = null,
            };

            switch (await _links.AddAsync(link))
            {
                case IdentityLinkAddResult.SubjectTaken:
                    // Lost a race to a concurrent link of the same identity. If the winner was
                    // this account, the outcome the caller asked for already holds.
                    var winner = await _links.FindBySubjectAsync(identity.Provider, identity.Id);
                    if (winner?.UserId == userId)
                        return winner;

                    throw RefuseLink(userId, identity.Provider, AuthSecurityReason.ProviderBoundToOtherAccount);

                case IdentityLinkAddResult.ProviderTaken:
                    throw RefuseLink(userId, identity.Provider, AuthSecurityReason.ProviderAlreadyConnected);
            }

            await AfterLinksChangedAsync(userId);
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.ProviderLinked,
                Outcome = AuthSecurityOutcome.Succeeded,
                Method = SecurityMethod(identity.Provider),
                Flow = flow,
                UserId = userId,
            });

            return link;
        }

        private ProviderLinkConflictException RefuseLink(int userId, string provider, AuthSecurityReason reason)
        {
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.ProviderLinkConflict,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Method = SecurityMethod(provider),
                UserId = userId,
            });

            return reason == AuthSecurityReason.ProviderAlreadyConnected
                ? ProviderLinkConflictException.AlreadyConnected()
                : ProviderLinkConflictException.AlreadyLinked();
        }

        /// <summary>
        /// Revokes every link flow the account still has outstanding, and drops the cached profile
        /// that reports which providers are linked.
        /// </summary>
        /// <remarks>
        /// Best effort after the change has committed. A cache that cannot be reached here cannot
        /// serve the outstanding state to a completion either, since consuming it needs the cache.
        /// </remarks>
        private async Task AfterLinksChangedAsync(int userId)
        {
            try
            {
                await _states.RevokeAllForUserAsync(userId);
            }
            catch (Exception e)
            {
                Logger.Warn($"[ProviderLinkService] Revoking pending link state failed: {e.Message}");
            }

            try
            {
                // Same key UserService caches the profile under.
                await _refreshCache.RemoveAsync($"user:{userId}");
            }
            catch (Exception e)
            {
                Logger.Warn($"[ProviderLinkService] Profile cache invalidation failed: {e.Message}");
            }
        }

        private IProviderLinkFlow RequireAvailableFlow(string provider)
        {
            if (!IdentityProviders.TryParseLinkable(provider, out var name)
                || !_flows.TryGetValue(name, out var flow))
            {
                throw new ResourceNotFoundException("That sign-in provider is not supported.");
            }

            if (!flow.IsAvailable)
                throw new NotAvailableException($"{ProviderLabel(name)} sign-in is not available.");

            return flow;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        private static string RandomToken() =>
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

        private static string CodeChallenge(string codeVerifier) =>
            WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

        private static AuthSecurityMethod SecurityMethod(string provider) =>
            provider == IdentityProviders.Microsoft ? AuthSecurityMethod.Microsoft : AuthSecurityMethod.Google;

        private static string ProviderLabel(string provider) =>
            provider == IdentityProviders.Microsoft ? "Microsoft" : "Google";
    }

    /// <summary>Which sign-in methods an account keeps when one provider link is removed.</summary>
    public static class SignInMethods
    {
        /// <summary>
        /// Whether removing one of <paramref name="linkCount"/> links leaves the account a way to
        /// sign in: its own password, or another link.
        /// </summary>
        public static bool RemainAfterRemoving(bool hasPassword, int linkCount) =>
            hasPassword || linkCount > 1;
    }
}
