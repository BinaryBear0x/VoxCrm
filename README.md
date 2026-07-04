# VoxCRM - Veteriner Klinik Yönetim Sistemi

VoxCRM, veteriner klinikleri ve klinik ağlarını (bayiler) yönetmek için tasarlanmış, çok kiracılı (multi-tenant) bir CRM sistemidir. Hasta ve sahip yönetimi, randevu planlama, mali takip ve otomatik WhatsApp bildirimleri tek bir platformda bir araya getirilmiştir.

---

## Ekran Görüntüleri

**Giriş Sayfası**

![Giriş Sayfası](docs/screenshots/login.png)

**Gösterge Paneli**

![Gösterge Paneli](docs/screenshots/dashboard.png)

**Müşteri Listesi**

![Müşteri Listesi](docs/screenshots/petowner.png)

**WhatsApp Entegrasyonu**

![WhatsApp Sayfası](docs/screenshots/whatsapp.png)

**Bayi Paneli**

![Bayi Paneli](docs/screenshots/dealer-panel.png)

---

## Sistem Mimarisi

Proje tek bir monorepo altında üç ana bölümden oluşuyor:

```
VoxCRM
├── VoxCrm.*         (C# / .NET 8)  <-- Ana uygulama
├── whatsapp-gateway (Python + Node) <-- WhatsApp geçidi
└── backups/         (Bash + pg_dump) <-- Yedekleme klasörü
```

Servisler arası iletişim aşağıdaki gibi işler:

```
Tarayıcı
   |
   v
VoxCrm.Web (:5114)
   |   |
   |   +-- WhatsAppNotifications tablosuna kayıt ekler (Pending)
   |
   v
VoxCrm.Api (:5072)   <-- gateway-api buraya poll eder
   |
   +-- PostgreSQL (voxcrm_dev)
   |
   v
whatsapp-gateway/gateway-api (:8088)  [Python / FastAPI]
   |
   v
whatsapp-gateway/wa-worker (:8090)    [Node.js / Baileys]
   |
   v
WhatsApp (gerçek mesaj gönderiliyor)
```

Bildirim akışı kasıtlı olarak asenkroniktir: Web uygulaması doğrudan gateway'i çağırmaz. Mesaj önce `WhatsAppNotifications` tablosuna `Pending` durumuyla yazılır; gateway periyodik olarak bu tabloyu okur, kilitleme (SELECT FOR UPDATE SKIP LOCKED) yaparak mesajları işleme alır ve WhatsApp'a iletir.

---

## Klasör Yapısı ve Amacı

### VoxCrm.Domain/

**Amaç:** Uygulamanın çekirdek iş mantığı ve entity'leri burada tanımlıdır. Hiçbir dışarıya bağımlılık yoktur; sadece saf C# sınıflarından oluşur.

**İçerik:**
- `Entities/` — Tüm veritabanı entity'leri (`Clinic`, `PetOwner`, `Patient`, `Appointment`, `VaccinationRecord`, `WhatsAppNotification` vb.)
- `Common/` — Paylaşılan arayüzler (`ITenantEntity` gibi tenant izolasyonunu zorunlu kılan arayüzler)

**Kiminle konuşur:** Hiçbir servis katmanıyla doğrudan iletişime geçmez. Tüm diğer katmanlar Domain'e bağlıdır, Domain hiçbir katmana bağımlı değildir.

---

### VoxCrm.Application/

**Amaç:** CQRS (Command Query Responsibility Segregation) kalıbını hayata geçiren katman. Komutlar (yazma), sorgular (okuma) ve iş kuralları buradadır.

**Kiminle konuşur:** Sadece `VoxCrm.Domain` entity'lerini kullanır. Infrastructure ve Web katmanları tarafından çağrılır.

---

### VoxCrm.Infrastructure/

**Amaç:** Veritabanı erişimi, migration'lar ve zamanlanmış arka plan işleri burada yer alır.

