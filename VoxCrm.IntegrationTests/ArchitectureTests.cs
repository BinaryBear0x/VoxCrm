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

    [Fact]
    public void Crm_controllers_do_not_access_db_context_directly()
    {
        var repoRoot = FindRepoRoot();
        var controllersDirectory = Path.Combine(repoRoot, "VoxCrm.Web", "Controllers");
        var crmControllers = Directory
            .EnumerateFiles(controllersDirectory, "*Controller.cs")
            .Where(path => !path.EndsWith("WhatsAppController.cs", StringComparison.Ordinal));

        foreach (var controllerPath in crmControllers)
        {
            var source = File.ReadAllText(controllerPath);
            Assert.DoesNotContain("VoxCrmDbContext", source, StringComparison.Ordinal);
            Assert.DoesNotContain("_context", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Dealer_operations_expose_gateway_health_and_handle_an_empty_clinic_list()
    {
        var repoRoot = FindRepoRoot();
        var healthService = File.ReadAllText(Path.Combine(
            repoRoot,
            "VoxCrm.Web",
            "Services",
            "SystemHealthService.cs"));
        var whatsAppController = File.ReadAllText(Path.Combine(
            repoRoot,
            "VoxCrm.Web",
            "Controllers",
            "WhatsAppController.cs"));

        Assert.Contains("await FillGatewayHealthAsync(model, cancellationToken);", healthService, StringComparison.Ordinal);
        Assert.DoesNotContain("Gateway ayrıntıları yalnız SystemAdmin", healthService, StringComparison.Ordinal);
        Assert.Contains("RedirectToAction(\"Create\", \"Dealer\")", whatsAppController, StringComparison.Ordinal);
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
