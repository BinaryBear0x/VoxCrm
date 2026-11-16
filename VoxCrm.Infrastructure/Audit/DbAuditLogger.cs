using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VoxCrm.Application.Audit;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.Infrastructure.Audit;

public sealed class DbAuditLogger : IAuditLogger
{
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "phone",
        "fromPhone",
        "phoneNumber",
        "message",
        "messageContent",
        "email",
        "password",
        "token",
        "jwt",
        "secret"
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VoxCrmDbContext? _legacyContext;

    public DbAuditLogger(VoxCrmDbContext context)
    {
        _scopeFactory = null!;
        _legacyContext = context;
    }

    [ActivatorUtilitiesConstructor]
    public DbAuditLogger(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var log = new SystemAuditLog
            {
                Level = entry.Level,
                Source = entry.Source,
                Category = entry.Category,
                Outcome = entry.Outcome,
                Action = entry.Action,
                Message = entry.Message,
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                DealerId = entry.DealerId,
                ClinicId = entry.ClinicId,
                ActorUserId = entry.ActorUserId,
                ActorUserName = entry.ActorUserName,
                ActorRole = entry.ActorRole,
                HttpMethod = entry.HttpMethod,
                Path = entry.Path,
                StatusCode = entry.StatusCode,
                IpAddress = entry.IpAddress,
                UserAgent = entry.UserAgent,
                ExceptionType = entry.ExceptionType,
                TraceId = entry.TraceId,
                CorrelationId = entry.CorrelationId,
                DurationMs = entry.DurationMs,
                ErrorCode = entry.ErrorCode,
                RequestId = entry.RequestId,
                MetadataJson = entry.Metadata.Count == 0
                    ? null
                    : JsonSerializer.Serialize(MaskMetadata(entry.Metadata))
            };

            // Audit must never reuse the request's unit of work.  In particular, a
            // failed create can leave Added entities tracked; reusing that context
            // would retry the primary write while trying to persist the audit entry.
            if (_legacyContext != null)
            {
                _legacyContext.SystemAuditLogs.Add(log);
                await _legacyContext.SaveChangesAsync(cancellationToken);
                return;
            }
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<VoxCrmDbContext>();
            context.SystemAuditLogs.Add(log);
            await context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Audit logging must never break the primary operation.
        }
    }

    private static Dictionary<string, object?> MaskMetadata(IReadOnlyDictionary<string, object?> metadata)
    {
        var masked = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in metadata)
        {
            if (value == null)
            {
                masked[key] = null;
                continue;
            }

            var text = value.ToString() ?? string.Empty;
            masked[key] = SensitiveKeys.Contains(key)
                ? MaskValue(key, text)
                : value;
        }

        return masked;
    }

    private static object MaskValue(string key, string value)
    {
        if (key.Contains("phone", StringComparison.OrdinalIgnoreCase)
            || key.Equals("fromPhone", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(value.Where(char.IsDigit).ToArray());
            return digits.Length <= 4 ? "****" : $"{new string('*', Math.Max(0, digits.Length - 4))}{digits[^4..]}";
        }

        if (key.Contains("message", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                length = value.Length,
                preview = value.Length <= 12 ? "***" : $"{value[..Math.Min(12, value.Length)]}...",
                sha256 = Hash(value)
            };
        }

        if (key.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            var at = value.IndexOf('@');
            return at <= 1 ? "***" : $"{value[0]}***{value[at..]}";
        }

        return "***";
    }

    private static string Hash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
