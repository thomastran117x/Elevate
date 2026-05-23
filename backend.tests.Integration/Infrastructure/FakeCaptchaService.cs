using backend.main.features.auth.captcha;

namespace backend.tests.Integration.Infrastructure;

public sealed class FakeCaptchaService : ICaptchaService
{
    public bool ShouldSucceed { get; set; } = true;

    public Task<bool> VerifyCaptchaAsync(string token, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ShouldSucceed);
    }
}
