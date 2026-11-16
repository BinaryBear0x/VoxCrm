# Hasta Sahibi Takibi

Klinik menüsü: `/OwnerFollowUp`. Müşteri kartındaki **Hasta sahibi takip sayfası** düğmesi aynı yönetimi müşteri seçili açar. Muayene kaydı gerekmez.

Müşteri seçilir, aktif sahiplik bağlantısı olan hayvanlar işaretlenir. Yaklaşan uygulamalar, yapılmış uygulama geçmişi, gelecek randevu tarih/saatleri ve geçerli veteriner onaylı bakım kartları ayrı ayrı açılır. İsteğe bağlı genel bakım notu klinik hesabından doğrudan paylaşılır; ayrı not onay yetkisi gerekmez. İç muayene notları, aşı parti kodları, tahsilat ve personel bilgileri paylaşılmaz.

Alıcı onayından sonra 1–30 günlük (varsayılan 14) özel bağlantı oluşur. Bağlantı yalnız oluşturulduğunda gösterilir; veritabanında yalnız SHA-256 özeti tutulur. Yenileme önceki bağlantıyı kapatır. Aynı formun tekrar gönderimi yeni bağlantı üretmez. Yönetim tablosundan bağlantı kapatılır; yeni bağlantı için müşteri ayarları yeniden açılır.

Hastanın sahiplik bağlantısı, aktifliği, müşteri adı/telefonu veya hasta adı değişirse eski bağlantı kullanılamaz. Klinik veya bayi pasifse sayfa açılmaz. Notu paylaşan hesap pasifse not gösterilmez. Bakım kartı iptal edildiğinde veya süresi dolduğunda takip sayfasından da kalkar.

Hasta sahibi birden fazla hayvanı aynı sayfada görür. Sonraki uygulama tarihi ayrı, kesinleşmiş randevu ayrı gösterilir. Aynı VaccineTypeId için daha yeni aktif uygulama varsa eski uygulama geçmişte **Yenilendi** olur; farklı doz/tür kimlikleri birbirini kapatmaz.

**Klinik beni arasın** ve **Randevu istiyorum** işlemleri mevcut ClinicTask tablosuna önemli görev olarak eklenir; takip yönetiminde, Görevler/Kapanış ve Bugün/Geri Arama Talepleri alanlarında görünür. Randevu talebi randevu oluşturmaz. Aynı bağlantı ve hayvan için aynı talep tekrar eklenmez. Otomatik mesaj veya WhatsApp gönderimi yapılmaz.

Anonim yanıtlar CSRF ve hız sınırıyla korunur. Geçerli bağlantı doğrulanmadan yazma kapsamı oluşturulmaz; kapsam yalnız ilgili kliniğe bağlanır. Token denetim loglarının yol/sorgu alanlarında maskelenir. Özel sayfalar önbelleğe ve arama motoru indeksine kapalıdır; referrer göndermez. Genel not mevcut PII şifrelemesiyle korunur.

Yerel geçiş: yedek alındı; `AddOwnerFollowUpSharing` yalnız yeni tablo/indeksler ekler. Gerçek yerel veriye örnek bağlantı veya talep eklenmedi. 98 entegrasyon testi geçti; yeni 6 test seçili kapsam, alıcı/klinik değişikliği, süre sonu, iptal/yenileme, eşzamanlı talep tekrarları, veteriner onayı, bakım kartının doğru hayvana bağlanması, gerçek HTTP/CSRF ve loglarda token gizliliğini doğrular.

Yerelde oluşturulan localhost bağlantıları başka telefondan internet üzerinden açılmaz. Uzak sunucuya dağıtım yapılmadı.

## Müşteri ekranı tasarımı

Müşteri sayfası klinik yönetiminden bağımsız, adaçayı/kremsi tonlar ve kendi SVG ikonlarıyla tasarlandı. En yakın gelecek uygulama üstte özetlenir; hasta kartları, takvim tarihleri, klinik notu, randevular ve bakım rehberi ayrı bölümlerdedir. Geçmiş uygulamalar açılır/kapanır. Klinik notu için ayrı onay yetkisi istenmez. Mobilde talepler tam genişlikte alt alta gelir; masaüstünde iletişim kartı yan sütundadır.

320, 390, 768 ve 1440 piksel genişliklerde tarayıcıyla yatay taşma kontrolü yapıldı. Telefon düğmeleri 49px, diğer genişliklerde en az 47px yüksekliğindedir. Geçmiş açılımı ve konsol hataları kontrol edildi. Altı ilgili entegrasyon testi geçti. Görsel incelemede yalnız sahte test verilerinden üretilen salt okunur önizleme kullanıldı; gerçek kişilere mesaj gönderilmedi. Ekran görüntüleri: `owner-portal-desktop.png`, `owner-portal-mobile.png`.
