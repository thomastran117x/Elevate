using backend.main.features.auth.password;
using backend.main.features.profile;
using backend.main.shared.requests;

using Microsoft.Extensions.Options;

namespace backend.tests.Unit.Support;

internal sealed class TestUserBuilder
{
    private readonly User _user = new()
    {
        Id = 1,
        Email = "user@example.com",
        Usertype = "Participant",
        AuthVersion = 1
    };

    public TestUserBuilder WithId(int id)
    {
        _user.Id = id;
        return this;
    }

    public TestUserBuilder WithEmail(string email)
    {
        _user.Email = email;
        return this;
    }

    public TestUserBuilder WithRole(string role)
    {
        _user.Usertype = role;
        return this;
    }

    public TestUserBuilder WithAvatar(string? avatar)
    {
        _user.Avatar = avatar;
        return this;
    }

    public TestUserBuilder Disabled(string reason = "disabled")
    {
        _user.IsDisabled = true;
        _user.DisabledReason = reason;
        return this;
    }

    public TestUserBuilder WithPassword(string password)
    {
        _user.Password = password;
        return this;
    }

    public TestUserBuilder WithGoogleId(string googleId)
    {
        _user.GoogleID = googleId;
        return this;
    }

    public TestUserBuilder WithMicrosoftId(string microsoftId)
    {
        _user.MicrosoftID = microsoftId;
        return this;
    }

    public User Build() => new()
    {
        Id = _user.Id,
        Email = _user.Email,
        Password = _user.Password,
        Usertype = _user.Usertype,
        Name = _user.Name,
        Username = _user.Username,
        Avatar = _user.Avatar,
        Address = _user.Address,
        Phone = _user.Phone,
        MicrosoftID = _user.MicrosoftID,
        GoogleID = _user.GoogleID,
        IsDisabled = _user.IsDisabled,
        DisabledReason = _user.DisabledReason,
        DisabledAtUtc = _user.DisabledAtUtc,
        AuthVersion = _user.AuthVersion,
        CreatedAt = _user.CreatedAt,
        UpdatedAt = _user.UpdatedAt
    };
}

internal static class TestRequestInfoFactory
{
    public static ClientRequestInfo Browser() => new()
    {
        IpAddress = "127.0.0.1",
        ClientName = "Chrome",
        DeviceType = "Desktop",
        IsBrowserClient = true
    };

    public static ClientRequestInfo ApiClient() => new()
    {
        IpAddress = "127.0.0.1",
        ClientName = "HttpClient",
        DeviceType = "API Client",
        IsBrowserClient = false
    };
}

internal static class TestPasswordHashers
{
    /// <summary>
    /// The production hasher with the cheapest parameters the options validator accepts, so the
    /// suite exercises real Argon2id without paying production cost on every hash.
    /// </summary>
    public static PasswordHasher Fast(PasswordHashAlgorithm algorithm = PasswordHashAlgorithm.Argon2id) =>
        new(Options.Create(FastOptions(algorithm)));

    public static PasswordPolicyOptions FastOptions(
        PasswordHashAlgorithm algorithm = PasswordHashAlgorithm.Argon2id) => new()
        {
            Hashing = new PasswordHashingOptions
            {
                Algorithm = algorithm,
                Argon2 = new Argon2Options { MemoryKiB = Argon2Options.MinMemoryKiB, Iterations = 1, Parallelism = 1 },
                BcryptWorkFactor = PasswordHashingOptions.MinBcryptWorkFactor,
            },
        };
}

internal static class TestPasswordPolicies
{
    /// <summary>
    /// The production policy with default options over a handful of blocklist entries, so tests
    /// do not load the full bundled list for every service they construct.
    /// </summary>
    public static PasswordPolicy Default(params string[] blocked) =>
        new(Options.Create(new PasswordPolicyOptions()), new InMemoryPasswordBlocklist(
            blocked.Length > 0 ? blocked : ["password", "letmein", "iloveyou"]));

    internal sealed class InMemoryPasswordBlocklist(IEnumerable<string> entries) : IPasswordBlocklist
    {
        private readonly HashSet<string> _entries = entries.Select(PasswordText.Skeleton).ToHashSet();

        public bool Contains(string skeleton) => _entries.Contains(skeleton);
    }
}
