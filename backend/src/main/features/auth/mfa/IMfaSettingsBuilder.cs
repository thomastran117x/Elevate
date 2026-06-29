using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.mfa
{
    public interface IMfaSettingsBuilder
    {
        Task<MfaSettingsResponse> BuildAsync(int userId, string email);
    }
}
