# ZIN-NUR v2 — QOLGAN ISHLAR REJASI

> Bu fayl **o'zi yetarli** (self-contained). Yangi sessiya yoki boshqa model
> shu faylni o'qib, oldingi suhbatsiz ham ishni davom ettira oladi.
>
> | | |
> |---|---|
> | Sana | 2026-07-30 |
> | Holat | 0-faza tugadi, ishlaydigan o'zak mavjud |
> | Shartnoma | `docs/SPEC.md` — nom/imzo/port qat'iy manbai |
> | Deploy | `docs/DEPLOY_UBUNTU.md` |

---

## 0. BAZAVIY HOLAT (tugagan — qayta qilinmaydi)

### Ishlayapti va tekshirilgan

```
docker compose up -d          →  5 xizmat sog'lom
Frontend  http://localhost:5173
API       http://localhost:5080/swagger
Kirish    +998900000001 (telefon + Telegram kodi; 2026-08-13 dan parol yo'q)
```

| Qatlam | Hajm | Holat |
|---|---|---|
| `Zinnur.Domain` | 12 fayl | ✅ 0 xato, 0 ogohlantirish |
| `Zinnur.Application` | 18 fayl | ✅ |
| `Zinnur.Infrastructure` | 19 fayl | ✅ |
| `Zinnur.WebApi` | 8 fayl | ✅ |
| Frontend (Vue 3 + TS) | 49 fayl | ✅ `vue-tsc` toza |
| Docker dev + prod | 10 fayl | ✅ `compose config` xatosiz |

### Mavjud entity'lar
`User` `Course` `CourseModule` `ModuleLesson` `Group` `GroupMember`
`LiveSession` `Attendance` `ChatMessage`

### Mavjud endpointlar
```
POST /api/v1/auth/login|refresh|logout      GET /api/v1/auth/me
GET  /api/v1/live-sessions                  GET /api/v1/live-sessions/{id}
POST /api/v1/live-sessions/{id}/start|end|token
GET  /api/v1/live-sessions/{id}/messages
     /hubs/live  (SignalR: JoinSession, SendMessage, RaiseHand, LeaveSession)
```

### Frontend sahifalar
`LoginPage` `StudentHomePage` `LiveRoomPage` `NotFoundPage`

### Tasdiqlangan muhim natijalar
- LiveKit `/rtc/validate` bizning tokenni **qabul qildi** (`success`), soxta tokenni rad etdi (401)
- Docker loyihalari ajratilgan: `zinnur-legacy` va `zinnur-v2` (konteyner/tarmoq/volume alohida)

---

## ⚠️ MAJBURIY QOIDALAR (har fazada amal qilinadi)

Bular eski tizim auditidan chiqqan — takrorlanmasligi shart:

1. **Pul `decimal`, hech qachon `float`.** Eski tizimda `float` edi.
2. **Vaqt `DateTimeOffset` (UTC).** Naive datetime taqiqlanadi.
3. **`ActualStart ??= now`** — hech qachon qayta yozilmaydi.
4. **Davomatda `FirstJoinAt` va `LastJoinAt` alohida** — qayta ulanishda vaqt ikki marta qo'shilmasin.
5. **Presence/kesh/rate-limit Redis'da**, jarayon xotirasida emas.
6. **Controller yupqa** (≤20 satr), SQL faqat Infrastructure'da.
7. **Har endpointda Pydantic/DTO** — qo'lda `dict`/anonim obyekt yo'q.
8. **`v-html` taqiqlanadi** frontendda.
9. **Og'ir amal (bcrypt, fayl, hisobot) — thread pool'ga** yoki fon navbatiga.
10. **Har PR:** `dotnet build` 0 ogohlantirish + `vue-tsc` toza + testlar yashil.

---

## FAZA 1 — POYDEVORNI MUSTAHKAMLASH ⭐ BIRINCHI

> Bularsiz keyingi fazalar qurilmaydi. Yangi funksiya qo'shishdan OLDIN.

