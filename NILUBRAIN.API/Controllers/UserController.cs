
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using NILUBRAIN.API.DTOs.User;
using NILUBRAIN.API.Entities;

namespace NILUBRAIN.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserController(
        UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    // GET: api/user/profile
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        string? userId = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user == null || !user.IsActive)
        {
            return Unauthorized();
        }

        var roles = await _userManager.GetRolesAsync(user);

        var profile = new UserProfileDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            Username = user.UserName ?? "",
            Email = user.Email ?? "",
            Roles = roles
        };

        return Ok(profile);
    }

    // GET: api/user/admin
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly()
    {
        return Ok(new
        {
            Success = true,
            Message = "Welcome to NILUBRAIN AI Admin Dashboard."
        });
    }
}
