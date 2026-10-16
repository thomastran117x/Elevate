using System.Security.Cryptography;

using backend.main.application.environment;
using backend.main.application.security;
using backend.main.features.auth.contracts;
using backend.main.features.auth.contracts.responses;
using backend.main.features.auth.device;
using backend.main.features.auth.events;
using backend.main.features.auth.mfa.totp;
using backend.main.features.auth.notifications;
using backend.main.features.auth.oauth;
using backend.main.features.auth.password;
using backend.main.features.auth.stepup;
using backend.main.features.auth.token;
using backend.main.features.cache;
using backend.main.features.profile;
using backend.main.shared.exceptions.app;
using backend.main.shared.exceptions.http;
using backend.main.shared.requests;
using backend.main.shared.utilities.logger;

using Google.Apis.Auth;

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;

namespace backend.main.features.auth
{
    public class AuthService : IAuthService
    {
        private readonly IAuthUserRepository _userRepository;
        private readonly IOAuthService _oauthService;
        private readonly ITokenService _tokenService;
        private readonly ICacheService _cacheService;
        private readonly IAuthNotificationService _authNotificationService;
        private readonly IDeviceService _deviceService;
        private readonly ITotpMfaEnrollmentService _totpMfaEnrollmentService;
        private readonly IDeviceTrustService _deviceTrustService;
        private readonly ILoginStepUpChallengeService _loginStepUpChallengeService;
        private readonly IAuthSessionService _authSessionService;
        private readonly IUsernameAvailabilityService _usernameAvailability;
        private readonly IEmailAvailabilityService _emailAvailability;
        private readonly SeedAccountBypassPolicy _seedBypass;
        private readonly ClientRequestInfo _requestInfo;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IPasswordPolicy _passwordPolicy;
        private readonly IAuthSecurityEventEmitter _securityEvents;
        private static readonly TimeSpan PendingOAuthSignupTtl = TimeSpan.FromMinutes(15);

        public AuthService(
            IAuthUserRepository userRepository,
            IOAuthService oauthService,
            ITokenService tokenService,
            ICacheService cacheService,
            IAuthNotificationService authNotificationService,
            IDeviceService deviceService,
            ITotpMfaEnrollmentService totpMfaEnrollmentService,
            IDeviceTrustService deviceTrustService,
            ILoginStepUpChallengeService loginStepUpChallengeService,
            IAuthSessionService authSessionService,
            IUsernameAvailabilityService usernameAvailability,
            IEmailAvailabilityService emailAvailability,
            SeedAccountBypassPolicy seedBypass,
            ClientRequestInfo requestInfo,
            IPasswordHasher passwordHasher,
            IPasswordPolicy passwordPolicy,
            IAuthSecurityEventEmitter securityEvents
        )
        {
            _userRepository = userRepository;
            _oauthService = oauthService;
            _tokenService = tokenService;
            _cacheService = cacheService;
            _authNotificationService = authNotificationService;
            _deviceService = deviceService;
            _totpMfaEnrollmentService = totpMfaEnrollmentService;
            _deviceTrustService = deviceTrustService;
            _loginStepUpChallengeService = loginStepUpChallengeService;
            _authSessionService = authSessionService;
            _usernameAvailability = usernameAvailability;
            _emailAvailability = emailAvailability;
            _seedBypass = seedBypass;
            _requestInfo = requestInfo;
            _passwordHasher = passwordHasher;
            _passwordPolicy = passwordPolicy;
            _securityEvents = securityEvents;
        }

