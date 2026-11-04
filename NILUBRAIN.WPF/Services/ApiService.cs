
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NILUBRAIN.WPF.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest request,
        string? token = null)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post, endpoint);

        message.Content = JsonContent.Create(request);

        if (!string.IsNullOrWhiteSpace(token))
        {
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        using var response =
            await _httpClient.SendAsync(message);

        var result = await response.Content
            .ReadFromJsonAsync<TResponse>();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"API Error: {(int)response.StatusCode}");
        }

        return result;
    }

    public async Task<TResponse?> GetAsync<TResponse>(
        string endpoint,
        string? token = null)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get, endpoint);

        if (!string.IsNullOrWhiteSpace(token))
        {
            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        using var response =
            await _httpClient.SendAsync(message);

        var result = await response.Content
            .ReadFromJsonAsync<TResponse>();

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"API Error: {(int)response.StatusCode}");
        }

        return result;
    }
}
