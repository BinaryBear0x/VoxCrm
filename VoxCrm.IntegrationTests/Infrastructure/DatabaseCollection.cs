namespace VoxCrm.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<PostgresDatabaseFixture>
{
    public const string Name = "postgres-database";
}
