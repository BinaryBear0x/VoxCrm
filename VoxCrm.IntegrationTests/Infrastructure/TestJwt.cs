using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VoxCrm.IntegrationTests.Infrastructure;

public static class TestJwt
{
    public const string Issuer = "voxcrm-whatsapp-gateway";
    public const string Audience = "voxcrm-api";
    public const string Secret = "test-only-whatsapp-gateway-secret-with-enough-length";

    public static string Create(
        string scope,
        string? issuer = null,
        string? audience = null,
        string? secret = null,
        DateTimeOffset? expiresAt = null,
        string? jti = null)
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer ?? Issuer,
            ["aud"] = audience ?? Audience,
            ["sub"] = "integration-test-gateway",
            ["scope"] = scope,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = (expiresAt ?? now.AddMinutes(5)).ToUnixTimeSeconds(),
            ["jti"] = jti ?? Guid.NewGuid().ToString("N"),
        };

        return CreateRaw(payload, secret ?? Secret);
    }

    public static string CreateRaw(IDictionary<string, object?> payload, string secret = Secret)
    {
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT",
        };
        var headerPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadPart = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signaturePart = Sign($"{headerPart}.{payloadPart}", secret);
        return $"{headerPart}.{payloadPart}.{signaturePart}";
    }

    private static string Sign(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
