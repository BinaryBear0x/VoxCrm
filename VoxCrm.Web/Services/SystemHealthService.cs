using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Data;
using VoxCrm.Web.Models;

namespace VoxCrm.Web.Services;

public class SystemHealthService
{
    private readonly VoxCrmDbContext _context;
    private readonly WhatsAppGatewayClient _gatewayClient;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SystemHealthService(
        VoxCrmDbContext context,
        WhatsAppGatewayClient gatewayClient,
        HttpClient httpClient,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _context = context;
        _gatewayClient = gatewayClient;
        _httpClient = httpClient;
        _configuration = configuration;
        _environment = environment;
    }

    public async Task<DealerHealthViewModel> BuildDealerHealthAsync(
        Guid dealerId,
        CancellationToken cancellationToken)
    {
        var process = Process.GetCurrentProcess();
        var clinicIds = await _context.Clinics
            .Where(c => c.DealerId == dealerId)
            .Select(c => c.ID)
            .ToListAsync(cancellationToken);

        var model = new DealerHealthViewModel
        {
            GeneratedAt = DateTime.UtcNow,
            StartedAt = process.StartTime.ToUniversalTime(),
            WorkingSetMb = process.WorkingSet64 / 1024 / 1024,
            ClinicCount = clinicIds.Count,
            ActiveClinicCount = await _context.Clinics.CountAsync(c => c.DealerId == dealerId && c.IsActive, cancellationToken),
            WhatsAppEnabledClinicCount = await _context.Clinics.CountAsync(c => c.DealerId == dealerId && c.IsWhatsAppEnabled, cancellationToken),
            PendingWhatsAppCount = await CountNotificationsAsync(clinicIds, WhatsAppNotificationStatuses.Pending, cancellationToken),
            FailedWhatsAppCount = await CountNotificationsAsync(clinicIds, WhatsAppNotificationStatuses.Failed, cancellationToken),
            NeedsReviewWhatsAppCount = await CountNotificationsAsync(clinicIds, WhatsAppNotificationStatuses.NeedsReview, cancellationToken),
        };

        model.DatabaseStatus = await _context.Database.CanConnectAsync(cancellationToken) ? "ok" : "error";
        await FillVoxCrmApiHealthAsync(model, cancellationToken);
        // Gateway health is global operational data. Dealer screens must not
        // disclose aggregate tenant/session information.
        model.GatewayWorkerSummary = "Gateway ayrıntıları yalnız SystemAdmin tarafından görüntülenebilir.";
        await FillContainerStatusAsync(model, cancellationToken);

        return model;
    }

    private async Task<int> CountNotificationsAsync(
        IReadOnlyCollection<Guid> clinicIds,
        string status,
        CancellationToken cancellationToken)
    {
        if (clinicIds.Count == 0) return 0;

        return await _context.WhatsAppNotifications
            .IgnoreQueryFilters()
            .CountAsync(n => clinicIds.Contains(n.ClinicID) && n.Status == status, cancellationToken);
    }

    private async Task FillGatewayHealthAsync(DealerHealthViewModel model, CancellationToken cancellationToken)
    {
        try
        {
            model.GatewayHealth = await _gatewayClient.GetHealthAsync(cancellationToken);
            model.GatewayWorkerSummary = model.GatewayHealth?.Worker == null
                ? null
                : JsonSerializer.Serialize(model.GatewayHealth.Worker, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            model.GatewayError = ex.Message;
        }
    }

    private async Task FillVoxCrmApiHealthAsync(DealerHealthViewModel model, CancellationToken cancellationToken)
    {
        var baseUrl = _configuration["VoxCrmApi:BaseUrl"] ?? "http://127.0.0.1:5072";
        try
        {
            using var response = await _httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/api/health", cancellationToken);
            model.VoxCrmApiStatus = response.IsSuccessStatusCode ? "ok" : $"http {(int)response.StatusCode}";
        }
        catch (Exception ex)
        {
            model.VoxCrmApiError = ex.Message;
        }
    }

    private async Task FillContainerStatusAsync(DealerHealthViewModel model, CancellationToken cancellationToken)
    {
        var dockerEnabled = _configuration.GetValue<bool?>("SystemHealth:EnableDockerStatus")
            ?? _environment.IsDevelopment();
        if (!dockerEnabled)
        {
            model.ContainerStatusError = "Container durumu bu ortamda kapalı.";
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add("ps");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{json .}}");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                model.ContainerStatusError = "Docker komutu başlatılamadı.";
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                model.ContainerStatusError = string.IsNullOrWhiteSpace(error)
                    ? $"Docker komutu {process.ExitCode} koduyla bitti."
                    : error.Trim();
                return;
            }

            model.Containers = ParseDockerPs(await outputTask);
        }
        catch (Exception ex)
        {
            model.ContainerStatusError = ex.Message;
        }
    }

    private static IReadOnlyList<ContainerStatusItem> ParseDockerPs(string output)
    {
        var items = new List<ContainerStatusItem>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            items.Add(new ContainerStatusItem
            {
                Name = GetJsonString(root, "Names"),
                Image = GetJsonString(root, "Image"),
                State = GetJsonString(root, "State"),
                Status = GetJsonString(root, "Status"),
                Ports = GetJsonString(root, "Ports")
            });
        }

        return items.OrderBy(item => item.Name).ToList();
    }

    private static string GetJsonString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }
}