### 1.1. EF Core migratsiyalari
**Muammo:** hozir `DbInitializer` `EnsureCreated()` ishlatadi — sxema o'zgarsa ma'lumot yo'qoladi, prod'ga yaroqsiz.

- [ ] `dotnet ef migrations add Initial` (Docker SDK konteyneri orqali)
- [ ] `DbInitializer` faqat `MigrateAsync()` ishlatsin, `EnsureCreated` olib tashlansin
- [ ] Bo'sh bazadan `docker compose up` → sxema to'liq qurilishini tekshirish
- [ ] `docs/MIGRATIONS.md` — migratsiya qo'shish/qaytarish tartibi

**Tayyor mezoni:** `docker compose down -v && up -d` → baza noldan to'g'ri quriladi.

### 1.2. Testlar + CI
**Muammo:** `backend/tests/` bo'sh (0 fayl). Eski tizimda ham test yo'q edi va shuning uchun `NameError` prod'ga chiqib ketgan.

- [ ] `Zinnur.UnitTests`: Domain qoidalari
  - `LiveSession.Start/End/Extend` (5 daq oldin, 10 daq limit, `ActualStart` qayta yozilmasligi)
  - `Attendance.RegisterJoin/Leave/Finalize` (qayta ulanish stsenariysi!)
  - `ChatMessage.NormalizeBody` (500 belgi, bo'sh matn)
- [ ] `Zinnur.IntegrationTests`: `WebApplicationFactory` + Testcontainers (Postgres + Redis)
  - auth oqimi (login → me → refresh → logout → eski token bekor)
  - ruxsat matritsasi (o'quvchi begona darsga 403)
  - LiveKit token endpointi
- [ ] `.github/workflows/ci.yml`: build + test + `vue-tsc` + `npm run build`

**Tayyor mezoni:** CI yashil, coverage ≥60% Domain/Application uchun.

### 1.3. Kuzatuv (observability)
- [ ] Sentry (yoki GlitchTip) — `api` va frontend
- [ ] Serilog → strukturali JSON, `traceId` bilan
- [ ] `/health/ready` da DB+Redis+LiveKit holati

**Baho:** 1.5–2 hafta

---

## FAZA 2 — CRM O'ZAGI (admin panel)

> Eski `academic_router.py` (3 488 satr) ning asosiy qismi.

### 2.1. Foydalanuvchilar
- [ ] `UserService`: CRUD, rol o'zgartirish, faollashtirish/o'chirish, parol tiklash
- [ ] **Ruxsat qoidasi:** `academic` roli `admin`/`academic` ni tahrirlay OLMAYDI (eski tizim zaifligi X-4)
- [ ] Telefon normalizatsiyasi + **unikal indeks** (eski tizimda O(N) skan edi)
- [ ] Qidiruv: `pg_trgm` GIN indeks (`ILIKE '%...%'` indekssiz skan qilmasin)
- [ ] CSV import (paketli, xato hisoboti bilan)

### 2.2. Guruhlar
- [ ] `GroupService`: CRUD, ustoz/kurator biriktirish, a'zolik (qo'shish/pauza/chiqarish/ko'chirish)
- [ ] Kurator guruhi bog'lanishi (`curator_group_id` mantiqi)

### 2.3. Jadval generatsiyasi
- [ ] `ScheduleService`: hafta kunlari + soat → 8 oylik dars jadvali
- [ ] **`RoomName` har darsga unikal** (eski tizimning B-4 bugi)
- [ ] Jadval o'zgarganda faqat kelajakdagi darslar qayta tuziladi

**Endpointlar:** `/api/v1/users`, `/api/v1/groups`, `/api/v1/groups/{id}/members`, `/api/v1/groups/{id}/schedule`

**Baho:** 2–3 hafta

---

## FAZA 3 — O'QUV JARAYONI

### 3.1. Kurs kontenti (LMS)
- [ ] Kurs → modul → dars CRUD
- [ ] Video: Cloudflare R2 ga **to'g'ridan-to'g'ri yuklash** (presigned PUT), server orqali o'tmasin
- [ ] Sifat variantlari (360p/480p/720p)
- [ ] Ko'rish uchun presigned GET (TTL bilan)

### 3.2. Gating (sur'at nazorati)
- [ ] `GatingService`: dars N ochiq ⟺ N−1 tugatilgan VA ustoz sur'atidan oshmagan
- [ ] **Keshlanadi** (Redis, 60s) — eski tizimda har so'rovda butun daraxt qayta hisoblanardi
- [ ] Bitta dars tekshiruvi butun daraxtni qurmasin

### 3.3. Uy vazifalari
- [ ] `Assignment`, `Submission`, `SubmissionFile` entity'lari
- [ ] Topshirish (matn/rasm/audio), format cheklovi
- [ ] **Fayl yuklash: chunk bilan, chegara TEKSHIRUVDAN OLDIN** (eski tizimning Q-2 bugi)
- [ ] Bir marta topshirish + kurator ruxsati bilan qayta topshirish
- [ ] Baholash + izoh

### 3.4. Testlar (quiz)
- [ ] `Test`, `TestQuestion`, `TestOption`, `TestAttempt`, `TestAnswer`
- [ ] Avto-baholash
- [ ] **`due_at` serverda tekshiriladi** (eski tizimda umuman tekshirilmasdi)
- [ ] **Ko'p to'g'ri javob** to'g'ri hisoblansin (eski tizimda faqat oxirgisi)
- [ ] Vaqt chegarasi serverda
- [ ] Bir vaqtda ikki topshirish → race yo'q (`SELECT FOR UPDATE`)

### 3.5. Davomat
- [ ] Qo'lda tahrirlash (ustoz/o'quv bo'limi)
- [ ] Hisobotlar, sabab yozish
- [ ] **Kurator darsida bog'langan guruh a'zolari ham hisobga olinsin** (eski B-8a)

**Baho:** 3–4 hafta

---

## FAZA 4 — MOLIYA MODULI ⚠️ ENG NOZIK

> Eski `payments_svc.py` (578 satr) + `finance_svc.py`. Pul bilan ishlaydi —
> har qadamda test majburiy.

- [ ] `Tariff` (narx tarixi), `StudentDiscount` (foiz/summa, muddatli)
- [ ] `Payment` (oylik, `paid_amount` bilan qisman to'lov)
- [ ] `PaymentTransaction` (jurnal, kvitansiya raqami)
- [ ] `PaymentAudit` (kim/qachon/nimadan-nimaga)
- [ ] Balans (ortiqcha to'lov yo'qolmasin)
- [ ] `AllocatePayment` — eng eski qarzdan ketma-ket yopish
- [ ] `ReversePayment` — refund hisobni orqaga qaytarsin
- [ ] Blok qoidasi (`threshold` + `scope`), istisno
- [ ] Moliya dashboard + Excel eksport

**Majburiy:**
- Barcha summa `decimal`
- `CHECK` constraint: `paid_amount BETWEEN 0 AND amount`, `balance >= 0`
- **To'lov kiritishning YAGONA yo'li** (eski tizimda ikki xil yo'l bor edi va boshqa-boshqa natija berardi)
- Har use-case uchun unit test

**Baho:** 2–3 hafta

---

## FAZA 5 — INTEGRATSIYALAR

### 5.1. Telegram
- [ ] Bot webhook (`TELEGRAM_WEBHOOK_SECRET` **majburiy**)
- [ ] Mini App kirish — **faqat `student` roli** (eski tizimning X-1 zaifligi: telefon orqali admin akkauntini egallash mumkin edi)
- [ ] Telefon **faqat bot `contact_shared` orqali**, qo'lda kiritish YO'Q
- [ ] Xabar yuborish — **fon navbati orqali** (HTTP so'rov ichida emas)
- [ ] HTML escape (matnda `<` bo'lsa xabar yetib bormaydi)

### 5.2. Notifikatsiya
- [ ] `MessageOutbox` + `BackgroundService` worker
- [ ] Rate-limit Redis'da (Telegram 30/s global)
- [ ] Ertalabki digest, 15-daqiqa eslatma
- [ ] **Commit-then-send** (eski tizimda send-then-commit → restart'da takror xabar)

### 5.3. Dars yozuvi
- [ ] LiveKit Egress → Cloudflare R2
- [ ] **Webhook imzo tekshiruvi** (eski tizimda umuman yo'q edi — X-3)
- [ ] Watchdog: yozuv boshlanmasa qayta urinish
- [ ] Presigned ko'rish linki

### 5.4. Fayllar
- [ ] Barcha media → R2 (lokal disk YO'Q — masshtab to'sig'i)
- [ ] **Autentifikatsiyali kirish** (eski tizimda `/media` ochiq edi — X-6)

### 5.5. Fon vazifalari
- [ ] Muddati o'tgan darslarni avto-yakunlash
- [ ] Oylik to'lov yozuvlari
- [ ] **DB leader lock** — ko'p instance'da bir marta ishlasin

**Baho:** 2–3 hafta

---

## FAZA 6 — FRONTEND PANELLAR

| Panel | Sahifalar | Baho |
|---|---|---|
| **Student** | Kurs, vazifalar, testlar, davomat, to'lov, chat | 1.5 hafta |
| **Teacher** | Guruhlar, darslar, baholash, davomat, DM | 1.5 hafta |
| **Academic/Admin (CRM)** | Foydalanuvchi, guruh, jadval, kontent, moliya, hisobot | 2–3 hafta |

**Qoida:** har panel `features/` + `entities/` dan yig'iladi, sahifada biznes mantiq yozilmaydi. FSD bog'liqlik yo'nalishi `eslint` bilan tekshiriladi.

**Baho:** 5–6 hafta

---

## FAZA 7 — PRODUCTION

- [ ] **Yuklama testi: 200 bir vaqtdagi foydalanuvchi** (k6 yoki Locust)
  - SignalR ulanishlari, chat oqimi, LiveKit media
  - Aniqlash: birinchi nima yiqiladi
- [ ] Ubuntu serverga deploy (`docs/DEPLOY_UBUNTU.md` bo'yicha)
- [ ] Domen + Let's Encrypt (`app.<domen>`, `livekit.<domen>`)
- [ ] `LiveKit__PublicUrl` = `wss://livekit.<domen>` (HTTPS sahifadan `ws://` bloklanadi)
- [ ] `network_mode: host` LiveKit uchun (ICE muammosi)
- [ ] sysctl tuning qo'llash va **tasdiqlash** (UDP bufer!)
- [ ] Zaxiradan tiklashni **amalda sinash**
- [ ] Staging muhiti
- [ ] Eski tizimdan ma'lumot ko'chirish skripti
- [ ] Parallel ishlatish davri → `zinnur-legacy` ni o'chirish

**Baho:** 2 hafta

---

## UMUMIY MUDDAT

| Faza | Muddat | Bog'liqlik |
|---|---|---|
| 1. Poydevor | 1.5–2 hafta | — |
| 2. CRM o'zagi | 2–3 hafta | 1 |
| 3. O'quv jarayoni | 3–4 hafta | 2 |
| 4. Moliya | 2–3 hafta | 2 |
| 5. Integratsiyalar | 2–3 hafta | 3 |
| 6. Frontend panellar | 5–6 hafta | 2,3,4 (parallel) |
| 7. Production | 2 hafta | hammasi |
| **JAMI** | **~4–5 oy** (1 dasturchi) | |

> 2 dasturchi (backend + frontend parallel) → **~2.5–3 oy**

---

## KEYINGI QADAM

**Faza 1.1 (EF migratsiyalari)** dan boshlanadi — bu eng kichik, eng muhim va
qolgan hamma narsa unga bog'liq.

```bash
cd ~/Documents/Projects/zinnur-v2
docker compose up -d          # ishlayotganini tasdiqlash
# keyin: dotnet ef migrations add Initial
```
