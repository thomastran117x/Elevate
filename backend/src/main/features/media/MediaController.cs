using backend.main.application.features;
using backend.main.application.security;
using backend.main.features.media.contracts.responses;
using backend.main.shared.exceptions.http;
using backend.main.shared.responses;
using backend.main.shared.utilities.logger;
using backend.main.utilities;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace backend.main.features.media;

/// <summary>
/// Lets an editor that has just attached an upload find out when it has been validated.
/// </summary>
/// <remarks>
/// No named rate-limit policy: this is polled every few seconds while an image is checked, so
/// it runs under the global per-user limiter. The image-upload policy allows 30 requests per ten
/// minutes and one upload's polling would exhaust it.
/// </remarks>
[ApiController]
[FeatureGate(FeatureFlagKeys.StorageQuarantine)]
[Route("media")]
public class MediaController : ControllerBase
{
    private readonly IMediaAssetQueryService _queryService;

    public MediaController(IMediaAssetQueryService queryService)
    {
        _queryService = queryService;
    }

    [Authorize]
    [HttpGet("{publicId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<MediaAssetResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMediaAsset(Guid publicId, CancellationToken cancellationToken)
    {
        try
        {
            var user = User.GetUserPayload();

            var asset = await _queryService.GetForViewerAsync(publicId, user.Id, user.Role, cancellationToken);

            return Ok(new ApiResponse<MediaAssetResponse>("Media asset retrieved.", asset));
        }
        catch (Exception e)
        {
            if (e is AppException)
                return HandleError.Resolve(e);

            Logger.Error($"[MediaController] GetMediaAsset failed: {e}");
            return HandleError.Resolve(e);
        }
    }
}
