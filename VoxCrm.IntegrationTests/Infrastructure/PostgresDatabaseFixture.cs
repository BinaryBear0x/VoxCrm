using Microsoft.EntityFrameworkCore;
using Npgsql;
using VoxCrm.Infrastructure.Data;

namespace VoxCrm.IntegrationTests.Infrastructure;

public sealed class PostgresDatabaseFixture : IAsyncLifetime
{
    private readonly string _databaseName = $"voxcrm_test_{Guid.NewGuid():N}";
    private string? _adminConnectionString;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _adminConnectionString = Environment.GetEnvironmentVariable("VOXCRM_TEST_ADMIN_CONNECTION")
            ?? "Host=127.0.0.1;Port=5432;Database=postgres;Username=voxcrm;Password=voxcrm_dev_password";

        var databaseBuilder = new NpgsqlConnectionStringBuilder(_adminConnectionString)
        {
            Database = _databaseName,
        };
        ConnectionString = databaseBuilder.ConnectionString;

        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE {QuoteIdentifier(_databaseName)}";
            await command.ExecuteNonQueryAsync();
        }

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminConnectionString))
            return;

        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS {QuoteIdentifier(_databaseName)} WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    public VoxCrmDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
            .UseNpgsql(ConnectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
            .Options;

        return new VoxCrmDbContext(options);
    }

    private static string QuoteIdentifier(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
