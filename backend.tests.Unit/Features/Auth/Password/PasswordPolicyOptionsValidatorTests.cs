using backend.main.features.auth.password;

using FluentAssertions;

namespace backend.tests.Unit.Features.Auth.Password;

public class PasswordPolicyOptionsValidatorTests
{
    [Fact]
    public void Defaults_ShouldBeArgon2idAtTheOwaspBaseline_AndValid()
    {
        var options = new PasswordPolicyOptions();

        options.Hashing.Algorithm.Should().Be(PasswordHashAlgorithm.Argon2id);
        options.Hashing.Argon2.MemoryKiB.Should().Be(19456);
        options.Hashing.Argon2.Iterations.Should().Be(2);
        options.Hashing.Argon2.Parallelism.Should().Be(1);
        options.Hashing.BcryptWorkFactor.Should().Be(12);
        new PasswordPolicyOptionsValidator().Validate(null, options).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(1023, 1, 1, 12, "MemoryKiB")]
    [InlineData(1024 * 1024 + 1, 1, 1, 12, "MemoryKiB")]
    [InlineData(19456, 0, 1, 12, "Iterations")]
    [InlineData(19456, 11, 1, 12, "Iterations")]
    [InlineData(19456, 2, 0, 12, "Parallelism")]
    [InlineData(19456, 2, 17, 12, "Parallelism")]
    [InlineData(19456, 2, 1, 3, "BcryptWorkFactor")]
    [InlineData(19456, 2, 1, 32, "BcryptWorkFactor")]
    public void Validate_ShouldReject_OutOfRangeHashingParameters(
        int memoryKiB, int iterations, int parallelism, int bcryptWorkFactor, string expectedKey)
    {
        var options = new PasswordPolicyOptions
        {
            Hashing = new PasswordHashingOptions
            {
                Argon2 = new Argon2Options
                {
                    MemoryKiB = memoryKiB,
                    Iterations = iterations,
                    Parallelism = parallelism,
                },
                BcryptWorkFactor = bcryptWorkFactor,
            },
        };

        var result = new PasswordPolicyOptionsValidator().Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain(expectedKey);
    }

    [Fact]
    public void Validate_ShouldReject_AnUndefinedAlgorithm()
    {
        var options = new PasswordPolicyOptions();
        options.Hashing.Algorithm = (PasswordHashAlgorithm)42;

        new PasswordPolicyOptionsValidator().Validate(null, options).FailureMessage
            .Should().Contain("Algorithm");
    }
}
