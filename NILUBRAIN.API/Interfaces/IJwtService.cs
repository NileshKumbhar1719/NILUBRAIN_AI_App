
using NILUBRAIN.API.Entities;

namespace NILUBRAIN.API.Interfaces
{
    public interface IJwtService
    {
        Task<(string Token, DateTime ExpiresAt)>
            GenerateTokenAsync(ApplicationUser user);
    }
}
