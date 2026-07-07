using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoxCrm.Web.Services;

public class WhatsAppGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public WhatsAppGatewayClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _httpClient.BaseAddress = new Uri(_configuration["WhatsAppGateway:BaseUrl"] ?? "http://127.0.0.1:8088");
    }

    public Task<GatewaySessionStatus?> GetStatusAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return SendAsync<GatewaySessionStatus>(HttpMethod.Get, $"/api/clinics/{clinicId}/whatsapp/status", "whatsapp.session.read", cancellationToken);
    }

    public Task<GatewayQrResponse?> GetQrAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return SendAsync<GatewayQrResponse>(HttpMethod.Get, $"/api/clinics/{clinicId}/whatsapp/qr", "whatsapp.session.read", cancellationToken);
    }

    public Task<GatewayHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return SendAsync<GatewayHealthResponse>(HttpMethod.Get, "/api/health", "whatsapp.session.read", cancellationToken);
    }

    public Task ConnectAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return SendWithoutResponseAsync(HttpMethod.Post, $"/api/clinics/{clinicId}/whatsapp/connect", "whatsapp.session.write", cancellationToken);
    }

    public Task DisconnectAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        return SendWithoutResponseAsync(HttpMethod.Post, $"/api/clinics/{clinicId}/whatsapp/disconnect", "whatsapp.session.write", cancellationToken);
    }

    /// <summary>Belirli bir telefona serbest metin mesajı gönderir.</summary>
    public async Task<GatewaySendResult?> SendMessageAsync(Guid clinicId, string phone, string message, CancellationToken cancellationToken = default)
    {
        var payload = new { phone, message };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = CreateRequest(HttpMethod.Post, $"/api/clinics/{clinicId}/whatsapp/send", "whatsapp.message.write");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new GatewaySendResult(response.IsSuccessStatusCode, (int)response.StatusCode, body);
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, string scope, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, url, scope);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return default;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private async Task SendWithoutResponseAsync(HttpMethod method, string url, string scope, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, url, scope);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Gateway istegi basarisiz oldu ({(int)response.StatusCode}): {body}",
            null,
            response.StatusCode);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string scope)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateJwt(scope));
        return request;
    }

    private string CreateJwt(string scope)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iss = _configuration["WhatsAppGateway:GatewayIssuer"]
                ?? _configuration["WhatsAppGateway:ClientIssuer"]
                ?? "voxcrm",
            aud = _configuration["WhatsAppGateway:GatewayAudience"]
                ?? _configuration["WhatsAppGateway:ClientAudience"]
                ?? "voxcrm-whatsapp-gateway",
            sub = "voxcrm-web",
            scope,
            iat = now.ToUnixTimeSeconds(),
            exp = now.AddMinutes(5).ToUnixTimeSeconds(),
            jti = Guid.NewGuid().ToString("N")
        }));

        var unsigned = $"{header}.{payload}";
        var secret = _configuration["WhatsAppGateway:JwtSecret"] ?? string.Empty;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        return $"{unsigned}.{signature}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

public record GatewaySessionStatus(
    Guid ClinicId,
    string Status,
    string? ConnectedPhone,
    DateTime? LastSeenAt,
    string? LastError,
    int PendingCount = 0,
    int RetryScheduledCount = 0,
    int FailedCount = 0,
    int NeedsReviewCount = 0,
    DateTime? LastSentAt = null,
    int QueueLagSeconds = 0);

public record GatewayQrResponse(
    Guid ClinicId,
    string? Qr,
    DateTime? UpdatedAt,
    string Status);

public record GatewayHealthResponse(
    string Status,
    string Service,
    string Database,
    object? Worker,
    int SessionCount,
    int ReadyClinicCount,
    int FailedCount,
    int NeedsReviewCount,
    int QueueLagSeconds,
    DateTime? LastSendAt,
    string? LastError);

public record GatewaySendResult(bool Success, int HttpStatus, string ResponseBody);
