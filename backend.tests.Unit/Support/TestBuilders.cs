using backend.main.features.profile;
using backend.main.shared.requests;

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