**İçerik:**
- `Data/` — `VoxCrmDbContext` (Entity Framework Core), `DbSeeder` (başlangıç test verileri)
- `Migrations/` — PostgreSQL şema migration dosyaları
- `Jobs/` — `ReminderJob.cs`: Hangfire tarafından her gün tetiklenen aşı hatırlatma işlemi; son tarihi geçmiş `VaccinationRecord` kayıtları için `WhatsAppNotifications` tablosuna otomatik `Pending` kayıt oluşturur

**Kiminle konuşur:** `VoxCrm.Domain` entity'lerini kullanır. `VoxCrm.Web` ve `VoxCrm.Api` tarafından servis olarak inject edilir.

---

### VoxCrm.Web/

**Amaç:** Klinik ve bayi kullanıcıları için ASP.NET Core MVC web uygulaması. Bootstrap 5 ve Razor view'larla oluşturulmuş arayüz.

**İçerik:**
- `Controllers/` — 13 controller: Auth, Home, PetOwner, Patient, Appointment, Vaccination, Finance, WhatsApp, ServiceItem, VaccineType, Muayene, ClinicSettings, Dealer
- `Views/` — Razor sayfaları (her controller için ayrı klasör)
- `Services/` — HTTP istemcileri ve iş mantığı servisleri
- `wwwroot/` — Statik dosyalar (CSS, JS, görseller)

**Önemli güvenlik mekanizmaları:**
- `[Bind("Id,Alan1,Alan2")]` attribute'u ile Mass Assignment koruması
- `ModelState.Remove()` ile tenant ID'nin form gönderiminde manipüle edilmesi engellenir
- Global Query Filter'lar sayesinde her sorgu otomatik olarak o kliniğe ait verileri getirir (tenant izolasyonu)

**Kiminle konuşur:** PostgreSQL'e `VoxCrmDbContext` üzerinden erişir. `whatsapp-gateway/gateway-api`'ye JWT imzalı HTTP istekleri atar (bağlantı durumu, QR kodu). Hangfire panelini içerir.

---

### VoxCrm.Api/

**Amaç:** Yalnızca `whatsapp-gateway`'e açılan REST API. Tarayıcıya veya son kullanıcıya açık değildir. JWT (HS256, scope tabanlı) ile korunur.

**Endpoint'ler:**
- `POST /api/whatsapp/notifications/claim` — Gateway, işleme alacağı mesajları çeker (SELECT FOR UPDATE SKIP LOCKED)
- `POST /api/whatsapp/notifications/{id}/status` — Gateway, mesaj sonucunu bildirir
- `POST /api/whatsapp/notifications/recover-expired-processing` — Kilit süresi dolan mesajları kurtarır
- `POST /api/whatsapp/inbound` — WhatsApp'tan gelen mesajları kaydeder
- `GET /api/health` — Sağlık kontrolü

**Kiminle konuşur:** Sadece `whatsapp-gateway/gateway-api` bu API'yi çağırır. PostgreSQL'e `VoxCrmDbContext` üzerinden doğrudan erişir.

---

### VoxCrm.IntegrationTests/

**Amaç:** Uygulama genelinde entegrasyon testleri. API endpoint davranışları ve veritabanı katmanı burada test edilir.

---

### whatsapp-gateway/

**Amaç:** VoxCRM'den bağımsız çalışabilen, iki servisten oluşan WhatsApp geçidi.

#### whatsapp-gateway/gateway-api/ (Python / FastAPI)

**Görev:** Orkestrasyon servisi. VoxCrm.Api'yi poll eder, gönderim sıralamasını yönetir, klinik WhatsApp durumlarını takip eder, kimlik doğrulama ve sağlık endpoint'lerini sunar.

**Temel dosyalar:**
- `app/main.py` — API endpoint'leri, polling döngüsü, sağlık kontrolü
- `app/sender.py` — Klinik bazlı gönderim zamanlayıcı (per-clinic interval + jitter)
- `app/voxcrm_client.py` — VoxCrm.Api ile HTTP konuşması (claim, status)
- `app/worker_client.py` — wa-worker ile HTTP konuşması (send, status)
- `app/auth.py` — JWT üretimi ve doğrulaması
- `app/config.py` — `.env` dosyasından ortam değişkenleri
- `alembic/` — Gateway veritabanı migration'ları (PostgreSQL: `voxcrm_gateway_dev`)

