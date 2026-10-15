using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlaraERP.IntegrationTests.Infrastructure;

/// <summary>An HttpClient that behaves like the SPA: keeps cookies and sends the CSRF header.</summary>
public sealed class ErpClient(HttpClient http) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public HttpClient Http { get; } = http;

    public async Task RefreshCsrfAsync()
    {
        var response = await Http.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf", Json);
        Http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        Http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", response!.Token);
    }

    public async Task<HttpResponseMessage> LoginAsync(string email, string password)
    {
        await RefreshCsrfAsync();
        var response = await Http.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);
        if (response.IsSuccessStatusCode)
            await RefreshCsrfAsync(); // the token is bound to the signed-in identity
        return response;
    }

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);
    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => Http.PostAsJsonAsync(url, body ?? new { }, Json);
    public Task<HttpResponseMessage> PutAsync(string url, object body) => Http.PutAsJsonAsync(url, body, Json);
    public Task<HttpResponseMessage> DeleteAsync(string url) => Http.DeleteAsync(url);

    public async Task<T> GetJsonAsync<T>(string url)
    {
        var response = await Http.GetAsync(url);
        await response.EnsureSuccessWithBodyAsync();
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public void Dispose() => Http.Dispose();

    private sealed record CsrfResponse(string Token);
}

public static class HttpResponseExtensions
{
    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(ErpClient.Json))!;

    /// <summary>Like EnsureSuccessStatusCode, but includes the problem body in the failure message.</summary>
    public static async Task EnsureSuccessWithBodyAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return doc.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
