# Veritabanı yükseltme provası — 31.10.2026

## Sonuç

İki gerçek veri içeren yedek, yalnız 127.0.0.1:55432 üzerindeki yeni, rastgele adlandırılmış QA veritabanlarına açıldı. Ana yerel veritabanına veya uzak sunucuya migration uygulanmadı. Mesaj/görev servisleri başlatılmadı; gerçek mesaj gönderilmedi.

| Yedek | Uygulanan migration | Eski tabloların kontrolü | Sonuç |
|---|---:|---:|---|
| Önceden indirilmiş voxcrm-db.dump | 9 | 23 tablo | Başarılı |
| Son düzeltme öncesi pre-five-fixes yedeği | 1 | 32 tablo | Başarılı |

Dağıtımda kullanılan --migrate-only uygulama yolu çalıştırıldı. Her eski tablonun eski sütunlarının tamamı için kayıt sayısı ve sıralı satır hash özeti migration öncesi/sonrası karşılaştırıldı. Eksik tablo/sütun yok; mevcut alanlarda kayıt kaybı veya içerik değişikliği yok. Yeni tablo/sütunlar beklenen ilavelerdir. Türkçe şema adları karşılaştırmada kayıpsız aktarılmıştır.

Her iki veritabanında migration ikinci kez çalıştırıldı: başarılı, eski veriler değişmedi. EF kod modeli ile son migration arasında bekleyen model farkı yok: db-model-check.log.

Kontrol edilen yedeklerde 147 hasta, 128 müşteri, 171 WhatsApp bildirimi mevcut. Eski borç tablosunda 1 kayıt var; aktif ve ödenmemiş eski borç sayısı 0. Yeni finans modülüne geçişte canlıda bu tip aktif borç oluştuysa ayrıca eşleme/taşıma gerekir; mevcut migrationlar bunu otomatik yapıyor varsayılmamalı.

Ayrıntılı şema/kayıt sayısı/hash sonuçları: db-upgrade-rehearsal.json. Yerel tekrar aracı: verify-db-upgrade.py. Aracın ilk karakter aktarım denemeleri başarısız olmuştur; bu uygulama migration hatası değildir. Son iki prova başarılıdır.

## Yayına çıkış koşulları

- Canlıdan yayın anına yakın güncel ve geri yüklenebilir yedek alınmalı; aynı prova güncel yedekte tekrarlanmalı.
- Uygulama ve migration aynı sürümden gelmeli. Yalnız DLL veya kaynak kopyalamak yeterli değildir.
- Yazma yapan eski uygulama/worker süreçleri yayın sırasında kontrollü durdurulmalı; CRM ve gateway migrationları tamamlanmalı; sonra web/API/worker birlikte uyumlu sürümle başlatılmalı.
- Compose dosyasında web/API için migrate-crm, gateway için migrate-gateway başarı bağımlılıkları mevcut. Dosya bu sırayı destekler; canlıda gerçekten hangi dağıtım yöntemi kullanıldığı bu turda doğrulanmadı.
- Mevcut PII anahtarı, bağlantı ayarları, kalıcı veri hacimleri korunmalı; yeni boş veritabanı oluşturulup ona yönlenilmemeli.
- Bu prova PostgreSQL 18 üzerinde yapıldı. Production compose PostgreSQL 16 tanımlar; PostgreSQL 16 üzerinde birebir prova bu turda yapılmadı.
- --migrate-only normal açılıştaki kullanıcı/demo seed işlemlerinden önce sonlanır. Normal uygulama açılışı migration yerine geçmez.

Bu sonuç, mevcut yedeklerle uyumu kanıtlar; canlı veritabanının 31.10.2026 anlık durumuna erişilmediği için koşulsuz üretim garantisi değildir.

Prova sonunda yalnız bu çalışmanın oluşturduğu 6 QA veritabanı temizlendi; yedekler ve sonuç raporları korundu. Ana yerel kopya korunur.