#### whatsapp-gateway/wa-worker/ (Node.js / Baileys)

**Görev:** WhatsApp linked-device protokolünü yöneten iç servis. QR kod yaşam döngüsü, oturum şifreleme ve gerçek mesaj iletiminden sorumludur. İnternete doğrudan bu servis bağlanır; gateway-api ve VoxCrm.Web'e kapatılıdır.

**Temel dosyalar:**
- `src/baileysProvider.js` — Baileys kütüphanesi entegrasyonu, oturum yönetimi
- `src/sessionStore.js` — Klinik bazlı oturum saklama ve şifreleme
- `src/security.js` — İç token doğrulaması (gateway-api'den gelen istekleri doğrular)
- `src/app.js` — Express endpoint'leri (send, connect, disconnect, status, qr)

#### whatsapp-gateway/scripts/

**Görev:** Bakım ve yedekleme araçları.

- `backup.sh` — PostgreSQL dump (voxcrm_dev + voxcrm_gateway_dev), klinik bazlı JSON export, WhatsApp session arşivi. Hedef klasörler: `backups/daily/`, `backups/weekly/`, `backups/monthly/`
- `restore.sh` — Belirlenen backup snapshot'ını yerel veritabanına yükler (kasıtlı olarak interaktif; yanlışlıkla çalıştırılmaz)
- `test-all.sh` — .NET build + NuGet audit + pytest + Vitest + syntax check + backup smoke test'ini sırayla çalıştırır
- `test-backup-smoke.sh` — Gerçek veri silmeden backup script'ini test eder

#### whatsapp-gateway/.env.example

Çalıştırmak için kopyalanması gereken ortam değişkeni şablonu. Gerçek `.env` dosyası repoya girmez.

---

### backups/

**Amaç:** Yedekleme klasörü yapısı. Gerçek dump dosyaları `.gitignore` ile Git dışında tutulur; sadece klasör iskelet yapısı (`.gitkeep`) repoda yer alır.

```
backups/
├── daily/    # Son 7 günlük yedekler (backup.sh otomatik temizler)
├── weekly/   # Son 4 haftanın pazartesi yedekleri
└── monthly/  # Son 3 ayın 1. günü yedekleri
```

**Her snapshot içeriği:**
- `voxcrm-db.dump` — Ana PostgreSQL veritabanı (pg_dump -Fc)
- `gateway-db.dump` — Gateway PostgreSQL veritabanı
- `clinic-{uuid}.json.gz` — Klinik bazlı okunabilir JSON export (hasta, sahip, randevu, mali kayıtlar, WhatsApp verileri)
- `whatsapp-sessions.tar.gz` — Baileys oturum dosyaları arşivi

---

### docs/screenshots/

**Amaç:** README içi uygulama ekran görüntüleri.

---

## Servisler Arası İlişki Haritası

```
+---------------------------+
|       Tarayıcı (UI)       |
+---------------------------+
            |
            v
+---------------------------+     +-------------------+
|      VoxCrm.Web           |---> | PostgreSQL        |
|  ASP.NET Core MVC :5114   |     | voxcrm_dev        |
+---------------------------+     +-------------------+
  |   ^                                   ^
  |   | (JWT HTTP)                        |
  v   |                                   |
+---------------------------+             |
|      VoxCrm.Api           |-------------+
|    REST API :5072          |
+---------------------------+
            ^
            | (JWT HTTP, polling)
            |
+---------------------------+     +-------------------+
|    gateway-api            |---> | PostgreSQL        |
|    FastAPI :8088           |     | voxcrm_gateway_dev|
+---------------------------+     +-------------------+
            |
            | (HTTP, iç token)
            v
+---------------------------+     +-------------------+
|      wa-worker            |---> | WhatsApp          |
|    Node.js :8090           |     | (Baileys)         |
+---------------------------+     +-------------------+
```

---

## Teknoloji Yığını

| Katman | Teknoloji |
|--------|-----------|
| Web Uygulaması | C# / .NET 8, ASP.NET Core MVC, Razor Pages |
| API | C# / .NET 8, ASP.NET Core Minimal API |
| ORM | Entity Framework Core 9, Code-First Migrations |
| Veritabanı | PostgreSQL 16 |
| Arka Plan İşleri | Hangfire (PostgreSQL storage) |
| WhatsApp Geçidi | Python 3.12 / FastAPI, SQLAlchemy, Alembic |
| WhatsApp İstemci | Node.js 22 / Baileys (linked-device) |
| Container | Docker Compose (geliştirme), OrbStack (yerel) |
| Ön Yüz | Bootstrap 5, jQuery, Vanilla CSS/JS |

---

## Kurulum ve Çalıştırma

### Ön Koşullar

- .NET 8 SDK
- Python 3.12
- Node.js 22
- PostgreSQL 16 (OrbStack veya Docker)
- Redis (gateway için)

### 1. Veritabanını Oluştur

```bash
docker start voxcrm-postgres voxcrm-redis
```

### 2. Ana Uygulamayı Başlat

```bash
# Bağlantı ayarlarını güncelle:
# VoxCrm.Web/appsettings.Development.json
# VoxCrm.Api/appsettings.Development.json

# Migration'ları uygula:
dotnet ef database update --project VoxCrm.Infrastructure --startup-project VoxCrm.Web

# API'yi başlat:
dotnet run --project VoxCrm.Api

# Web uygulamasını başlat:
dotnet run --project VoxCrm.Web
```

### 3. WhatsApp Gateway'i Başlat

```bash
cd whatsapp-gateway

# .env oluştur:
cp .env.example .env
# .env içindeki VOXCRM_API_BASE_URL değerini düzenle

# Docker ile (önerilen):
docker compose up -d gateway-api wa-worker

# Veya elle:
cd gateway-api
python3 -m venv .venv && . .venv/bin/activate
pip install -r requirements.txt
alembic -c alembic.ini upgrade head
uvicorn app.main:app --host 0.0.0.0 --port 8088 --reload

cd ../wa-worker
npm install
npm run dev
```

### 4. WhatsApp Bağlantısı

Tarayıcıda `VoxCrm.Web > WhatsApp` menüsünden kliniğe ait "Bağla" düğmesine tıklayarak QR kod taranır. QR kodunu wa-worker üretir, gateway-api aracılığıyla Web'e iletilir.

---

## Yedekleme

```bash
# Günlük yedek al:
./whatsapp-gateway/scripts/backup.sh

# Yedekler şu klasöre yazılır:
# backups/daily/YYYYMMDDTHHMMSSZ/

# Geri yükle (dikkatli kullan, yerel veritabanını değiştirir):
./whatsapp-gateway/scripts/restore.sh backups/daily/<timestamp>
```

---

## Testler

```bash
# Tüm test süreci (build + NuGet + pytest + Vitest + backup smoke):
./whatsapp-gateway/scripts/test-all.sh

# Sadece .NET testleri:
dotnet test

# Sadece Gateway testleri:
cd whatsapp-gateway
. .venv/bin/activate
pytest gateway-api/tests/

# Sadece Worker testleri:
cd whatsapp-gateway/wa-worker
npm test
```

---

---

## Test Kullanıcıları (Geliştirme Ortamı)

Veritabanı ilk çalışmada `DbSeeder` tarafından aşağıdaki test verileriyle doldurulur:

| Rol | E-posta | Şifre |
|-----|---------|-------|
| Bayi Yöneticisi | admin@voxcrm.com | Admin123! |
| Klinik - Mutlu Patiler | iletisim@mutlupatiler.com | Klinik123! |
| Klinik - Şifa Vet | bilgi@sifavet.com | Klinik123! |
