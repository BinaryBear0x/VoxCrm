namespace VoxCrm.IntegrationTests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApiProgram_stays_as_composition_root()
    {
        var repoRoot = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(repoRoot, "VoxCrm.Api", "Program.cs"));

        Assert.DoesNotContain("MapPost(\"/api/whatsapp", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UPDATE \"WhatsAppNotifications\"", program, StringComparison.Ordinal);
        Assert.DoesNotContain("record WhatsAppClaimRequest", program, StringComparison.Ordinal);
        Assert.Contains("MapWhatsAppEndpoints", program, StringComparison.Ordinal);
        Assert.Contains("AddVoxCrmApi", program, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VoxCrm.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repo root could not be found.");
    }
}
