
using NILUBRAIN.WPF.Models;

namespace NILUBRAIN.WPF.Services;

public class AuthService
{
    private readonly ApiService _apiService;

    public AuthService(ApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<AuthResponse?> RegisterAsync(
        RegisterRequest request)
    {
        return await _apiService.PostAsync
            <RegisterRequest, AuthResponse>(
                "api/Auth/register",
                request);
    }

    public async Task<AuthResponse?> LoginAsync(
        LoginRequest request)
    {
        return await _apiService.PostAsync
            <LoginRequest, AuthResponse>(
                "api/Auth/login",
                request);
    }
}
