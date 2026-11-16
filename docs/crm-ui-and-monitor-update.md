# Yerel CRM arayüzü ve WhatsApp bildirim düzeltmesi

## Gerçek uygulama

Adres `http://127.0.0.1:5114`. Geçici `5115` görsel kontrol sunucusu kapatıldı; o adresteki sentetik sayfa tam uygulama değildi ve alt sayfa bağlantıları 404 veriyordu. Görsel kontrol için kullanılan sekme kapatılıp gerçek bayi sekmesi yenilendi.

Klinik ana sayfası 1 ve 3 numaralı tasarımların birleşimini kullanır: koyu menü, açık yüzeyler, günlük sayaçlar, hafta/gün seçimi, gerçek randevu tablosu, arama/durum/işlem filtreleri, kompakt görünüm, yapılacaklar, bakım kartı görüşme talepleri, hızlı işlemler ve kalıcı klinik notu. Klinik ve bayi sayfaları ortak modern temayı kullanır. Stok modülü bulunmadığı için bağlantısı “Yakında” olarak pasiftir.

Klinik notu `Clinics.DashboardNote` alanında mevcut PII korumasıyla şifrelenir; revision ve audit kontrolü vardır. Migration `20261030103519_AddClinicDashboardNote` yerelde yedek alınarak uygulandı. Gerçek veriye örnek kayıt eklenmedi. Şifreli telefon ve tam sahip adı araması, yalnız ilgili klinik kayıtları materialize/decrypt edildikten sonra eşleşir.

İkon fontları uygulamanın kendi `/lib/bootstrap-icons/` dosyalarından yüklenir. Görsel kontroldeki farklı porttan font çağrısı düzeltilmiştir; uygulamanın CORS veya CSP koruması gevşetilmemiştir. Logo arka planındaki eski CSS `!important` çakışması da giderilmiştir.

## Telegram / cihaz bağlantısı

Kaynak değişiklikleri:

- `deploy/scripts/monitor.sh` WhatsApp bölümünü `monitor-whatsapp.sh` üzerinden çalıştırır.
- Yeni betik klinik bazında cihaz durumuna bakar. Bağlantı kapalıysa retry/gecikme uyarıları göndermez; ilk kopmada bir kez cihaz bağlantısı uyarısı verir.
- Kopma `disconnected → qr → connecting` gibi alt durumlara geçse de aynı kesinti için yeni uyarı verilmez. Bağlantı tekrar hazır olduğunda bir kez bilgi mesajı verilir.
- Gateway/worker durumu doğrulanamıyorsa bunu gerçek cihaz kopmasından ayrı ifade eder ve kuyruk uyarılarını yine susturur.
- Açık bağlantıda aynı retry sayısı veya artmaya devam eden aynı gecikme için her cron çalışmasında tekrar mesaj üretilmez. Kuyruk normale döndükten sonra yeni problem yeni uyarı üretebilir.
- Geleceğe planlanmış retry, gecikmiş sayılmaz. Yeniden bağlantıdan sonra offline geçen süre anında kuyruk arızası sayılmaz; eşik kadar izleme süresi verilir.
- Durum `/var/lib/voxcrm/monitor` altında, `MONITOR_STATE_DIR` ile değiştirilebilir kalıcı dosyalarda tutulur. `flock` örtüşen cron çalıştırmalarını engeller. Durum dizini her çalışmada silinmemelidir.
- Gateway `sender.py`, ekran açık olmasa da worker durumunu her poll turunda kontrol edip heartbeat günceller. Kopmayı / yeniden bağlanmayı durum değişiminde audit eder; offline kliniğin kuyruğunu almaz. İki dakikadan eski ready kaydı monitor tarafından doğrulanamayan durum sayılır.

**Bu değişiklikler üretime aktarılmadı.** Sunucuya dokunulmadı ve Telegram/WhatsApp test mesajı gönderilmedi. Canlı Telegram davranışının değişmesi için monitor betiklerinin ve gateway durum güncellemesinin yetkili dağıtımda alınması gerekir. Gateway'in önceki alıcı güvenliği değişiklikleriyle birlikte dağıtımında API/Web uyumu da korunmalıdır; bkz. `whatsapp-safety-local.md`.

Doğrulama: 86 .NET entegrasyon testi, 20 gateway Python testi ve Docker/Telegram tamamen sahte olan 4 monitor geçiş testi başarılı. Bash ve JavaScript sözdizimi kontrolleri geçti; yerel uygulama `/healthz` 200 döndü.
