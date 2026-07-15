namespace VoxCrm.Infrastructure.Security;

public interface IPiiProtector
{
    bool Enabled { get; }
    string? Protect(string? value);
    string? Unprotect(string? value);
    string? BlindIndex(Guid tenantId, string? normalizedValue);
}

public sealed class NoOpPiiProtector : IPiiProtector
{
    public static NoOpPiiProtector Instance { get; } = new();
    public bool Enabled => false;
    public string? Protect(string? value) => value;
    public string? Unprotect(string? value) => value;
    public string? BlindIndex(Guid tenantId, string? normalizedValue) => normalizedValue;
}
