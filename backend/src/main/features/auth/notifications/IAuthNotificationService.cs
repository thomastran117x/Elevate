namespace backend.main.features.auth.notifications
{
    public interface IAuthNotificationService
    {
        Task SendSignupVerificationAsync(
            string email,
            string token,
            string code,
            string? recipientName = null);

        Task SendPasswordResetAsync(
            string email,
            string token,
            string code,
            string? recipientName = null);

        Task SendDeviceVerificationAsync(
            string email,
            string token,
            string? recipientName = null);

        Task SendSmsMfaAsync(
            string phoneNumber,
            string code,
            string challenge,
            DateTime expiresAtUtc,
            string purpose);
    }
}
