# WhatsApp sağlamlaştırma yayını — kontrol listesi

Kapsam: WhatsApp kuyruğu, gateway, worker, web ekranı ve günlük hatırlatma işi. Bu yayında **yeni migration yoktur** ve hiçbir entity/tablo değişmedi
(`dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration").

## 0. Önce kontrol et (canlıya salt-okunur bakış)

1. **Canlı DB hangi migration'da?** Önceki değerlendirmeye göre 20261030–20261031 arası 9 migration ve 31 Ekim düzeltmeleri sunucuya henüz uygulanmamıştı.

   ```sql
   SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 3;
   ```

   En sonuncu `20261031121507_PersistExcelImportBatches` değilse bu yayın o migration'ları da getirir; `docs/db-upgrade-assessment-2026-10-31.md` adımlarını izle.
2. **Gönderim hızı ayarı:** `.env` / `production.env` içinde `WHATSAPP_SEND_INTERVAL_SECONDS` + `WHATSAPP_SEND_JITTER_SECONDS` + 120 sn değeri **600'ü aşmamalı**
   (varsayılan 60 + 15 uygundur). Aşarsa gateway artık açılışta bilerek hata verir.
3. **Üç parça birlikte yayınlanır:** web + api (.NET), `gateway-api` (Python), `wa-worker` (Node). Gateway'in yeni 422/502 eşlemesi ile worker'ın yeni karantina kuralı birbirine bağlıdır.

## 1. Yayın sırası

1. `deploy/scripts/backup-production.sh` ile yedek al ve `verify-latest-backup.sh` ile doğrula.
2. Migration gerekiyorsa (bölüm 0.1): `docker compose run --rm migrate-crm` ve `migrate-gateway`.
3. `docker compose up -d --no-build --wait web api gateway-api wa-worker`
4. `/healthz` kontrolü, ardından nginx.

## 2. Yayın sonrası duman testi (10 dakika)

| Kontrol | Beklenen |
|---|---|
| Klinik hesabıyla `/WhatsApp` | Sayfa açılır; "Gönderim izinleri" içinde **"WhatsApp gönderimini etkinleştir" kutusu görünmez** |
| Klinik hesabıyla otomatik/manuel kutusunu kapat-aç | Kaydedilir, sayfa 403 vermez |
| **Bayi** hesabıyla kuyruktaki bir mesajı iptal et | Başarılı (eskiden hata veriyordu) |
| Bayi hesabıyla ayar kaydet | Başarılı |
| Test numarasından "DUR" yaz | Müşterinin WhatsApp izni kapanır, bekleyen mesajlar `Cancelled` olur; audit'te `WhatsAppInbound.OptOut` |
| Bilinmeyen numaradan "DUR" yaz | Audit'te `WhatsAppInbound.OptOutUnmatched` (uyarı) |
| Tek bir test hatırlatması gönder | `Sent`; çift gönderim yok |
| Gateway'i kısa süre durdur | Sayfa "WhatsApp servisine erişilemiyor" der, 500 vermez |

## 3. İzlenecekler (ilk 48 saat)

- Audit: `WhatsAppInbound.OptOutUnmatched` (eşleşmeyen durdurma talebi — elle bakılmalı), `WhatsAppNotifications.RecoverExpiredProcessing` sıklığı.
- Kuyrukta `NeedsReview` sayısı. Yeni kod, kilidi tekrar tekrar dolan mesajı 3. denemeden sonra `NeedsReview`'a alır (çift gönderimi önlemek için).
- `wa-worker` loglarında `Session restore failed` (okunamayan oturum → o klinik `auth_failed` olur, "bağlantıyı kes → yeniden bağla").
- Worker `rejected/` klasörü: `pending-inbound/<klinik>/rejected/` altında dosya birikiyorsa gateway'e reddedilen mesaj var demektir.
- Hangfire günlük iş: bir kayıt işlenemezse iş **sonunda** hata verir (diğer kayıtlar işlenmiş olur); bu artık normal bir uyarıdır.

## 4. Davranış değişiklikleri (bilerek)

- Klinik kullanıcısı artık WhatsApp modülünü kendisi kapatamaz (bayi izni); sadece otomatik/manuel gönderimi kapatır.
- "DUR", "STOP", "DURDUR", "UNSUBSCRIBE", "Abonelikten çık", "Listeden çık", "Mesaj istemiyorum", "Mesaj gönderme" mesajları (tam eşleşme) onayı kapatır. Müşteriye otomatik cevap gitmez.
- Audit loglarına artık müşteri mesajı ve tam numara yazılmaz. **Eski kayıtlar düz metin olarak durur** (365 gün saklanır); gerekirse ayrıca temizlenmeli.
- Hatırlatma metninde boş soyad / çift boşluk düzeltildi. Bekleyen hatırlatmaların metni bir sonraki günlük çalışmada yenilenir.
- `00` ile başlayan numaralar (`0090 555…`) artık `+90 555…` ile aynı sayılır. Bu numaraların bekleyen mesajları bir kerelik `NeedsReview`'a düşebilir.
- Worker 10 haneli her numarayı CRM ile aynı şekilde `90` önekiyle adresler.

## 5. Geri alma

Veritabanı şeması değişmediği için önceki imajlara dönmek güvenlidir. Kalıcı olarak kalacak etkiler:
DUR ile kapanan WhatsApp izinleri, iptal edilen bekleyen mesajlar, `NeedsReview`'a alınan mesajlar. Bunlar kasıtlı veri değişiklikleridir; geri dönüşte otomatik geri alınmaz.
Şema/veri bozulursa tek yol, yayın öncesi yedekten geri yüklemedir (iki kişi onayı — `PRODUCTION_RUNBOOK.md`).

## 6. Bilinen açık konular (bu yayına dahil değil)

- `IsWhatsAppEnabled` hem bayi izni hem klinik anahtarı; kalıcı çözüm için ayrı bir sütun (migration) gerekir.
- Varsayılan hatırlatma şablonunda sabit "Veteriner Hekim Ebru Hanım" metni var (test bunu bekliyor).
- Şablon düzenleme formu arayüzden kalkmış; `SaveTemplate` ucu duruyor.
- `DirectSending` / `StartImmediate…` metotları web'den çağrılmıyor (testler kullanıyor).
- Gateway'in tek ortak HS256 sırrı ve bellek içi jti koruması; teslim/okundu durumları izlenmiyor.
- Prova PostgreSQL 18'de yapıldı, canlı 16: bu yayın şema değiştirmediği için etkisi yok, ama sonraki migration'larda PG16 üzerinde prova gerekir.

## 7. Sonradan eklenenler (elle test sonrası)

- Şablon formu geri geldi (klinik ve bayi), "Otomatik hatırlatma özeti" kartı, `NeedsReview` için "Gözden geçir → yeniden sıraya al / iptal".
- Müşterinin WhatsApp izni kapatılınca bekleyen mesajları **anında** iptal edilir (eskiden günlük işi bekliyordu) ve toast ile bildirilir.
- Alıcı "parmak izi" (`RecipientSnapshot`) artık due date'i takvim günü olarak hesaplar; eski biçimde yazılmış parmak izleri geçerli kalır. Bu olmadan, düzenleme sonrası doğru mesajlar yanlışlıkla `NeedsReview`'a düşüyordu.
- Tarayıcı tarafı olaylar (bağlantı kuruldu/koptu, QR hazır, durum alınamıyor) toast ile bildirilir; sunucu mesajları zaten toast idi.

**Yayın öncesi ek kontrol:** canlı PostgreSQL'de `SHOW timezone;` sonucu `UTC` olmalı. Web uygulaması eski Npgsql zaman damgası davranışını kullanıyor; sunucu saat dilimi UTC değilse tüm zamanlar kayar (yerel testte Europe/Istanbul PG ile 3 saatlik kayma görüldü). Canlı Docker PG varsayılan olarak UTC'dir, ama doğrulayın.

## Ek (2026-11-11): Görevler / Kapanış asistanı modül anahtarı

- `Clinics.EnabledModules` bit 4 = "Görevler ve Kapanış Asistanı" (bayi panelinde Klinik modülleri sayfasında açılır/kapanır).
- Migration `20261111103819_AddWorkdayModuleSwitch`: varsayılan 3 → 7 ve **mevcut tüm kliniklerde bit 4 açılır** (davranış değişmez). Deploy sonrası `SELECT "EnabledModules", count(*) FROM "Clinics" GROUP BY 1;` ile doğrulayın.
- Kapalıyken: Bugün ekranında Yapılacaklar, Kapanış asistanı, Bekleyen işler, Görev ekle, Geri Arama Talepleri; menüdeki Görevler / Kapanış, zil ikonu ve `/Workday` sayfaları (403) gizlenir/kapanır. Geri Arama Talepleri için Hasta Sahibi Takibi **ve** Görevler birlikte açık olmalı.
- WhatsApp modülü kapalıyken "WhatsApp mesajı hazırla" hızlı işlemi de gizlenir.
- Bayi panelinden yeni açılan kliniklerde Görevler/Kapanış varsayılan açık (4), diğer modüller kapalı.
