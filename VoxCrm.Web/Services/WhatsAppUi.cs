namespace VoxCrm.Web.Services;

public static class WhatsAppUi
{
    /// <summary>Phone of the linked device without the "@s.whatsapp.net" / ":device" suffixes.</summary>
    public static string ConnectedPhone(string? jid) =>
        VoxCrm.Infrastructure.WhatsApp.WhatsAppRecipientGuard.Digits(jid?.Split('@')[0].Split(':')[0]);

    public static string Status(string? s, bool connected = true) => s switch
    {
        "Pending" => connected ? "Bekliyor" : "Bağlantı bekliyor",
        "RetryScheduled" => "Yeniden denenecek", "Sent" => "Gönderildi", "Failed" => "Başarısız",
        "Cancelled" => "İptal edildi", "NeedsReview" => "İnceleme gerekiyor",
        "Processing" or "DirectSending" => "İşleniyor", "ready" => "Bağlı", "qr" => "QR okutulması bekleniyor",
        "connecting" => "Bağlanıyor", "auth_failed" => "Oturum doğrulanamadı — yeniden bağlayın",
        _ => "Bağlantı yok — yeniden bağlayın"
    };
    public static string Error(string? error) => string.IsNullOrWhiteSpace(error) ? "" : error switch
    {
        var e when e.Contains("SESSION_NOT_READY") || e.Contains("session is not ready", StringComparison.OrdinalIgnoreCase) => "WhatsApp bağlantısı hazır değil. Bağlantı bölümünden QR ile yeniden bağlayın.",
        var e when e.Contains("INVALID_PHONE") || e.Contains("NOT_REGISTERED") || e.Contains("NOT_ON_WHATSAPP") || e.Contains("not registered on WhatsApp", StringComparison.OrdinalIgnoreCase) => "Alıcı numarası geçersiz veya WhatsApp kullanmıyor. Müşteri telefonunu kontrol edin.",
        var e when e.Contains("NETWORK_TIMEOUT") || e.Contains("WORKER_TRANSIENT") => "WhatsApp servisine erişilemiyor. Bağlantıyı kontrol edin; sonraki deneme zamanı kuyrukta gösterilir.",
        var e when e.Contains("lock expired", StringComparison.OrdinalIgnoreCase) || e.Contains("before the final delivery", StringComparison.OrdinalIgnoreCase) => "Teslim sonucu doğrulanamadı. Çift mesajı önlemek için WhatsApp telefonundan kontrol etmeden tekrar göndermeyin.",
        _ => error
    };
}
