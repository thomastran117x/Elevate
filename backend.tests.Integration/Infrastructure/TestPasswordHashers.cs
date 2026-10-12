using backend.main.features.auth.password;

using Microsoft.Extensions.Options;

namespace backend.tests.Integration.Infrastructure;

internal static class TestPasswordHashers
{
    /// <summary>
    /// Must match the <c>Auth:PasswordPolicy:Hashing</c> overrides in
    /// <see cref="TestWebApplicationFactory"/>, or every hash made here would be upgraded on its
    /// first login through the app.
    /// </summary>
    public static PasswordHasher Fast() => new(Options.Create(new PasswordPolicyOptions
    {
        Hashing = new PasswordHashingOptions
        {
            Argon2 = new Argon2Options { MemoryKiB = Argon2Options.MinMemoryKiB, Iterations = 1, Parallelism = 1 },
            BcryptWorkFactor = PasswordHashingOptions.MinBcryptWorkFactor,
        },
    }));
}
