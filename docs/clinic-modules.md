# Klinik paket ve modül kontrolü

Bayi paneli → Kliniklerim → Modüller.

Ana paket: Bugün, Hastalar, Müşteriler, Randevular, Muayeneler, Görevler/Kapanış, Tahsilat, Excel Aktarım, Aşı Takvimi, Aşı Tanımları, Hizmet Kataloğu.

Ek modüller:
- Hızlı Ziyaret.
- Hasta Sahibi Takibi / Bakım Kartları (birlikte).
- WhatsApp (mevcut klinik WhatsApp yetkisi).

Ayarlar klinik bazındadır. Bayi yalnız kendi kliniklerini yönetebilir. Kaydetme CSRF korumalıdır, sürüm kontrolü ve satır kilidi kullanır, audit kaydı oluşturur. Klinik kullanıcıları modül açamaz. Kapalı modüller menüden gizlenir, doğrudan GET/POST erişimleri 403 verir. Bakım kartı kapalıyken Workday/Care üzerindeki temel hesap kontrolü açık kalır, bakım formu gizlenir. Dış takip ve bakım bağlantıları kapalıyken 404 verir; kayıtlar silinmez ve modül tekrar açıldığında geçerli bağlantılar yeniden çalışabilir.

Hızlı ziyarette bakım kartı modülü ayrıca kontrol edilir; kapalı modül için kart oluşturulmaz. WhatsApp açılması otomatik hatırlatmayı kendiliğinden etkinleştirmez. WhatsApp kapatılması otomatik aşı hatırlatmalarını da kapatır; mevcut kayıtlar korunur. Yeniden açılınca otomatik hatırlatma tercihi ayrıca verilmelidir.

Migration: AddClinicModuleSwitches. Mevcut kliniklerde hızlı ziyaret/takip erişimi korunur (3). Yeni klinikler bu iki ek modül kapalı oluşturulur (0); bayi Modüller ekranından açar. Mevcut WhatsApp durumu korunur. Bekleyen formların ayar değişimini atlamaması için erişim her istekte veritabanından kontrol edilir; kalıcı oturum claim'ine sabitlenmez.

Testler: bayi sahipliği, yanlış sürüm, temel paket, mevcut kayıtların korunması, kapalı modülün menü/URL kontrolü, klinik hesabının bayi yetkisi alamaması, yeni klinikte sıfır modül değerinin korunması, dış takip bağlantısının kapanıp yeniden açılması. Tam regresyon çıktısı module-full-tests.log.

Yalnız yerel veritabanına uygulandı; uzak sunucuya bağlantı kurulmadı, gerçek mesaj gönderilmedi.
