using backend.main.features.auth.contracts.responses;

namespace backend.main.features.auth.device
{
    /// <summary>The signed-in owner's view of their trusted devices, and forgetting them.</summary>
    public interface ITrustedDeviceManagementService
    {
        /// <summary>
        /// The owner's unexpired devices. The one whose token the request presented, if any, is
        /// marked as current.
        /// </summary>
        Task<TrustedDevicesResponse> ListAsync(int userId, string? currentDeviceToken);

        /// <summary>
        /// Forgets one of the owner's devices, so its next sign-in asks for verification again.
        /// </summary>
        /// <exception cref="shared.exceptions.http.ResourceNotFoundException">
        /// The id is not one of the owner's devices, whether it is unknown or another account's.
        /// </exception>
        Task<TrustedDeviceRevocationResponse> RevokeAsync(int userId, int deviceId, string? currentDeviceToken);

        /// <summary>Forgets every device of the owner, the current one included.</summary>
        Task<TrustedDeviceRevocationResponse> RevokeAllAsync(int userId, string? currentDeviceToken);
    }
}
