namespace backend.main.features.auth.notifications
{
    public interface IAuthNotificationService
    {
        Task SendSignupVerificationAsync(
            string email,
            string token,
            string code);

        Task SendPasswordResetAsync(
            string email,
            string token,
            string code);

        Task SendDeviceVerificationAsync(
            string email,
            string token);

        Task SendSmsMfaAsync(
            string phoneNumber,
            string code,
            string challenge,
            DateTime expiresAtUtc,
            string purpose);
    }
}
