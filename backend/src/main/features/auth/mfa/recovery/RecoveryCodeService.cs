using backend.main.shared.exceptions.http;
using backend.main.shared.utilities.logger;

namespace backend.main.features.auth.mfa.recovery
{
    /// <summary>
    /// Generates, reports, and spends single-use recovery codes. Plaintext codes are returned
    /// once by <see cref="RegenerateAsync"/> and never stored, logged, or cached.
    /// </summary>
    public sealed class RecoveryCodeService : IRecoveryCodeService
    {
        private readonly IRecoveryCodeRepository _repository;
        private readonly IAuthUserRepository _userRepository;
        private readonly RecoveryCodeHasher _hasher;
        private readonly TimeProvider _timeProvider;

        public RecoveryCodeService(
            IRecoveryCodeRepository repository,
            IAuthUserRepository userRepository,
            RecoveryCodeHasher hasher,
            TimeProvider timeProvider)
        {
            _repository = repository;
            _userRepository = userRepository;
            _hasher = hasher;
            _timeProvider = timeProvider;
        }

        public async Task<RecoveryCodeStatus> GetStatusAsync(int userId)
        {
            try
            {
                return await _repository.GetStatusAsync(userId);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[RecoveryCodeService] GetStatusAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<bool> HasUnusedAsync(int userId)
        {
            try
            {
                return await _repository.HasUnusedAsync(userId);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[RecoveryCodeService] HasUnusedAsync failed: {ex}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<RecoveryCodeSet> RegenerateAsync(int userId)
        {
            try
            {
                var generatedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                var plaintext = RecoveryCodeFormat.GenerateSet();

                // Hashed before the repository takes the account lock, so the slow part never
                // holds it.
                var rows = plaintext
                    .Select(code => new RecoveryCode
                    {
                        Selector = _hasher.Selector(userId, code),
                        CodeHash = _hasher.Hash(userId, code),
                        CreatedAtUtc = generatedAtUtc,
                    })
                    .ToList();

                var replaced = await _repository.ReplaceSetAsync(userId, rows);
                return new RecoveryCodeSet(
                    [.. plaintext.Select(RecoveryCodeFormat.Display)],
                    generatedAtUtc,
                    replaced);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                Logger.Error($"[RecoveryCodeService] RegenerateAsync failed for userId={userId}: {ex.GetType().Name}");
                throw new InternalServerErrorException();
            }
        }

        public async Task<bool> TryConsumeAsync(int userId, string code)
        {
            if (!RecoveryCodeFormat.TryNormalize(code, out var normalized))
                throw new BadRequestException("Enter a recovery code in the format shown when it was generated.");

            try
            {
                var user = await _userRepository.GetUserAsync(userId);
                if (user == null || user.IsDisabled)
                    return false;

                var match = await _repository.FindUnusedAsync(userId, _hasher.Selector(userId, normalized));

                // One derivation either way: a miss is checked against a dummy hash.
                if (!_hasher.Verify(userId, normalized, match?.CodeHash) || match == null)
                    return false;

                return await _repository.TryConsumeAsync(match.Id, userId, _timeProvider.GetUtcNow().UtcDateTime);
            }
            catch (Exception ex)
            {
                if (ex is AppException)
                    throw;

                // The exception type only: nothing on this path should echo the submitted code.
                Logger.Error($"[RecoveryCodeService] TryConsumeAsync failed for userId={userId}: {ex.GetType().Name}");
                throw new InternalServerErrorException();
            }
        }
    }
}
