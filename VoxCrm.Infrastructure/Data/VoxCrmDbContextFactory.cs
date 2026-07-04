using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VoxCrm.Infrastructure.Data
{
    public class VoxCrmDbContextFactory : IDesignTimeDbContextFactory<VoxCrmDbContext>
    {
        public VoxCrmDbContext CreateDbContext(string[] args)
        {
            var connectionString =
                Environment.GetEnvironmentVariable("VOXCRM_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "Host=127.0.0.1;Port=5432;Database=voxcrm_dev;Username=voxcrm;Password=voxcrm_dev_password";

            var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
                .UseNpgsql(connectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
                .Options;

            return new VoxCrmDbContext(options);
        }
    }
}
