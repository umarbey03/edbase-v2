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
| Admin | `+998900000001` (`admin@zinnur.uz`) |
| Ustoz | `+998900000002` (`teacher@zinnur.uz`) |
| O'quvchi | `+998900000003` (`student@zinnur.uz`) |
| Postgres / Redis / MinIO (host) | 5440 / 6390 / **9010** (konsol 9011) |
| Eski loyiha (TEGILMAYDI) | http://localhost:8000 (`zinnur-legacy`) |

### 🔴 KIRISH — EMAIL VA PAROL YO'Q (2026-08-13 dan)

`POST /api/v1/auth/login` **olib tashlandi**. Kirish ikki bosqichli:

```
POST /api/v1/auth/phone/request-code   { "phone": "+998900000001" }
POST /api/v1/auth/phone/verify         { "phone": "...", "code": "123456" }
```

Kod **Telegram orqali** yuboriladi, ya'ni brauzerda kirish uchun profilga
Telegram BOG'LANGAN bo'lishi shart.

**⚠️ DEV MASHINASIDA KOD KELMAYDI** — seed qilingan foydalanuvchilarda
`TelegramId` yo'q va lokal botda token ham yo'q. Ikki yo'l bor:

```bash
# A) Haqiqiy bot bilan: paneldan token qo'yib, botga raqamni ulash.

# B) Kodni bazadan o'qish (bot kerak emas, faqat token sozlangan bo'lsin):
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "SELECT \"Body\" FROM \"MessageOutbox\" WHERE \"TemplateKey\"='auth_login_code' \
   ORDER BY \"Id\" DESC LIMIT 1;"

# C) Telegram'ni qo'lda bog'lash (eng tez yo'l dev uchun):
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "UPDATE \"Users\" SET \"TelegramId\"=111111111, \"TelegramLinkedAt\"=now() \
   WHERE \"Email\"='admin@zinnur.uz';"
```

