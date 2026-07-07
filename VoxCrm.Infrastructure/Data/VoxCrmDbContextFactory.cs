using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VoxCrm.Infrastructure.Configuration;

namespace VoxCrm.Infrastructure.Data
{
    public class VoxCrmDbContextFactory : IDesignTimeDbContextFactory<VoxCrmDbContext>
    {
        public VoxCrmDbContext CreateDbContext(string[] args)
        {
            var env = EnvFile.Load(FindEnvPath());
            var connectionString =
                Environment.GetEnvironmentVariable("VOXCRM_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? (env.TryGetValue("VOXCRM_CONNECTION_STRING", out var legacy) ? legacy : null)
                ?? (env.TryGetValue("ConnectionStrings__DefaultConnection", out var current) ? current : null);

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Connection string is missing. Configure VOXCRM_CONNECTION_STRING or ConnectionStrings__DefaultConnection in environment/.env.");

            var options = new DbContextOptionsBuilder<VoxCrmDbContext>()
                .UseNpgsql(connectionString, builder => builder.MigrationsAssembly("VoxCrm.Infrastructure"))
                .Options;

            return new VoxCrmDbContext(options);
        }

        private static string FindEnvPath()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var directory = new DirectoryInfo(start);
                while (directory != null)
                {
                    var candidate = Path.Combine(directory.FullName, ".env");
                    if (File.Exists(candidate)) return candidate;
                    directory = directory.Parent;
                }
            }

            return Path.Combine(Directory.GetCurrentDirectory(), ".env");
        }
    }
}
