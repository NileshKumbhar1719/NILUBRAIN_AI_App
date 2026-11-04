
using Microsoft.AspNetCore.Identity;

namespace NILUBRAIN.API.Entities;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } =
        DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}
