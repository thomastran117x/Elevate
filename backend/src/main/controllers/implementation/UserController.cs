using backend.main.dtos.requests.auth;
using backend.main.dtos.responses.auth;
using backend.main.dtos.responses.general;
using backend.main.exceptions.http;
using backend.main.models.core;
using backend.main.models.other;
using backend.main.services.interfaces;
using backend.main.utilities.implementation;

using Microsoft.AspNetCore.Mvc;

namespace backend.main.implementation.controllers
{
    [ApiController]
    [Route("users")]
    public class UserController : ControllerBase
    {
        private readonly IAuthService _authService;

        public UserController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpDelete("")]
        public async Task<IActionResult> DeleteUser([FromBody] LoginRequest request)
        {
            try
            {
                UserToken userToken = await _authService.LoginAsync(request.Email, request.Password);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    token.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Login successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserController] DeleteUser failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPut("avatar/{id}")]
        public async Task<IActionResult> UpdateAvatar([FromBody] SignUpRequest request)
        {
            try
            {
                await _authService.SignUpAsync(request.Email, request.Password, request.Usertype);

                return StatusCode(
                    200,
                    new MessageResponse("Verification email sent.")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserController] UpdateAvatar failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PartialUpdateUser([FromBody] SignUpRequest request)
        {
            try
            {
                await _authService.SignUpAsync(request.Email, request.Password, request.Usertype);

                return StatusCode(
                    200,
                    new MessageResponse("Verification email sent.")
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserController] PartialUpdateUser failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUsers([FromQuery] string token)
        {
            try
            {
                UserToken userToken = await _authService.VerifyAsync(token);
                User user = userToken.user;
                Token authToken = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, authToken.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    authToken.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Verification successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserController] GetUsers failed: {e}");
                return HandleError.Resolve(e);
            }
        }

        [HttpGet("")]
        public async Task<IActionResult> GetUser([FromBody] GoogleRequest request)
        {
            try
            {
                UserToken userToken = await _authService.GoogleAsync(request.Token);

                User user = userToken.user;
                Token token = userToken.token;

                HttpUtility.SetRefreshTokenCookie(Response, token.RefreshToken);

                AuthResponse response = new(
                    user.Id,
                    user.Email,
                    user.Usertype,
                    token.AccessToken
                );

                return StatusCode(
                    200,
                    new ApiResponse<AuthResponse>(
                        $"Login successful",
                        response
                    )
                );
            }
            catch (Exception e)
            {
                if (e is AppException)
                    return HandleError.Resolve(e);

                Logger.Error($"[UserController] GetUser failed: {e}");
                return HandleError.Resolve(e);
            }
        }
    }
}
