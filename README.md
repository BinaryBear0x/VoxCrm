# VoxCRM - Veteriner Klinik Yonetim Sistemi

VoxCRM, veteriner klinikleri ve klinik aglarini (bayiler) yonetmek icin tasarlanmis, cok kiracili (multi-tenant) bir CRM sistemidir. Hasta ve sahip yonetimi, randevu planlama, mali takip ve otomatik WhatsApp bildirimleri tek bir platformda bir araya getirilmistir.

---

## Ekran Goruntuleri

**Giris Sayfasi**

![Giris Sayfasi](docs/screenshots/login.png)

**Goçsterge Paneli**

![Gosterge Paneli](docs/screenshots/dashboard.png)

**Musteri Listesi**

![Musteri Listesi](docs/screenshots/petowner.png)

**WhatsApp Entegrasyonu**

![WhatsApp Sayfasi](docs/screenshots/whatsapp.png)

**Bayi Paneli**

![Bayi Paneli](docs/screenshots/dealer-panel.png)

---

## Sistem Mimarisi

Proje tek bir monorepo altinda uc ana bolumden olusuyor:

```
VoxCRM
├── VoxCrm.*         (C# / .NET 8)  <-- Ana uygulama
├── whatsapp-gateway (Python + Node) <-- WhatsApp gecidi
└── backups/         (Bash + pg_dump) <-- Yedekleme klasoru
```

Servisler arasi iletisim asagidaki gibi isler:

```
Tarayici
   |
   v
VoxCrm.Web (:5114)
   |   |
   |   +-- WhatsAppNotifications tablosuna kayit ekler (Pending)
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
WhatsApp (gercek mesaj gonderiyor)
```

Bildirim akisi kasitli olarak asenkrondir: Web uygulamasi dogrudan gateway'i cagirmaz. Mesaj once `WhatsAppNotifications` tablosuna `Pending` durumuyla yazilir; gateway periyodik olarak bu tabloyu okur, kilitleme (SELECT FOR UPDATE SKIP LOCKED) yaparak mesajlari isleme alir ve WhatsApp'a iletir.

---

## Klasor Yapisi ve Amaci

### VoxCrm.Domain/

**Amac:** Uygulamanin cekirdek is mantigi ve entity'leri burada tanimlidir. Hicbir disariya bagimlilik yoktur; sadece saf C# siniflarindan olusur.

**Icerik:**
- `Entities/` — Tum veritabani entity'leri (`Clinic`, `PetOwner`, `Patient`, `Appointment`, `VaccinationRecord`, `WhatsAppNotification`, vb.)
- `Common/` — Paylasilan arayuzler (`ITenantEntity` gibi tenant izolasyonunu zorunlu kilan arayuzler)

**Kiminle konusur:** Hicbir servis katmaniyla dogrudan iletisme gecmez. Tum diger katmanlar Domain'e baglidir, Domain hicbir katmana bagimli degildir.

---

### VoxCrm.Application/

**Amac:** CQRS (Command Query Responsibility Segregation) kalibini hayata geciren katman. Komutlar (yazma), sorgular (okuma) ve is kurallari buradadir.

**Kiminle konusur:** Sadece `VoxCrm.Domain` entity'lerini kullanir. Infrastructure ve Web katmanlari tarafindan cagrilir.

---

### VoxCrm.Infrastructure/

**Amac:** Veritabani erisimi, migration'lar ve zamanlanmis arka plan isleri burada yer alir.

**Icerik:**
- `Data/` — `VoxCrmDbContext` (Entity Framework Core), `DbSeeder` (baslangic test verileri)
- `Migrations/` — PostgreSQL sema migration dosyalari
- `Jobs/` — `ReminderJob.cs`: Hangfire tarafindan her gun tetiklenen asi hatirlatma islemi; son tarihi gecmis `VaccinationRecord` kayitlari icin `WhatsAppNotifications` tablosuna otomatik `Pending` kayit olusturur

**Kiminle konusur:** `VoxCrm.Domain` entity'lerini kullanir. `VoxCrm.Web` ve `VoxCrm.Api` tarafindan servis olarak inject edilir.

---

### VoxCrm.Web/

**Amac:** Klinik ve bayi kullanicilari icin ASP.NET Core MVC web uygulamasi. Bootstrap 5 ve Razor view'lar ile olusturulmus UI.

**Icerik:**
- `Controllers/` — 13 controller: Auth, Home, PetOwner, Patient, Appointment, Vaccination, Finance, WhatsApp, ServiceItem, VaccineType, Muayene, ClinicSettings, Dealer
- `Views/` — Razor sayfalari (her controller icin ayri klasor)
- `Services/` — HTTP istemcileri ve is mantigi servisleri
- `wwwroot/` — Statik dosyalar (CSS, JS, resimler)

