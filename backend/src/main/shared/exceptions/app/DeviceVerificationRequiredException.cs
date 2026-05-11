using backend.main.shared.exceptions.http;

namespace backend.main.shared.exceptions.app
{
    public class DeviceVerificationRequiredException : AppException
    {
        public DeviceVerificationRequiredException()
            : base("Device verification required.", StatusCodes.Status403Forbidden, "DEVICE_VERIFICATION_REQUIRED") { }
    }
}
