namespace backend.main.features.auth.captcha
{
    public interface ICaptchaService
    {
        Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default);
    }
}
