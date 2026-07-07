namespace VoxCrm.Api.Security;

public static class GatewayJwtSecretGuard
{
    private const string DevOnlySecret = "dev-only-change-this-very-long-whatsapp-gateway-secret";

    public static void ThrowIfUnsafeSecret(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var secret = configuration["WhatsAppGateway:JwtSecret"];
        if (!environment.IsDevelopment() && (string.IsNullOrWhiteSpace(secret) || secret == DevOnlySecret))
            throw new InvalidOperationException("WhatsAppGateway:JwtSecret must be configured with a non-development value.");
    }
}