        public async Task<LoginAuthenticationResult> LoginAsync(
            string username,
            string password,
            SessionTransport transport,
            bool rememberMe = false,
            string? returnUrl = null
        )
        {
            try
            {
                var normalizedUsername = UsernamePolicy.Normalize(username);
                UserAuthRecord? user = string.IsNullOrEmpty(normalizedUsername)
                    ? null
                    : await _userRepository.GetAuthByUsernameAsync(normalizedUsername);

                var hashToCheck = user?.Password ?? _passwordHasher.DummyHash;
                var verification = VerifyPassword(password, hashToCheck);

                if (user == null || user.Password == null || verification == PasswordVerificationResult.Failed)
                    throw new UnauthorizedException("Invalid username or password");

                var resolvedUser = ToUser(user);
                await EnsureUserEnabledAsync(resolvedUser, signInMethod: AuthSecurityMethod.Password);

                if (verification == PasswordVerificationResult.SuccessRehashNeeded)
                    await TryUpgradePasswordHashAsync(user.Id, password, user.Password);

                return await ResolvePostAuthOutcomeAsync(
                    resolvedUser, AuthSecurityMethod.Password, transport, rememberMe, returnUrl,
                    LoginAuthenticationResult.Authenticated,
                    LoginAuthenticationResult.RequiresStepUp
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] LoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationOtpChallenge> SignUpAsync(
            string email,
            string username,
            string password,
            string userType)
        {
            try
            {
                var usernameForms = UsernamePolicy.NormalizeAndValidateWithDisplay(username);
                username = usernameForms.Username;

                // Only the lookup gets the lowercased form. The address carried forward keeps the
                // casing the user typed, because it is the one we store and deliver mail to, and
                // RFC 5321 leaves the local part case-sensitive to the destination host.
                email = EmailPolicy.Sanitize(email);
                var probeEmail = EmailPolicy.NormalizeAndValidate(email);

                _passwordPolicy.EnsureAllowed(
                    password,
                    new PasswordPolicyContext(username, email),
                    "Password");

                // Advisory: this method sends a verification mail, it does not create the account.
                // The verify step below re-checks authoritatively before inserting, so a filter
                // that has not yet seen a signup from another instance only defers the conflict
                // rather than admitting a duplicate.
                if (await _emailAvailability.IsRegisteredAsync(probeEmail, AvailabilityLookupMode.Advisory))
                    throw new ConflictException($"An account is already registered with the email: {email}");
                if (await _usernameAvailability.IsUnavailableAsync(username, DateTime.UtcNow))
                    throw new UsernameTakenException(username);

                userType = AuthRoles.NormalizeOrThrow(userType);
                string hashedPassword = HashPassword(password);

                User user = new User
                {
                    Email = email,
                    Username = username,
                    // Carried through the verification token, or the casing typed here is lost by
                    // the time the account is actually inserted on the verify step.
                    UsernameDisplay = usernameForms.Display,
                    Password = hashedPassword,
                    Usertype = userType
                };

                var artifacts = await _tokenService.GenerateVerificationArtifactsAsync(
                    user,
                    VerificationPurpose.SignUp
                );
                await _authNotificationService.SendSignupVerificationAsync(
                    email,
                    artifacts.LinkToken,
                    artifacts.OtpChallenge.Code
                );

                return artifacts.OtpChallenge;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[InternalServerErrorException] SignUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyAsync(string token, SessionTransport transport)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationToken(
                    token,
                    VerificationPurpose.SignUp
                );

                user.Email = EmailPolicy.Sanitize(user.Email);
                var probeEmail = EmailPolicy.Normalize(user.Email);
                // Authoritative: CreateUserAsync is a few lines away, so a stale "absent" here
                // would turn a clean 409 into a unique-index violation surfacing as a 500.
                if (await _emailAvailability.IsRegisteredAsync(probeEmail))
                    throw new ConflictException($"An account is already registered with the email: {user.Email}");
                if (string.IsNullOrWhiteSpace(user.Username))
                    throw new BadRequestException("A username is required to complete signup.");
                // The username is derived from Username, never from UsernameDisplay. ComputeOtpProof
                // deliberately does not bind the display form — adding it would invalidate every
                // challenge minted before it existed — so deriving the account name from it would
                // put the value that decides the username outside the integrity check.
                //
                // The display is therefore only adopted when it agrees with the bound username,
                // which is exactly the casing it was stored with at signup. Anything else falls
                // back, the same repair CreateUserAsync applies as a choke point.
                var verifiedUsername = UsernamePolicy.NormalizeAndValidateWithDisplay(user.Username);
                user.Username = verifiedUsername.Username;
                user.UsernameDisplay =
                    UsernamePolicy.IsValidDisplayFor(verifiedUsername.Username, user.UsernameDisplay)
                        ? user.UsernameDisplay
                        : verifiedUsername.Display;
                if (await _usernameAvailability.IsUnavailableAsync(user.Username, DateTime.UtcNow))
                    throw new UsernameTakenException(user.Username);

                await _userRepository.CreateUserAsync(user);
                await _usernameAvailability.MarkTakenAsync(user.Username);
                await _emailAvailability.MarkRegisteredAsync(probeEmail);

                return _securityEvents.SignedIn(
                    await _authSessionService.IssueAsync(user, transport),
                    AuthSecurityMethod.Email,
                    AuthSecurityFlow.Signup);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> VerifyOtpAsync(
            string code,
            string challenge,
            SessionTransport transport
        )
        {
            try
            {
                var user = await _tokenService.VerifyVerificationOtpAsync(
                    code,
                    challenge,
                    VerificationPurpose.SignUp
                );

                user.Email = EmailPolicy.Sanitize(user.Email);
                var probeEmail = EmailPolicy.Normalize(user.Email);
                // Authoritative: CreateUserAsync is a few lines away, so a stale "absent" here
                // would turn a clean 409 into a unique-index violation surfacing as a 500.
                if (await _emailAvailability.IsRegisteredAsync(probeEmail))
                    throw new ConflictException($"An account is already registered with the email: {user.Email}");
                if (string.IsNullOrWhiteSpace(user.Username))
                    throw new BadRequestException("A username is required to complete signup.");
                // The username is derived from Username, never from UsernameDisplay. ComputeOtpProof
                // deliberately does not bind the display form — adding it would invalidate every
                // challenge minted before it existed — so deriving the account name from it would
                // put the value that decides the username outside the integrity check.
                //
                // The display is therefore only adopted when it agrees with the bound username,
                // which is exactly the casing it was stored with at signup. Anything else falls
                // back, the same repair CreateUserAsync applies as a choke point.
                var verifiedUsername = UsernamePolicy.NormalizeAndValidateWithDisplay(user.Username);
                user.Username = verifiedUsername.Username;
                user.UsernameDisplay =
                    UsernamePolicy.IsValidDisplayFor(verifiedUsername.Username, user.UsernameDisplay)
                        ? user.UsernameDisplay
                        : verifiedUsername.Display;
                if (await _usernameAvailability.IsUnavailableAsync(user.Username, DateTime.UtcNow))
                    throw new UsernameTakenException(user.Username);

                await _userRepository.CreateUserAsync(user);
                await _usernameAvailability.MarkTakenAsync(user.Username);
                await _emailAvailability.MarkRegisteredAsync(probeEmail);

                return _securityEvents.SignedIn(
                    await _authSessionService.IssueAsync(user, transport),
                    AuthSecurityMethod.Email,
                    AuthSecurityFlow.Signup);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<VerificationOtpChallenge> RecoverPasswordAsync(string username)
        {
            try
            {
                var normalizedUsername = UsernamePolicy.Normalize(username);
                var existingUser = string.IsNullOrEmpty(normalizedUsername)
                    ? null
                    : await _userRepository.GetRecoveryByUsernameAsync(normalizedUsername);
                if (existingUser == null || existingUser.IsDisabled)
                {
                    EmitRecoveryRequested(
                        AuthSecurityFlow.PasswordRecovery,
                        AuthSecurityReason.Suppressed,
                        existingUser?.Id,
                        AuthSecurityTarget.Username(username));
                    return BuildPlaceholderRecoveryChallenge();
                }

                if (!existingUser.HasLocalPassword)
                {
                    var reminded = existingUser.SignInProviders.Count > 0;
                    if (reminded)
                    {
                        await _authNotificationService.SendProviderSignInReminderAsync(
                            existingUser.Email,
                            existingUser.SignInProviders,
                            existingUser.RecipientName
                        );
                    }

                    EmitRecoveryRequested(
                        AuthSecurityFlow.PasswordRecovery,
                        reminded ? AuthSecurityReason.ProviderReminder : AuthSecurityReason.Suppressed,
                        existingUser.Id,
                        AuthSecurityTarget.Username(username));
                    return BuildPlaceholderRecoveryChallenge();
                }

                User user = new User
                {
                    Email = existingUser.Email,
                    Password = "placeholder",
                    Usertype = "placeholder"
                };

                var artifacts = await _tokenService.GenerateVerificationArtifactsAsync(
                    user,
                    VerificationPurpose.ResetPassword,
                    replaceExisting: true
                );
                await _authNotificationService.SendPasswordResetAsync(
                    existingUser.Email,
                    artifacts.LinkToken,
                    artifacts.OtpChallenge.Code,
                    existingUser.RecipientName
                );

                EmitRecoveryRequested(
                    AuthSecurityFlow.PasswordRecovery,
                    AuthSecurityReason.Issued,
                    existingUser.Id,
                    AuthSecurityTarget.Username(username));
                return artifacts.OtpChallenge;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] RecoverPasswordAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task RecoverUsernameAsync(string email)
        {
            try
            {
                // Normalising matters more than it looks: the column is citext, so the old
                // Trim() alone was enough for the database, but the filter hashes the literal
                // string and would miss a mixed-case address.
                var normalizedEmail = EmailPolicy.Normalize(email);

                // Skips the query outright for an address no account has ever used, which is what
                // most probes against this endpoint are. Deliberately the filter-only check rather
                // than IsRegisteredAsync: the latter would fall back to its own query whenever the
                // filter cannot answer, leaving this path doing two round trips instead of one.
                // Advisory, and safely so — the method reports nothing to the caller either way,
                // so a filter lagging a very recent signup costs one unsent reminder.
                var target = AuthSecurityTarget.Email(email);
                if (_emailAvailability.IsDefinitelyUnregistered(normalizedEmail))
                {
                    EmitRecoveryRequested(AuthSecurityFlow.UsernameRecovery, AuthSecurityReason.Suppressed, null, target);
                    return;
                }

                var existingUser = await _userRepository.GetRecoveryByEmailAsync(normalizedEmail);
                if (existingUser == null || existingUser.IsDisabled)
                {
                    EmitRecoveryRequested(
                        AuthSecurityFlow.UsernameRecovery,
                        AuthSecurityReason.Suppressed,
                        existingUser?.Id,
                        target);
                    return;
                }

                if (existingUser.HasLocalPassword
                    && !string.IsNullOrWhiteSpace(existingUser.Username))
                {
                    await _authNotificationService.SendUsernameReminderAsync(
                        existingUser.Email,
                        existingUser.Username,
                        existingUser.RecipientName
                    );
                    EmitRecoveryRequested(AuthSecurityFlow.UsernameRecovery, AuthSecurityReason.Issued, existingUser.Id, target);
                    return;
                }

                var reminded = existingUser.SignInProviders.Count > 0;
                if (reminded)
                {
                    await _authNotificationService.SendProviderSignInReminderAsync(
                        existingUser.Email,
                        existingUser.SignInProviders,
                        existingUser.RecipientName
                    );
                }

                EmitRecoveryRequested(
                    AuthSecurityFlow.UsernameRecovery,
                    reminded ? AuthSecurityReason.ProviderReminder : AuthSecurityReason.Suppressed,
                    existingUser.Id,
                    target);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] RecoverUsernameAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ResetPasswordAsync(string token, string password)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationToken(
                    token,
                    VerificationPurpose.ResetPassword,
                    pending => EnsureResetPasswordAllowedAsync(pending.Email, password)
                );
                await ChangePasswordInternalAsync(
                    user.Email,
                    password,
                    AuthSecurityReason.PasswordReset,
                    EmitRecoveryCompleted);
                await SendPasswordChangedBestEffortAsync(user.Email);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ResetPasswordAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ResetPasswordWithOtpAsync(string code, string challenge, string password)
        {
            try
            {
                var user = await _tokenService.VerifyVerificationOtpAsync(
                    code,
                    challenge,
                    VerificationPurpose.ResetPassword,
                    pending => EnsureResetPasswordAllowedAsync(pending.Email, password)
                );
                await ChangePasswordInternalAsync(
                    user.Email,
                    password,
                    AuthSecurityReason.PasswordReset,
                    EmitRecoveryCompleted);
                await SendPasswordChangedBestEffortAsync(user.Email);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ResetPasswordWithOtpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task ChangePasswordForAuthenticatedUserAsync(string email, string currentPassword, string newPassword)
        {
            int? userId = null;
            var committed = false;
            try
            {
                var existingUser = await _userRepository.GetAuthByEmailAsync(email)
                    ?? throw new UnauthorizedException("User not found.");
                userId = existingUser.Id;

                await EnsureUserEnabledAsync(ToUser(existingUser));

                if (existingUser.Password == null)
                    throw new BadRequestException(
                        "This account uses social login and has no password set."
                    );

                var verification = VerifyPassword(currentPassword, existingUser.Password);
                if (verification == PasswordVerificationResult.Failed)
                    throw new UnauthorizedException("Current password is incorrect.");

                if (currentPassword == newPassword)
                    throw new BadRequestException(
                        "New password must be different from your current password."
                    );

                _passwordPolicy.EnsureAllowed(
                    newPassword,
                    new PasswordPolicyContext(existingUser.Username, existingUser.Email),
                    "NewPassword");

                await ChangePasswordInternalAsync(
                    email,
                    newPassword,
                    AuthSecurityReason.PasswordChanged,
                    changedUserId =>
                    {
                        committed = true;
                        _securityEvents.Emit(new AuthSecurityEvent
                        {
                            Definition = AuthSecurityEvents.FactorChanged,
                            Outcome = AuthSecurityOutcome.Succeeded,
                            Factor = AuthSecurityFactor.Password,
                            Change = AuthFactorChange.Changed,
                            UserId = changedUserId,
                        });
                    });
            }
            catch (Exception e)
            {
                // Every refusal is recorded: a wrong current password, a disabled account, a provider-only
                // account, a reused or disallowed password, and faults. A failure after the password was
                // committed, such as in the session sweep, is not a failed change: the success is recorded.
                if (!committed)
                {
                    _securityEvents.FactorChangeFailed(
                        userId,
                        AuthSecurityFactor.Password,
                        AuthFactorChange.Changed,
                        e,
                        e switch
                        {
                            UnauthorizedException => userId is null
                                ? AuthSecurityReason.InvalidState
                                : AuthSecurityReason.InvalidCredentials,
                            ForbiddenException => AuthSecurityReason.AccountDisabled,
                            _ => null,
                        });
                }

                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] ChangePasswordForAuthenticatedUserAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> GoogleAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null,
            string? returnUrl = null
        )
        {
            try
            {
                OAuthUser oauthUser = await VerifyProviderTokenAsync(
                    AuthSecurityMethod.Google,
                    () => _oauthService.VerifyGoogleTokenAsync(token, expectedNonce));
                if (oauthUser == null)
                    throw ProviderRejected(AuthSecurityMethod.Google, "Invalid Google Token");

                var user = await ResolveGoogleUserAsync(oauthUser);
                if (user == null)
                {
                    return OAuthAuthenticationResult.RoleSelectionRequired(
                        await CreatePendingOAuthSignupAsync(oauthUser, transport)
                    );
                }

                await EnsureUserEnabledAsync(user, signInMethod: AuthSecurityMethod.Google);
                user = await EnsureOAuthRoleAsync(user);

                return await ResolvePostAuthOutcomeAsync(
                    user, AuthSecurityMethod.Google, transport, rememberMe: false, returnUrl,
                    OAuthAuthenticationResult.Authenticated,
                    OAuthAuthenticationResult.RequiresStepUp
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> GoogleCodeAsync(
            string code,
            string codeVerifier,
            string redirectUri,
            SessionTransport transport,
            string? nonce = null,
            string? returnUrl = null
        )
        {
            try
            {
                var idToken = await VerifyProviderTokenAsync(
                    AuthSecurityMethod.Google,
                    () => _oauthService.ExchangeGoogleCodeAsync(code, codeVerifier, redirectUri));
                return await GoogleAsync(idToken, transport, nonce, returnUrl);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GoogleCodeAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<OAuthAuthenticationResult> MicrosoftAsync(
            string token,
            SessionTransport transport,
            string? expectedNonce = null,
            string? returnUrl = null
        )
        {
            try
            {
                OAuthUser oauthUser = await VerifyProviderTokenAsync(
                    AuthSecurityMethod.Microsoft,
                    () => _oauthService.VerifyMicrosoftTokenAsync(token, expectedNonce));
                if (oauthUser == null)
                    throw ProviderRejected(AuthSecurityMethod.Microsoft, "Invalid Microsoft Token");

                var user = await ResolveMicrosoftUserAsync(oauthUser);
                if (user == null)
                {
                    return OAuthAuthenticationResult.RoleSelectionRequired(
                        await CreatePendingOAuthSignupAsync(oauthUser, transport)
                    );
                }

                await EnsureUserEnabledAsync(user, signInMethod: AuthSecurityMethod.Microsoft);
                user = await EnsureOAuthRoleAsync(user);

                return await ResolvePostAuthOutcomeAsync(
                    user, AuthSecurityMethod.Microsoft, transport, rememberMe: false, returnUrl,
                    OAuthAuthenticationResult.Authenticated,
                    OAuthAuthenticationResult.RequiresStepUp
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] MicrosoftAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> CompleteOAuthSignupAsync(
            string signupToken,
            string usertype,
            string? username,
            SessionTransport transport
        )
        {
            try
            {
                usertype = AuthRoles.NormalizeOrThrow(usertype);
                var pending = await GetPendingOAuthSignupAsync(signupToken)
                    ?? throw new UnauthorizedException(
                        "OAuth signup session is invalid or expired."
                    );

                if (pending.Transport != transport)
                    throw new UnauthorizedException("OAuth signup transport mismatch.");

                var oauthUser = new OAuthUser(
                    pending.ProviderUserId,
                    pending.Email,
                    pending.Name,
                    pending.Provider,
                    pending.EmailVerified
                );
                var user = pending.Provider switch
                {
                    "google" => await ResolveGoogleUserAsync(oauthUser),
                    "microsoft" => await ResolveMicrosoftUserAsync(oauthUser),
                    _ => throw new BadRequestException("Unsupported OAuth provider.")
                };

                if (user == null)
                {
                    // Same shape as VerifyAsync: validate, check authoritatively because the insert
                    // is a few lines away, then create and record both names in the filters.
                    var oauthUsername = UsernamePolicy.NormalizeAndValidateWithDisplay(username);
                    var newUsername = oauthUsername.Username;
                    if (await _usernameAvailability.IsUnavailableAsync(newUsername, DateTime.UtcNow))
                        throw new UsernameTakenException(newUsername);

                    user = await _userRepository.CreateUserAsync(new User
                    {
                        Email = pending.Email,
                        Username = newUsername,
                        UsernameDisplay = oauthUsername.Display,
                        Usertype = usertype,
                        GoogleID = pending.Provider == "google" ? pending.ProviderUserId : null,
                        MicrosoftID = pending.Provider == "microsoft"
                            ? pending.ProviderUserId
                            : null,
                    });
                    // Without these writes, the name and address that just signed up through a
                    // provider would keep reporting as free until the next scheduled rebuild.
                    await _usernameAvailability.MarkTakenAsync(newUsername);
                    await _emailAvailability.MarkRegisteredAsync(EmailPolicy.Normalize(user.Email));
                }
                else
                {
                    // Any username supplied is deliberately ignored here. The provider account
                    // resolved to one that already exists, and renaming it from an unauthenticated
                    // signup token would bypass the MFA gate and cooldown on PATCH /profile/username.
                    await EnsureUserEnabledAsync(user, signInMethod: ProviderMethod(pending.Provider));
                    user = await EnsureOAuthRoleAsync(user);
                }

                await _cacheService.DeleteKeyAsync(PendingOAuthSignupKey(signupToken));
                return _securityEvents.SignedIn(
                    await _authSessionService.IssueAsync(user, transport),
                    ProviderMethod(pending.Provider),
                    AuthSecurityFlow.OAuthCompletion);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] CompleteOAuthSignupAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<User> GetCurrentUserAsync(int userId)
        {
            try
            {
                var user = await _userRepository.GetUserAsync(userId)
                    ?? throw new ResourceNotFoundException($"User with ID {userId} is not found");

                await EnsureUserEnabledAsync(user);
                return user;
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] GetCurrentUserAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<UserToken> HandleTokensAsync(
            string oldRefreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        )
        {
            try
            {
                var validation = await _tokenService.ValidateRefreshToken(
                    oldRefreshToken,
                    sessionBindingToken,
                    transport,
                    _requestInfo
                );
                var user = await _userRepository.GetUserAsync(validation.UserId);
                if (user == null)
                    throw new ResourceNotFoundException($"User with ID {validation.UserId} is not found");
                await EnsureUserEnabledAsync(user, revokeSessions: true, signInMethod: AuthSecurityMethod.RefreshToken);

                // A password change, disable or email change bumps AuthVersion and then sweeps
                // the account's sessions, but the sweep revokes nothing when the cache is
                // unreachable. Comparing here, where the user is already loaded, keeps a session
                // that survived it from minting access tokens for the new version.
                if (validation.AuthVersion is int issuedVersion && issuedVersion != user.AuthVersion)
                {
                    await _tokenService.RevokeRefreshSessionAsync(validation.SessionId, AuthSecurityReason.AuthVersionMismatch);
                    throw new UnauthorizedException(
                        "Refresh session is no longer valid. Please sign in again."
                    );
                }

                return await _authSessionService.IssueAsync(
                    user,
                    validation.Transport,
                    validation.SessionId
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HandleTokensAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyDeviceLoginAsync(
            string token,
            SessionTransport transport
        )
        {
            try
            {
                return await _deviceService.VerifyDeviceAsync(token, transport);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyDeviceLoginAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<StartLoginStepUpResponse> StartLoginStepUpAsync(string challenge, string method)
        {
            try
            {
                return await _loginStepUpChallengeService.StartAsync(challenge, method);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] StartLoginStepUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyLoginStepUpAsync(string challenge, string code)
        {
            try
            {
                return await _loginStepUpChallengeService.VerifySmsAsync(challenge, code);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyLoginStepUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<AuthenticatedSessionResult> VerifyTotpLoginStepUpAsync(string challenge, string code)
        {
            try
            {
                return await _loginStepUpChallengeService.VerifyTotpAsync(challenge, code);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyTotpLoginStepUpAsync failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        public async Task HandleLogoutAsync(
            string refreshToken,
            string? sessionBindingToken,
            SessionTransport transport
        )
        {
            try
            {
                var validation = await _tokenService.ValidateRefreshToken(
                    refreshToken,
                    sessionBindingToken,
                    transport,
                    _requestInfo
                );
                await _tokenService.RevokeRefreshSessionAsync(validation.SessionId, AuthSecurityReason.Logout);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HashPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private string HashPassword(string password)
        {
            try
            {
                return _passwordHasher.Hash(password);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] HashPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        private PasswordVerificationResult VerifyPassword(string password, string hashedPassword)
        {
            try
            {
                return _passwordHasher.Verify(password, hashedPassword);
            }
            catch (Exception e)
            {
                if (e is AppException)
                    throw;

                Logger.Error($"[AuthService] VerifyPassword failed: {e}");
                throw new InternalServerErrorException();
            }
        }

        /// <summary>
        /// Replaces an obsolete stored hash with one made from the current parameters, after the
        /// password has just been proven correct. Best effort: any failure leaves the old hash in
        /// place, which still verifies, so a failed upgrade can never lock the account out.
        /// </summary>
        private async Task TryUpgradePasswordHashAsync(int userId, string password, string currentHash)
        {
            try
            {
                var upgradedHash = _passwordHasher.Hash(password);

                // A hash that cannot verify the password it was made from must never replace one
                // that can.
                if (_passwordHasher.Verify(password, upgradedHash) == PasswordVerificationResult.Failed)
                {
                    Logger.Warn($"[AuthService] Upgraded password hash for user {userId} did not verify; kept the existing hash.");
                    return;
                }

                // Conditional on the hash we verified against, so a concurrent reset or upgrade wins
                // and this write becomes a no-op rather than overwriting it.
                await _userRepository.TryReplacePasswordHashAsync(userId, currentHash, upgradedHash);
            }
            catch (Exception e)
            {
                Logger.Warn($"[AuthService] Password hash upgrade for user {userId} failed: {e.GetType().Name}");
            }
        }

        /// <summary>
        /// Checks a reset's new password against the policy while the reset token is still unused,
        /// so a rejected choice can be corrected without requesting another email.
        /// </summary>
        private async Task EnsureResetPasswordAllowedAsync(string email, string password)
        {
            var account = await _userRepository.GetAuthByEmailAsync(email);
            _passwordPolicy.EnsureAllowed(
                password,
                new PasswordPolicyContext(account?.Username, account?.Email ?? email),
                "Password");
        }

        /// <param name="onCommitted">Records the change. It runs once the new password is committed and
        /// before the session sweep, which can fail after the change has already taken effect.</param>
        private async Task ChangePasswordInternalAsync(
            string email,
            string password,
            AuthSecurityReason revocationReason,
            Action<int> onCommitted)
        {
            var hashedPassword = HashPassword(password);

            var existingUser = await _userRepository.GetAuthByEmailAsync(email)
                ?? throw new UnauthorizedException("Invalid token");
            await EnsureUserEnabledAsync(ToUser(existingUser));

            await _userRepository.SetPasswordAndBumpAuthVersionAsync(existingUser.Id, hashedPassword);
            onCommitted(existingUser.Id);
            await _tokenService.RevokeAllRefreshSessionsAsync(existingUser.Id, revocationReason);
        }

        private async Task SendPasswordChangedBestEffortAsync(string email)
        {
            try
            {
                var user = await _userRepository.GetRecoveryByEmailAsync(email);
                await _authNotificationService.SendPasswordChangedAsync(
                    email,
                    user?.RecipientName
                );
            }
            catch (Exception e)
            {
                Logger.Error($"[AuthService] Password changed notification failed: {e}");
            }
        }

        private async Task<User?> ResolveGoogleUserAsync(OAuthUser oauthUser)
        {
            // The provider id is the authoritative identity for a provider sign-in, so it is
            // resolved first and on its own. An account that changes its email address keeps the
            // address the provider still reports, and once that released address is claimed by
            // someone else the two lookups disagree permanently — comparing them here would lock
            // the original account out of the only sign-in method it may have.
            var providerUser = await _userRepository.GetOAuthByGoogleIdAsync(oauthUser.Id);
            if (providerUser != null)
                return ToUser(providerUser);

            // Google refuses to vouch for an unverified address upstream, so any address
            // reported here may drive the fallback.
            var email = oauthUser.Email
                ?? throw ProviderRejected(AuthSecurityMethod.Google, "Missing Google email claim");
            var emailUser = await _userRepository.GetOAuthByEmailAsync(email);
            if (emailUser == null)
                return null;

            EnsureEmailFallbackAllowed(emailUser, AuthSecurityMethod.Google);
            emailUser = await _userRepository.UpdateProviderIdsAsync(
                emailUser.Id,
                oauthUser.Id,
                null
            ) ?? emailUser;
            EmitProviderLinked(emailUser.Id, AuthSecurityMethod.Google);

            return ToUser(emailUser);
        }

        private async Task<User?> ResolveMicrosoftUserAsync(OAuthUser oauthUser)
        {
            // The provider id is the authoritative identity for a provider sign-in, so it is
            // resolved first and on its own. An account that changes its email address keeps the
            // address the provider still reports, and once that released address is claimed by
            // someone else the two lookups disagree permanently — comparing them here would lock
            // the original account out of the only sign-in method it may have.
            var providerUser = await _userRepository.GetOAuthByMicrosoftIdAsync(oauthUser.Id);
            if (providerUser != null)
                return ToUser(providerUser);

            // Without a linked subject the address is all there is to go on, so a token that
            // reports none cannot sign in or sign up.
            if (string.IsNullOrWhiteSpace(oauthUser.Email))
                throw ProviderRejected(AuthSecurityMethod.Microsoft, "Missing Microsoft email claim");

            // An unverified address can still start a new account. That signs no one into an
            // existing account, though the new account holds an address nobody has proven.
            var emailUser = await _userRepository.GetOAuthByEmailAsync(oauthUser.Email);
            if (emailUser == null)
                return null;

            EnsureEmailFallbackAllowed(emailUser, AuthSecurityMethod.Microsoft);

            // Any Entra tenant can issue a token whose email claim names someone else's address,
            // so linking on it would hand that account to whoever runs the tenant.
            if (!oauthUser.EmailVerified)
            {
                EmitProviderLinkConflict(emailUser.Id, AuthSecurityMethod.Microsoft, AuthSecurityReason.EmailUnverified);
                throw new MicrosoftEmailUnverifiedException();
            }

            emailUser = await _userRepository.UpdateProviderIdsAsync(
                emailUser.Id,
                null,
                oauthUser.Id
            ) ?? emailUser;
            EmitProviderLinked(emailUser.Id, AuthSecurityMethod.Microsoft);

            return ToUser(emailUser);
        }

        /// <summary>
        /// Refuses to sign in to an account by email match once it has any provider link.
        /// </summary>
        /// <remarks>
        /// Called only after the caller's provider id matched no account, so any link present
        /// belongs to a different identity — typically a Workspace or Entra address reassigned
        /// after the link was made. Both providers are checked because the new holder of the
        /// address can authenticate through either one. Password recovery never emails a reset to
        /// an account without a local password, so a provider link may be the account's only
        /// credential and an address match must not stand in for it. An account with no link
        /// still links on an address match; control of that inbox could already reset its password.
        /// That holds only when the provider proves the caller controls the inbox: Google requires
        /// <c>email_verified</c>, and Microsoft resolution refuses an unverified address after this
        /// check with <see cref="MicrosoftEmailUnverifiedException"/>.
        /// </remarks>
        private void EnsureEmailFallbackAllowed(UserOAuthRecord emailUser, AuthSecurityMethod provider)
        {
            if (string.IsNullOrWhiteSpace(emailUser.GoogleID)
                && string.IsNullOrWhiteSpace(emailUser.MicrosoftID))
                return;

            EmitProviderLinkConflict(emailUser.Id, provider, AuthSecurityReason.EmailBoundToOtherProvider);
            throw new ConflictException(
                "This email address belongs to an account that signs in with a different Google or Microsoft account."
            );
        }

        private async Task<User> EnsureOAuthRoleAsync(User user)
        {
            user.Usertype = AuthRoles.NormalizeStored(user.Usertype);

            if (AuthRoles.IsKnownRole(user.Usertype))
                return user;

            var updatedUser = await _userRepository.UpdateUserAsync(user.Id, new User
            {
                Email = user.Email,
                Usertype = AuthRoles.DefaultOAuthRole,
            });

            if (updatedUser != null)
                return updatedUser;

            user.Usertype = AuthRoles.DefaultOAuthRole;
            return user;
        }

        private async Task<PendingOAuthSignupChallenge> CreatePendingOAuthSignupAsync(
            OAuthUser oauthUser,
            SessionTransport transport
        )
        {
            var signupToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var email = oauthUser.Email
                ?? throw new UnauthorizedException("OAuth provider did not report an email address.");
            var pendingState = new PendingOAuthSignupState
            {
                ProviderUserId = oauthUser.Id,
                Email = email,
                EmailVerified = oauthUser.EmailVerified,
                Name = oauthUser.Name,
                Provider = oauthUser.Provider,
                Transport = transport,
            };

            var stored = await _cacheService.SetValueAsync(
                PendingOAuthSignupKey(signupToken),
                JsonConvert.SerializeObject(pendingState),
                PendingOAuthSignupTtl
            );

            if (!stored)
                throw new NotAvailableException();

            return new PendingOAuthSignupChallenge
            {
                SignupToken = signupToken,
                Email = email,
                Name = oauthUser.Name,
                Provider = oauthUser.Provider,
            };
        }

        private async Task<PendingOAuthSignupState?> GetPendingOAuthSignupAsync(string signupToken)
        {
            var json = await _cacheService.GetValueAsync(PendingOAuthSignupKey(signupToken));
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonConvert.DeserializeObject<PendingOAuthSignupState>(json);
        }

        private static string PendingOAuthSignupKey(string signupToken) =>
            $"oauth:pending:{signupToken}";

        private async Task<T> ResolvePostAuthOutcomeAsync<T>(
            User user,
            AuthSecurityMethod method,
            SessionTransport transport,
            bool rememberMe,
            string? returnUrl,
            Func<AuthenticatedSessionResult, T> onAuthenticated,
            Func<LoginStepUpChallengeResponse, T> onStepUp
        )
        {
            // Dev/test-only: seed accounts skip the MFA step-up challenge entirely.
            if (_seedBypass.IsBypassEnabledFor(user.Email))
            {
                return onAuthenticated(new AuthenticatedSessionResult
                {
                    UserToken = _securityEvents.SignedIn(
                        await _authSessionService.IssueAsync(user, transport, rememberMe: rememberMe),
                        method,
                        AuthSecurityFlow.Login)
                });
            }

            if (await _deviceTrustService.IsTrustedAsync(user.Id, _requestInfo))
            {
                return onAuthenticated(new AuthenticatedSessionResult
                {
                    UserToken = _securityEvents.SignedIn(
                        await _authSessionService.IssueAsync(user, transport, rememberMe: rememberMe),
                        method,
                        AuthSecurityFlow.Login)
                });
            }

            var shouldRequireTotpStepUp = false;
            if (EnvironmentSetting.AuthTotpMfaStepUpEnabled)
            {
                shouldRequireTotpStepUp = (await _totpMfaEnrollmentService.GetEnrollmentAsync(user.Id))?.IsTotpMfaEnabled == true;
            }

            if (!EnvironmentSetting.AuthSmsMfaEnforcementEnabled && !shouldRequireTotpStepUp)
            {
                try
                {
                    await _deviceService.EnsureDeviceKnownAsync(user.Id, user.Email, user.AuthVersion, _requestInfo, returnUrl);
                }
                catch (DeviceVerificationRequiredException)
                {
                    EmitChallenged(user, method, AuthSecurityReason.DeviceVerificationRequired);
                    throw;
                }

                return onAuthenticated(new AuthenticatedSessionResult
                {
                    UserToken = _securityEvents.SignedIn(
                        await _authSessionService.IssueAsync(user, transport, rememberMe: rememberMe),
                        method,
                        AuthSecurityFlow.Login)
                });
            }

            var stepUp = await _loginStepUpChallengeService.CreateChallengeAsync(user, transport, rememberMe, returnUrl);
            EmitChallenged(user, method, AuthSecurityReason.StepUpRequired);
            return onStepUp(stepUp);
        }

        private void EmitChallenged(User user, AuthSecurityMethod method, AuthSecurityReason reason) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.SignInChallenged,
                Outcome = AuthSecurityOutcome.Succeeded,
                Reason = reason,
                Method = method,
                Flow = AuthSecurityFlow.Login,
                UserId = user.Id,
            });

        /// <param name="signInMethod">Set when the check gates a sign-in or refresh, which records
        /// a disabled account's attempt as a failed sign-in.</param>
        private async Task EnsureUserEnabledAsync(
            User user,
            bool revokeSessions = false,
            AuthSecurityMethod? signInMethod = null)
        {
            if (!user.IsDisabled)
                return;

            if (signInMethod is not null)
            {
                _securityEvents.Emit(new AuthSecurityEvent
                {
                    Definition = AuthSecurityEvents.SignInFailed,
                    Outcome = AuthSecurityOutcome.Denied,
                    Reason = AuthSecurityReason.AccountDisabled,
                    Method = signInMethod,
                    Flow = signInMethod == AuthSecurityMethod.RefreshToken
                        ? AuthSecurityFlow.Refresh
                        : AuthSecurityFlow.Login,
                    UserId = user.Id,
                });
            }

            if (revokeSessions)
                await _tokenService.RevokeAllRefreshSessionsAsync(user.Id, AuthSecurityReason.AccountDisabled);

            throw new ForbiddenException("This account is disabled.");
        }

        /// <summary>
        /// Runs a provider token check, recording a refused token as a failed sign-in. A validation
        /// error that escapes the provider service is refused with the same generic 401, so the event,
        /// the operation metric, and the response agree that the credential was bad.
        /// </summary>
        private async Task<T> VerifyProviderTokenAsync<T>(AuthSecurityMethod provider, Func<Task<T>> verify)
        {
            try
            {
                return await verify();
            }
            catch (UnauthorizedException)
            {
                EmitProviderRejected(provider);
                throw;
            }
            catch (Exception e) when (e is SecurityTokenException or InvalidJwtException)
            {
                EmitProviderRejected(provider);
                throw new UnauthorizedException("Invalid provider token.");
            }
        }

        /// <summary>
        /// Records a provider token whose claims cannot sign anyone in, such as one without the email
        /// an unlinked account needs, and returns the 401 to throw.
        /// </summary>
        private UnauthorizedException ProviderRejected(AuthSecurityMethod provider, string message)
        {
            EmitProviderRejected(provider);
            return new UnauthorizedException(message);
        }

        private void EmitProviderRejected(AuthSecurityMethod provider) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.SignInFailed,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = AuthSecurityReason.ProviderRejected,
                Method = provider,
                Flow = AuthSecurityFlow.Login,
            });

        private static AuthSecurityMethod ProviderMethod(string provider) =>
            provider == "google" ? AuthSecurityMethod.Google : AuthSecurityMethod.Microsoft;

        /// <remarks>
        /// The reason says whether anything was sent, which the response deliberately hides. It is
        /// for operators only and must never be surfaced to the requester.
        /// </remarks>
        private void EmitRecoveryRequested(
            AuthSecurityFlow flow,
            AuthSecurityReason reason,
            int? userId,
            AuthSecurityTarget target) =>
            _securityEvents.Emit(
                new AuthSecurityEvent
                {
                    Definition = AuthSecurityEvents.RecoveryRequested,
                    Outcome = AuthSecurityOutcome.Succeeded,
                    Reason = reason,
                    Method = AuthSecurityMethod.Email,
                    Flow = flow,
                    UserId = userId,
                },
                target);

        private void EmitRecoveryCompleted(int userId) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.RecoveryCompleted,
                Outcome = AuthSecurityOutcome.Succeeded,
                Method = AuthSecurityMethod.Email,
                Flow = AuthSecurityFlow.PasswordReset,
                UserId = userId,
            });

        private void EmitProviderLinked(int userId, AuthSecurityMethod provider) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.ProviderLinked,
                Outcome = AuthSecurityOutcome.Succeeded,
                Method = provider,
                UserId = userId,
            });

        private void EmitProviderLinkConflict(int userId, AuthSecurityMethod provider, AuthSecurityReason reason) =>
            _securityEvents.Emit(new AuthSecurityEvent
            {
                Definition = AuthSecurityEvents.ProviderLinkConflict,
                Outcome = AuthSecurityOutcome.Denied,
                Reason = reason,
                Method = provider,
                UserId = userId,
            });

        private static User ToUser(UserAuthRecord record)
        {
            return new User
            {
                Id = record.Id,
                Email = record.Email,
                Password = record.Password,
                Usertype = AuthRoles.NormalizeStored(record.Usertype),
                IsDisabled = record.IsDisabled,
                AuthVersion = record.AuthVersion,
            };
        }

        private static User ToUser(UserOAuthRecord record)
        {
            return new User
            {
                Id = record.Id,
                Email = record.Email,
                Usertype = AuthRoles.NormalizeStored(record.Usertype),
                GoogleID = record.GoogleID,
                MicrosoftID = record.MicrosoftID,
                IsDisabled = record.IsDisabled,
                AuthVersion = record.AuthVersion,
            };
        }

        private static VerificationOtpChallenge BuildPlaceholderRecoveryChallenge()
        {
            return new VerificationOtpChallenge
            {
                Code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6"),
                Challenge = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)),
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(30),
            };
        }

        private sealed class PendingOAuthSignupState
        {
            public required string ProviderUserId
            {
                get; set;
            }
            public required string Email
            {
                get; set;
            }
            /// <summary>
            /// Carried so completing the signup re-resolves under the same rule. An entry written
            /// before this field existed reads as false, which refuses the email fallback.
            /// </summary>
            public bool EmailVerified
            {
                get; set;
            }
            public required string Name
            {
                get; set;
            }
            public required string Provider
            {
                get; set;
            }
            public SessionTransport Transport
            {
                get; set;
            }
        }
    }
}
