# VoxCrm VPS envanteri — 15 Temmuz 2026

Bu rapor salt-okunur envanterden üretilmiştir. SSH ayarları kullanıcı talebiyle kapsam
dışıdır ve değiştirilmemiştir. Sunucuda hiçbir dosya silinmemiştir.

## Kaynaklar

- Ubuntu 24.04.4 LTS, Linux 6.8, VMware, x86_64/amd64.
- 14 vCPU, 23 GiB RAM, 1.5 GiB swap.
- 373 GiB root disk; yaklaşık 317 GiB boş alan.
- Docker 28.2.2; AppArmor, seccomp ve cgroup namespace aktif.
- NTP, UFW, fail2ban, unattended-upgrades ve logrotate aktif.

Kaynak ve işletim sistemi production minimumlarını karşılıyor.

## Ağ ve mevcut servisler

- Nginx mevcut siteler için 80/443 portlarını kullanıyor; Caddy aynı portlarda çalışamaz.
- VoxCrm bu nedenle yalnız `127.0.0.1:5180` üzerinde yayınlanacak ve mevcut Nginx ters
  proxy olarak kullanılacak.
- PostgreSQL, MariaDB ve Redis public dinlemiyor. VoxCrm PostgreSQL/API/gateway/worker
  servisleri Docker internal ağlarında kalacak.
- UFW incoming varsayılanı deny; 22 ve 80/443 açık. Mevcut 6677 kuralının sahibi
  belirlenmeden kural değiştirilmedi.
- Sunucuda başka üretim siteleri, Node/Apache süreçleri ve CAS Docker stack'i var;
  global firewall, Docker daemon veya ağ sysctl değişiklikleri bakım penceresi olmadan
  uygulanmayacak.

## Güvenlik ve log bulguları

- SSH internete açık ve yoğun parola denemesi alıyor. Fail2ban `sshd` jail aktif;
  envanter anında 20.733 başarısız deneme ve 856 ban kaydı vardı. Kullanıcı talebiyle
  SSH portu, root/parola erişimi ve SSH yapılandırması aynen korunacaktır.
- `mrmec.service` olmayan çalışma dizini nedeniyle 5 saniyede bir yeniden başlıyor ve
  2.228.146 restart üretmişti. Dosya silmeden servis durdurulup devre dışı bırakıldı.
- Log fırtınası sırasında rsyslog `omfile` yazma aksaklıkları üretmişti. Bozuk servis
  durdurulduktan sonra rsyslog yeniden başlatıldı ve aktif duruma geldi.
- Host Caddy, Nginx ile 443 çakışması nedeniyle üç aydır failed durumdaydı; dosya
  silmeden devre dışı bırakıldı. VoxCrm mevcut Nginx üzerinden yayınlanacak.
- Certbot timer aktif fakat başka bir alan adının süresi dolmuş sertifikası tüm yenileme
  job'unu failed gösteriyor. VoxCrm sertifikası alınmadan önce bu operasyonel durum ayrıca
  izlenecek; başka site sertifika dosyalarına dokunulmadı.
- 42 paket güncellemesi ve reboot gereksinimi var. Paylaşımlı servisleri kesmemek için
  paket güncellemesi/reboot bakım penceresine bırakıldı.

## Yapılan değişiklikler

- `mrmec.service`: stopped + disabled.
- `caddy.service`: disabled; Nginx aktif bırakıldı.
- `rsyslog.service`: yeniden başlatıldı ve aktif doğrulandı.
- SSH, UFW, fail2ban, Nginx site dosyaları, mevcut uygulamalar ve Docker container'ları
  değiştirilmedi.

## Kalan production kapıları

- `petcrm.fenrirsoftware.com` DNS A kaydı henüz çözülmüyor; VPS adresine yönlendirilmeli.
- Nginx VoxCrm site dosyası kurulup `nginx -t` ile doğrulanmalı.
- DNS yayıldıktan sonra Certbot sertifikası alınmalı; TLS/HSTS/ZAP kontrolleri yapılmalı.
- Sürümlü release aktarılmalı, boş DB migration/bootstrap ve health/smoke tamamlanmalı.
- Production backup, geçici DB restore, rollback ve Telegram/e-posta alarm tatbikatı
  yapılmalı.
- Backup yalnız aynı VPS'te tutulacak. Disk/VPS tamamen kaybolursa tüm kopyalar kaybolur;
  bu kabul edilmiş kalan risktir.
