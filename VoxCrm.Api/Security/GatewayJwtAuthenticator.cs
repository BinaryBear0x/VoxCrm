using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace VoxCrm.Api.Security;

public sealed class GatewayJwtAuthenticator
{
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _replayCache;

    public GatewayJwtAuthenticator(IConfiguration configuration, IMemoryCache replayCache)
    {
        _configuration = configuration;
        _replayCache = replayCache;
    }

    public GatewayAuthResult Authorize(HttpRequest request, string requiredScope)
    {
        var authHeader = request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return new GatewayAuthResult(false, string.Empty);

        var token = authHeader["Bearer ".Length..].Trim();
        var parts = token.Split('.');
        if (parts.Length != 3) return new GatewayAuthResult(false, string.Empty);

        var secret = _configuration["WhatsAppGateway:JwtSecret"];
        if (string.IsNullOrWhiteSpace(secret)) return new GatewayAuthResult(false, string.Empty);

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}", secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSignature),
                Encoding.UTF8.GetBytes(parts[2])))
            return new GatewayAuthResult(false, string.Empty);

        using var payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        var root = payload.RootElement;

        var expectedIssuer = _configuration["WhatsAppGateway:ApiIssuer"]
            ?? _configuration["WhatsAppGateway:Issuer"];
        if (!TryGetString(root, "iss", out var issuer) || issuer != expectedIssuer)
            return new GatewayAuthResult(false, string.Empty);

        var expectedAudience = _configuration["WhatsAppGateway:ApiAudience"]
            ?? _configuration["WhatsAppGateway:Audience"];
        if (!TryGetString(root, "aud", out var audience) || audience != expectedAudience)
            return new GatewayAuthResult(false, string.Empty);

        if (!root.TryGetProperty("exp", out var expElement)
            || DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64()) <= DateTimeOffset.UtcNow)
            return new GatewayAuthResult(false, string.Empty);
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());

        if (!TryGetString(root, "scope", out var scopes)
            || !scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(requiredScope))
            return new GatewayAuthResult(false, string.Empty);

        if (!TryGetString(root, "jti", out var jti))
            return new GatewayAuthResult(false, string.Empty);

        var replayKey = $"whatsapp-jti:{jti}";
        if (_replayCache.TryGetValue(replayKey, out _))
            return new GatewayAuthResult(false, string.Empty);
        _replayCache.Set(replayKey, true, expiresAt);

        TryGetString(root, "sub", out var subject);
        return new GatewayAuthResult(true, subject);
    }

    private static string Sign(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(name, out var element)) return false;
        value = element.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed record GatewayAuthResult(bool IsAuthorized, string Subject);
