# Yangi sessiyada davom ettirish — TOPSHIRIQ

> Bu fayl **kontekst tugaganda** ish uzilib qolmasligi uchun yuritiladi.
> Yangi sessiya shu faylni o'qib, oldingi suhbatsiz davom eta oladi.
>
> **Oxirgi yangilanish:** 2026-07-31, tungi sessiya (PM + parallel agentlar)
> **Holat:** ish COMMIT QILINMAGAN — `main` da, working tree'da turibdi.
> Repo LOKAL, GitHub remote sozlanmagan.
>
> Batafsil jurnal: `docs/PROGRESS.md` · Reja: `docs/ROADMAP.md` ·
> Dizayn ko'chirish: `docs/DIZAYN_KOCHIRISH_REJASI.md` · Shartnoma: `docs/SPEC.md`

---

## 1. HOLATNI TIKLASH

```bash
cd ~/Documents/Projects/zinnur-v2
docker compose up -d
docker compose ps
curl -s localhost:5080/health/ready
```

| | |
|---|---|
| Frontend | http://localhost:5173 |
| API + Swagger | http://localhost:5080/swagger |
| Admin | `admin@zinnur.uz` / `Admin!2345` |
| Ustoz | `teacher@zinnur.uz` / `Demo!2345` |
| O'quvchi | `student@zinnur.uz` / `Demo!2345` |
| Postgres / Redis / MinIO (host) | 5440 / 6390 / **9010** (konsol 9011) |
| Eski loyiha (TEGILMAYDI) | http://localhost:8000 (`zinnur-legacy`) |

### ⚠️ Образ eskirmaganmi — HAR SAFAR tekshiring

```bash
docker inspect zinnur/api:dev --format '{{.Created}}'
docker inspect zinnur/web:dev --format '{{.Created}}'
docker compose build api web && docker compose up -d api web
```

