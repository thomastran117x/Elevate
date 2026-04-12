namespace worker.Interfaces
{
    public interface IEmailService
    {
        Task SendVerificationEmailAsync(string toEmail, string token, string? code = null);
        Task SendResetPasswordEmailAsync(string toEmail, string token, string? code = null);
        Task SendConfirmationEmailAsync(string toEmail, string token);
        Task SendNewDeviceEmailAsync(string toEmail, string token);
        bool isEmailEnabled();
    }
}
