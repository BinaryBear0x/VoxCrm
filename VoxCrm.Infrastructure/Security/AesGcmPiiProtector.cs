using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace VoxCrm.Infrastructure.Security;

public sealed class AesGcmPiiProtector : IPiiProtector
{
    private const string Prefix = "enc:v1:";
    private readonly byte[]? _key;

    public AesGcmPiiProtector(IConfiguration configuration)
    {
        var keyFile = configuration["PiiEncryption:KeyFile"];
        if (string.IsNullOrWhiteSpace(keyFile))
        {
            if (string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Production", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PiiEncryption:KeyFile is required in production.");
            return;
        }
        if (!File.Exists(keyFile)) throw new InvalidOperationException("PII encryption key file does not exist.");
        var raw = Convert.FromBase64String(File.ReadAllText(keyFile).Trim());
        if (raw.Length != 32) throw new InvalidOperationException("PII encryption key must be exactly 32 bytes encoded as base64.");
        _key = raw;
    }

    public bool Enabled => _key != null;

    public string? Protect(string? value)
    {
        if (!Enabled || string.IsNullOrEmpty(value) || value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key!, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        var payload = new byte[nonce.Length + tag.Length + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, payload, nonce.Length + tag.Length, cipher.Length);
        return Prefix + Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? value)
    {
        if (!Enabled || string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return value;
        var payload = Convert.FromBase64String(value[Prefix.Length..]);
        if (payload.Length < 29) throw new CryptographicException("Encrypted PII payload is invalid.");
        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var cipher = payload.AsSpan(28);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key!, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    public string? BlindIndex(Guid tenantId, string? normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue)) return null;
        if (!Enabled) return normalizedValue;
        using var hmac = new HMACSHA256(_key!);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{tenantId:N}:{normalizedValue}"))).ToLowerInvariant();
    }
}
