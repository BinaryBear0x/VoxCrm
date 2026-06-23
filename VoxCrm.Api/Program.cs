using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VoxCrm.Infrastructure.Data;


var builder = WebApplication.CreateBuilder(args);
// API'mizi Veritabanına Bağlıyoruz
builder.Services.AddDbContext<VoxCrmDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddOpenApi();
var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

const string API_KEY ="VOXCRM_SECRETBOTKEY"; // Bu key çoook gizli, botlar bu key ile mesaj atacak

app.MapGet("/api/whatsapp/reminders", async ([FromHeader(Name = "x-api-key")] string apiKey, VoxCrmDbContext db) =>
{
    if (apiKey != API_KEY) return Results.Unauthorized();
    var pendingMessages = await db.WhatsAppNotifications
        .Where(w => w.Status == "Pending")
        .Select(w => new
        {
            NotificationId = w.ID,
            PhoneNumber = w.PhoneNumber,
            Message = w.MessageContent
        })
        .ToListAsync();
    return Results.Ok(pendingMessages);
});

//Python botu mesajı attıktan sonra buraya istek atıp "Gönderdim" veya "Numara Yok Hata" diyecek
app.MapPost("/api/webhooks/whatsapp/status", async ([FromHeader(Name = "x-api-key")] string apiKey, [FromBody] WebhookPayload payload, VoxCrmDbContext db) =>
{
    if (apiKey != API_KEY) return Results.Unauthorized();
    // Hangi mesajın sonucunu bildiriyor onu buluyoruz
    var notification = await db.WhatsAppNotifications.FindAsync(payload.NotificationId);
    if (notification == null) return Results.NotFound("Mesaj bulunamadı");
    notification.Status = payload.Status; // "Sent" veya "Failed"

    if (payload.Status == "Sent")
        notification.SentAt = DateTime.UtcNow;
    else
        notification.ErrorMessage = payload.ErrorMessage;
    await db.SaveChangesAsync();
    return Results.Ok("Statü güncellendi");
});


app.Run();
// Python'dan gelecek JSON paketini C#'ın anlayacağı formata (Modele) çeviriyoruz
public class WebhookPayload
{
    public Guid NotificationId { get; set; }
    public string Status { get; set; } = string.Empty; // Sent, Failed
    public string? ErrorMessage { get; set; }
}