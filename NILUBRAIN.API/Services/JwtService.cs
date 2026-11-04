
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

using NILUBRAIN.API.Configuration;
using NILUBRAIN.API.Entities;
using NILUBRAIN.API.Interfaces;

namespace NILUBRAIN.API.Services;

public class JwtService : IJwtService
{
    private readonly JwtSettings _settings;

    private readonly UserManager<ApplicationUser> _userManager;

    public JwtService(
        IOptions<JwtSettings> settings,
        UserManager<ApplicationUser> userManager)
    {
        _settings = settings.Value;
        _userManager = userManager;
    }

    public async Task<(string Token, DateTime ExpiresAt)>
        GenerateTokenAsync(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_settings.Secret));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256);

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),

            new Claim(ClaimTypes.NameIdentifier, user.Id),

            new Claim(ClaimTypes.Name, user.UserName ?? ""),

            new Claim(ClaimTypes.Email, user.Email ?? ""),

            new Claim(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
        };

        foreach (var role in roles)
        {
            claims.Add(
                new Claim(ClaimTypes.Role, role));
        }

        DateTime expiresAt = DateTime.UtcNow.AddMinutes(
            _settings.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAt,
            signingCredentials: credentials
        );

        string tokenString =
            new JwtSecurityTokenHandler().WriteToken(token);

        return (tokenString, expiresAt);
    }
}
