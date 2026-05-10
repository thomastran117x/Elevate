namespace backend.main.exceptions.http
{
    public class DeviceVerificationRequiredException : AppException
    {
        public DeviceVerificationRequiredException()
            : base("Device verification required.", StatusCodes.Status403Forbidden, "DEVICE_VERIFICATION_REQUIRED") { }
    }
}
