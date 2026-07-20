# VoxCrm Yerel UAT Raporu — 20 Temmuz 2026

## Kapsam

Testler yalnızca `http://127.0.0.1:5114` üzerindeki yerel geliştirme ortamında
yapıldı. Production ortamına veri yazılmadı veya deployment yapılmadı.

Kullanıcının isteği doğrultusunda şu maddeler bu turda kapsam dışı bırakıldı:

- müşteri parolası oluşturma ve eski/default parolanın kalmadığını doğrulama;
- ilk giriş parola değişimi ve MFA;
- çalışan rol/yetki testi;
- gerçek WhatsApp QR ve mesaj akışı;
- Telegram, sağlık alarmı ve backup bildirimi.

## Manuel arayüz testleri

| Akış | Sonuç | Kanıt / test verisi |
|---|---|---|
| Sahipli kişi kaydı | Başarılı | `UAT-Codex Sahipli` |
| Bilgisi olmayan kişi kaydı | Başarılı | Boş form uyarısından sonra `Kimliği belirsiz kişi b13c120a` |
| Sahipli hasta kaydı | Başarılı | `UAT-Pati`, sahibi `UAT-Codex Sahipli` |
| Minimum bilgiyle sahipsiz hasta | Başarılı | Yalnız ad ve notla `UAT-Sokak` |
| Aşı türü ve aşı kaydı | Başarılı | `UAT-Codex Karma`, `UAT-Pati` |
| Aşı arşivleme ve geri alma | Başarılı | Arşiv ekranı ve `Geri Al` işlemiyle doğrulandı |
| Randevu oluşturma | Başarılı | `UAT-Pati`, 23.07.2026 14:00, 30 dakika |
| Randevu değiştirme | Başarılı | Saat değişikliği listede doğru yerel saatle görüldü |
| Randevu çakışması | Başarılı | Aynı zaman aralığı ikinci kez kaydedilmeden çakışma uyarısı gösterildi |
| Ondalıklı muayene | Başarılı | 38,6 °C ve 12,4 kg ile SOAP kaydı oluşturuldu |
| Borç kaydı | Başarılı | `UAT Codex muayene borcu`, 1.250 TL |
| Tahsilat | Başarılı | 1.250 TL Nakit; borç `Tahsil Edildi` oldu |
| Tenant liste izolasyonu | Başarılı | Şifa Vet hesabında Mutlu Patiler'e ait `UAT-Pati` listelenmedi |

## Otomatik testler

- VoxCrm tam entegrasyon paketi: **38/38 başarılı**.
- Klinik yaşam döngüsü, tenant izolasyonu, aşı arşivi, randevu ve finans için
  seçili kabul paketi: **30/30 başarılı**.
- WhatsApp Gateway API: **12/12 başarılı**.
- WhatsApp Worker: **15/15 başarılı**.

Kabul paketi klinik oluşturma/aktivasyonunu, başka dealera ait kliniğe erişim
reddini, tenant dışı hasta/randevu/aşı/finans erişimini, atomik çift rezervasyon
engelini ve eşzamanlı tahsilat sınırlarını kapsar.

## Test sırasında bulunan ve düzeltilen sorunlar

1. Randevu saati macOS yerel saat diliminde ikinci kez dönüştürülerek üç saat
   ileri gösteriliyordu. UTC `DateTime.Kind` normalizasyonu düzeltildi ve arayüzde
   girilen 14:00 yeniden 14:00 olarak doğrulandı.
2. Aşı kaydı backend'de geri alınabiliyor fakat arşiv/geri alma arayüzü yoktu.
   `Arşivi Göster`, `Aktifleri Göster` ve `Geri Al` akışları eklendi.
3. Muayene sıcaklık/kilo alanlarında ondalık değer bağlama hataları kullanıcıya
   görünmüyordu. Nokta ve virgül kabul eden decimal binder ile alan/özet hata
   mesajları eklendi; 38.6 ve 12.4 değerleriyle kayıt doğrulandı.
4. Günlük aşı hatırlatma işi aynı klinik için birden fazla aktif şablon olduğunda
   duplicate-key hatasıyla durabiliyordu. Klinik başına en güncel şablon seçilerek
   işin çökmesi engellendi.

## Sonuç

Bu turda kapsama alınan yerel CRM işlemleri başarılıdır. Bu rapor gerçek
WhatsApp bağlantısı, MFA, müşteri credential teslimi, production backup/alarm
ve production deployment için kabul belgesi değildir; bu maddeler ayrıca
tamamlanmalıdır.
