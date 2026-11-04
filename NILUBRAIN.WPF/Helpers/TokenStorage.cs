
using System;

namespace NILUBRAIN.WPF.Helpers;

public static class TokenStorage
{
    private static string? _accessToken;
    private static DateTime _expiresAtUtc;

    // Save JWT Token
    public static void SaveToken(
        string accessToken,
        DateTime expiresAt)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new ArgumentException(
                "Invalid access token.");
        }

        _accessToken = accessToken;

        // Convert expiry to UTC
        _expiresAtUtc = expiresAt.Kind switch
        {
            DateTimeKind.Utc => expiresAt,
            DateTimeKind.Local => expiresAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(
                expiresAt, DateTimeKind.Utc)
        };
    }

    // Get JWT Token
    public static string? GetToken()
    {
        if (string.IsNullOrWhiteSpace(_accessToken))
            return null;

        if (IsTokenExpired())
        {
            Clear();
            return null;
        }

        return _accessToken;
    }

    // Get Expiry Date
    public static DateTime GetExpiry()
    {
        return _expiresAtUtc;
    }

    // Check Token Expiry
    public static bool IsTokenExpired()
    {
        return string.IsNullOrWhiteSpace(_accessToken)
               || DateTime.UtcNow >= _expiresAtUtc;
    }

    // Check Token Exists
    public static bool HasToken()
    {
        return GetToken() != null;
    }

    // Clear Token
    public static void Clear()
    {
        _accessToken = null;
        _expiresAtUtc = DateTime.MinValue;
    }
}