Testlarda kirish HTTP orqali EMAS — `ZinnurApiFactory.LoginAsAdminAsync()`
tokenni to'g'ridan-to'g'ri yasaydi (sabab o'sha faylda). Oqimning O'ZI
`PhoneLoginEndpointsTests` da to'liq sinaladi.

### 🧪 NAMUNAVIY (DEMO) MA'LUMOT — qo'lda tekshirish uchun

Bo'sh bazada `SEED_DEMO=true` (`.env`) qo'yilsa, ilova ishga tushganda
TO'LIQ o'quv markazi yoziladi: o'quv bo'limi + 2 ustoz + 2 kurator +
12 o'quvchi, 5 guruh (jumladan individual, kurator va ARXIV), kurs →
modul → darslar (bittasida **3 qismli video**), o'tgan/kelgusi/hozir
boshlanadigan darslar, davomatning BARCHA holati, dars baholari,
vazifa javoblarining har bir holati, testlar, **chegaradan oshgan
qarzdor**, chat (ikki kanal + DM + darsga bog'langan savol), yozuvlar
(yashirilgani va sifat nazorati bilan) va o'qilmagan bildirishnomalar.

```bash
# Kirish raqamlari jadvali — LOGDA:
docker compose logs api | grep -A 25 "Namunaviy hisoblar"

# Kirish kodi soxta Telegram ID'ga ketadi, ya'ni telefonga KELMAYDI:
docker compose exec -T postgres psql -U zinnur -d zinnur -c \
  "SELECT \"Body\" FROM \"MessageOutbox\" WHERE \"TemplateKey\"='auth_login_code' \
   ORDER BY \"Id\" DESC LIMIT 1;"
```

🔴 **Uch qatlamli himoya** (`DemoDataSeeder` izohi): (1) `Seed__Demo`
oshkor kaliti, standarti `false`; (2) bazada 3 tadan ko'p foydalanuvchi
bo'lsa seeder ISHLAMAYDI va logga xato yozadi; (3) marker profil
(`academic@zinnur.uz`) — ikkinchi ishga tushirish hech nima qilmaydi.

⚠️ **VIDEO OCHILMAYDI (404).** Dars videolari va dars yozuvlari faqat
METAMA'LUMOT sifatida yoziladi (yaroqli MP4 ni kodsiz yasab bo'lmaydi).
Rasm va hujjat biriktirmalari esa HAQIQATAN omborga yoziladi va
ochiladi. Batafsil — `DemoMedia.cs` izohi.

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
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build Zinnur.sln -v q --nologo --no-incremental

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

**Kutilgan natija (2026-08-11 da o'lchangan): `main` da 621 unit + 434
integratsiya = 1055 test, 0 yiqilgan.** Hujjatdagi eski "413 / 1034" raqami
noto'g'ri edi — har safar buyruqni yurgizib haqiqiy bazani o'lchang.

⚠️ `TEST_STORAGE_*` bermasangiz 5 ta fayl testi yiqiladi (MinIO'ga yetmaydi).
⚠️ `--no-incremental` MAJBURIY: Docker bind-mount'da inkremental build eski DLL
bilan "succeeded" deb yozadi va sizni chalg'itadi (bu bir marta yuz bergan).

---

## 2. HOZIRGI HOLAT

```
Build       : backend 0 xato / 0 ogohlantirish · frontend + eslint toza
Testlar     : 1034 yashil (sessiya boshida 614 edi)
Migratsiya  : 13 ta, oxirgisi AddSessionRecordings
```

### ✅ Tugatildi

**FAZA 5 to'liq yopildi:**
- **5.1** Telegram bot + Mini App autentifikatsiyasi (backend)
- **5.2** notifikatsiya outbox + worker (commit-then-send, `SKIP LOCKED`)
- **5.3** dars yozuvi (LiveKit Egress + webhook imzo + watchdog)
- **5.4** fayl ombori (MinIO + `GET /submissions/files/{id}`)
- **5.5** fon vazifalari + Postgres advisory leader lock

**FAZA 7:** ma'lumot ko'chirish vositasi + sintetik sinov + hujjat
(`docs/MA_LUMOT_KOCHIRISH.md`). 🔴 **Prod'da hech qachon yurgizilmagan.**

**Boshqa:**
- Guruh chati backend (ikki kanal, cursor sahifalash, o'qilmaganlar, SignalR)
- Sozlamalar paneli: backend + UI, **13 sozlama runtime** (avval 2 ta edi)
- Moliya `GET /payments/summary` + CSV eksport + dashboard UI
- Tekshirish navbati UI (klaviatura yorliqlari + himoyalangan fayl ochish)

---

## 3. KEYINGI QADAM — shu yerdan davom eting

Backend fazalari yopildi. Qolgani — asosan **frontend** va **deploy**.

| № | Ish | Holat |
|---|---|---|
| 1 | **Telegram Mini App frontend** — o'quvchi kirish oqimi | backend tayyor, UI qolgan |
| 2 | **Guruh chati UI** — brauzerda ko'rinish | protokol isbotlangan, UI qolgan |
| 3 | **Dars yozuvi UI** — parite bo'shlig'i #4 | backend tayyor, UI qolgan |
| 4 | **Hub xato tarjimasi uchun regressiya testi** | ishlaydi, lekin test YO'Q |
| 5 | **Qolgan parite bo'shliqlari** — #5 xabarlar, #7 KPI, #8 profil modali, #9 tranzaksiya tarixi | boshlanmagan |
| 6 | **Deploy** — staging, prod cutover | boshlanmagan |

### 3.1. Guruh chati UI — protokol ALLAQACHON isbotlangan

Koordinator ikki haqiqiy SignalR WebSocket klienti bilan tekshirdi (14/15):
realtime yetkazish · **yuboruvchiga ham broadcast kelishi** · kanal
izolyatsiyasi · emoji buzilmasligi · tarix tartibi va takrorsizligi.

🔴 **UI uchun eng muhim natija:** yuboruvchi o'z xabarining broadcast'ini ham
oladi, ya'ni **`id` bo'yicha dedupe SHART** — aks holda xabar ikki marta
ko'rinadi.

⚠️ Brauzer sinovida **ikki alohida kontekst** naqshi bu muhitda ikki marta
qotib qolgan. Bitta kontekst + protokol darajasidagi Node sinovi ishonchli.

### 3.2. FAZA 7 — ko'chirishdan OLDIN o'qing

Vosita tayyor va sintetik ma'lumotda isbotlangan, lekin **prod'da hech qachon
yurgizilmagan**. Majburiy birinchi qadam: prod bazasining **nusxasida**
`--only=preflight`.

🔴 **Loyiha egasi qaror qabul qilishi kerak:** 18 ta jadval ko'chmaydi
(`grades`, `student_notes`, `lesson_videos`…) va `users` dagi butun shaxsiy
anketa yo'qoladi — v2 da bu maydonlar yo'q. To'liq ro'yxat:
`docs/MA_LUMOT_KOCHIRISH.md`.

### 3.3. Ochiq biznes savoli

**Juda eskirgan, lekin hech qachon boshlanmagan darslar** bilan nima qilish
kerak? Avto-yakunlash qamrovi ataylab faqat boshlangan darslar bilan
cheklangan: `AttendanceSummaryService` har `Ended` darsni "o'tkazilgan" deb
maxrajga qo'shadi, ya'ni o'tkazilmagan darsni yopish **har o'quvchining
davomat foizini jimgina pasaytirardi**. Kerak: alohida "o'tkazilmadi" holati
yoki avto-bekor qilish — bu Domain o'zgarishi.

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
| 403 | profil FAOL EMAS | "O'quv bo'limi bilan bog'laning" |
| 409 | Telegram bog'lanmagan | **botga yo'naltir** — "raqamni ulashing" |
| 429 | rate-limit | `Retry-After` sarlavhasi bor |
| 503 | Telegram sozlanmagan | "vaqtincha ishlamayapti" (zaxira yo'l YO'Q) |

⚠️ **403 ning ma'nosi 2026-08-13 da O'ZGARDI.** Ilgari u "siz xodimsiz"
degani edi (Telegram kanali `Student` bilan cheklangan edi). Endi xodim
ham Mini App orqali kiradi — rol filtri olib tashlandi, chunki
email+parol eshigi yo'q.

🔴 **MINI APP ICHIDA telefon kiritish oynasi YARATILMASIN** — bog'lash
faqat botda (eski tizimning X-1/X-1b zaifligi aynan qo'lda kiritishdan
kelib chiqqan).

★ **`LoginPage` dagi telefon formasi BOSHQA NARSA va u TO'G'RI.** Farq
hal qiluvchi: u yerda raqam faqat "kimga kod yuborilsin" degan savolga
javob beradi, kirish esa KOD bilan bo'ladi — kod hujumchi ko'ra
olmaydigan kanalga (jabrlanuvchining Telegram hisobiga) ketadi. Eski
zaiflikda esa raqamning O'ZI kirish berardi.

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
10. ~~**Oltin fonda `text-white` ishlatmang** — kontrast ~1.9:1.~~
    ❌ **2026-08-11 dan kuchda emas:** aksent indigo (`#4f4de8`), `on-brand`
    hamma joyda oq. Ranglar `frontend/src/style.css` da yagona yorug' palitra;
    qo'lda hisoblash o'rniga `frontend/scripts/contrast-audit.mjs` darvozasi
    ishlatiladi (`exit 1` bilan yiqiladi). Batafsil:
    `docs/YANGI_TALABLAR_REJASI.md` 1-bo'limi.
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
