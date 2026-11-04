
using Microsoft.AspNetCore.Identity;
using NILUBRAIN.API.DTOs.Auth;
using NILUBRAIN.API.Entities;
using NILUBRAIN.API.Interfaces;

namespace NILUBRAIN.API.Services;

public class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
    }

    // REGISTER
    public async Task<AuthResponseDto> RegisterAsync(
        RegisterRequestDto request)
    {
        string email = request.Email.Trim().ToLowerInvariant();
        string username = request.Username.Trim();

        var existingEmail = await _userManager
            .FindByEmailAsync(email);

        if (existingEmail != null)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Email already registered."
            };
        }

        var existingUsername = await _userManager
            .FindByNameAsync(username);

        if (existingUsername != null)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Username already taken."
            };
        }

        var user = new ApplicationUser
        {
            FullName = request.FullName.Trim(),
            UserName = username,
            Email = email,
            EmailConfirmed = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Identity automatically hashes the password.
        var result = await _userManager.CreateAsync(
            user,
            request.Password);

        if (!result.Succeeded)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = string.Join(
                    " | ",
                    result.Errors.Select(e => e.Description))
            };
        }

        // Every normal registration gets User role.
        var roleResult = await _userManager.AddToRoleAsync(
            user, "User");

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            return new AuthResponseDto
            {
                Success = false,
                Message = "Unable to assign user role."
            };
        }

        var (token, expiresAt) =
            await _jwtService.GenerateTokenAsync(user);

        return new AuthResponseDto
        {
            Success = true,
            Message = "Registration successful.",
            AccessToken = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            Username = user.UserName,
            Email = user.Email,
            Roles = await _userManager.GetRolesAsync(user)
        };
    }

    // LOGIN
    public async Task<AuthResponseDto> LoginAsync(
        LoginRequestDto request)
    {
        string email = request.Email.Trim().ToLowerInvariant();

        var user = await _userManager.FindByEmailAsync(email);

        if (user == null || !user.IsActive)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = "Invalid email or password."
            };
        }

        // Checks password and handles failed login lockout.
        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            return new AuthResponseDto
            {
                Success = false,
                Message = result.IsLockedOut
                    ? "Account locked. Please try again later."
                    : "Invalid email or password."
            };
        }

        var (token, expiresAt) =
            await _jwtService.GenerateTokenAsync(user);

        return new AuthResponseDto
        {
            Success = true,
            Message = "Login successful.",
            AccessToken = token,
            ExpiresAt = expiresAt,
            UserId = user.Id,
            FullName = user.FullName,
            Username = user.UserName,
            Email = user.Email,
            Roles = await _userManager.GetRolesAsync(user)
        };
    }
}
