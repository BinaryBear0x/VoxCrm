# Deneme hesabıyla kullanıcı testi — 30.10.2026

Deneme kliniğinin yerel yedekten türetilmiş izole `voxcrm_uiqa_20261030` kopyası, `localhost:5116` üzerinde kullanıldı. Asıl yerel veritabanındaki deneme hesabının parolası değiştirilmedi. Test kopyasının giriş parolası değiştirildi; sahte QA hastaları ve uygulama türleri yalnız bu kopyaya eklendi. Otomatik işler ve WhatsApp izinleri kapalıydı. Uzak sunucuya erişilmedi, mesaj gönderilmedi.

Tarayıcıda sınananlar:

- Uygulama seçilmezse kaydet düğmesi kapalı.
- Hasta seçmeden, yalnız arama metni yazarak veya mevcut seçimi değiştirip listeden tekrar seçmeden kayıt engelleniyor.
- Aynı isimli iki Burak, soyadı/telefon/hayvan bilgileriyle ayırt ediliyor; telefon araması doğru hastayı getiriyor.
- Boş tarih tarayıcıda, gelecek tarih sunucuda engelleniyor. Hata sonrası hasta, seçimler ve kodlar korunuyor.
- 200 karakteri aşan kod sunucuda reddediliyor. Türkçe uyarı tekrar doğrulandı.
- Üç ayrı uygulama tek kaydetmeyle oluşuyor; ayrı kod ve sonraki tarihleri korunuyor. HTML işaretleri ve özel karakterler düz metin gösteriliyor.
- Kaydetme sonrası sayfa yenilemek kayıt sayısını artırmıyor.
- İki satırlık girişin bir satırı mükerrerse diğer satır da eklenmiyor.
- Sonraki tarihli iç/dış parazit uygulaması eski iki beklentiyi “Yenilendi” yapıyor; ayrı tür kimliğindeki ikinci doz korunuyor.
- Son uygulamayı arşivleme eski beklentiyi gündeme geri getiriyor. Geri alma ve düzenleme ekranı denendi.

Bulunan ve düzeltilen hatalar:

1. Yeni formda hata yokken boş kırmızı doğrulama kutusu görünüyordu.
2. Uzun kod doğrulaması İngilizceydi.
3. Başarı bildirimi hem sayfada hem genel bildirimde tekrarlanıyordu.
4. Arşivleme değişmemiş tarihleri yeniden UTC dönüşümüne sokarak uygulama/sonraki tarihini kaydırabiliyordu. Ortak DbContext yalnız eklenen veya gerçekten değiştirilen tarih alanlarını normalleştiriyor. Gerçek HTTP testine arşivleme ve geri alma öncesi/sonrası klinik tarihler ile oluşturma zamanının değişmediği doğrulaması eklendi. Tarayıcıda geri alma sonrası tarih korunması da doğrulandı.

Arşiv onayının “silme” ifadesi de geri alınabilir arşivleme olarak düzeltildi. Tarayıcı otomasyonunda onay diyaloğu zaman zaman takıldı; başarılı işlemler ekran ve veritabanıyla ayrıca doğrulandı. Son arşiv denemesinde onay uygulanmadı; bunu başarılı saymadık.

Son doğrulama: 92/92 entegrasyon testi geçti. Ana yerel uygulama HTTP 200 dönüyor. Asıl yerel veride 147 hasta, 171 WhatsApp bildirimi ve sıfır toplu test kaydı var. Tarih düzeltmesi eski veriye toplu güncelleme uygulamaz.

Ekran: `vaccination-user-test.png`.