**Onemli guvenlik mekanizmalari:**
- `[Bind("Id,Alan1,Alan2")]` attribute'u ile Mass Assignment koruması
- `ModelState.Remove()` ile tenant ID'nin form gonderiminde manipüle edilmesi engellenir
- Global Query Filter'lar sayesinde her sorgu otomatik olarak o klinige ait verileri getirir (tenant izolasyonu)

**Kiminle konusur:** PostgreSQL'e `VoxCrmDbContext` uzerinden erisir. `whatsapp-gateway/gateway-api`'ye JWT imzali HTTP istekleri atar (baglanti durumu, QR kodu). Hangfire panel'i icerir.

---

### VoxCrm.Api/

**Amac:** Yalnizca `whatsapp-gateway`'e acilan REST API. Tarayiciya veya son kullaniciya acik degildir. JWT (HS256, scope tabanli) ile korunur.

**Endpointler:**
- `POST /api/whatsapp/notifications/claim` — Gateway, isleme alacagi mesajlari ceker (SELECT FOR UPDATE SKIP LOCKED)
- `POST /api/whatsapp/notifications/{id}/status` — Gateway, mesaj sonucunu bildirir
- `POST /api/whatsapp/notifications/recover-expired-processing` — Kilit suresi dolan mesajlari kurtarir
- `POST /api/whatsapp/inbound` — WhatsApp'tan gelen mesajlari kaydeder
- `GET /api/health` — Saglik kontrolu

**Kiminle konusur:** Sadece `whatsapp-gateway/gateway-api` bu API'yi cagirir. PostgreSQL'e `VoxCrmDbContext` uzerinden dogrudan erisir.

---

### VoxCrm.IntegrationTests/

**Amac:** Uygulama genelinde entegrasyon testleri. API endpoint davranislari ve veritabani katmani burada test edilir.

---

### whatsapp-gateway/

**Amac:** VoxCRM'den bagimsiz calisabilen, iki servisten olusan WhatsApp gecidi. `voxcrm-whatsapp-gateway` deposundan monorepo'ya tasindi.

#### whatsapp-gateway/gateway-api/ (Python / FastAPI)

**Gorev:** Orchestrasyon servisi. VoxCrm.Api'yi poll eder, gonderim siralamasini yonetir, klinik WhatsApp durumlarini takip eder, kimlik dogrulama ve saglik endpointlerini sunar.

**Temel dosyalar:**
- `app/main.py` — API endpoint'leri, polling dongusu, saglik kontrolu
- `app/sender.py` — Klinik bazli gonderim zamanlayici (per-clinic interval + jitter)
- `app/voxcrm_client.py` — VoxCrm.Api ile HTTP konusmasi (claim, status)
- `app/worker_client.py` — wa-worker ile HTTP konusmasi (send, status)
- `app/auth.py` — JWT uretimi ve dogrulamasi
- `app/config.py` — `.env` dosyasindan ortam degiskenleri
- `alembic/` — Gateway veritabani migration'lari (PostgreSQL: `voxcrm_gateway_dev`)

#### whatsapp-gateway/wa-worker/ (Node.js / Baileys)

**Gorev:** WhatsApp linked-device protokolunu yoneten ic servis. QR kod yasam dongusu, oturum sifreleme ve gercek mesaj iletiminden sorumludur. Internete dogrudan bu servis baglanir; gateway-api ve VoxCrm.Web'e kapatilidir.

