using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using VoxCrm.Infrastructure.Security;
using VoxCrm.Application.DependencyInjection;
using VoxCrm.Application.Audit;
using VoxCrm.Application.WhatsApp;
using VoxCrm.Domain.Entities;
using VoxCrm.Infrastructure.Audit;
using VoxCrm.Infrastructure.Configuration;
using VoxCrm.Infrastructure.DependencyInjection;
using VoxCrm.Web.Services;
using VoxCrm.Web.ModelBinding;

if (args.Contains("--healthcheck", StringComparer.Ordinal))
{
    using var healthClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    try
    {
        using var response = await healthClient.GetAsync("http://127.0.0.1:8080/healthz");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch
    {
        Environment.ExitCode = 1;
    }
    return;
}

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 1 * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
});
var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, ".."));
builder.Configuration
    .AddVoxCrmEnvFile(repoRoot)
    .AddEnvironmentVariables();
var dataProtectionKeyPath = builder.Configuration["DataProtection:KeyPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
        .SetApplicationName("VoxCrm");
}

const string DevOnlyWhatsAppSecret = "dev-only-change-this-very-long-whatsapp-gateway-secret";
var configuredWhatsAppSecret = builder.Configuration["WhatsAppGateway:JwtSecret"];
if (!builder.Environment.IsDevelopment()
    && (string.IsNullOrWhiteSpace(configuredWhatsAppSecret) || configuredWhatsAppSecret == DevOnlyWhatsAppSecret))
{
    throw new InvalidOperationException("WhatsAppGateway:JwtSecret must be configured with a non-development value.");
}

builder.Services.AddScoped<AuditLogActionFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<AuditLogActionFilter>();
    options.ModelBinderProviders.Insert(0, new FlexibleDecimalModelBinderProvider());
});
builder.Services.AddHttpClient<WhatsAppGatewayClient>();
builder.Services.AddHttpClient<SystemHealthService>();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    options.AddPolicy("authentication", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<VoxCrm.Domain.Common.ITenantService, VoxCrm.Web.Services.TenantService>();
builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
builder.Services.AddVoxCrmApplication();
builder.Services.AddVoxCrmWebApplication();

builder.Services.AddSingleton<IPiiProtector, AesGcmPiiProtector>();
builder.Services.AddSingleton<PiiEncryptionInterceptor>();
builder.Services.AddDbContext<VoxCrm.Infrastructure.Data.VoxCrmDbContext>((provider, options) =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
            b => b.MigrationsAssembly("VoxCrm.Infrastructure"))
        .AddInterceptors(provider.GetRequiredService<PiiEncryptionInterceptor>());
    // Release kapısı aşağıda model farkını ayrıca reddeder. Published Linux runtime'ın
    // platforma özgü yanlış-pozitif uyarısı yalnız tek seferlik migration job'unda bastırılır.
    if (args.Contains("--migrate-only", StringComparer.Ordinal))
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
});
builder.Services.AddVoxCrmWebInfrastructureServices();

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase       = true;
    options.Password.RequireLowercase       = true;

    options.Lockout.AllowedForNewUsers  = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(10);
})
.AddEntityFrameworkStores<VoxCrm.Infrastructure.Data.VoxCrmDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath       = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.ExpireTimeSpan  = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(options =>
    options.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.Zero);

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

var app = builder.Build();

if (args.Contains("--migrate-only", StringComparer.Ordinal))
{
    await using var migrationScope = app.Services.CreateAsyncScope();
    var migrationContext = migrationScope.ServiceProvider.GetRequiredService<VoxCrm.Infrastructure.Data.VoxCrmDbContext>();
    var migrations = migrationContext.Database.GetMigrations();
    Console.WriteLine($"Discovered CRM migrations: {migrations.Count()} (latest: {migrations.LastOrDefault() ?? "none"}).");
    await migrationContext.Database.MigrateAsync();
    return;
}

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<VoxCrm.Infrastructure.Data.VoxCrmDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    
    var seedDemoData = app.Environment.IsDevelopment()
        && app.Configuration.GetValue<bool>("DataSeeding:SeedDemoData");
    await VoxCrm.Infrastructure.Data.DbSeeder.SeedAsync(
        context,
        userManager,
        roleManager,
        seedDemoData);

    var systemAdminOptions = app.Configuration
        .GetSection("DataSeeding:SystemAdmin")
        .Get<VoxCrm.Infrastructure.Data.SystemAdminBootstrapOptions>()
        ?? new VoxCrm.Infrastructure.Data.SystemAdminBootstrapOptions();
    await VoxCrm.Infrastructure.Data.DbSeeder.BootstrapSystemAdminAsync(
        userManager,
        systemAdminOptions);

    if (!app.Environment.IsDevelopment())
    {
        var bootstrapOptions = app.Configuration
            .GetSection("DataSeeding:ProductionDealer")
            .Get<VoxCrm.Infrastructure.Data.ProductionDealerBootstrapOptions>()
            ?? new VoxCrm.Infrastructure.Data.ProductionDealerBootstrapOptions();
        await VoxCrm.Infrastructure.Data.DbSeeder.BootstrapProductionDealerAsync(
            context,
            userManager,
            bootstrapOptions);
    }
}





app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    context.Items["CspNonce"] = nonce;
    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
    context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.TryAdd(
        "Permissions-Policy",
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        $"default-src 'self'; script-src 'self' 'nonce-{nonce}'; style-src 'self' 'nonce-{nonce}'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; upgrade-insecure-requests");
    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && (context.User.IsInRole("SystemAdmin") || context.User.IsInRole("Dealer"))
        && !context.Request.Path.StartsWithSegments("/Auth")
        && !context.Request.Path.StartsWithSegments("/css")
        && !context.Request.Path.StartsWithSegments("/js")
        && !context.Request.Path.StartsWithSegments("/lib"))
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user?.MustChangePassword == true)
        {
            context.Response.Redirect("/Auth/ChangePassword");
            return;
        }
        if (user is { TwoFactorEnabled: false })
        {
            context.Response.Redirect("/Auth/SetupMfa");
            return;
        }
    }
    await next();
});
app.UseAuthorization();


app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

RecurringJob.AddOrUpdate<VoxCrm.Infrastructure.Jobs.ReminderJob>(
    "daily-reminders",
    job => job.ProcessDailyRemindersAsync(),
    Cron.Daily);
RecurringJob.AddOrUpdate<VoxCrm.Infrastructure.Jobs.DataRetentionJob>(
    "data-retention",
    job => job.ExecuteAsync(CancellationToken.None),
    Cron.Daily);
app.MapStaticAssets();
app.MapGet("/healthz", async (VoxCrm.Infrastructure.Data.VoxCrmDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program;
