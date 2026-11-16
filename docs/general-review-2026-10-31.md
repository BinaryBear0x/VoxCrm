# Genel mantık incelemesi — 31.10.2026

Yalnız yerel kaynak ve izole test veritabanları incelendi. Uygulama işleyişine müdahale edilmedi. Ana yerel kayıtlar ve uzak sunucu değiştirilmedi; gerçek mesaj gönderilmedi. Geçici tanılama kodları test kaynağından çıkarıldı.

## Öncelikli bulgular

### 1. İkinci sahip ekleme isteği kontrolsüz veritabanı hatasına düşüyor — yüksek

PatientService.AddOwnerAsync yeni PatientOwner oluştururken IsPrimaryOwner belirtmiyor. Entity varsayılanı true. Mevcut birincil sahip varken ikinci sahip eklenince IX_PatientOwners_ClinicID_PatientId tekillik kısıtı ihlal ediliyor. Bu normal iş akışı için anlaşılır bir doğrulama sonucu yerine DbUpdateException üretiyor. İzole testte yeniden üretildi: general-review-primary-probe.log. İlk statik incelemedeki iki birincil oluşması ihtimalini veritabanı engelliyor; doğrulanmış gerçek hata uygulamanın isteği yönetememesi.

Çözüm: ikincil sahip açıkça ikincil oluşturulmalı; birincil değiştirme ayrı, atomik işlem olmalı. Arşivden sahip bağlantısını geri açarken de aynı kural uygulanmalı. WhatsApp alıcısı birincil seçimiyle tutarlı kalmalı.

### 2. Aşı düzenleme, çoklu eklemenin mükerrer kontrolünü atlıyor — yüksek

CreateBatchAsync aynı hasta/tür/gün tekrarını reddediyor. UpdateAsync yalnız tarih ve referans doğruluyor. Eski bir kaydın tarihi başka uygulamanın tarihine taşındığında iki aktif aynı-gün uygulaması oluşabiliyor. İzole deneme sonucu: UpdateSucceeded=True; SameDayRecords=2. Kanıt: general-review-vaccine.txt ve general-review-vaccine-probe.log.

Çözüm: ekle/güncelle/geri yükle yolları ortak mükerrerlik kuralı kullanmalı; eşzamanlı işlemler hasta/tür kapsamında kilitlenmeli. Aynı gün tekrar uygulaması iş gereği gerekiyorsa açık gerekçeli ayrı akış olmalı.

### 3. Gün ve saat dilimi kuralları tutarsız — orta

Çoklu aşı klinik saat dilimine göre bugünü hesaplıyor. Aşı düzenleme DateTime.UtcNow.Date, finans özeti DateTime.Today ve bazı ödeme kontrolleri sunucu gününü kullanıyor. Türkiye'de 00:00–02:59 aralığında yerel bugün, UTC dün olabilir; aynı uygulama eklemede kabul edilip düzenlemede reddedilebilir. Finansın UTC sunucuda vade/gecikme günü de klinikten farklılaşabilir. Kaynak üzerinden doğrulandı; saati değiştiren gece sınırı çalışma testi yapılmadı.

Çözüm: klinik saat dilimini kullanan ortak saat/tarih servisi, tüm iş kurallarında aynı today değeri.

### 4. Eski bayi düzenleme formu modül kararını geri çevirebilir — orta

Modules ekranı ModulesRevision kontrol ediyor. Dealer/Edit üzerinden ClinicManagementRepository.UpdateAsync aynı IsWhatsAppEnabled alanını sürüm kontrolü olmadan yazıyor. WhatsApp açıkken açılmış eski düzenleme formu, arada Modüller ekranından kapatılan WhatsApp'ı eski true değeriyle yeniden açabilir. Tek ekranın revision artırması, diğer yazma yolunun eski değer kontrolü yapmamasını çözmüyor. Kaynak üzerinden doğrulandı; iki tarayıcı sekmesiyle çalışma testi yapılmadı.

Çözüm: modül yetkisini tek yazma yolunda yönetmek veya bütün güncelleme yollarına aynı concurrency sözleşmesini uygulamak.

### 5. Excel geri alma geçmişi, genel log saklama süresine bağlı — orta/düşük

