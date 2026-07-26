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

    [Fact]
    public void WhatsApp_ui_exposes_tenant_scoped_manual_messaging()
    {
        var repoRoot = FindRepoRoot();
        var controller = File.ReadAllText(Path.Combine(
            repoRoot,
            "VoxCrm.Web",
            "Controllers",
            "WhatsAppController.cs"));
        var view = File.ReadAllText(Path.Combine(
            repoRoot,
            "VoxCrm.Web",
            "Views",
            "WhatsApp",
            "Index.cshtml"));

        Assert.Contains("SendManual", controller, StringComparison.Ordinal);
        Assert.Contains("/WhatsApp/SendManual", view, StringComparison.Ordinal);
        Assert.Contains("Manuel Bildirim Gönder", view, StringComparison.Ordinal);
        Assert.Contains("data-wa-send-button", view, StringComparison.Ordinal);
        Assert.Contains("setSendControlsReady(payload.status === 'ready')", view, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsApp_worker_has_outbound_connectivity_without_a_published_port()
    {
        var repoRoot = FindRepoRoot();
        var compose = File.ReadAllText(Path.Combine(repoRoot, "deploy", "docker-compose.prod.yml"));
        var workerStart = compose.IndexOf("  wa-worker:", StringComparison.Ordinal);
        var caddyStart = compose.IndexOf("  caddy:", workerStart, StringComparison.Ordinal);
        var worker = compose[workerStart..caddyStart];

        Assert.Contains("networks: [backend, egress]", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", worker, StringComparison.Ordinal);
        Assert.Contains("  egress: {}", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatsApp_claim_decrypts_raw_sql_pii_before_leaving_the_api()
    {
        var repoRoot = FindRepoRoot();
        var repository = File.ReadAllText(Path.Combine(
            repoRoot,
            "VoxCrm.Infrastructure",
            "WhatsApp",
            "WhatsAppNotificationRepository.cs"));

        Assert.Contains("_protector.Unprotect(reader.GetString(3))", repository, StringComparison.Ordinal);
        Assert.Contains("_protector.Unprotect(reader.GetString(4))", repository, StringComparison.Ordinal);
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
