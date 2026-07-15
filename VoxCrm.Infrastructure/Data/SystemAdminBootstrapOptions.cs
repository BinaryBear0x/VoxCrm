namespace VoxCrm.Infrastructure.Data;

public sealed class SystemAdminBootstrapOptions
{
    public bool Enabled { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
