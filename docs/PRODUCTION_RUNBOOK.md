# VoxCrm production runbook

Hedef: `petcrm.fenrirsoftware.com`, tek Ubuntu 22.04/24.04 VPS, 1–5 müşteri. RPO 6 saat, RTO 8 saat.

## 1. Zorunlu envanter kapısı

Sunucuda yalnız aşağıdaki salt-okunur komut çalıştırılır ve çıktı onaylanır:

```bash
bash deploy/scripts/server-inventory.sh > server-inventory.txt
```

Dağıtımı durdurma koşulları: Ubuntu 22.04/24.04 dışında sürüm; 2 vCPU, 4 GB RAM veya 80 GB diskten az kaynak; açıklanamayan public port/servis; kritik OS güncellemesi beklemesi. Envanter onaylanmadan SSH, UFW, kullanıcı, Docker veya paket ayarı değiştirilmez.

## 2. Sertleştirme (ayrı onay gerektirir)

- Bu VPS için kullanıcı talebiyle SSH kapsam dışıdır: port 22, root girişi, parola girişi,
  SSH yapılandırması ve authorized_keys değiştirilmez.
- Sunucu çok sayıda aktif site barındırır. Nginx, Apache, Docker daemon, PM2, UFW ve
  global ağ ayarları mevcut sitelerin baseline kontrolleri alınmadan değiştirilmez.
- `fail2ban`, `unattended-upgrades`, NTP ve logrotate etkinleştirilir.
- `voxcrm` sistem kullanıcısı oluşturulur. Docker grubu root eşdeğeri kabul edilir; yalnız deployment kullanıcısı erişir.
- `/etc/voxcrm/secrets`, `/var/lib/voxcrm/whatsapp-sessions`, `/var/backups/voxcrm` dizinleri `voxcrm:voxcrm`, mod `700`; secret/key dosyaları mod `600` olur.

## 3. Secret hazırlığı

`deploy/production.env.example`, `/etc/voxcrm/secrets/production.env` olarak kopyalanır ve tüm placeholder değerler değiştirilir. Birbirinden bağımsız en az 32 bayt değerler kullanılır. Aşağıdaki anahtarlar ayrıca oluşturulur:

```bash
openssl rand -base64 32 > /etc/voxcrm/secrets/pii.key
openssl rand -base64 48 > /etc/voxcrm/secrets/backup.key
chmod 600 /etc/voxcrm/secrets/*
```

PII ve backup anahtarı kaybolursa ilgili veriler geri döndürülemez. Backup anahtarı DB parolasıyla aynı olmamalıdır. Secretlar release arşivine veya repoya girmez.

## 4. Release ve ilk dağıtım

Release yalnız temiz, commit edilmiş çalışma ağacından üretilir:

```bash
deploy/scripts/release.sh 1.0.0
sha256sum -c artifacts/releases/voxcrm-1.0.0.tar.gz.sha256
```

Arşiv ve `.sha256` dosyası sunucuda `/opt/voxcrm/incoming` altına aktarılır. Sunucuda checksum tekrar doğrulanır, `/opt/voxcrm/releases/1.0.0` altına açılır ve `current` symlink’i yalnız doğrulama sonrası değiştirilir.

Bu VPS'te 80/443 mevcut Nginx tarafından kullanılır. Compose içindeki Caddy `caddy`
profilindedir ve başlatılmaz; Web yalnız `127.0.0.1:5180` adresine bind edilir. Nginx
şablonu `deploy/nginx/petcrm.fenrirsoftware.com.conf` dosyasındadır.

Dağıtım sırası:

1. `backup-production.sh` ile pre-deploy backup.
2. `docker compose ... run --rm migrate-crm` ve `migrate-gateway`.
3. `docker compose ... up -d --no-build --wait web api gateway-api wa-worker`.
4. `curl http://127.0.0.1:5180/healthz` ile origin health/smoke yapılır.
5. Nginx site dosyası kurulur; `nginx -t` başarılı olmadan reload yapılmaz. Reload
   öncesi ve sonrası mevcut sitelerin HTTP baseline sonuçları karşılaştırılır.
6. DNS açılır, Certbot ile Nginx sertifikası alınır ve `verify-production.sh` çalıştırılır.
7. SystemAdmin ve Dealer ilk girişte parolayı değiştirir, TOTP’yi kurar ve recovery code’ları offline saklar.
8. `DataSeeding__SystemAdmin__Enabled`, `DataSeeding__ProductionDealer__Enabled` `false` yapılır; bootstrap password satırları env dosyasından kaldırılır ve servisler yeniden oluşturulur.

## 5. Rollback

Yeni sürüm readiness veya smoke testini geçmezse DNS açılmaz. `current` önceki release’e döndürülür ve eski image sürümü `--no-build` ile başlatılır. Migration geriye uyumlu değilse servisler durdurulur ve yalnız pre-deploy şifreli snapshot geçici DB’de doğrulandıktan sonra production DB’ye restore edilir. Restore geri döndürülemez bir işlem olduğundan iki kişi kontrolüyle yapılır.

## 6. Backup ve restore

`voxcrm-backup.timer` 00:10, 06:10, 12:10, 18:10 UTC’de çalışır. Saklama: 28 adet altı-saatlik snapshot (7 gün), 30 günlük kopya, 12 aylık kopya. Paket CRM DB, gateway DB ve şifreli WhatsApp session dizinini içerir; paket AES-256 ile şifrelenir ve iç/dış SHA-256 manifest taşır.

Günlük `verify-latest-backup.sh` çalıştırılır. Ayda bir `test-backup-smoke.sh`, production container/DB adları ve backup key verilerek geçici iki DB’ye tam restore yapar; sonuç audit/operasyon kaydına eklenir. RTO tatbikatı sekiz saatin altında bitmelidir.

Yalnız VPS üzerinde backup tutulması sunucu/disk kaybında tüm kopyaların kaybı demektir. Off-site immutable kopya eklenene kadar production durumu koşulludur; tam felaket kurtarma onayı verilmez.

## 7. Alarm ve kabul

Telegram ve e-posta test mesajları doğrulanır. `voxcrm-monitor.timer` disk %75/%85 ve HTTPS health alarmlarını izler. Backup, restore, migration ve retention job hataları; yüksek login hatası/lockout ve WhatsApp retry/NeedsReview ayrıca alarm üretmelidir.

Go-live öncesi: tüm testler, EF pending-model kapısı, NuGet/npm/Python/container CVE taramaları, OWASP ZAP baseline, gerçek restore, rollback, tenant izolasyonu, MFA, TLS/HSTS/CSP, açık-port kontrolü ve demo parola kontrolü başarılı olmalıdır. Önce yalnız yönetici, sonra ilk dealer, sonra tek klinik açılır; 48 saat hatasız izlemden sonra kalan müşteriler eklenir.
