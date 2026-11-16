# Borç ve Tahsilat — yerel geliştirme

Başlatma: proje kökünde `./Start-Local.ps1`. URL: http://127.0.0.1:5114/Finance

Yerel veritabanı: localhost:55432 / voxcrm_snapshot_20261029. Şema geçişi: RebuildFinanceAccounts. Yerel geçiş öncesi yedek, proje dışındaki VoxCRM-local-private/before-finance-20261030.dump dosyasıdır.

## Kullanım

- Yeni borç kaydı: kayıtlı müşteri ve isteğe bağlı hasta seçin veya manuel isim girin.
- Bir kaydın borcunu artırmak için detayındaki Borca ekle düğmesini kullanın.
- Borç kalemleri, taksitler ve gerçekleşen ödemeler ayrı eklenir, düzenlenir ve silinir.
- Taksitlere bölme yalnızca henüz planlanmamış bakiyeyi aylara böler. Kuruş farkı son taksite eklenir.
- Ödemede ilgili taksiti seçin; taksit dışı ödemeler plansız bakiyeden düşer. Gelecek tarihli ödeme yerine taksit kullanılır.
- Ödenmiş taksit silinirse ödeme korunur ve taksit bağlantısı kaldırılır.
- Silinen kayıtlar normal listeden kalkar; Silinenler dahil filtresiyle işlem geçmişine erişilir.
- Her değişiklik kim, ne zaman, önce ve sonra bilgileriyle aynı veritabanı işlemi içinde kaydedilir. Genel sistem loguna da özet yazılır.
- Ana panel ve hasta finans sekmesi yeni tablolardan kalan tutarı hesaplar. Hasta sekmesinde o hastaya bağlı kayıtlar ile sahiplerin hasta seçilmemiş genel hesapları bulunur.

Finans servisinde WhatsApp istemcisi, bildirim oluşturma veya mesaj işi yoktur. Yerel başlatıcı zamanlanmış işleri ve demo veri eklemeyi kapatır. WhatsApp gateway başlatılmaz.

Eski Borçlar/Payments tabloları yalnızca eski verilerin kaybolmaması için korunur; yeni arayüz ve servis bunlara yazmaz. Yerel yedekte yalnız iptal edilmiş eski borç bulunduğundan yeni sisteme aktif hesap aktarılmadı.

Testler: VOXCRM_TEST_ADMIN_CONNECTION yerel PostgreSQL bağlantısı verilerek `dotnet test VoxCrm.IntegrationTests --filter FullyQualifiedName~FinanceIntegrationTests`. Test fixture ayrı rastgele isimli veritabanı oluşturur ve yalnızca bu test veritabanını temizler.
