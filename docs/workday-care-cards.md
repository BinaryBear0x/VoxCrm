# Bugün yapılacaklar, bakım kartı ve kapanış asistanı

Yalnız yerel geliştirme. Başlangıç: klinik hesabıyla `/Workday`. Ana panelden ve yan menüden erişilir. Muayene detayındaki **Bakım kartı / Hesap kontrolü** bağlantısı `/Workday/Care/{examinationId}` ekranını açar.

## Günlük işler

- Kliniğin saat diliminde gün seçimi, o günkü randevular, son 30 gündeki vadesi gelmiş aktif aşılar ve açık/geciken görevler.
- Görev ekleme/düzenleme, sorumlu seçme, hayvan ilişkilendirme, öncelik ve tarih; tamamlama, yeniden açma ve iptal.
- Görevler kaybolmadan geçmiş gününden incelenebilir. Aynı formun tekrar gönderimi idempotenttir; revision kontrolü eski ekranın yeni kaydı ezmesini önler. Klinikler arası hasta/personel ataması engellenir.
- Bu ekran aşı mesajı veya tahsilat bildirimi göndermez.

## Bakım kartı

Muayene başına bir kart: bugün yapılanlar, evde uygulanacak adımlar, isteğe bağlı kontrol tarihi ve kliniği arama talimatları. İç muayene notları kendiliğinden kopyalanmaz.

Taslağı klinik personeli hazırlayabilir. Paylaşım için `ApplicationUser.CanApproveCareCards` yetkisi ve açık veteriner onay kutusu gerekir. Bayi, kendi kliniğinin kullanıcısı için **Kullanıcılar / Güvenlik → Veteriner bakım kartı onay yetkisi ver/kaldır** işlemini bayi parolası ve gerekçeyle yapar. Mevcut hesaplara kendiliğinden onay yetkisi verilmez.

Onay 256 bit rastgele, 14 günlük bir özel bağlantı üretir. Veritabanında tokenın yalnız SHA-256 özeti tutulur; bağlantı onay ekranında bir kez gösterilir. Yeni bağlantı eskisini kapatır. Taslağı düzenlemek onayı kaldırıp eski bağlantıyı kapatır. Klinik ayrıca bağlantıyı iptal edebilir. Aktif müşteri/hayvan/birincil sahip bağlantısı, alıcı snapshotı ve onaylayan hesabın yetkisi her okumada kontrol edilir. Kart anonim olarak yalnız paylaşılmış içeriği, hayvan adını ve klinik iletişimini gösterir; finans ve iç muayene notları görünmez. Cache ve indeksleme kapalıdır; uygulama audit loglarında ham özel bağlantı maskelenir.

Hasta sahibi “Uyguladım” yanıtı verebilir; bu yeni görev oluşturmaz. “Klinikle görüşmek istiyorum” yanıtı bugüne öncelikli görev oluşturur. Tekrarlanan aynı yanıt ikinci görev oluşturmaz. Kontrol tarihi onaylandığında ayrıca o tarihe kontrol görevi eklenir; randevu kendiliğinden oluşturulmaz. Anonim POST antiforgery ve hız sınırıyla korunur; yazma kapsamı yalnız geçerli tokenın doğruladığı kliniğe bağlanır.

Hiçbir bağlantı otomatik WhatsApp/e-posta göndermez. `127.0.0.1` bağlantısı yalnız yerel bilgisayarda açılır; gerçek mobil paylaşım için ileride yetkili bir yayın ortamı gerekir.

## Kapanış asistanı

Somut veri kuralları:

- Hesap kontrolü yapılmamış muayene; “borcu var” iddiasında bulunmaz.
- Muayenede boş değerlendirme/plan alanı.
- Tamamlandı durumundaki randevunun aktif muayene bağlantısının bulunmaması.
- Onaylı bakım kartındaki bugünkü kontrol tarihi için randevu bulunmaması.
- Tamamlanmamış/gecikmiş görevler.
- Aynı hayvan/aşı/uygulama tarihi için olası mükerrer aktif aşı kayıtları.

Uyarılar kaynak kayda bağlanır. Kullanıcı gerekçeli kontrol notu bırakabilir; bu not uyarıyı çözülmüş saymaz. Kaynak kayıt düzeltildiğinde uyarı kalkar. Stok tüketimi ilişkisi bulunmadığı için olmayan bir stok/maliyet kontrolü varmış gibi gösterilmez.

Muayene hesabı mevcut, aynı hayvan/aktif sahibine ait bir finans hesabına ilişkilendirilebilir veya gerekçesiyle ücretsiz/kapsam içi kontrol olarak işaretlenebilir. Borç/ödeme oluşturulmaz. Muayene düzenlenirse hesap kontrolü yeniden istenir.

## Veri ve doğrulama

Migration `20261030101127_AddClinicWorkdayAndCareCards` yeni tablolar/alanlar ekler. Görev/bakım metinleri ve hesap kontrol notları mevcut PII şifrelemesine dahildir. İşlemler aktör ve kayıt kimliğiyle audit edilir.

Mevcut 83 testlik entegrasyon paketi geçti. Sonradan eklenen şifreleme testiyle beraber `WorkdayIntegrationTests` içindeki 8 test ayrıca geçti: görev yaşam döngüsü/idempotency, tenant/aktör sınırları, veteriner onayı, içerik gizliliği, yanıt görevleri, bağlantı yaşam döngüsü, kapanış kuralları, gerçek HTTP/antiforgery ve şifrelenmiş veri okuma. Testler ayrı rastgele adlı yerel veritabanında çalışır; gerçek mesaj gönderilmez.
