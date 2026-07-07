using Microsoft.Extensions.Configuration;

namespace VoxCrm.Infrastructure.Configuration;

public static class EnvFileConfigurationExtensions
{
    public static IConfigurationBuilder AddVoxCrmEnvFile(this IConfigurationBuilder builder, string rootPath)
    {
        var envPath = Path.Combine(rootPath, ".env");
        var values = EnvFile.Load(envPath);
        if (values.Count == 0) return builder;

        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            normalized[key.Replace("__", ":", StringComparison.Ordinal)] = value;
        }

        if (values.TryGetValue("VOXCRM_CONNECTION_STRING", out var connectionString)
            && !normalized.ContainsKey("ConnectionStrings:DefaultConnection"))
        {
            normalized["ConnectionStrings:DefaultConnection"] = connectionString;
        }

        return builder.AddInMemoryCollection(normalized);
    }
}

public static class EnvFile
{
    public static IReadOnlyDictionary<string, string> Load(string envPath)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(envPath)) return values;

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Trim('"');
            values[key] = value;
        }

        return values;
    }
}
