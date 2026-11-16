# Bayi klinik kullanıcı yönetimi

Yerel adres: `/Dealer` → **Kullanıcılar / Güvenlik**. Düzenleme ekranından ve mobil klinik listesinden de erişilir.

Bayi yalnız kendisine ait kliniklerin `Clinic` rolündeki kullanıcılarını yönetir. `Dealer` veya `SystemAdmin` rolü de bulunan kullanıcılar bu ekranda gösterilmez ve işlem uç noktalarından yönetilemez. Bayiye klinik rolü, klinik adına giriş veya hasta/tahsilat verilerine erişim verilmez.

İşlemler:

- Yeni klinik kullanıcısı ve 24 saatlik tek kullanımlık aktivasyon bağlantısı.
- Ad, soyad ve giriş e-postası düzenleme. Giriş e-postası klinik iletişim e-postasından ayrıdır.
- 12–128 karakterlik geçici parola. İlk girişte parola değişikliği zorunludur.
- Hatalı giriş kilidini açma, hesabı askıya alma/yeniden açma.
- Tüm oturumları kapatma.
- MFA anahtarını ve kurtarma kodlarını sıfırlama.
- Henüz parola belirlememiş kullanıcının aktivasyon bağlantısını yenileme; önceki bağlantı geçersizleşir.

Her kullanıcı yazma işlemi antiforgery doğrulaması, kimliği doğrulanmış bayiyle veritabanı sahiplik kontrolü, yeniden bayi parolası doğrulaması, zorunlu gerekçe ve authentication hız sınırı içerir. Başarılı işlemler kullanıcı, aktör, klinik ve gerekçesiyle `SystemAuditLogs` içinde `ClinicAccess` kategorisinde, hesap değişikliğiyle aynı transaction içinde kaydedilir. Parolalar ve aktivasyon tokenları loga konmaz. İşlemler güvenlik damgasını yenileyerek eski oturum/tokenları iptal eder. Bağlantılar ve parola alanları cache edilmez; otomatik e-posta/WhatsApp gönderimi yapılmaz.

`LoginDisabled` alanı hesabın askıya alınmasını klinik pasifliğinden ayırır. Klinik yeniden aktifleştirildiğinde askıdaki kullanıcılar açılmaz. Pasif klinikte yeni kullanıcı, aktivasyon, geçici parola veya hesap açma yapılamaz. Klinik yaşam döngüsü ve kullanıcı yönetimi aynı klinik satır kilidini kullanır.

Migration: `20261030091556_AddClinicUserAccessControls`, yalnız `AspNetUsers.LoginDisabled` alanını false varsayılanıyla ekler. Yerel snapshot üzerinde önceden yedek alınıp uygulandı; mevcut hesap parolaları değiştirilmedi.

Doğrulama: `ClinicAccessIntegrationTests` içinde 7 test; sahiplik/rol sınırı, parola ve token iptali, askı/klinik yaşam döngüsü, MFA/kurtarma kodları, aktivasyon, benzersiz giriş adresi ve rollback, gerçek HTTP giriş/antiforgery/oturum iptali/zorunlu parola değişikliği. Tüm entegrasyon paketi: 65 başarılı test.
