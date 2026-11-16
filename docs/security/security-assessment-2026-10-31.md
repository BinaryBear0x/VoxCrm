# VoxCRM yerel güvenlik değerlendirmesi — 31.10.2026

## Kapsam ve yöntem

Yalnız yerel kaynak kod, HTTP TestServer üzerinden oturum açılmış test hesapları ve rastgele adlandırılmış izole PostgreSQL test veritabanları kullanıldı. İki ayrı klinik, klinik kullanıcısı ve birbirinden farklı hasta/sahip/muayene/aşı/randevu/finans/görev kayıtları oluşturuldu. Mevcut dealer/MFA ve müşteri takip bağlantısı regresyon testleri de çalıştırıldı. Üretim sunucusuna bağlantı kurulmadı; gerçek WhatsApp veya Telegram mesajı gönderilmedi. Gateway testlerinde dış gönderim/CRM doğrulama çağrıları mock kullanır.

Bu rapor tam kapsamlı üretim penetrasyon testi veya güvenlik sertifikası değildir. Negatif sonuçlar aşağıdaki istekler ve incelenen kod yolları için geçerlidir.

## Doğrulanan ve giderilen bulgular

| Bulgu | Etki / ön koşul | Düzeltme ve doğrulama |
|---|---|---|
| Mevcut oturumda hesap durumunun yeterince yeniden doğrulanmaması | Geçerli oturumu bulunan kullanıcı için `LoginDisabled` doğrudan veritabanında değiştirildiğinde eski oturum erişebiliyordu. Normal yönetim işlemlerinin security stamp iptaline ek savunma açığıdır; doğrudan anonim erişim değildir. | Her uygulama oturumunda kullanıcı, kilit, aktif klinik/dealer kapsamı ve güncel roller kontrol edilir. Kapalı hesap ve rol kaldırma testleri erişimi reddeder. MFA ara çerezleri ayrı tutulur; recovery-code testi de geçer. |
| Kullanıcı metnindeki `enc:v1:` önekinin şifreli veri kabul edilmesi | Kayıt yetkisi olan kullanıcı, bu önekle başlayan normal notu kaydedip sonraki okumada decryption hatası/kalıcı sayfa hatası oluşturabilirdi. Gizli anahtar elde etme veya RCE gösterilmedi. | Girdi her zaman şifrelenir; EF interceptor yalnız kendi dönüştürdüğü alanların düz metnini geri yükler. Hata/iptal durumunda da geri yükleme yapılır. Üç önek girdisi, çoklu kayıt, iptal ve yeniden okuma doğrulanır. |
| Geçerli imzalı fakat bozuk JWT claim'lerinde istisna | İmza anahtarını bilen entegrasyonun hatalı `exp` göndermesi 500 üretebiliyordu. İmzasız token ile kimlik doğrulama atlatma gösterilmedi. | Bozuk claim/JSON/biçimler yetkisiz sonuç verir; HS256, uzunluk, `nbf` ve `jti` sınırları denetlenir. Replay kontrolü atomik yapılır; 64 eşzamanlı aynı token isteğinden yalnız biri kabul edilir. |
| Bilinen bağımlılık açıkları | NuGet test bağımlılığında 2 yüksek bildirim; npm ağacında 14 paket bulgusu (3 kritik, 7 yüksek, 4 orta); Python kilidinde 3 etkilenen paket. Geliştirme/test araçlarındaki açıklar üretimde çalışan RCE olarak sunulmaz. | SSH.NET 2026.0.0, Vitest ve etkilenen npm bağımlılıkları; cryptography 50.0.0, PyJWT 2.15.1, certifi 2026.10.18 güncellendi. Python kilidinde yalnız ilgili sürüm/hash blokları yenilendi. Üç son taramada bilinen açık sayısı 0. NU1903 uyarı bastırması kaldırıldı. |

## Saldırı denemeleri

- **IDOR / yatay yetki:** Başka kliniğin hasta, sahip, muayene, aşı, aşı türü, randevu, finans, görev, bakım ve takip bağlantısı için 14 okuma yolu 404 döndü. Yedi liste/arama yolunda karşı klinik verisi bulunmadı. Sekiz silme yolunda yabancı kayıt reddedildi ve kayıtların korunması kontrol edildi. Mevcut tenant filtre testleri tüm tenant entity türlerini de kapsar.
- **Dikey yetki:** Klinik hesabı Dealer, Dealer/Health ve Hangfire ekranlarına erişemedi. Dealer sahiplik, parola değiştirme, oturum iptali ve MFA recovery-code senaryoları ayrıca geçti.
- **CSRF / overposting:** Altı kritik POST yolunda anti-forgery token eksikliği 400 verdi. Hasta güncellemede gönderilen yabancı ClinicID ve IsActive alanları yetkisiz değişiklik oluşturmadı.
- **Stored/reflected XSS:** Script, textarea kapatma, img/onerror ve svg/onload girdileri kaydetme/arama yollarında denendi. HTML çıktısı encode edildi; aktif script/etiket bulunmadı. Bu kontrol HTTP çıktı doğrulamasıdır; tüm tarayıcı DOM etkileşimleri taranmadı.
- **SQL / template / komut enjeksiyonu:** SQL koşulu, template ifadesi, komut ve traversal biçimli girdiler normal metin kaldı; kayıt sayısı/kapsamı değişmedi. Komut canary dosyası oluşmadı. Zararlı komut çalıştırılmadı.
- **RCE kaynak incelemesi:** Web/API içinde kullanıcı girdisini shell/eval ile çalıştıran yol bulunmadı. Sağlık kontrolündeki docker işlemi sabit executable/ArgumentList ve UseShellExecute=false kullanır; yerelde kapalıdır. Worker işlem/oturum yollarındaki girdi kısıtları incelendi. Test edilen yollar için RCE kanıtı yoktur; işletim sistemi/Docker daemon üzerinde exploit denenmedi.
- **Dosya ifşası / traversal:** Anonim `.env`, appsettings, `.git/config`, log ve yedek yolları erişim vermedi. Harici returnUrl ile açık yönlendirme denenip reddi kontrol edildi.
- **JWT / replay:** Yanlış scope, karşı klinik, replay, bozuk imzalı exp ve eşzamanlı replay senaryoları denetlendi. Web şifreli alanlarının diskte düz metin olmaması ve tenant bağlı arama hash'leri mevcut testlerle doğrulandı.