**Temel dosyalar:**
- `src/baileysProvider.js` — Baileys kutuphanesi entegrasyonu, oturum yonetimi
- `src/sessionStore.js` — Klinik bazli oturum saklama ve sifreleme
- `src/security.js` — Ic token dogrulamasi (gateway-api'den gelen istekleri authenticate eder)
- `src/app.js` — Express endpoint'leri (send, connect, disconnect, status, qr)

#### whatsapp-gateway/scripts/

**Gorev:** Bakim ve yedekleme araclari.

- `backup.sh` — PostgreSQL dump (voxcrm_dev + voxcrm_gateway_dev), klinik bazli JSON exportu, WhatsApp session arsivi. Ozel klasorler: `backups/daily/`, `backups/weekly/`, `backups/monthly/`
- `restore.sh` — Belirlenen backup snapshot'ini yerel veritabanina yukler (kasitli olarak interaktif; yanlislikla calistirilmaz)
- `test-all.sh` — .NET build + NuGet audit + pytest + Vitest + syntax check + backup smoke test'ini sirayla calistirir
- `test-backup-smoke.sh` — Gercek veri silmeden backup script'ini test eder

#### whatsapp-gateway/.env.example

Calistirmak icin kopyalanmasi gereken ortam degiskeni sablonu. Gercek `.env` dosyasi repoya girmez.

---

### backups/

**Amac:** Yedekleme klasoru yapisi. Gercek dump dosyalari `.gitignore` ile Git disinda tutulur; sadece klasor iskelet yapisi (`.gitkeep`) repoda yer alir.

```
backups/
├── daily/    # Son 7 gunluk yedekler (backup.sh otomatik temizler)
├── weekly/   # Son 4 haftanin pazartesi yedekleri
└── monthly/  # Son 3 ayin 1. gunu yedekleri
```

**Her snapshot icerigi:**
- `voxcrm-db.dump` — Ana PostgreSQL veritabani (pg_dump -Fc)
- `gateway-db.dump` — Gateway PostgreSQL veritabani
- `clinic-{uuid}.json.gz` — Klinik bazli okunabilir JSON export (hasta, sahip, randevu, mali kayitlar, WhatsApp verileri)
- `whatsapp-sessions.tar.gz` — Baileys oturum dosyalari arsivi

---

### docs/screenshots/

**Amac:** README icin uygulama ekran goruntuleri.

---

## Servisler Arasi Iliski Haritasi

```
+---------------------------+
|       Tarayici (UI)       |
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
            | (HTTP, ic token)
            v
+---------------------------+     +-------------------+
|      wa-worker            |---> | WhatsApp          |
|    Node.js :8090           |     | (Baileys)         |
+---------------------------+     +-------------------+
```

---

## Teknoloji Yigini

| Katman | Teknoloji |
|--------|-----------|
| Web Uygulamasi | C# / .NET 8, ASP.NET Core MVC, Razor Pages |
| API | C# / .NET 8, ASP.NET Core Minimal API |
| ORM | Entity Framework Core 9, Code-First Migrations |
| Veritabani | PostgreSQL 16 |
| Arka Plan Isleri | Hangfire (PostgreSQL storage) |
| WhatsApp Gateway | Python 3.12 / FastAPI, SQLAlchemy, Alembic |
| WhatsApp Istemci | Node.js 22 / Baileys (linked-device) |
| Container | Docker Compose (gelistirme), OrbStack (yerel) |
| Frontend | Bootstrap 5, jQuery, Vanilla CSS/JS |

---

## Kurulum ve Calistirma

### Onkosullar

- .NET 8 SDK
- Python 3.12
- Node.js 22
- PostgreSQL 16 (OrbStack veya Docker)
- Redis (gateway icin)

### 1. Veritabani Olustur

```bash
# OrbStack veya Docker ile:
docker start voxcrm-postgres voxcrm-redis
```

### 2. Ana Uygulamayi Baslat

```bash
# Baglanti ayarlarini guncelle:
# VoxCrm.Web/appsettings.Development.json
# VoxCrm.Api/appsettings.Development.json

# Migration'lari uygula:
dotnet ef database update --project VoxCrm.Infrastructure --startup-project VoxCrm.Web

# API'yi baslat:
dotnet run --project VoxCrm.Api

# Web uygulamasini baslat:
dotnet run --project VoxCrm.Web
```

### 3. WhatsApp Gateway'i Baslat

```bash
cd whatsapp-gateway

# .env olustur:
cp .env.example .env
# .env icindeki VOXCRM_API_BASE_URL degerini duzenle

# Docker ile (onerilen):
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

### 4. WhatsApp Baglantisi

Tarayicida `VoxCrm.Web > WhatsApp` menusunden klinige ait "Bagla" dugmesine tiklayarak QR kod taranir. QR kodunu wa-worker uretir, gateway-api araciligiyla Web'e iletilir.

---

## Yedekleme

```bash
# Gunluk yedek al:
./whatsapp-gateway/scripts/backup.sh

# Yedekler su klasore yazilir:
# backups/daily/YYYYMMDDTHHMMSSZ/

# Geri yukle (dikkatli kullan, yerel veritabanini degistirir):
./whatsapp-gateway/scripts/restore.sh backups/daily/<timestamp>
```

---

## Testler

```bash
# Tum test suiti (build + NuGet + pytest + Vitest + backup smoke):
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

## Test Kullanicilari (Gelistirme Ortami)

Veritabani ilk calismada `DbSeeder` tarafindan asagidaki test verileriyle doldurulur:

| Rol | E-posta | Sifre |
|-----|---------|-------|
| Bayi Yoneticisi | admin@voxcrm.com | Admin123! |
| Klinik - Mutlu Patiler | iletisim@mutlupatiler.com | Klinik123! |
| Klinik - Sifa Vet | bilgi@sifavet.com | Klinik123! |