Excel aktarım manifesti (oluşturulan kayıt kimlikleri ve kontrol hash'leri) SystemAuditLogs.MetadataJson içinde. DataRetentionJob eski tüm SystemAuditLogs kayıtlarını siler; varsayılan AuditDays 365. Bu süre sonunda Excel aktarım geçmişi ve geri alma metadatası kaybolur. Hasta verisi silinmez. Arayüzde bu süreye bağlı kısıt açıklanmıyor. Kaynak üzerinden doğrulandı; ana veritabanında retention çalıştırılmadı.

Çözüm: aktarım batch/manifestini audit logundan bağımsız kalıcı tabloda tutmak veya açık bir geri alma süresi tanımlamak.

## Kontroller / sınırlar

- Mevcut 129 entegrasyon testi geçti: general-review-tests.log.
- Ek tanılamada kapalı modüllerin küçük/büyük harfli URL'leri de 403 döndürdü; harf değiştirerek modül aşımı doğrulanmadı.
- Test randevusu bulunan Workday ekranı HTTP 200 verdi; önceki tarih hatasının bu yolda yeniden üretildiği iddia edilmiyor.
- Aşı mükerrer düzenleme çalışma sırasında yeniden üretildi.
- İkinci sahip ekleme çalışma sırasında veritabanı tekillik hatasıyla yeniden üretildi; bu tanılama testinin başarısızlığı bulgunun kanıtıdır.
- Tenant/IDOR, CSRF, oturum iptali, finans atomikliği, hızlı ziyaret rollback/idempotency, Excel kontrol/geri alma, takip bağlantıları ve modül kapatma mevcut testlerle tekrar kontrol edildi. Bu bütün uç durumların hatasız olduğunu garanti etmez.
- Mikrofon donanımı, gerçek mesaj teslimi, üretim OS/Docker/TLS ve üretim yükü test edilmedi.

Önerilen düzeltme sırası: sahip ekleme → aşı mükerrerlik kuralı → klinik saat servisi → ortak modül concurrency → aktarım manifestinin ayrılması.

## Düzeltme tamamlandı

Beş bulgu yerelde giderildi:
- Sahip ekleme ve arşivden bağlantı açma aynı hasta kilidi altında çalışır; mevcut birincil korunur, ek sahip ikincildir. Birincil değişimi ayrı işlem ve audit kaydıdır. Diğer sahipler varken birincili doğrudan kaldırma anlaşılır uyarıyla durdurulur.
- Aşı tekli/toplu ekleme, düzenleme ve geri yükleme aynı hasta kilidi ve aktif hasta/tür/gün tekrar kuralına uyar. Hasta değişiminde eski/yeni hasta kilitleri sıralı alınır. Mevcut kayıtlar otomatik silinmez/birleştirilmez.
- ClinicTime/ClinicCalendar üzerinden klinik günü kullanılır: aşı doğrulama/seçimleri, finans gecikme/ödeme ve varsayılan form tarihleri, hasta doğum tarihi, Excel doğum tarihi ve WhatsApp aşı tarih kontrolü. Randevu varsayılan günü de klinik tarihidir.
- Bayi düzenleme formu ModulesRevision taşır. Güncelleme ile Modüller ekranı aynı klinik satır kilidini kullanır. Eski sürüm veya sürümsüz yetki değişimi reddedilir; kullanıcıya hata gösterilir.
- ExcelImportBatches kalıcı tenant tablosu oluşturuldu. Eski aktarım manifestleri ve geri alma durumu migration ile korundu; History/Confirm/Undo genel audit loglarından bağımsızdır. Retention sonrası geri alma ve migration backfill testi eklendi.

137/137 entegrasyon testi geçti: five-fixes-final-tests.log. Yeni testler ikinci sahip/birincil değişimi, aşı düzenlemede ve geri yüklemede mükerrerlik, eşzamanlı tekli/toplu aşı, yerel gece yarısı, eski bayi formu, log temizliği sonrası Excel geri alma ve migration ile eski kayıtları taşımayı kapsar.

Yerel migration öncesi yedek VoxCRM-local-private klasörüne alındı. Ana yerel kopyada 147 hasta ve 171 WhatsApp bildirimi korundu. Uzak sunucuya dokunulmadı; gerçek mesaj gönderilmedi.
