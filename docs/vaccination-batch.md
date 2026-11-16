# Çoklu aşı ve uygulama kaydı

Yerel ekrandan `/Vaccination/Create`: hasta ve ortak yapılma tarihi seçilir; aynı ziyarette gerçekten yapılan uygulamalar işaretlenir. Her satırın isteğe bağlı parti/seri kodu vardır. Sonraki tarih, uygulama tanımındaki gün aralığından hesaplanır ve kaydetmeden önce gösterilir.

Her uygulama ayrı geçmiş kaydı olur. Kayıtlar ve denetim logları tek PostgreSQL işlemindedir; hatırlatma mutabakatı da aynı işleme katılır. Kayıt tamamlanamazsa tüm yeni satırlar geri alınır. Form kimliği ve içerik özeti tekrar gönderimde aynı kayıtları döndürür. Aynı hasta, tür ve güne ikinci bir formdan yinelenen kayıt da engellenir.

`/Vaccination` güncel beklentileri; `?history=true` bütün uygulama geçmişini gösterir. Aynı hastanın **aynı VaccineTypeId** için daha sonraki tarihli aktif uygulaması varsa eski satır “Yenilendi” görünür. Eski yapılma tarihi değiştirilmez ve kayıt silinmez. Farklı tanımlanmış dozlar/türler birbirini kapatmaz; bu özellik tıbbi doz eşleştirmesi yapmaz. Son uygulama arşivlenirse gündem kalan aktif kayıtlardan yeniden hesaplanır.

Yenilenmiş uygulamalar günlük gündeme alınmaz ve eski hatırlatma gönderimine izin verilmez. Toplu kayıtta önceki gönderilmemiş hatırlatmalar iptal edilir; gönderilmiş mesaj geçmişi korunur. Form doğrudan mesaj göndermez; otomatik hatırlatmalar ayrıca etkinleştirilmiş klinik izinlerine bağlıdır.

Doğrulama: ayrı yerel test veritabanında çoklu form POST, tekrar gönderim, eşzamanlı istek, geçersiz seçim, işlem sonrası hata ile tam geri alma, geçmişi koruma ve arşiv sonrası gündemin açılması senaryoları. Testler gerçek mesaj göndermez.
