# WhatsApp alıcı güvenliği ve kuyruk yönetimi

Yerel ekran: `/WhatsApp`. Bağlantı, izinler, manuel mesaj hazırlama ve yönetilebilir kuyruk ayrı bölümlerdir. Eski gelen/giden mesaj log tabloları ekrandan kaldırılmıştır; teslim/idempotency kayıtları ve güvenlik audit verileri silinmez.

## Gönderim akışı

- Manuel mesajda müşteri adı/soyadı/telefonu ve varsa hayvan seçilir. Önizlemeden önce bildirim kaydı oluşturulmaz. Önizleme 10 dakikalık Data Protection tokenıyla korunur. Onay aynı tokenla tekrarlandığında ikinci bildirim oluşturulmaz.
- Aşı ekranındaki gönderme/yeniden deneme işlemleri alıcı önizlemesine yönlenir; doğrudan ağ gönderimi yapmaz. Mevcut bekleyen/gönderilmiş/belirsiz bildirim varsa yeni kopya oluşturulmaz.
- Alıcı kimliği, adı, telefonu, izin durumu, ilgili hayvan ve aşı tarihi snapshot hashine dahil edilir. Klinik, müşteri, hayvan ve sahip bağlantısının aktifliği ve aşı için aktif birincil sahip doğrulanır.
- Claim sırasında kontrol tekrarlanır. Gateway de worker çağrısından hemen önce CRM API `/api/whatsapp/notifications/{id}/validate` üzerinden kimlik, telefon, içerik, snapshot ve işlem kilidini doğrular. Doğrulanamayan mesaj gönderilmez; `NeedsReview` durumunda sebebiyle tutulur. Bu endpoint için mevcut `whatsapp.notifications.status` gateway scope'u kullanılır.
- Aktif gönderime geçilmiş mesaj geri alınamaz. İşlenmekte olan veya teslimi belirsiz mesajlarda kullanıcıya yeniden gönderme seçeneği verilmez. Worker'ın klinik/bildirim kimliğine göre kalıcı idempotency koruması korunmuştur.
- Önceden hazırlanmış snapshot içermeyen bildirimler güvenlik kontrolünden geçmez. Alıcı doğrulanarak yeni önizleme hazırlanmalıdır; belirsiz teslimlerde önce telefondan kontrol edilmelidir.

## İzinler ve kuyruk

`IsWhatsAppEnabled` genel gönderim iznidir. `AutomaticVaccinationRemindersEnabled` otomatik aşı hatırlatması için açık rıza niteliğinde ayrı klinik ayarıdır; varsayılan kapalıdır. `ManualWhatsAppEnabled` manuel gönderimi ayrı kontrol eder. Migration mevcut kliniklerde iki yeni izni de kapalı başlatır. QR bağlamak bu izinleri kendiliğinden değiştirmez. Kapatılan türün bekleyen bildirimleri iptal edilir.

Kuyrukta alıcı/hayvan, gönderim türü, Türkçe durum, hata sebebi, deneme sayısı ve sonraki deneme zamanı gösterilir. Bekleyen mesaj iptal edilebilir. Başarısız/iptal edilmiş ve teslim kaydı olmayan mesaj, güncel alıcı kontrolü ve önizleme onayıyla yeniden sıraya alınabilir. Gönderilmiş veya teslim sonucu belirsiz mesajlar tekrar kuyruğa alınamaz. İptal edilen otomatik mesaj zamanlanmış iş tarafından kendiliğinden canlandırılmaz.

Tahsilat alanıyla WhatsApp arasında gönderim bağlantısı eklenmemiştir. Yerel `Start-Local.ps1` arka plan işlerini kapalı tutar; gateway/worker başlatılmaz.

## Doğrulama

76 .NET entegrasyon testi; gateway sender/auth için 17 Python testi geçti. Testler rastgele adlandırılmış localhost veritabanlarını ve sahte göndericileri kullanır. Gerçek WhatsApp mesajı gönderilmedi. JavaScript sözdizimi kontrolleri geçti.

Migration: `AddWhatsAppRecipientSafety`. Yalnız yeni izin/snapshot/hayvan alanları ekler; geçmiş mesaj veya müşteri kaydı silmez. Üretime aktarılırken Web, API ve gateway sürümleri birlikte güncellenmelidir; eski API doğrulama endpointini sağlamazsa yeni gateway gönderimi durdurur.
