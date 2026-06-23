using Microsoft.EntityFrameworkCore;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Dashboard;
using Microsoft.AspNetCore.Identity;
using VoxCrm.Domain.Entities;
using VoxCrm.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// HTTP bağlamını okumak için (TenantService kullanıyor)
builder.Services.AddHttpContextAccessor();
// BOLA koruması için aktif kliniği bulan servis
builder.Services.AddScoped<VoxCrm.Domain.Common.ITenantService, VoxCrm.Web.Services.TenantService>();

// Veritabanı bağlantısı
builder.Services.AddDbContext<VoxCrm.Infrastructure.Data.VoxCrmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"),
        b => b.MigrationsAssembly("VoxCrm.Infrastructure")));

// ─── GÜVENLİK AÇIĞI #7 DÜZELTİLDİ: Şifre kuralları güçlendirildi ───────────
// Neden? "123456" veya "aaaaaa" gibi tahmin edilmesi çok kolay şifreler
// sistemi brute-force saldırısına açık bırakır.
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    // En az 8 karakter, büyük harf, rakam VE özel karakter zorunlu.
    options.Password.RequireDigit           = true;
    options.Password.RequiredLength         = 8;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequireUppercase       = true;
    options.Password.RequireLowercase       = true;

    // ─── GÜVENLİK AÇIĞI #8 DÜZELTİLDİ: Brute-force (Kaba Kuvvet) koruması ──
    // Neden? Lockout kapalıyken birisi şifrenizi saniyede 1000 kez deneyebilir.
    // 5 yanlış denemeden sonra hesabı 10 dakika kilitleyerek bunu engelliyoruz.
    options.Lockout.AllowedForNewUsers  = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(10);
})
.AddEntityFrameworkStores<VoxCrm.Infrastructure.Data.VoxCrmDbContext>()
.AddDefaultTokenProviders();

// Çerez (cookie) ayarları — kimlik doğrulama yolları
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath       = "/Auth/Login";
    options.AccessDeniedPath = "/Auth/AccessDenied";
    options.ExpireTimeSpan  = TimeSpan.FromHours(8); // 8 saatte bir yeniden giriş
    options.SlidingExpiration = true; // Aktif kullanımda süre sıfırlanır
});

// Hangfire (zamanlanmış görevler)
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(opts => opts.UseNpgsqlConnection(
        builder.Configuration.GetConnectionString("DefaultConnection"))));
builder.Services.AddHangfireServer();

var app = builder.Build();

// ─── ROLLER TOHUMU (Seed) — Uygulama ilk açıldığında rolleri oluştur ─────────
// Neden? Rolleri elle veritabanına yazmak yerine kod garantisi veriyoruz.
// "Dealer" rolü → bayi/admin. "Clinic" rolü → klinik personeli.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<VoxCrm.Infrastructure.Data.VoxCrmDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    
    // Veritabanını örnek verilerle doldur (Admin hesabı, klinikler, hastalar, borçlar)
    await VoxCrm.Infrastructure.Data.DbSeeder.SeedAsync(context, userManager, roleManager);
}





if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication(); // Giriş yaptı mı?
app.UseAuthorization();  // Bu sayfaya yetkisi var mı?

// ─── GÜVENLİK AÇIĞI #5 DÜZELTİLDİ: Hangfire paneline yetki koruması ────────
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthFilter() }
});

// Günlük hatırlatma jobı
RecurringJob.AddOrUpdate<VoxCrm.Infrastructure.Jobs.ReminderJob>(
    "daily-reminders",
    job => job.ProcessDailyRemindersAsync(),
    Cron.Daily);
app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
