using backend.main.dtos.general;
using backend.main.models.other;

namespace backend.main.services.interfaces
{
    public interface IDeviceService
    {
        Task EnsureDeviceKnownAsync(int userId, string userEmail, ClientRequestInfo requestInfo);
        Task<UserToken> VerifyDeviceAsync(string token);
    }
}
