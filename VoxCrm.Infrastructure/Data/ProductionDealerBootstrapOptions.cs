namespace VoxCrm.Infrastructure.Data;

public sealed class ProductionDealerBootstrapOptions
{
    public bool Enabled { get; init; }
    public string CompanyName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Phone { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
}