Bu tuzoq bu loyihada **bir necha marta** vaqt yedi: kod yozilgan, lekin
konteyner eski образdan ishlab endpoint **404** qaytargan (javob tanasi ham
bo'sh — UI'da tushunarli xato ko'rinmaydi).

### Build va test — AYNAN ishlaydigan buyruqlar

```bash
cd ~/Documents/Projects/zinnur-v2/backend

# Build (NuGet keshi bilan ~2 s, keshsiz ~4 daqiqa)
docker run --rm -v "$PWD":/src -w /src -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build Zinnur.sln -v q --nologo

# Testlar — jonli Postgres/Redis/MinIO ga ulanadi
docker run --rm --add-host=host.docker.internal:host-gateway \
  -v "$PWD":/src -w /src -v zinnur-nuget-cache:/root/.nuget/packages \
  -e TEST_POSTGRES="Host=host.docker.internal;Port=5440;Database=postgres;Username=zinnur;Password=zinnur_dev_only_change_me" \
  -e TEST_REDIS="host.docker.internal:6390" \
  -e TEST_STORAGE_URL="http://host.docker.internal:9010" \
  -e TEST_STORAGE_BUCKET="zinnur-dev" \
  -e TEST_STORAGE_ACCESS_KEY="zinnur_dev_minio" \
  -e TEST_STORAGE_SECRET_KEY="zinnur_dev_minio_secret" \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet test Zinnur.sln --nologo -v q
```

**Kutilgan natija: 537 unit + 323 integratsiya = 860 test, 0 yiqilgan.**

⚠️ `TEST_STORAGE_*` bermasangiz 5 ta fayl testi yiqiladi (MinIO'ga yetmaydi).

---

## 2. HOZIRGI HOLAT

```
Build       : backend 0 xato / 0 ogohlantirish · frontend + eslint toza
Testlar     : 860 yashil (oldingi sessiyada 614 edi)
Migratsiya  : 11 ta, oxirgisi 20260730200125_AddNotificationOutbox...
```

### ✅ Bu sessiyada tugatildi

- **FAZA 5.4** fayl ombori (MinIO + `GET /submissions/files/{id}`)
- **FAZA 5.2** notifikatsiya outbox + worker
- **FAZA 5.1** Telegram bot + Mini App autentifikatsiyasi
- Moliya `GET /payments/summary` + CSV eksport
- Super-admin sozlamalar paneli (**backend**)
- Tekshirish navbati UI (klaviatura yorliqlari + fayl ochish)
- Moliya dashboard UI

---

## 3. KEYINGI QADAM — shu yerdan davom eting

Quyidagilar boshlangan yoki rejalashtirilgan, lekin **sessiya limiti** tufayli
tugallanmagan. Tartib — muhimlik bo'yicha.

| № | Ish | Holat |
|---|---|---|
| 1 | **Sozlamalarni runtime qilish** (`IOptions` → `ISettingsResolver`) | boshlangan, kod yozilmagan |
| 2 | **Sozlamalar paneli UI** (shartnoma tayyor, 4-bo'limga qarang) | boshlangan, kod yozilmagan |
| 3 | **Guruh chati** (backend + UI) — parite bo'shlig'i #1/#2 | `GroupChatChannel.cs` yozilgan, qolgani yo'q |
| 4 | **FAZA 5.5** fon vazifalari + DB leader lock | boshlangan, kod yozilmagan |
| 5 | **FAZA 5.3** dars yozuvi (LiveKit Egress → R2) | boshlanmagan |
| 6 | **FAZA 7** ma'lumot ko'chirish + staging + prod deploy | boshlanmagan |

### 3.1. Sozlamalar panelining halol cheklovi — 1-ishning MOHIYATI

Panel 27 sozlamani ko'rsatadi, lekin **faqat 2 tasi tahrirlanadi**
(`finance.block_threshold`, `finance.block_scope`). Qolgani ishga tushishda
`IOptions<T>` singleton'ga **qotadi** — bazadan boshqarilsa panel "saqlandi"
derdi-yu tizim eskisi bilan ishlayverardi (**jimgina yolg'on**).

Loyiha egasi "barcha env sozlamalarini paneldan boshqarish" ni so'ragan, ya'ni
bu ish uning talabining o'zagi.

**Retsept:** iste'molchini `ISettingsResolver` ga o'tkaz + registrda
`Source = Database`. Nomzodlar: Telegram (`BotToken`, `WebhookSecret`,
`MiniAppUrl`), Storage (`ServiceUrl`, `Bucket`, `AccessKey`, `SecretKey`),
LiveKit kalitlari — hammasi har so'rovda ishlatiladi.

⚠️ **Uch tuzoq:**
1. `R2SubmissionStorage` `_options` ni konstruktorda oladi — har chaqiruvda
   olinishi kerak.
2. Telegram yuboruvchisida `.RemoveAllLoggers()` bor (bot tokeni logga ochiq
   tushmasligi uchun) — **buni tasodifan orqaga qaytarmang**, regressiya testi
   bor: `TelegramHttpClient_HasNoLoggingHandlers`.
3. `ValidateOnStart` "TO'LIQ yoki BO'SH" himoyasi ishga tushish paytida
   ma'nosini yo'qotadi — uni **yozish paytiga** (validatsiyaga) ko'chiring.

⚠️ **Kesh kerak** (har so'rovda bazaga borish qimmat), lekin kesh bilan
yangilanish ko'rinmay qolishi mumkin — ya'ni tuzatayotgan muammo boshqa
shaklda qaytadi. Ko'p instance holatini hisobga oling.

---

## 4. SOZLAMALAR UI UCHUN SHARTNOMA (tayyor, kod kutilmoqda)

Hammasi `[Authorize(Roles="Admin")]` — Academic ham ko'rmaydi.

| Metod | Yo'l | Javob |
|---|---|---|
| GET | `/api/v1/settings` | `{ groups: [...] }` |
| GET | `/api/v1/settings/{key}` | `SettingDto` |
| PUT | `/api/v1/settings/{key}` | tana `{"value":"..."}` |
| POST | `/api/v1/settings/{key}/reset` | `SettingDto` |

Xatolar: **400** (validatsiya yoki "faqat o'qish" — sabab
`problem.errors[key][0]`), **403** (rol), **404** (noma'lum kalit), **401**.

`SettingDto`: `key, group, groupName, name, description, kind, isSecret,
isEditable, readOnlyReason, origin, isSet, value, maskedValue, defaultValue,
constraints{choices, minimum, maximum, maxLength, format}, updatedAt, updatedById`

Enumlar (JSON'da SATR): `kind` = `Text|Number|Money|Toggle|Choice|Secret` ·
`origin` = `Default|Environment|Database` · `format` = `None|Url|TimeZone` ·
`group` = `General|Finance|Telegram|LiveKit|Storage|Security`

**UI qoidalari:**
1. Qiymat **har doim satr** (`"true"`, `"600000"`).
2. `isSecret` → `value` va `defaultValue` DOIM `null`; faqat `maskedValue` +
   `isSet`. **"Ko'rsatish" tugmasi BO'LMASIN** — server sirni umuman bermaydi.
3. `isEditable=false` → maydon o'chiq, yonida `readOnlyReason` matni ko'rinsin.
4. `origin !== "Database"` → "standartga qaytarish" tugmasi ma'nosiz, yashiring.
5. Chegaralar `constraints` dan olinsin, kodda takrorlanmasin.
6. 🔴 **Bo'sh sir maydoni:** foydalanuvchi maskani ko'rib, maydonga tegmasdan
   "Saqlash" bossa — bo'sh qiymat yuborilib **sir o'chib ketmasligi** kerak.

---

## 5. TELEGRAM — frontend integratsiyasi uchun shartnoma

**`POST /api/v1/telegram/mini-app/auth`** (anonim)
```jsonc
{ "initData": "<window.Telegram.WebApp.initData — AYNAN, o'zgartirmasdan>" }
// 200 -> mavjud AuthResponse bilan BIR XIL (accessToken, refreshToken, user)
```

| Kod | Ma'no | Frontend nima qilsin |
|---|---|---|
| 200 | kirish | tokenlarni odatdagidek saqla |
| 401 | imzo yaroqsiz/eskirgan | "Ilovani yopib, qaytadan oching" |
| 403 | `Student` emas | "Xodimlar email+parol bilan kiradi" |
| 409 | Telegram bog'lanmagan | **botga yo'naltir** — "raqamni ulashing" |
| 429 | rate-limit | `Retry-After` sarlavhasi bor |
| 503 | Telegram sozlanmagan | email+parol ekraniga qaytar |

🔴 **Telefon kiritish oynasi YARATILMASIN** — bog'lash faqat botda
(eski tizimning X-1/X-1b zaifligi aynan qo'lda kiritishdan kelib chiqqan).

---

## 6. TUZOQLAR — vaqt yo'qotmaslik uchun

1. **`PUT` = TO'LIQ ALMASHTIRISH.** Yuborilmagan maydon `null` bo'lib bazaga
   tushadi. Har tahrirlash formasi mavjud qiymatlarni yuklab, HAMMASINI
   qaytarsin. (Sozlamalar bundan mustasno — har kalit alohida resurs.)
2. **Migratsiyasiz model o'zgarishi** → `PendingModelChangesWarning` → ilova
   UMUMAN ko'tarilmaydi (build yashil bo'lsa ham).
3. **Parallel agentlar migratsiya yaratmasin** — zanjir buziladi. Koordinator
   batch oxirida BITTA migratsiya yaratsin. (Bu sessiyada shunday qilindi va
   ishladi: uch agentning model o'zgarishi bitta bog'inga tushdi.)
4. **Npgsql 9 da `UseXminAsConcurrencyToken()` YO'Q** →
   `Property<uint>("xmin").IsRowVersion()`.
5. **400 va 409 boshqa joyda:** 400 da sabab `problem.errors` da, 409 da
   `detail` to'liq. `toUserMessage(error)` ikkalasini to'g'ri o'qiydi.
6. **Qidiruv minimal uzunligi:** foydalanuvchi 3, guruh 2, kurs 2 belgi.
7. **`reorder` TO'LIQ ro'yxat kutadi** (yetishmasa 400).
8. **Redis kalitlari makon bilan** (`Redis:KeyPrefix`).
9. **Tema `<html>` ga qo'yiladi**, karkas `<div>` iga emas (teleport).
10. **Oltin fonda `text-white` ishlatmang** — kontrast ~1.9:1.
11. **`DayOfWeek`:** eski Python dushanba=0, .NET yakshanba=0 →
    `dotnet = (python + 1) % 7`. Ma'lumot ko'chirishda MAJBURIY.
12. **LiveKit ICE:** prod'da `NODE_IP=127.0.0.1` qolib ketsa media hech qachon
    ishlamaydi. Compose overlay'lari `environment` ni BIRLASHTIRADI —
    prod'da `!reset null` kerak. Yangi sozlama qo'shganda **literal emas,
    `${VAR:-}` havolasi** yozing, shunda dev qiymati prod'ga oqmaydi.
13. **`CA1848`** (`[LoggerMessage]`), **`CA1305`** (`CultureInfo.InvariantCulture`),
    `CA1711` (tur nomi `Queue` bilan tugamasin), `CA1716`.
14. **Kalendarda `localDate`** ishlatilsin — `scheduledStart` dan brauzerda
    sana chiqarilsa, boshqa vaqt mintaqasida dars kechagi kunga tushadi.
15. **zsh:** `GID` — tizim o'zgaruvchisi, skriptda ishlatib bo'lmaydi.
16. 🔴 **`SubmissionFiles` — `objectKey` UI'ga chiqmasin** (ichki ombor kaliti).
17. 🔴 **Telegram: `contact.user_id == from.id` tekshiruvi shart.** Telegram'da
    BOSHQA odamning kontaktini yuborish mumkin — bu tekshiruvsiz akkaunt
    egallash yo'li ochiq qoladi.

---

## 7. TOZALANMAGAN SINOV MA'LUMOTI

Oldingi sessiyalardan qolgan (dev bazada):
- tariflar **"UI sinov tarifi"** (guruh `UI-MOLIYA-TEST`), **"Smoke tarif"**
  (guruh `MOLIYA-SMOKE`)
- foydalanuvchilar `Begona O'quvchi (begona-...@zinnur.uz)`,
  `Curl Oquvchi (curl-st-...@zinnur.uz)`
- **`assignment id=7`** — "QA-NAVBAT sinov vazifasi (o'chirilishi mumkin)".
  API orqali o'chirib bo'lmaydi (409: javoblar topshirilgan), `psql` bilan
  olib tashlash mumkin.
- ikkita `zinnur_test_*` bazasi (uzilib qolgan test yurishidan)

---

## 8. ISH USLUBI — nima ishladi

1. **Buyruq bermang, muammoni tushuntiring.** Agentlar shu tufayli mustaqil
   ikkita HAQIQIY bug topdi: SigV4 imzosida port tushib qolishi va Telegram
   tokenining logga ochiq tushishi. Ikkalasi ham topshiriqda yo'q edi.
2. **Hisobotni DALIL bilan tekshiring.** Har agent hisobotidan keyin
   koordinator o'zi build, test va jonli tekshiruv qildi. Bir agent o'z
   tiplarini "isbotlanmagan" deb belgiladi — koordinator haqiqiy API javobi
   bilan solishtirib tasdiqladi.
3. **Umumiy fayllarni oldindan taqsimlang.** Har agentga "SENIKI / UMUMIY
   (faqat `Edit`, `Write` emas) / TEGMA" ro'yxati berildi. Bitta ham
   to'qnashuv bo'lmadi.
4. **Migratsiyani markazlashtiring** (3-tuzoq).
