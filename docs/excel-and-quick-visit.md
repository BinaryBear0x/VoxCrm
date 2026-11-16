# Excel aktarım ve hızlı ziyaret — yerel ilk sürüm

## Menü

Klinik hesabıyla giriş yaptıktan sonra **Excel Aktarım** (/ExcelTransfer) ve **Hızlı Ziyaret** (/QuickVisit) menüleri görünür. Hasta listesinden Excel aktarımına, hasta detayından hasta seçili hızlı ziyarete geçilebilir.

## Excel

- .xlsx, ilk sayfa, ilk satır başlık. 5 MB, 1000 veri satırı, 20 sütun sınırı. Eski .xls önce Excel üzerinden .xlsx kaydedilmelidir.
- Boş şablon indirme; kendi sütunlarını eşleştirme; önizleme; satırların ayrı ayrı seçilerek onaylanması.
- Sahip adı/soyadı/telefon, hasta adı/tür/ırk/doğum tarihi/çip/not alanları. Sahip adı, telefon, hasta adı, tür zorunludur.
- Aynı isim otomatik birleştirilmez. Aynı ad+telefonlu mevcut aktif sahip, önizlemede belirtilerek yalnız seçilen satırlarda yeniden kullanılır. Aynı telefon farklı isim/çoklu kayıt ile eşleşirse aktarım engellenir.
- Çip ve aynı sahip/hasta eşleşmesi, dosya içi tekrarlar, belirsiz tarihler kontrol edilir. Formül, makro, harici bağlantı, gömülü dosya ve aşırı açılmış ZIP boyutu reddedilir.
- Önizleme 30 dakika geçerlidir ve klinik/kullanıcıya bağlıdır. Yeni dosya önceki önizlemeyi kapatır. Onayda kayıtlar yeniden kontrol edilir ve tek transaction kullanılır; tekrar onay aynı aktarımı çoğaltmaz.
- Yeni sahipte WhatsApp izni daima kapalıdır; aktarım mesaj üretmez.
- Aktarım geçmişi audit kayıtlarında tutulur. Geri alma yalnız bu aktarımın oluşturduğu ve sonradan değişmemiş/işlem görmemiş kayıtları arşivler. Mevcut sahipler korunur. Sonradan sahiplik, muayene, aşı, finans, görev veya takip bağlantısı oluştuğunda geri alma durdurulur.
- Dışa aktarım aktif hastalar ve aktif sahiplerini taşır. Bir hastanın birden fazla sahibi varsa ayrı satırlar üretilir. Sahipsiz hasta da dışa alınır; içe aktarmada sahip bilgisi tamamlanmalıdır. Telefon/çip/formül görünümlü notlar metin olarak yazılır.
- Finans, aşı geçmişi, belgeler, fotoğraflar bu ilk aktarımın kapsamında değildir. Excel çıktısı tam sistem yedeği değildir.

## Söyle, kontrol et, ziyareti tamamla

Hasta ve aktif sahibi seçilir. Türkçe tarayıcı ses tanıma desteği varsa, kullanıcı ses işleme bilgisini onaylayarak mikrofonu başlatabilir. Mikrofon izni reddinde, tarayıcı desteklemediğinde veya bağlantı hatasında yazıyla devam edilir. Tarayıcının ses hizmeti dış servis kullanabilir; uygulamaya AI API anahtarı eklenmedi.

İlk sürüm kural tabanlı sayısal taslaktır; serbest metni anlayan bir tıbbi AI değildir. Örnek: **4 kilo 200 gram. Toplam 1500 lira. 1000 lira nakit ödendi.** Kilo, toplam ve tahsilat alanları önerilir. Yazıyla sayı, doğal dilde tarih, ilaç/doz veya yapılan aşı tahmin edilmez. Ödeme yöntemi, tarihler ve gerçekten yapılan uygulamalar kullanıcı tarafından seçilir. Alanlar düzenlenebilir.

Son kontrolde hasta/sahip/telefon, not, kilo, uygulamalar, ödeme ve kalan bakiye birlikte gösterilir. Önizleme veritabanına kayıt yapmaz. Taslak 20 dakika geçerlidir, kullanıcı/klinik kapsamındadır ve yeni önizleme önceki taslağı kapatır. Kayıt token'ı HTTP katmanında sunucu tarafından üretilir.

Onayla birlikte muayene, seçili çoklu aşı uygulamaları, isteğe bağlı finans hesabı/tahsilat ve bakım kartı **tek transaction** ile kaydedilir. Bir adım başarısızsa tamamı geri alınır; aynı onay token'ı tekrar gönderildiğinde çoğaltılmaz. Bakım kartı taslaktır, ayrıca onaylanıp paylaşılır. Finans WhatsApp'a bağlanmaz. Aşılar mevcut yenileme ve otomatik hatırlatma ayarlarına uyar; bu ekran doğrudan mesaj göndermez.

## Doğrulama

- Excel ve ziyaret için 10 yeni entegrasyon testi: gerçek login/form/upload/map/preview/confirm/ziyaret commit akışı; CSRF; kullanıcı/klinik izolasyonu; mükerrer ve formül kontrolü; metin olarak export; güvenli geri alma; idempotency; başarısız ziyarette tam rollback; hatalı finans ve değişmiş alıcı kontrolü.
- Tam .NET regresyon sonucu: 123 test. Log: excel-visit-full-tests.log.
- ClosedXML 0.105.1 eklendikten sonra NuGet doğrudan/geçişli açık taraması: 0 bilinen advisory. docs/security/excel-visit-dotnet-audit.json.
- JavaScript sözdizimi node --check ile doğrulandı. Fiziksel mikrofonla konuşma testi yapılmadı; ses tanıma doğruluğu ve desteği tarayıcıya bağlıdır.
- Yeni migration yok; mevcut audit tabloları kullanılır. Üretime uygulanmadı, sunucuya bağlanılmadı ve gerçek mesaj gönderilmedi.

Son kontrol: 123/123 tam regresyon, ardından uyarı bileşeni eklendikten sonra 10/10 özellik testi geçti. Yerel giriş HTTP 200; ana kopyada 147 hasta ve 171 WhatsApp bildirimi korundu.
