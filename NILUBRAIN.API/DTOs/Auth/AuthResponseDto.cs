
namespace NILUBRAIN.API.DTOs.Auth;

public class AuthResponseDto
{
    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string? AccessToken { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? UserId { get; set; }

    public string? FullName { get; set; }

    public string? Username { get; set; }

    public string? Email { get; set; }

    public IList<string> Roles { get; set; } = new List<string>();
}
