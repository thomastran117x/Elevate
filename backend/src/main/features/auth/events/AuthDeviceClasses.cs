namespace backend.main.features.auth.events;

public static class AuthDeviceClasses
{
    /// <summary>
    /// Maps a device type the request classifier produced, such as
    /// <c>ClientRequestInfo.DeviceType</c>, onto <see cref="AuthDeviceClass"/>.
    /// </summary>
    public static AuthDeviceClass FromDeviceType(string? deviceType) =>
        deviceType switch
        {
            "Desktop" => AuthDeviceClass.Desktop,
            "Mobile" => AuthDeviceClass.Mobile,
            "Tablet" => AuthDeviceClass.Tablet,
            "API Client" => AuthDeviceClass.ApiClient,
            _ => AuthDeviceClass.Unknown,
        };
}