## OWASP Top 10:2025 kapsamı

| Kategori | Yapılan kontrol | Sınır |
|---|---|---|
| A01 Broken Access Control | Tenant filtreleri, IDOR, dealer/klinik yetkisi, overposting | Seçili HTTP yolları; tüm olası iş akışı kombinasyonları değildir. |
| A02 Security Misconfiguration | Güvenlik başlıkları, hassas dosya yolları, prod config guard kodu | Üretim TLS, proxy, firewall ve OS incelenmedi. |
| A03 Software Supply Chain Failures | NuGet/npm/Python kilit taraması ve güncellemeler | Bilinen advisory verisi; tedarik zinciri/provenance tam denetimi değil. |
| A04 Cryptographic Failures | AES-GCM at-rest, önek girdisi, hata/iptal ve tenant hash testleri | Üretim anahtar saklama, rotasyon ve yedek anahtar yönetimi incelenmedi. |
| A05 Injection | XSS/SQL/template/komut girdileri ve tehlikeli sink kaynak incelemesi | Tarayıcı DOM fuzzing ve her parametrenin exhaustive taraması yok. |
| A06 Insecure Design | Mesaj alıcı doğrulama, consent, tenant kapsamı ve mevcut iş akışı testleri | Tam tehdit modellemesi / tüm suiistimal senaryoları değil. |
| A07 Authentication Failures | Login CSRF/rate limit, oturum iptali, güncel roller, MFA testleri | Gerçek hesaplara brute force uygulanmadı. |
| A08 Software or Data Integrity Failures | JWT imza/algoritma/scope/replay, şifreli veri bütünlüğü | Build/artifact imzalama zinciri tam incelenmedi. |
| A09 Security Logging and Alerting Failures | Mevcut audit ve retention testleri | Canlı Telegram/SIEM alarmı tetiklenmedi; uçtan uca müdahale testi değil. |
| A10 Mishandling of Exceptional Conditions | Bozuk JWT ve şifreleme hata/iptal davranışı | Yük/DoS/chaos testi uygulanmadı. |

## Test kanıtları

- .NET: **113/113**, `dotnet-full-tests.log`; yeni saldırı test sınıfı 15 test vakası içerir.
- Node worker: **18/18**, `worker-tests.log`.
- Python gateway: **25/25**, `python-tests.log`.
- Son bağımlılık taramaları: `dotnet-audit-after.json`, `npm-audit-after.json`, `python-audit-after.json`: **0 bilinen açık**.
- Önceki taramalar ve ilk saldırı testi çıktıları aynı klasörde korunur. İlk test çıktısındaki geçersiz uzunlukta XSS test girdisi test düzeneği sorunudur, uygulama açığı değildir.
- Windows'ta worker POSIX dosya modu assertion'ı uygulanmaz; Linux chmod/izin davranışı üretim platformunda ayrıca çalıştırılmalıdır. Diğer worker kontrolleri geçti.
- Gateway Alembic testinin sabit 5432 hedefi kaldırılıp test DSN'sine bağlandı; testler yerel 55432 üzerinde kendi rastgele veritabanlarını kullanır.

## Kaynaklar

- [OWASP Top 10:2025](https://top10.owasp.org/2025/)
- [SSH.NET SCP path traversal advisory](https://github.com/advisories/GHSA-q939-rpr3-3284)
- [SSH.NET SCP command injection advisory](https://github.com/advisories/GHSA-mggc-4xg6-vcxf)

Yerel sonuçlar üretime uygulanmış değildir. Yayın öncesinde Linux testleri, üretim yapılandırması ve yedekten geri dönüş provası ayrı olarak doğrulanmalıdır.

## Son yerel kontrol

Yerel uygulama 5114 portunda yeniden başlatıldı; giriş sayfası HTTP 200. Ana yerel kopyada hasta sayısı 147, WhatsApp bildirim sayısı 171; test öncesi sayılarla aynı. Bu sayım içerik hash denetimi değildir; testler ana veri kopyasında yürütülmedi. Son web derlemesi 0 hata / 0 uyarı ile tamamlandı.
