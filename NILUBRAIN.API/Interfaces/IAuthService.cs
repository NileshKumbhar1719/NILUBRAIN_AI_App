
using NILUBRAIN.API.DTOs.Auth;

namespace NILUBRAIN.API.Interfaces
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(
            RegisterRequestDto request);

        Task<AuthResponseDto> LoginAsync(
            LoginRequestDto request);
    }
}
