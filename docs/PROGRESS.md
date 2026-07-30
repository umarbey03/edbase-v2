# Ish jurnali — tunги sessiya (2026-07-29 → 30)

> Bu fayl **avtomatik yuritiladi**. Maqsad: sessiya uzilib qolsa ham nima
> qilinganini va keyingi qadam nima ekanini aniq bilish.
>
> Reja: `docs/ROADMAP.md` · Shartnoma: `docs/SPEC.md`

---

## ✅ FAZA 1.1 — EF Core migratsiyalari — **TUGADI**

### Qilingan ishlar

| Fayl | Nima |
|---|---|
| `backend/src/Zinnur.Infrastructure/Persistence/DesignTimeDbContextFactory.cs` | `dotnet ef` uchun fabrika (ilovani ishga tushirmasdan migratsiya yaratish) |
| `.../Persistence/Migrations/20260729191315_Initial.cs` | Birinchi migratsiya (avtomatik generatsiya) |
| `.../Persistence/DbInitializer.cs` | `EnsureCreated` **olib tashlandi** — faqat `MigrateAsync` |
| `backend/.editorconfig` | Generatsiya qilingan migratsiya kodini analizdan chiqarish |
| `docs/MIGRATIONS.md` | Migratsiya qo'shish/bekor qilish/qo'llash tartibi |

### Nima uchun `EnsureCreated` olib tashlandi

U sxemani yaratadi, lekin `__EFMigrationsHistory` jadvalini **yozmaydi**.
Natijada keyinchalik birinchi migratsiya qo'llanganda EF "relation already
exists" bilan yiqiladi — ishlab chiqarish bazasida tuzatib bo'lmaydigan holat.

### Tasdiqlangan natija

```
docker compose down -v && docker compose up -d
→ __EFMigrationsHistory: 20260729191315_Initial | 9.0.0   ← ilgari BO'SH edi
→ 10 jadval, 29 indeks
→ /health/ready 200, login 200
```

Kritik indekslar bazada tasdiqlandi:
`UX_LiveSessions_RoomName` · `UX_Attendances_SessionId_StudentId` · `UX_GroupMembers_GroupId_StudentId`

### Yo'l-yo'lakay tuzatilgan

`CA1861` — EF generatsiya qilgan migratsiya kodida analizator xatosi. Kodni
qo'lda tuzatish noto'g'ri bo'lardi (keyingi migratsiyada yo'qoladi), shuning
uchun `.editorconfig` da `**/Migrations/*.cs` uchun qoida o'chirildi va sabab
izohlandi.

---

## ✅ CI liniyasi — yozildi

`.github/workflows/ci.yml` — 4 ta job:

| Job | Nima tekshiradi |
|---|---|
| `backend` | `dotnet build` (ogohlantirish = xato) + unit testlar + coverage |
| `integration` | Postgres 17 + Redis 7 xizmatlari bilan integratsiya testlari |
| `frontend` | `vue-tsc --noEmit` + lint + `npm run build` |
| `docker` | dev va prod compose config + shell skript sintaksisi |

> Hali GitHub'ga push qilinmagan — repo lokal.

---

## 🔄 Hozir parallel ishlamoqda

| Faza | Ish | Egasi |
|---|---|---|
| 1.2 | Domain unit testlari (`tests/Zinnur.UnitTests`) | agent |
| 1.3 | Sentry + strukturali log + health checks | agent |
| 2.1 | Foydalanuvchilar moduli (CRM) | agent |

---

## ⏭ Keyingi navbatda

1. **Faza 2.2/2.3** — Guruhlar + jadval generatsiyasi
2. **Faza 1.2** integratsiya testlari (migratsiyalar tayyor, endi mumkin)
3. **Faza 3** — o'quv jarayoni (kurs, vazifa, test, davomat)

---

## Muhim eslatmalar

### Build tezligi
NuGet keshi uchun `zinnur-nuget-cache` nomli Docker volume yaratilgan.
**Har `dotnet` buyrug'ida uni ulang**, aks holda build 3-4 daqiqa o'rniga
har safar paketlarni qaytadan yuklaydi:

```bash
docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build Zinnur.sln -v q --nologo
```

Kesh bilan: **~3 sekund**. Keshsiz: ~4 daqiqa.

### Analizator tuzoqlari (agentlar doim urinadi)
- `CA1848` — `logger.LogX(...)` taqiqlangan, `[LoggerMessage]` ishlating (`ApiLog.cs`)
- `CA1305` — har `ToString()`/`Parse` ga `CultureInfo.InvariantCulture`
- `CA1711` — tur nomi `Queue`/`Flags` bilan tugamasin
- `CA1716` — zaxiralangan kalit so'zlar (`Module` → `CourseModule`)

### Docker loyihalari
- `zinnur-legacy` — eski Python tizimi, `localhost:8000`, **tegilmaydi**
- `zinnur-v2` — yangi C# tizimi, `localhost:5173` / `:5080`

Ikkalasi to'liq ajratilgan (konteyner, tarmoq, volume). Bir loyihadagi
`docker compose down` ikkinchisiga ta'sir qilmaydi — amalda sinalgan.

---

## ✅ FAZA 1.2 (unit testlar qismi) — **TUGADI**

`backend/tests/Zinnur.UnitTests/` — **124 test, hammasi yashil**

| Fayl | Testlar |
|---|---|
| `Entities/LiveSessionTests.cs` | 46 |
| `Entities/AttendanceTests.cs` | 30 |
| `Entities/UserTests.cs` | 24 |
| `Entities/ChatMessageTests.cs` | 16 |
| `Entities/GroupTests.cs` | 9 |

`FluentAssertions` **7.0.0** ataylab tanlandi — 8.x dan boshlab tijorat
foydalanish uchun pullik litsenziya (Xceed) talab qilinadi, 7.x esa Apache-2.0.

### ⚠️ Testlar topgan 4 ta HAQIQIY Domain bugi — tuzatildi

| # | Bug | Oqibati | Tuzatish |
|---|---|---|---|
| 1 | `LiveSession.End()` bekor qilingan darsni tekshirmasdi | `POST /end` bekor qilingan darsni jimgina "Ended" qilib, bekor qilish yozuvini yo'q qilardi; `Finalize()` esa bo'lmagan dars uchun davomat yozardi | `Cancelled` → `DomainException` |
| 2 | `Attendance.RegisterJoin()` `IsManual` ni e'tiborsiz qoldirardi | Ustoz qo'lda "Absent" qo'yadi → o'quvchi qayta ulanadi → status "Present" ga o'zgaradi → `Finalize()` aynan `IsManual` tufayli qayta hisoblamaydi → **noto'g'ri baho abadiy qoladi** | `if (IsManual) return;` (vaqt belgilari baribir yangilanadi) |
| 3 | `NormalizeBody` 500-belgida surrogat juftlikni ikkiga bo'lardi | 500-belgisi emojiga to'g'ri kelgan xabar Postgres'da `U+FFFD` ga aylanadi yoki `EncoderFallbackException` bilan yiqiladi | `char.IsHighSurrogate` tekshiruvi bilan bitta belgi orqaga |
| 4 | `GenerateRoomName()` 4 bayt entropiya | Jadval generatsiyasi bir sekundda 10 000 nom yaratadi → to'qnashuv ehtimoli ~1.2% → `UX_LiveSessions_RoomName` unikal indeksi INSERT'ni yiqitardi | 8 baytga oshirildi |

Qo'shimcha: `Attendance` uchta mutatorida ham `UpdatedAt` endi yoziladi.

### Ochiq qolgan (ataylab)
- `User.ChangeRole/SetPassword` va `ChatMessage.SentAt` ichida `DateTimeOffset.UtcNow`.
  `TimeProvider` ga o'tkazish Application qatlamiga tegadi — keyingi faza.

---

## Git

Repo lokal, **push qilinmagan**. Toza tarix (1 commit, 157 fayl).
`.env` (DB paroli, JWT/LiveKit sirlari) `.gitignore` da — tarixda ham yo'q.

---

## ✅ Integratsiya testlari — yozildi (koordinator)

`backend/tests/Zinnur.IntegrationTests/` — HAQIQIY Postgres + JWT bilan,
mock'siz. Har test sinfi **o'z bazasini** oladi (`zinnur_test_<guid>`) va
tugagach o'chiradi — parallel ishlay oladi.

| Fayl | Nima tekshiradi |
|---|---|
| `Infrastructure/ZinnurApiFactory.cs` | API'ni xotirada ko'taradi, izolyatsiyalangan baza yaratadi |
| `Api/AuthEndpointsTests.cs` | kirish, `/me`, refresh, **chiqish tokenlarni bekor qiladimi** |
| `Api/LiveSessionEndpointsTests.cs` | ruxsat matritsasi, **LiveKit token formati** |

### Nima uchun mock emas
Eng qimmat buglar qatlamlar CHEGARASIDA yashiringan bo'ladi: EF
konfiguratsiyasi, indekslar, JWT claim xaritalash, ruxsat tekshiruvi.
Mock bilan ular ko'rinmaydi — bu tunda topilgan `name` claim bugi buning
tirik misoli.

Ulanish manzillari muhitdan olinadi: lokalda ishlab turgan stack
(`localhost:5440/6390`), CI'da service konteynerlar (`5432/6379`).

---

## ✅ SignalR hub — JONLI sinov o'tkazildi

Ishlab turgan stack'ga qarshi, ikkita haqiqiy WebSocket klient bilan:

```
✅ hub'ga ulanish (WebSocket, ?access_token=)
✅ JoinSession -> to'liq ro'yxat bir marta qaytadi
✅ PresenceChanged DELTA keladi (to'liq ro'yxat EMAS) ← 200 kishi uchun kritik
✅ chat xabari ikkinchi klientga yetib bordi
✅ rate-limit ishladi (1 xabar / 2 sek, server tomonda)
✅ qo'l ko'tarish tarqaldi
✅ xabar bazaga yozildi (fon navbati orqali)
```

### ⚠️ Jonli sinov topgan bug — `name` claim xaritalanmagan

Chatda har xabar muallifi **"Noma'lum"** deb ko'rinardi.

Sabab: JWT'da `name` claim'i bor, lekin ASP.NET'ning default "inbound claim
map" i `name` ni `ClaimTypes.Name` ga **xaritalamaydi** (faqat `unique_name` ni).
`sub` va `role` xaritalanadi — shuning uchun auth va rol tekshiruvi ishlagan
va bug sezilmay qolgan.

Yechim: `TokenValidationParameters` da `NameClaimType = "name"` va
`RoleClaimType = "role"` ni ANIQ ko'rsatish. `Program.cs` ni observability
agenti tahrirlayotgani uchun tuzatish unga topshirildi (fayl egaligi buzilmasin).

---

## Frontend sifat tekshiruvi (koordinator ko'rigi)

| Mezon | Natija |
|---|---|
| `any` tipi | ✓ yo'q |
| Izohsiz `!` (non-null) | ✓ yo'q |
| `v-html` | ✓ ishlatilmagan (izoh bilan ataylab chetlab o'tilgan) |
| Tozalash (`onBeforeUnmount`) | ✓ trek detach, tinglovchi olib tashlash, `disposed` bayrog'i |
| Chat chegarasi | ✓ `MAX_RENDERED_MESSAGES`, `pruneSeenIds()` |

---

## ✅ FAZA 1.2 — regressiya testlari — **TUGADI**

**159 test** (124 → +35), 0 xato, 0 ogohlantirish, Debug va Release'da yashil.

### Testlar HAQIQIY qo'riqchi ekani isbotlandi

Test agenti shunchaki yangi xatti-harakatni tasdiqlamadi — har tuzatishni
scratchpad'da **orqaga qaytarib**, testlar qizarishini tekshirdi:

> Eski (tuzatilmagan) Domain'ga qarshi **21 ta test yiqiladi**, joriy kodda
> hammasi yashil.

Bundan tashqari `IsManual` qo'riqchisi uchun **mutatsion sinov**: `return` ni
metod boshiga ko'chirib ko'rgan (ehtimoliy "ortiqcha tuzatish") — 5 ta test
qizargan. Ya'ni qo'riqchining AYNAN QAYERDA turishi ikki tomondan mahkamlangan.

| Tuzatish | Yangi test | Eski kodda qizaradi |
|---|---|---|
| `End()` `Cancelled` ni rad etadi | 4 | 4 |
| `RegisterJoin()` `IsManual` ni hurmat qiladi | 9 (status + vaqt belgilari) | 3 + 5 (mutatsiya) |
| `NormalizeBody()` surrogat-xavfsiz | 5 + emoji stsenariylari | hammasi |
| `GenerateRoomName()` 8 bayt | 2 | 2 |
| `Attendance.UpdatedAt` | 6 | 6 |

Surrogat testi ayniqsa puxta: 499 ASCII + emoji (kesish kerak), 498 ASCII +
emoji (kesish KERAK EMAS), va `"x" + 400 emoji` (eng yomon holat) — hammasi
qat'iy `UTF8Encoding(false, true)` bilan tekshiriladi.

---

## ✅ Yuklama testi yozildi

`tests/load/signalr-load.mjs` — 200 bir vaqtdagi SignalR klienti.

```bash
node tests/load/signalr-load.mjs              # 200 klient, 60 sekund
USERS=50 node tests/load/signalr-load.mjs     # kichikroq sinov
```

O'lchaydi: ulanish vaqti, JoinSession, **chat kechikishi (end-to-end)**,
uzilishlar, rate-limit. Baho mezoni: ulanish ≥98%, chat p95 < 1 sek.

> Media (LiveKit) ATAYLAB o'lchanmaydi — video backend'dan o'tmaydi,
> u to'g'ridan-to'g'ri brauzer ↔ LiveKit orasida ketadi.

---

## ⚠️ Koordinator xatosi — agent tuzatdi (yozib qo'yish muhim)

Jonli sinovda chatda ism "Noma'lum" chiqishini topgach, men tuzatish sifatida
`NameClaimType = "name"` va `RoleClaimType = "role"` ni taklif qilgandim.

**Bu ikki tomonlama noto'g'ri edi** va observability agenti buni rad etib,
sababini kodga yozib qo'ydi:

1. `NameClaimType` claim'ning **saqlangan turini o'zgartirmaydi** — u faqat
   `Identity.Name` xossasi qaysi turdan o'qishini belgilaydi. Hub'dagi
   `FindFirstValue(ClaimTypes.Name)` baribir topmasdi, ya'ni bug qolardi.

2. Yonidagi `RoleClaimType = "role"` esa **butun avtorizatsiyani buzardi**:
   `role` claim'i kirish xaritasida allaqachon `ClaimTypes.Role` ga aylangan,
   demak `"role"` turidagi claim umuman qolmaydi va `[Authorize(Roles = ...)]`
   HAMMA joyda 403 bergan bo'lardi.

**To'g'ri yechim (agent qo'llagani):** `OnTokenValidated` da faqat
YETISHMAYOTGAN xaritalashni qo'shish, ishlab turgan `sub`/`role` ga tegmasdan:

```csharp
if (identity.FindFirst(ClaimTypes.Name) is null
    && identity.FindFirst(JwtNameClaim) is { Value.Length: > 0 } shortName)
    identity.AddClaim(new Claim(ClaimTypes.Name, shortName.Value));
```

**Saboq:** agentlarga buyruq berganda "nima qilish" emas, "qanday muammoni
hal qilish" ni aytish kerak — shunda ular yechimni tekshirib, kerak bo'lsa
yaxshirog'ini taklif qiladi.

---

## ✅ FAZA 1.3 — Kuzatuv — **TUGADI**

`backend/src/Zinnur.WebApi/Observability/` — 8 fayl + `docs/OBSERVABILITY.md`

| Komponent | Holat |
|---|---|
| Sentry (backend) | ✓ ixtiyoriy, DSN bo'lmasa o'chiq |
| Sentry (frontend) | ✓ **dinamik yuklanadi** — DSN bo'lmasa 0 bayt |
| Sirlarni tozalash | ✓ `Authorization`, cookie, `?access_token=`, parol |
| Strukturali log | ✓ prod'da JSON (CLEF), dev'da matn, `traceId` + `userId` |
| `/health/ready` | ✓ postgres + redis + **LiveKit** alohida |
| 4xx filtri | ✓ kutilgan xatolar Sentry'ga yuborilmaydi |

### Bundle o'lchami — muhim qaror

Sentry SDK'si asosiy bundle'ga tushganda `vendor` **67 → 215 KB** ga o'sdi
(+49 KB gzip). Foydalanuvchilar Telegram Mini App'ni mobil internetda ochadi,
shuning uchun:

- `main.ts` da **dinamik import**
- `vite.config.ts` da alohida `sentry` bo'lagi
- Natija: `vendor` 67 KB, `sentry` 450 KB **alohida va lazy**
- DSN bo'lmasa — **umuman yuklanmaydi**

---

## ✅ FAZA 2.1 — Foydalanuvchilar moduli — **TUGADI va TASDIQLANDI**

8 endpoint, `PagedResult<T>`, CSV import, `pg_trgm` qidiruv.

### ★ Xavfsizlik qoidasi — AMALDA isbotlandi

Eski tizimda `academic` roli admin akkauntini egallashi mumkin edi (audit X-4).
Ishlab turgan tizimda tekshirdim:

```
Academic → Admin parolini tiklash      -> 403 ✅
Academic → Adminni o'chirish           -> 403 ✅
Academic → Adminni Teacher qilish      -> 403 ✅
Academic → Admin rolli user yaratish   -> 403 ✅
Academic → Student yaratish            -> 201 ✅ (ruxsat berilgan)
Academic → Qidiruv                     -> 200 ✅
```

Javob: `{"title":"Ruxsat yo'q","status":403,"detail":"Bu profilni faqat administrator..."}`

**Qo'shimcha mustahkamlash (agent qarori):** har chaqiruvda aktyorning roli
**bazadan** qayta o'qiladi, JWT claim'idan emas — chunki access token
darajasi tushirilgandan keyin ham 15 daqiqa yaroqli qoladi.

### Migratsiya
`AddUserPhoneNormalizedAndSearchIndexes` — jonli bazaga **qo'llandi va ishladi**:
- `PhoneNormalized` + filtrlangan unikal indeks (`+998 90 123 45 67` va
  `998901234567` endi bir xil hisoblanadi)
- `pg_trgm` + 3 ta GIN indeks (`ILIKE '%...%'` uchun)
- Mavjud qatorlar `regexp_replace` bilan to'ldirildi

---

## ✅ Integratsiya testlari — 19/19 o'tdi

Yo'l-yo'lakay ikkita muammo topildi va tuzatildi:

**1. `AddInMemoryCollection` kuchga kirmasdi.** Minimal hosting'da `Program.cs`
konfiguratsiyani host QURILAYOTGANDA o'qiydi (`Jwt:Secret` tekshiruvi, Redis
backplane), factory callback'lari esa keyinroq ishlaydi. Natijada ilova
`appsettings.json` dagi `postgres`/`redis` Docker DNS nomlarini ishlatib,
test konteynerida "Name or service not known" berdi. **Yechim:** `UseSetting`.

**2. API assimetrik edi.** So'rovda `"role": 3` (raqam) kutilardi, javobda
`"role": "Academic"` (satr) qaytardi. Klient ikki tomonga o'girishga majbur
bo'lardi va enum tartibi o'zgarsa **jimgina noto'g'ri rol** yuborardi.
**Yechim:** `JsonStringEnumConverter` — ikki tomonda ham satr.

---

## 📊 TUNGI ISH YAKUNI

| Ko'rsatkich | Boshida | Oxirida |
|---|---|---|
| Backend `.cs` | 57 fayl / 3 620 satr | **76+ fayl / 7 800+ satr** |
| Testlar | 0 | **178 test (159 unit + 19 integratsiya), hammasi yashil** |
| Migratsiyalar | 0 (`EnsureCreated`) | **2, jonli bazada tasdiqlangan** |
| Hujjatlar | 2 fayl | **7 fayl / 3 000+ satr** |
| Topilgan va tuzatilgan buglar | — | **9 ta** (4 Domain + 5 boshqa) |

### Topilgan 9 bug
1. `LiveSession.End()` bekor qilingan darsni yakunlardi
2. `Attendance.RegisterJoin()` qo'lda qo'yilgan bahoni buzardi
3. `NormalizeBody()` emojini ikkiga bo'lardi
4. `GenerateRoomName()` entropiyasi jadval generatsiyasi uchun kam
5. `Attendance` `UpdatedAt` yozmasdi
6. `name` claim xaritalanmagan → chatda "Noma'lum"
7. `EnsureCreated` migratsiya tarixini yozmasdi
8. `AddInMemoryCollection` test konfiguratsiyasi kuchga kirmasdi
9. API enum'lari assimetrik (raqam ↔ satr)

---

## ✅ FAZA 2.2/2.3 — Domain: guruh jadvali — **TUGADI**

`Group` entity'si jadval qoidasini o'z ichiga oldi + `ScheduleGenerator`
(sof funksiya, bazasiz test qilinadi).

### ⚠️ Ma'lumot ko'chirishda BIR KUNLIK SILJISH xavfi

Eski Python tizimi `date.weekday()` ni ishlatadi — **dushanba = 0**.
.NET `DayOfWeek` esa — **yakshanba = 0**.

```
dotnet = (python + 1) % 7
```

Bu `Group.Weekdays` izohida yozib qo'yilgan. Ko'chirish skriptida
konvertatsiya **majburiy**, aks holda barcha darslar bir kun siljib ketadi.

### Eski tizimdan tuzatilgan qoidalar
- Hafta kunlari soni guruh **turiga** bog'liq (eski tizimda kurator guruhini
  saqlashning umuman imkoni yo'q edi — u ham "aniq 2 kun" shartiga tushardi)
- `ScheduleRuleDiffersFrom()` — jadval **faqat kerak bo'lganda** qayta tuziladi
  (eski tizimda kursni almashtirish ham butun kelajak jadvalni o'chirardi)
- DST (yozgi vaqt) o'tishida mavjud bo'lmagan soat to'g'ri ishlanadi
- `MaxSessionsPerGroup = 1000` — noto'g'ri sana bazani to'ldirib qo'ymasin

---

## ✅ FAZA 3 — Domain: o'quv jarayoni entity'lari — **TUGADI**

`Assignment` · `Submission` · `SubmissionFile` · `Test` · `TestQuestion`
`TestOption` · `TestAttempt` · `TestAnswer` · `LessonProgress`

Migratsiya **hali yaratilmagan** — Groups agenti migratsiya zanjirini
boshqarayotgani uchun kutamiz (bir vaqtda ikki migratsiya zanjirni buzadi).

### Eski tizim buglari arxitektura darajasida yopildi

| Eski bug | v2 yechimi |
|---|---|
| `due_at` ustuni bor edi, lekin **hech qayerda tekshirilmasdi** | `Test.EnsureOpenForSubmission()` + `Assignment.IsOverdue()` — Domain majburlaydi |
| Ko'p to'g'ri javobda faqat **oxirgisi** hisoblanardi | `TestQuestion.Score()` — to'plamlar solishtiriladi ("hammasi yoki hech nima") |
| `(attempt, question)` unikal edi → ko'p tanlov **umuman ishlamasdi** | Unikal kalit `(attempt, question, option)` |
| Klient yuborgan begona variant ID'lari tekshirilmasdi | `SubmitAnswers()` savolga tegishli bo'lmagan ID'ni filtrlaydi |
| Qayta topshirish ruxsati yopilmasdi | `Submit()` da `AllowResubmit = false` avtomatik |
| Javob formati cheklovi faqat frontend'da | `Assignment.EnsureFormatAllowed()` — server majburlaydi |
| Progress denormalizatsiya qilingan, manbalar mos kelmasdi | `LessonProgress` faqat video holatini saqlaydi; vazifa/test holati manbadan hisoblanadi |

---

## ✅ Faza 3 Domain testlari — 206 unit test (159 → +47)

| Fayl | Nima qo'riqlaydi |
|---|---|
| `TestQuestionScoringTests.cs` | Ko'p to'g'ri javob, begona variant ID, qisman ball berilmasligi |
| `TestAttemptTests.cs` | Server tomonda baholash, vaqt chegarasi, `(attempt,question,option)` ko'p qator |
| `SubmissionTests.cs` | Bir marta topshirish, ruxsat avtomatik yopilishi, baho tozalanishi |

### ⚠️ Testlar Domain dizaynidagi nuqsonni tutdi

`Submission.Submit()` "allaqachon topshirilgan"ni **`Id != 0`** bilan
aniqlardi — ya'ni **saqlash holatini** (bazada bormi) **biznes holati**
(topshirilganmi) bilan chalkashtirardi. Test yozayotganda bu darhol
ko'rindi: `ExistingSubmission()` helper'ining o'zi istisno ko'tardi.

**Yechim:** ikki niyat ikki metodga ajratildi —

```csharp
Submission.Create(...)   // BIRINCHI topshirish (static factory)
submission.Resubmit(...) // QAYTA topshirish (AllowResubmit talab qiladi)
```

Endi chaqiruvchi nima qilayotganini aniq bildiradi va `Id` ga tayanish yo'q.
Bu — testlarning kodni yaxshilashga majburlashining tipik misoli.

---

## ✅ FAZA 2.2/2.3 — Guruhlar + jadval — **TASDIQLANDI**

`Application/Groups/` (4 fayl) · `Application/Scheduling/` (4 fayl) ·
`GroupsController.cs` · migratsiya `AddGroupScheduleRuleAndMemberPause`

### Jonli tekshiruv natijalari

```
POST /api/v1/groups  (dush+chor, 19:00, 80 daq, 8 oy)
  -> 69 dars generatsiya qilindi

Jadval to'g'riligi:
  2026-09-02 14:00Z = Toshkent 19:00 (Wed)  80 daq  ATF-2026 — 1-dars
  2026-09-07 14:00Z = Toshkent 19:00 (Mon)  80 daq  ATF-2026 — 2-dars
  ...
  ✅ faqat dush/chor, aynan 19:00 Toshkent, 80 daqiqa, ketma-ket raqamlangan
```

### ★ Regeneratsiya qoidasi — eski tizim buzgan xatti-harakat

| Sinov | Natija |
|---|---|
| Faqat **nom** o'zgardi | ✅ dars ID'lari `2,3,4,5,6` **o'zgarmadi** |
| **Hafta kunlari** o'zgardi | ✅ o'tilgan (`Ended`) dars **saqlandi**, kelajakdagilar Sesh/Pay ga qayta tuzildi |

Eski tizimda regeneratsiya **shartsiz** ishlardi: kursni yoki kuratorni
almashtirsangiz ham butun kelajak jadval o'chib qayta yaratilardi, dars
ID'lari o'zgarib tashqi havolalar buzilardi.

### Baza xaritalashi tasdiqlandi
```
Weekdays        : ARRAY _int4        ← integer[], JSON EMAS (to'g'ri)
StartTime       : time without time zone
Type            : integer
CuratorGroupId  : bigint
```

### Agentga qaytarilgan ikki ish
1. Unit + integratsiya testlari yozilmagan (test soni o'zgarmagan: 206 + 19)
2. `PUT /groups/{id}` javobida `scheduleTouched`/`regenerated`/`hostsUpdated`
   xulosasi ko'rinmadi — chaqiruvchi nima bo'lganini bilishi kerak

---

## ✅ LiveKit — o'z serverimizda (self-hosted) TASDIQLANDI

```
Образ    : livekit/livekit-server:v1.8  (bulutga bog'liqlik YO'Q)
Portlar  : 7880/tcp (WS+API) · 7881/tcp (RTC zaxira) · 7882/udp (media mux) · 3478/udp (TURN)
Konfig   : infra/livekit/livekit.yaml — 405 satr, max_participants: 250
Signalling: /rtc/validate -> "success" (soxta token 401)
```

### ⚠️ Topilgan va tuzatilgan ICE muammosi

Loglar aynan men ogohlantirgan nosozlikni ko'rsatdi:

```
could not validate external IP {"ip": "185.213.230.94"}
no external IPs found, using node IP for NAT1To1Ips {"ip": "185.213.230.94"}
```

LiveKit lokal mashinada **oq IP'ni** ICE nomzodi sifatida e'lon qilardi.
Brauzer o'sha manzilga UDP yuborishga urinardi, holbuki konteyner faqat
`localhost:7882` da ochiq. Natija: **xona ochiladi, ro'yxat to'ladi, ovoz
va video KELMAYDI** — self-hosted LiveKit'dagi eng ko'p uchraydigan va
topish qiyin nosozlik.

**Yechim (dev):** `docker-compose.yml` da env orqali
```
NODE_IP=127.0.0.1
LIVEKIT_RTC_USE_EXTERNAL_IP=false
LIVEKIT_RTC_ENABLE_LOOPBACK_CANDIDATE=true
```
Natija: `nodeIP: 127.0.0.1` ✓

**Nima uchun env, alohida yaml emas:** ikki konfiguratsiya fayli vaqt o'tib
bir-biridan uzoqlashadi va "mening mashinamda ishlaydi" holati tug'iladi.
LiveKit har sozlama uchun env o'zgaruvchisi beradi (`help-verbose` bilan
topildi), shuning uchun bitta fayl qoldi.

### ⚠️ Yo'l-yo'lakay O'ZIM kiritgan bug — darhol tuzatildi

Compose overlay'lari `environment` ni **birlashtiradi**, ya'ni dev
override'lari `docker-compose.prod.yml` ga ham o'tib ketgan edi.
Production'da `NODE_IP=127.0.0.1` **halokat** bo'lardi: LiveKit masofadagi
brauzerlarga loopback manzilini e'lon qilardi va media hech qachon ishlamasdi.

Prod overlay endi ularni `!reset null` bilan bekor qiladi. Tasdiqlash:
```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml config \
  | grep -E "NODE_IP|USE_EXTERNAL"
# faqat LIVEKIT_NODE_IP: "" ko'rinadi ✓
```

### Hali isbotlanmagan
Ikki brauzer ochib **haqiqiy media oqimi** sinalmagan. Signalling ishlaydi,
lekin bu media kelishini kafolatlamaydi — aynan shu farq yuqoridagi
nosozlikni shunday xavfli qiladi.

---

## 📋 SESSIYA TUGADI — kontekst chegarasi

Yangi sessiya uchun to'liq topshiriq: **`docs/DAVOM_ETTIRISH.md`**

Unda: holatni tiklash buyruqlari, tugallanmagan ishlar, build/test
buyruqlari, analizator tuzoqlari, 3 ta "mina" (DayOfWeek konvensiyasi,
LiveKit ICE, test konfiguratsiyasi) va isbotlanmagan da'volar ro'yxati.

**Faza 3 Application/API agenti ishni BOSHLAMAGAN** (0 fayl) — kontekst
tugagani uchun. Domain tayyor va testlangan, faqat Application + WebApi +
migratsiya qoldi.

---

## ⚠️ REPO BUZUQ HOLATDAN TIKLANDI (sessiya oxirida)

Groups agenti tugagach muhim ogohlantirish berdi va u **to'g'ri chiqdi**:
repozitoriy kompilyatsiya bo'lmayotgan holatda edi, ishlab turgan konteyner
esa faqat **eski образ** tufayli tirik edi. Rebuild qilinsa ishga tushmasdi.

### Muammo 1 — Npgsql 9 da olib tashlangan API

Faza 3 agenti `SubmissionConfiguration.cs` va `TestAttemptConfiguration.cs`
da `builder.UseXminAsConcurrencyToken()` yozgan. Bu metod **Npgsql 9 da
olib tashlangan** → `error CS1061`.

**Tuzatish:** rasmiy almashtiruv
```csharp
builder.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
```
Izoh qo'shildi: nima uchun optimistik qulflash kerak (o'quvchi ikki tabda
"Topshirish" bosса yoki tarmoq uzilib qayta yuborilsa — 409, jimgina
ma'lumot yo'qolishi o'rniga).

### Muammo 2 — model va migratsiya mos kelmasdi

Faza 3 EF konfiguratsiyalari modelda bor edi, lekin **migratsiya yo'q**.
Natijada `DbInitializer.MigrateAsync` `PendingModelChangesWarning` bilan
istisno ko'tarardi va **ilova umuman ishga tushmasdi** — shu jumladan
oldindan ishlab turgan Auth integratsiya testlari ham yiqilardi.

**Tuzatish:** `20260730032934_AddLearningProcessTables` generatsiya qilindi.

### Tiklangandan keyingi holat — TASDIQLANGAN

```
Build            : 0 xato, 0 ogohlantirish
Testlar          : 298 (267 unit + 31 integratsiya), hammasi yashil
Migratsiyalar    : 4 ta, bazaga qo'llangan
Yangi jadvallar  : Assignments LessonProgress SubmissionFiles Submissions
                   TestAnswers TestAttempts TestOptions TestQuestions Tests
API              : qayta qurildi va ISHGA TUSHDI (healthy)
```

Kritik unikal kalitlar bazada tasdiqlandi:
```
UX_TestAnswers_AttemptId_QuestionId_OptionId   ← UCHTALIK (to'g'ri)
UX_Submissions_AssignmentId_StudentId
UX_TestAttempts_TestId_StudentId
UX_LessonProgress_StudentId_ModuleLessonId
```

### Saboq
Agent "build yashil" deb hisobot bergani — **butun solution yashil** degani
emas. Groups agenti buni o'zi sezdi va aytdi; agar aytmagan bo'lsa,
buzuq holat commit qilinib ketardi. Har agent hisobotidan keyin
**butun solution**ni o'zim tekshirishim shart.

---

## ✅ Groups moduli — agent TUGATDI

- `tests/Zinnur.UnitTests/Scheduling/` (+61 test)
- Integratsiya testlari (+12): teacher-only ID'larni saqlaydi, name-only
  o'rnida tahrirlaydi, **course-only hech nimaga tegmaydi**, hafta kuni
  o'zgarishi o'tmishni saqlaydi
- `Weekdays` uchun `PrimitiveCollection(...).HasColumnType("integer[]")`
  ANIQ berilishi kerak edi — EF Core 9 default'i JSON ustun
- `PUT` javobidagi xulosa MAVJUD edi, `"schedule"` ichida ichma-ich —
  mening tekshiruvim ildizga qaragani uchun bo'sh chiqqan
- ⚠️ `pausedUntil` EF shadow ustunida (Domain qamrovdan tashqari edi) —
  keyingi Domain o'zgarishida xossa qilib qo'shish kerak, migratsiya
  kerak bo'lmaydi

---

# Kunduzgi sessiya (2026-07-30, 10:45 dan)

## 🔴 MA'LUMOT YO'Q QILADIGAN XATO — topildi, isbotlandi, tuzatildi

`GroupFormDialog` (CRM guruh formasi) tahrirlashda **guruhning kursini
jimgina uzib qo'yardi.**

### Sabab

`PUT /api/v1/groups/{id}` — TO'LIQ ALMASHTIRISH semantikasi. Request DTO C#
`record`, ixtiyoriy maydonlar `= null` standart qiymatga ega
(`GroupDtos.cs:96`), servis esa qiymatni to'g'ridan-to'g'ri yozadi:

```csharp
group.CourseId = request.CourseId;   // GroupService.cs:197
```

Frontend `buildPayload()` esa `courseId` va `curatorGroupId` ni **umuman
yubormasdi** → ikkisi ham `null` bo'lib bazaga tushardi.

### Oqibati (nima uchun bu kritik)

Kurssiz qolgan guruhning **barcha o'quvchilarida** gating `NotInCourse`
qiladi: butun kurs qulflanadi, vazifa va testlar ko'rinmay qoladi. Kurator
bog'lanishi ham yo'qolardi. Xato hech qanday xabar bermaydi — forma
muvaffaqiyatli saqlanadi.

### Jonli isbot (tuzatishdan oldin)

```
POST /groups   {"courseId":1,...}          → courseId=1  courseName=ATF
PUT  /groups/11  (frontend tanasi, courseId YO'Q)
                                           → courseId=None  courseName=None
GET  /groups/11                            → courseId=None   ← bazada ham yo'q
```

### Tuzatish

`courseId` va `curatorGroupId` endi **haqiqiy forma maydonlari**:

| Maydon | Manba | Ehtiyot choralari |
|---|---|---|
| Kurs | `GET /courses?IsActive=true` | Guruhda **arxivlangan** kurs turgan bo'lsa u ham ro'yxatga qo'shiladi — aks holda select bo'sh qolib, saqlashda kurs yana uzilardi |
| Kurator guruhi | tahrirlashda `GET /groups/{id}/curator-candidates`, yaratishda `GET /groups?Type=Curator` | Tur "Kurator guruhi" ga o'zgarsa bog'lanish tozalanadi (Domain qoidasi: kurator kuratorga bog'lanmaydi) |

Payload'da ikkisi **har doim** yuboriladi. Tuzatishdan keyingi jonli tekshiruv:
`PUT` (yangi tana bilan) → `courseId=1 courseName=ATF` — saqlanib qoldi.

## ✅ FAZA 6 — Kurs kontenti paneli (o'quv bo'limi/admin)

**Muammo:** `CoursesController` + `CourseService` (FAZA 3.1) to'liq yozilgan va
testlangan edi, lekin **hech qanday UI uni chaqirmaydi** — kurs, modul va dars
faqat `curl` bilan boshqarilardi.

### Yangi fayllar

| Fayl | Nima |
|---|---|
| `shared/types/api.ts` | `CourseDto` `CourseTreeDto` `CourseModuleDto` `CourseLessonDto`, yozish shakllari, `ReorderRequest`, `PositionDto`, `LessonLockReasonName`, `CuratorCandidateDto` |
| `entities/course/api/course-api.ts` | 14 endpoint (kurs/modul/dars CRUD + 3 xil reorder) |
| `entities/course/model/types.ts` | `courseContentSummary`, `lessonDurationLabel`, `lessonLockReasonLabel` |
| `features/course-form/ui/CourseFormDialog.vue` | Kurs yaratish/tahrirlash |
| `features/course-tree/ui/CourseTreeEditor.vue` | Modul → dars daraxti: qo'shish, tahrirlash, o'chirish, tartib |
| `features/course-tree/ui/{ModuleFormDialog,LessonFormDialog}.vue` | Modul va dars formalari |
| `shared/ui/ConfirmDeleteDialog.vue` | O'chirishni tasdiqlash (409 sababi oynada QOLADI) |
| `pages/manage/ManageCoursesPage.vue` | Kurslar ro'yxati: qidiruv, holat filtri, tartib |
| `pages/manage/ManageCoursePage.vue` | Bitta kursning kontenti |
| `shared/ui/AppIcon.vue` | `arrow-up`, `trash` ikonkalari |

Marshrutlar: `boshqaruv/kurslar` va `boshqaruv/kurslar/:courseId` (`MANAGERS`
roli). Yon menyuga "Kurs kontenti" qo'shildi.

### Qabul qilingan qarorlar

1. **Kurslar tartibi har doim ochiq emas.** `POST /courses/reorder` BARCHA kurs
   Id'sini talab qiladi (yetishmasa 400). Qidiruv/filtr yoqilganda yoki ro'yxat
   bir necha sahifa bo'lganda ekranda kurslarning faqat bir qismi turadi —
   shu holatda tugmalar YASHIRILADI va sabab yozib qo'yiladi. Filtrsiz
   ro'yxat esa arxivlanganlar bilan birga TO'LIQ keladi (`ListAsync`
   `IsActive` bo'lmasa filtr qo'ymaydi) — tekshirildi.
2. **O'chirish `window.confirm` bilan emas.** Server 409 da sababni matn qilib
   beradi ("bu kursga 3 ta guruh biriktirilgan...", "12 ta topshirilgan vazifa
   bog'langan..."). Brauzer oynasi yopilgach o'sha matnni ko'rsatadigan joy
   qolmaydi, shuning uchun oyna xato kelganda ochiq turadi.
3. **Daraxt qayta saralanmaydi.** Server modul/darslarni gating hisoblagan
   ketma-ketlikda beradi; frontend `sort` qilsa ekrandagi tartib bilan gating
   ajralib ketardi.
4. **Tartib to'liq ro'yxat bilan.** "Bu elementni yuqoriga sur" so'rovi yo'q —
   har surishda joriy ketma-ketlikdan yangi to'liq Id ro'yxati quriladi.

## 🔴 BLOKLOVCHI — ishlab turgan API образи kurs kodidan OLDIN qurilgan edi

| | vaqt (UTC) |
|---|---|
| `zinnur/api:dev` образ qurilgan | `04:18:20` |
| `6a89c72` "FAZA 3.1: kurs kontenti CRUD API" | `04:58:11` |

Ya'ni 5080 portdagi API kurs endpointlarini **bilmasdi**: `GET /api/v1/courses`
→ **404, tanasi bo'sh**. Swagger'dagi 48 yo'lning birortasida "course" yo'q edi.
Yangi UI brauzerda ochilsa tushunarli xato ham ko'rsatmasdi.

`docker compose build api && docker compose up -d api` — hal qildi, endi
`GET /courses` → 200. **Saboq:** kod commit qilingani образ yangilangani
degani emas; frontend yangi endpointga ulanganda образ sanasini tekshirish kerak.

## 🔴 Uchta xato — agent auditi topdi, hammasi tasdiqlandi va tuzatildi

### 1. Jonli darsda ishtirokchilar ro'yxati HECH QACHON to'ldirilmasdi

`useLiveHub.ts` `JoinSession` javobini `Array.isArray(result)` bilan
tekshirardi. Hub esa **obyekt** qaytaradi:
`JoinSessionResult(Session, Participants, Count)` (`LiveClassHub.cs:216`).
Shart doim `false` → `presence` Map bo'sh qolardi.

**Amalda:** 20 kishi o'tirgan darsga kirgan odam bo'sh ishtirokchilar panelini
va `0` sanog'ini ko'rardi. Faqat undan KEYIN kirganlar delta bilan qo'shilardi.
Xato chiqmagani uchun ko'zga tashlanmagan.

**Tuzatildi:** `result.participants` massividan o'qiladi. Sanoq ro'yxatdan
hisoblanadi (server `count` ham beradi, lekin ikki manba ajralib qolmasin).

### 2. `SessionEnded` hodisasi backendda umuman yuborilmaydi

`grep -rn 'IHubContext' backend/src` → **0 natija**. `LiveSessionService.EndAsync`
faqat bazaga yozadi. Frontend esa hodisani tinglaydi (`HubEvent.SessionEnded`,
`SessionEndedPayload { sessionId }`).

**Amalda:** ustoz "Darsni yakunlash" ni bossa, o'quvchilar ekranida hech narsa
o'zgarmaydi — video ulanishi ochiq qoladi, "dars tugadi" ekrani chiqmaydi,
sahifani qo'lda yangilamaguncha bilmaydi.

**Tuzatildi** (agent API xatosi bilan uzilib qolgani uchun qo'lda yozildi):

| Fayl | Nima |
|---|---|
| `Application/Common/Interfaces/ILiveSessionNotifier.cs` | PORT — use-case shu abstraksiyaga murojaat qiladi |
| `WebApi/Services/LiveSessionNotifier.cs` | SignalR implementatsiyasi (`IHubContext<LiveClassHub>`) |
| `WebApi/Hubs/LiveClassHub.cs` | `SessionEndedEvent(long SessionId)` shartnomaga qo'shildi; `GroupName` `private` → `internal` |
| `Application/LiveSessions/Services/LiveSessionService.cs` | `EndAsync` oxirida — `SaveChangesAsync` dan KEYIN — xabar |
| `WebApi/Program.cs`, `ApiLog.cs` | DI ro'yxati + ikkita `[LoggerMessage]` (CA1848) |

**Qabul qilingan qarorlar:**

1. **Nima uchun port, controller emas.** Darsni yakunlash bitta yo'l bilan
   cheklanmaydi — rejada muddati o'tgan darslarni avto-yakunlaydigan fon
   xizmati ham bor (FAZA 5.5). Broadcast controller'da bo'lsa o'sha yo'l
   jimgina xabarsiz qolardi. Use-case ichida — dars QANDAY yakunlansa ham
   xabar ketadi. Qatlam yo'nalishi saqlanadi: `Application` SignalR ni
   bilmaydi (`IChatMessageWriter` bilan bir xil naqsh).
2. **Yuborish hech qachon istisno ko'tarmaydi** (port kelishuvi, izohda
   yozilgan). Xabar yetmasa ham dars bazada yakunlangan bo'lib qoladi —
   aks holda ustoz 500 ko'rib "yakunlanmadi" deb qayta bosardi.
3. **`GroupName` ikki joyda qo'lda yozilmaydi** — hub'dagi yagona manba
   `internal` qilindi, aks holda biri o'zgarganda ikkinchisi bo'sh xonaga
   xabar yuborib turardi.

**Testlar** (`LiveSessionEndBroadcastTests`, 3 ta):
- host yakunlaganda xabar ketadi;
- ★ **commit-then-send qulflandi**: josus xabar kelgan PAYTDA yangi scope'dan
  bazani o'qiydi va holat `Ended` ekanini tekshiradi — kelajakda kimdir
  qatorlarni almashtirsa test yiqiladi;
- 403 holatida xabar KETMAYDI (aks holda begona odam so'rov yuborib, jonli
  darsdagi hammani ekrandan chiqarib yuborardi).

```
dotnet build : 0 xato, 0 ogohlantirish
dotnet test  : 407 test (296 unit + 111 integratsiya), 0 yiqilgan
api образi   : qayta qurildi, /health/ready → Healthy
```

### 3. Qidiruvda bitta harf yozilishi bilan jadval yo'qolardi

Server minimal uzunlik talab qiladi: foydalanuvchilar **3**, guruhlar **2**,
kurslar **2** belgi (`MinSearchLength`). Qisqasi → 400. Frontend esa bir belgidan
boshlab yuborardi → `DataStatus` xato holatiga o'tib **jadval butunlay
yo'qolardi**, o'rniga qizil banner chiqardi.

**Tuzatildi:** har entity o'z chegarasini eksport qiladi
(`USER_SEARCH_MIN=3`, `GROUP_SEARCH_MIN=2`, `COURSE_SEARCH_MIN=2` — umumiy
doimiy YO'Q, chunki chegaralar ataylab boshqacha). Qisqa satr **umuman
yuborilmaydi**, maydon ostida "kamida N belgi kiriting" yoziladi. `queryKey` ham
amaldagi qidiruvga bog'landi — qisqa satr yozilganda keraksiz so'rov ketmaydi.

## Jonli tekshiruv natijalari (5080, haqiqiy stack)

| Tekshiruv | Natija |
|---|---|
| Kurs → modul → 3 dars yaratish (UI tanalari bilan) | 201, `id` javob tanasida |
| `PUT` uch maydon bilan → `position` saqlanadimi | ✅ saqlanadi (kurs/modul/dars) |
| Darslarni teskari tartiblash (to'liq ro'yxat) | 200, `GET` yangi tartibni qaytardi |
| Reorder — bitta Id tushirilgan | 400 + sabab `errors.orderedIds` da, tartib **o'zgarmadi** |
| `DELETE /courses/1` (3 guruh biriktirilgan) | 409, `detail` da to'liq sabab (UI shu matnni ko'rsatadi) |
| Qidiruv 1 belgi / 2 belgi | 400 / 200 — klient tuzatishi shuning uchun kerak edi |
| `unlocked` / `lockReason` admin uchun | `true` / `null` (gating faqat o'quvchiga) |
| Tozalash | dars/modul/kurs 204, bazada faqat ATF qoldi |

### Bilib qo'yish kerak (xato emas, xatti-harakat)

1. **Kurs o'chirilganda qolgan kurslarning `position` va `updatedAt` i qayta
   yoziladi** (`DeleteAsync` ichidagi `Reindex`) — sinov kursi o'chirilgandan
   keyin ATF `position` `1` → `0` bo'ldi.
2. **400 va 409 xatolari boshqa joyda:** 400 da `detail` doim umumiy
   ("Kiritilgan ma'lumotlarda xatolik bor"), sabab `errors` ichida; 409 da esa
   `detail` to'liq. Klientdagi `ApiError.userMessage` ikkalasini ham to'g'ri
   o'qiydi (`validationSummary ?? message`) — tekshirildi.
3. **`orderedIds: null` boshqa konvert beradi** (framework validatsiyasi, kalit
   `OrderedIds` bosh harf bilan). `validationSummary` kalit nomiga qaramaydi.
4. **Modul/dars yaratishda `Location` sarlavhasi kursga ishora qiladi**, yangi
   obyektga emas — `id` faqat tanadan olinadi (UI shunday qiladi).
5. **Bo'sh query qiymati 400 beradi** (`?Page=` → `The value '' is invalid`),
   lekin klient `buildUrl` da `undefined`/`null` ni tashlab ketadi — haqiqiy
   so'rovda bo'sh parametr ketmaydi (tekshirildi).

## Holat

```
vue-tsc --noEmit   : toza
eslint --max-warnings 0 : toza
web образ          : qayta qurildi, konteyner healthy, yangi sahifalar bundle'da
api образ          : qayta qurildi (kurs endpointlari endi bor)
```

**Hali sinalmagan:** sahifalar brauzerda ko'z bilan ko'rilmagan (loyihada
Playwright/Vitest yo'q — faqat `vue-tsc`, lint va API sathidagi tekshiruv).

---

# Keyingi fazalar (o'sha kun, 12:00 dan)

## ✅ Guruh a'zoligi boshqaruvi — UI ulandi

**Muammo:** a'zolik endpointlari (qo'shish, pauza, davom ettirish, chiqarish,
ko'chirish) va guruh hayot sikli (arxivlash, tiklash, jadvalni qayta tuzish)
backendda TAYYOR edi, lekin ularni **hech qanday UI chaqirmasdi** — o'quv
bo'limi ilovadan turib guruhga o'quvchi qo'sha olmasdi.

| Fayl | Nima |
|---|---|
| `features/group-members/ui/GroupMembersPanel.vue` | Ro'yxat + barcha amallar (telefon kartochkasi va desktop jadvali) |
| `features/group-members/ui/AddMemberDialog.vue` | Serverdagi qidiruv orqali o'quvchi tanlash (1500+ foydalanuvchi bitta `select` ga sig'maydi) |
| `features/group-members/ui/PauseMemberDialog.vue` | Muddatli yoki muddatsiz pauza |
| `features/group-members/ui/MoveMemberDialog.vue` | Boshqa guruhga ko'chirish |
| `entities/group/api/group-api.ts` | 8 yangi chaqiruv |
| `pages/teacher/TeacherGroupPage.vue` | 92 satrlik ichki ro'yxat panelga almashtirildi; arxivlash/tiklash va jadvalni qayta tuzish qo'shildi |
| `shared/ui/ConfirmDeleteDialog.vue` | `confirmLabel` prop'i |

**Qarorlar:**
1. **"Chiqarish", "o'chirish" emas.** Server yumshoq o'chiradi (yozuv qoladi,
   holati `Stopped`) — davomat va to'lov tarixi a'zolikka ishora qilib turadi.
   Tugmani "o'chirish" deb atash foydalanuvchini ma'lumot yo'qoladi deb
   o'ylashga majburlardi.
2. **Ko'chirish bitta so'rov.** "Avval chiqar, keyin qo'sh" ketma-ketligi
   takrorlanmaydi — server buni bitta tranzaksiyada bajaradi, aks holda
   yarim bajarilgan ko'chirishda o'quvchi hech qaysi guruhda qolmasdi.
3. **Tarixiy yozuvlar (`Stopped`/`Moved`) ustida amal yo'q.**
4. **Rol tekshiruvi takrorlanmaydi:** `canManage` faqat tugmalarni yashiradi,
   haqiqiy qoida serverda (`[Authorize(Roles="Academic,Admin")]`).

**Jonli tekshirildi** (vaqtinchalik guruh va o'quvchida, keyin tozalandi):

```
qo'shish            201  Active
takror qo'shish     409  "O'quvchi allaqachon shu guruhda."
muddatli pauza      Paused   pausedUntil=2026-09-01
muddatsiz pauza     Paused   pausedUntil=null
davom ettirish      Active
ko'chirish A->B     left=Moved  arrived=Active
chiqarish           200  Stopped
jadval qayta tuzish +5 / −5, saqlangan 0
arxivlash/tiklash   isActive false -> true
```

## ✅ FAZA 4.1 — Moliya DOMAIN qatlami (pul mantig'i)

FAZA 4 umuman boshlanmagan edi. Loyihaning isbotlangan tartibi bo'yicha
(FAZA 3 da ham shunday) avval **sof Domain va uning testlari** yozildi;
EF konfiguratsiyasi, migratsiya, servis va endpointlar — keyingi bosqich.

| Fayl | Nima |
|---|---|
| `Domain/Finance/BillingPeriod.cs` | `YYYY-MM` qiymat turi (taqqoslash SON bo'yicha) |
| `Domain/Finance/ReceiptNumber.cs` | `ZN-2026-07-000123` formati va ketma-ketligi |
| `Domain/Finance/PaymentAllocator.cs` | Taqsimlash, balansdan yopish, qaytarish |
| `Domain/Finance/PaymentBlockPolicy.cs` | Qarz chegarasi va qamrov ierarxiyasi |
| `Domain/Entities/Payment.cs` | Oylik yozuv: `ApplyPayment`, `Waive`, `Reverse`, invariantlar |
| `Domain/Entities/Tariff.cs` | Narx tarixi + aniqlik darajasi (guruh > kurs > umumiy) |
| `Domain/Entities/StudentDiscount.cs` | Foiz/summa chegirma, muddat, qo'llash |
| `Domain/Entities/StudentAccount.cs` | Balans (ortiqcha to'lov shu yerda saqlanadi) |
| `Domain/Entities/PaymentTransaction.cs` | Moliya jurnali |
| `Domain/Entities/PaymentAudit.cs` | Kim/qachon/nimadan-nimaga |
| `Domain/Enums/Enums.cs` | `PaymentStatus`, `DiscountKind`, `PaymentTransactionKind`, `PaymentBlockScope` |

### Eski tizimning qaysi xatolari ATAYLAB takrorlanmadi

| Eski xato | v2 da qanday |
|---|---|
| `months_covered = max(1, round(amount/monthly))` — 100 000 so'm 540 000 lik oyni "to'langan" qilardi | Pul qancha bo'lsa shuncha yopiladi; qolgani `Partial` bo'lib qarz bo'lib turadi |
| Ortiqcha pul jim yo'qolardi | `AllocationResult.Leftover` → balansga; balans keyingi oy avtomatik ishlatiladi |
| Qaytarish faqat jurnalga yozilardi, oy "to'langan" qolardi | `Payment.Reverse` holatni `Partial`/`Due` ga qaytaradi, `PaidAt` tozalanadi |
| Qarz butun `amount` bo'yicha hisoblanardi | `Outstanding = Amount − PaidAmount` |
| Davr oddiy satr edi (`"2026-7"` tartibni buzardi) | `BillingPeriod` qiymat turi, taqqoslash son bo'yicha |
| Pul `float` edi | Hamma joyda `decimal` |
| Bloklash sharti endpointlar bo'ylab tarqalgan, ba'zi joyda `>=`, ba'zi joyda `>` | `PaymentBlockPolicy` — bitta joy, chegaraga TENG qarz bloklamaydi |
| Qisman to'lovda ham `paid_at` yozilardi | To'liq to'lanmaguncha sana qo'yilmaydi (kunlik tushum soxta bo'lmasin) |

### Ataylab Domain'dan tashqarida qoldirildi

Tarif va chegirmani **tanlash** (aniqlikdan umumiyga qidirish) — bu so'rov
(query) ishi, Application qatlamiga tegishli. Domain faqat TARTIB QOIDASINI
beradi (`Specificity`) va qo'llashni bajaradi (`Apply`) — shuning uchun
qoida SQL ichida yashirinib qolmaydi.

### Natija

```
dotnet build Zinnur.sln : 0 xato, 0 ogohlantirish
dotnet test UnitTests   : 346 test (ilgari 296), 0 yiqilgan
shundan moliya testlari : 50 ta
```

**Keyingi qadam (FAZA 4.2):** EF konfiguratsiyasi + migratsiya
(`CHECK` cheklovlari: `PaidAmount BETWEEN 0 AND Amount`, `Balance >= 0`,
`(StudentId, GroupId, Period)` unikal), so'ng `PaymentService` va endpointlar.

---

# Agentlar jamoasi bilan sessiya (2026-07-30, 12:00 dan)

Uch agent parallel ishladi (backend infra · frontend · QA), har biriga qat'iy
fayl chegarasi berildi. Natijalar tekshirilib qabul qilindi — quyida faqat
DALIL bilan tasdiqlangan holat.

## 🔴 XAVFSIZLIK — kirish tokeni bekor qilinmasdi (QA topdi, tuzatildi)

`JwtTokenService` tokenga `ver` (sessiya versiyasi) qo'yadi va izohida
*"WebApi ham SHU nomni tekshiradi"* deb yozilgan edi — **tekshiruv esa
yozilmagan edi**. Natijada imzosi to'g'ri kirish tokeni 15 daqiqa so'zsiz
qabul qilinardi.

**Amaldagi oqibati (jonli isbotlangan):** o'chirilgan (haydalgan yoki
to'lamagan) o'quvchi eski tokeni bilan `POST /live-sessions/{id}/token` dan
**200** olib, LiveKit xonasiga `canPublish:true` bilan kirardi va chatga
yozardi. `logout` ham tokenni o'ldirmasdi.

Kurs/vazifa/guruh servislari buni `IsActive` tekshiruvi bilan qoplagan edi,
**`LiveSessionService` esa qoplamagan** — ya'ni qoida har servisda qo'lda
takrorlanishi kerak edi va bir joyda tushib qolgan.

**Tuzatish — markaziy joyda:**

| Fayl | Nima |
|---|---|
| `WebApi/Program.cs` (`OnTokenValidated`) | Tokendagi `ver` endi joriy versiya bilan solishtiriladi; mos kelmasa yoki hisob faol bo'lmasa `context.Fail()` → 401. SignalR ulanishi ham shu darvozadan o'tadi |
| `Application/Common/Interfaces/IAuthStateCache.cs` | Sessiya holati porti (yangi) |
| `Application/Auth/Services/AuthStateCache.cs` | Redis kesh (60s) + baza; kesh chiqish/o'chirish/parol tiklash/rol o'zgarishida ANIQ tozalanadi — ya'ni amal darhol kuchga kiradi |
| `Application/Users/Services/UserService.cs` | `SetActive`, `ResetPassword`, rol o'zgarishida kesh tozalash |
| `Application/LiveSessions/Services/LiveSessionService.cs` | `IsActive` tekshiruvi (ikkinchi qatlam) |

**Yo'l-yo'lakay topilgan ikkinchi xato:** `RedisCacheService` kalitlarni
YALANG'OCH ishlatardi. Integratsiya testlarida har sinf o'z Postgres bazasini
oladi, Redis esa umumiy — `auth:state:4` kaliti turli bazalardagi bir xil
Id'lar uchun bir-birining ustiga yozildi va **9 ta test yiqildi**. Kalitlarga
sozlanadigan MAKON qo'shildi (`Redis:KeyPrefix`, standart `zinnur`); test
fabrikasi har sinfga o'z makonini beradi. Bu prod uchun ham to'g'ri: bitta
Redis'ni ikki muhit baham ko'rsa endi aralashmaydi.

**Testlar (yangi):** `AccessTokenRevocationTests` — o'chirilgan foydalanuvchi
eski token bilan 401; **jonli dars tokeni ham 401**; logoutdan keyin 401;
qayta faollashtirilgan foydalanuvchi yana kira oladi (kesh "faol emas"
holatida qotib qolmasin).

**Jonli tasdiq (образ qayta qurilgandan keyin):**
```
o'chirishdan oldin  /auth/me                    200
o'chirilgandan keyin /auth/me                   401
                     /live-sessions             401
                     POST /live-sessions/N/token 401   ← ilgari 200 + video tokeni
logoutdan keyin      /auth/me                   401   ← ilgari 200
```

## ✅ FAZA 4.2 — moliya sxemasi bazada

6 konfiguratsiya, 6 `DbSet`, 2 migratsiya (`AddFinanceTables` +
`AddPaymentAmountConsistencyCheck`). Jonli bazada tasdiqlandi: 6 jadval,
**11 `CHECK`**, 3 unikal indeks.

Muhim qarorlar: pul `numeric(18,2)`; `Period` — `varchar(7)` (`char(7)`
Postgres'da bo'shliq bilan to'ldiriladi va `==` ni jimgina buzardi); barcha
FK `Restrict` (pul tarixi kaskad bilan yo'qolmasin; `SetNull` esa guruhga
atalgan chegirmani JIMGINA umumiy qilib yuborardi); `xmin` optimistik qulf
faqat `Payment` va `StudentAccount` da.

**Agent Domain'da bo'shliq topdi va o'zi tuzatmadi (to'g'ri qaror):**
`Payment.Validate()` da `Amount = BaseAmount − DiscountAmount` invarianti
yo'q edi — `BaseAmount=600 000, DiscountAmount=60 000, Amount=999 999` qatori
hamma tekshiruvdan o'tib ketardi va moliya hisoboti uydirmaga aylanardi.
Invariant + `CK_Payments_Amount_Consistent` + 2 test qo'shildi. Amaliy
ma'nosi: oy summasini qo'lda kamaytirish `DiscountAmount` orqali ifodalanadi
va hisobotda ko'rinadi.

## ✅ Uy vazifalari oqimi UI'ga ulandi

Ilgari ustoz vazifa YARATA olmasdi, o'quvchi javob TOPSHIRA olmasdi —
endpointlar faqat `curl` bilan ishlatilardi.

Yangi: `features/assignment-form/` (nishon tanlagich + forma),
`features/assignment-submit/` (matn + fayl, multipart),
`features/grading/ui/ReopenDialog.vue`, `pages/manage/ManageAssignmentsPage.vue`
(+ marshrut va menyu bandi).

Muhim nuqtalar:
- **`multipart`**: `http.ts` da `FormData` uchun shox — `Content-Type` QO'LDA
  QO'YILMAYDI (boundary'ni brauzer qo'yadi). Agent buni jonli isbotladi:
  qo'lda qo'yilganda server `400 "Missing content-type boundary"` beradi.
- **`PUT` tuzog'i**: `UpdateAssignmentRequest` TS turida hamma maydon
  MAJBURIY qilindi — "yuborishni unutish" endi kompilyatsiya xatosi.
- **Tuzatilgan bug**: `assignmentState()` da `isOverdue` mustaqil to'siq edi va
  bu serverga zid (`SubmitAsync` kechikkan javobni RAD ETMAYDI, faqat `IsLate`
  deb belgilaydi) — kechikkan o'quvchida tugma umuman chiqmasdi.
- **503 shoxi**: R2 sozlanmaganda server foydali maslahat beradi
  ("matnli javob yuborishingiz mumkin"), eski kod uni "Serverda xatolik" bilan
  almashtirardi.

## QA supurgisi — 34 tekshiruv

Auth, ruxsat matritsasi (o'quvchi va ustoz uchun 20 tadan endpoint — 40/40
`403`), to'liq CRM zanjiri, gating, jonli dars (`start` → `token` → `end`),
davomat yozuvi, sog'liq, SPA marshrutlari, rate-limit. **`SessionEnded`
hodisasi haqiqiy SignalR klienti bilan qabul qilindi** — ya'ni bugun ertalab
qo'shilgan broadcast uchdan-uchgacha ishlaydi.

Bugungi `courseId` tuzatishi ham jonli tasdiqlandi: frontend tanasi bilan kurs
saqlanadi; `courseId` siz yuborilsa kurs uziladi va o'quvchida `GET /courses/N`
**200 → 403** bo'ladi.

## Ochiq qolgan savollar va ishlar

1. **Refresh token qayta ishlatish aniqlanmaydi** (QA: M-3). Rotatsiya bor,
   lekin eski refresh token 14 kun ishlayveradi — `jti` hech qayerda
   saqlanmaydi. O'g'irlangan token sezilmaydi. Ataylabmi yoki xatomi — kodda
   izoh yo'q, qaror kerak.
2. **`Method` maydoni erkin satr** (`"naqd"`, `"cash"`, `"CASH"`) — kunlik
   kassa usul bo'yicha bo'linmaydi. Enum qilinsinmi va qanday qiymatlar bilan?
3. **429 javobida `Retry-After` yo'q** — foydalanuvchi qancha kutishini bilmaydi.
4. **403 xabarlari umumiy** — server sababi ("Faqat o'z guruhingizga vazifa
   bera olasiz") `ApiError.userMessage` da yo'qoladi. Tuzatish hamma sahifaga
   ta'sir qiladi.
5. **Vazifa #4 va QA chat xabari (`ChatMessages id=4450`)** dev bazasida qoldi
   — o'chirish endpointi yo'q.

## Holat

```
dotnet build : 0 xato, 0 ogohlantirish
dotnet test  : 463 test (348 unit + 115 integratsiya), 0 yiqilgan
vue-tsc      : toza · eslint --max-warnings 0 : toza
api + web    : qayta qurildi, 5 xizmat healthy, moliya jadvallari bazada
```

---

# Qabul qilingan qarorlar (foydalanuvchi) va keyingi faza

## To'rt qaror bajarildi

| Qaror | Amalga oshirilishi | Jonli dalil |
|---|---|---|
| Refresh token **7 kun** (14 emas) | `Jwt:RefreshDays`, `.env`, `.env.example`, `JwtOptions` | Login javobi: refresh 7.0 kun, access 15 daqiqa |
| To'lov usuli **naqd/karta** | `PaymentMethod { Cash, Card }`; `Payment.Method` va `PaymentTransaction.Method` enum | Bazada ikkala ustun ham `integer` |
| `Retry-After` | `OnRejected` da sarlavha; klient uni o'qib aniq soniyani ko'rsatadi | 21-urinish → `429` + `Retry-After: 60` |
| 403 matnlari | `ApiError.userMessage` server `detail` ini ko'rsatadi | `vue-tsc`/`eslint` toza |

**7 kun — yarim yechim, ochiq yozib qo'yildi:** refresh rotatsiya qilinadi, lekin
eskisi bekor bo'lmaydi (`jti` saqlanmaydi) — o'g'irlangan token muddatigacha
ishlaydi. Muddatni qisqartirish oynani kichraytiradi, yopmaydi.

**Migratsiya qo'lda yozildi:** EF `varchar → integer` uchun `AlterColumn`
generatsiya qildi, Postgres esa buni `USING` siz rad etadi. Yozildi:
`USING (CASE lower(trim("Method")) WHEN 'naqd' THEN 0 ... ELSE NULL END)` —
tanilmagan qiymat `NULL` bo'ladi, chunki "usul noma'lum" deb qoldirish
"naqd" deb taxmin qilishdan yaxshiroq (kassa hisobotiga soxta qator qo'shilmasin).

## ✅ FAZA 4.3 — moliya servisi va API

`/api/v1/payments` — 20 ga yaqin endpoint: oylik yozuvlarni ochish (idempotent),
to'lov kiritish (kvitansiya bilan), kechirim, qaytarish, o'quvchi hisobi va
jurnali, bloklash holati, istisno, sozlamalar, tarif va chegirma CRUD.

**Muhim qarorlar:**
- **Chegara va qamrov — BAZADA** (`AppSettings`, kalit nomlari eski tizim bilan
  bir xil: ko'chirish skripti qiymatlarni o'zgartirmasdan ko'chiradi). Sabab:
  bu biznes qarori, uni o'zgartirish uchun reliz kutish noto'g'ri.
- **Yumshoq rejim (`Payments:EnforceBlock`) — KONFIGURATSIYADA.** Sabab: bu
  MUHIT xossasi. Staging bazasi prod nusxasidan tiklanadi — kalit bazada tursa
  prod'ning "qattiq rejim" qiymati staging'ga ko'chib, sinovchilarni bloklardi.
- **To'lov/kechirim/qaytarish — bitta `SaveChanges`**: oy holati + jurnal +
  balans + audit birga. Yarim holat yo'q.
- **Bloklash darvozasi** `LiveSessionService.CreateJoinTokenAsync` (scope
  `Live`) va `CourseService.GetAsync` (scope `Video`) da — token berilgandan
  keyin klient to'g'ridan-to'g'ri LiveKit'ga ulanadi, ya'ni "yo'q" deyishning
  oxirgi nuqtasi shu.

**Jonli tekshirildi (uchdan-uchgacha, keyin tozalandi):**
```
tarif 500 000 + 10% chegirma  -> oy summasi 450 000 (base 500 000, chegirma 50 000)
oy ochish 2 marta             -> created=1, keyin alreadyOpen=1  (idempotent)
qisman to'lov 200 000 (naqd)  -> ZN-2026-07-000001, qarz 250 000, status Partial
ortiqcha to'lov 400 000       -> 250 000 oyga, 150 000 BALANSGA, qarz 0
qaytarish 100 000             -> avval balansdan, oylarga tegilmadi
ustoz /payments               -> 403 (moliya faqat Academic/Admin)
o'quvchi o'z hisobi / begona  -> 200 / 403
```
★ 6-qator eski tizimning eng qimmat xatosining teskarisi: ortiqcha pul endi
yo'qolmaydi, balansga tushadi.

## ✅ FAZA 3.4 — testlar oqimi UI'da

O'quvchi: ro'yxat → kirish ekrani → yechish varaqasi (taymer bilan) → natija.
O'quv bo'limi: test tuzish, savol va variantlar, e'lon qilish/qaytarish,
natijalar jadvali va CSV eksport.

- **Vaqt chegarasi** — taymer server bergan `deadline` bo'yicha sanaydi va
  hech qanday qaror qabul qilmaydi: muddat tugaganda javoblar serverga
  yuboriladi, qabul qilish yoki 409 bilan yopish server ishi. Qo'shimcha
  himoya: avto-topshirish faqat sanoq haqiqatan kuzatilgan bo'lsa ishlaydi —
  telefon soati oldinda bo'lsa varaqa ochilishi bilanoq bo'sh javob
  yuborilib, urinish behuda yopilardi.
- **Ko'p to'g'ri javobli savol** — checkbox va aniq ogohlantirish; "oxirgisi
  yutadi" (eski tizim xatosi) UI tomonda takrorlanmaydi.
- **CSV eksport** `<a href>` bilan ishlamaydi (`Authorization` ketmaydi) —
  `http.download()` qo'shildi, `Accept` endi chaqiruvchi aytmagan bo'lsagina
  JSON bo'ladi.

★ **Birinchi marta HAQIQIY BRAUZERDA sinaldi** (headless Chrome + CDP, jonli
API): o'quvchi 4/4 natija oldi, xodim testni tuzdi va e'lon qildi, konsol toza.
Shu paytgacha frontend faqat `vue-tsc`/`eslint`/API darajasida tekshirilardi.

## Holat (yakuniy)

```
dotnet build : 0 xato, 0 ogohlantirish
dotnet test  : 499 test (356 unit + 143 integratsiya), 0 yiqilgan
vue-tsc + eslint : toza
api + web    : qayta qurildi; AppSettings jadvali va Users.PaymentExempt bazada
```

## Ochiq savollar (qaror kutilmoqda)

1. **Kvitansiya raqami** — Postgres `SEQUENCE` kerakmi? Hozir ketma-ketlik
   chaqiruvchidan olinadi; ikki kassir bir vaqtda ishlasa unikal indeks 409
   beradi (pul yo'qolmaydi, lekin qayta urinish kerak).
2. **Moliya dashboard + Excel eksport** yozilmadi (ROADMAP FAZA 4 qoldig'i).
3. **Oylik yozuvlarni avtomatik ochish** fon vazifasi yo'q — endpoint idempotent,
   jobga tayyor, DB leader lock kerak.
4. **Qarzdorga kurs daraxti butunlay yopiladi** (`Video` qamrovi). Yumshoqroq
   variant: daraxt ko'rinsin, faqat dars mazmuni yopilsin — gating DTO'siga
   yangi `LockReason` kerak.
5. **Test taymeri 60 s tolerantlikni ham ko'rsatadi** (10 daqiqalik testda
   11:00 dan sanaydi) — server `deadline` ga `SubmitGracePeriod` qo'shib beradi.
   Qaror backend tomonda: alohida `expiresAt` yuborilsinmi?
6. **Domain'da qoldirilgan bo'shliqlar:** `User.PaymentExempt` soya ustunda
   (`GroupMember.PausedUntil` naqshi); `Payment` da `TariffId`/`DiscountId` yo'q
   (nizoda "nega 486 000?" savoliga javob faqat audit izohidan topiladi);
   `Payment.Waive` sababni `Note` ustiga yozadi.
7. **Tozalanmagan sinov ma'lumoti:** testlar 11/12/13 (urinishlari bor, server
   409 beradi) — e'londan olingan; vazifa #4; QA chat xabari.

---

# DIZAYN KO'CHIRISH — 1–4 to'lqin (2026-07-30, kechki sessiya)

To'liq reja va qabul mezoni: `docs/DIZAYN_KOCHIRISH_REJASI.md`.

**Sabab:** loyiha egasi eski ilova ekranlarini ko'rsatib, v2 unga o'xshamasligini
aytdi. Tekshiruvda ildiz sabab topildi — v2 rangni `app/static/app.css` dan,
ya'ni BAZAVIY fayldan olgan (yashil `#2f9e41`), holbuki eski loyihada HAR BIR
panel uni inline ustidan yozadi va navy + oltin ishlatadi. Ya'ni hech bir
foydalanuvchi ko'rmagan rang butun tizimga tarqalgan edi.

Talab: **"deyarli bir xil"**, "ruhida" emas — eski tizimdan bugun haqiqiy
foydalanuvchilar ishlaydi.

## Natija

| Rol | Tema | Karkas | Holat |
|---|---|---|---|
| O'quvchi | `#051e2d` / `#f5b731` | 390px, pastda 5 tab (Bosh sahifa · Kalendar · O'quv · Reyting · Chat) | ✅ |
| Ustoz | `#092235` / `#ffcc33` | yon menyu: Bosh sahifa · Guruhlarim · Darslarim · Vazifalar · Savollar | ✅ |
| Kurator | `#092235` / `#ffcc33` | ustiga **Kuratorlik**; guruh ichida Testlar/Reyting YASHIRIN | ✅ |
| O'quv bo'limi | `#0f2d48` / `#f2c84b` | yon menyu + To'lovlar va Moliya | ✅ |

Tema `<html>` ga qo'yiladi (teleport qilingan modal/toast ham temada qolsin),
komponentlar nusxalanmadi — bitta `shared/ui`, uchta token to'plami.

## Yo'l-yo'lakay topilgan HAQIQIY xatolar

| Xato | Oqibati | Kim topdi |
|---|---|---|
| `data-theme` karkas `<div>` ida edi | Profil varag'i xodim temasining yashil rangida chiqardi (teleport karkasdan tashqarida) | o'quvchi karkasi agenti, brauzerda |
| `BaseButton` primary'da `text-white` | Oltin fonda kontrast ~1.9:1 — o'qib bo'lmaydi | o'sha agent |
| `outstanding` holatga qaramaydi | Kechirilgan oy jadvalda "qarz 540 000" deb turardi, hisobda esa "qarz 0" — kassir yana pul so'rardi | moliya agenti, brauzerda |
| Modal qatlamlari | Hisob oynasidan ochilgan to'lov oynasi ORTIDA chizilardi (`BaseModal` teleport, DOM tartibi = e'lon tartibi) | moliya agenti |
| Menyuda "Kuratorlik" ustozda, kuratorda yo'q | Eski shablonda `{% if role == 'assistant' %}` — teskari edi | ustoz paneli agenti (men tuzatdim) |

## Ataylab qilingan chekinishlar (eng muhimlari)

- **Ustozga guruh chati yo'q** — v2 da guruh chati umuman mavjud emas (faqat
  dars ichidagi SignalR chati va kurator↔o'quvchi DM). Sabab ekranda yozilgan.
- **Testlar ro'yxati ustozga 403** — server `Academic,Admin` bilan yopgan;
  ustozga sabab tushuntiriladi va "Reyting"dagi `Test` ustuniga yo'naltiriladi.
- **Moliya dashboard'i chizilmadi** — backendda yig'ma endpoint yo'q, mijozda
  hisoblash minglab qatorni yuklashni talab qilardi. To'g'ri yechim:
  `GET /payments/summary`.
- **"Jami qarz" o'rniga "shu sahifadagi qarz"** — noto'g'ri jami raqami
  hisobotga tushib ketmasin.
- **Kalendarda `localDate`** ishlatiladi, `scheduledStart` dan brauzerda sana
  chiqarilmaydi — chet eldagi o'quvchida kechqurungi dars kechagi kunga
  tushib qolardi.

## Holat

```
614 test yashil (408 unit + 206 integratsiya) · build 0 ogohlantirish
vue-tsc + eslint toza · api va web qayta qurildi
uch tema jonli tasdiqlandi (brauzer, 390px va 1280px)
```

Demo ma'lumot: bir agent sinov uchun `ATF-1 (demo)` darsini "Ended" qilgan va
o'quvchini "Absent" belgilagan edi — ochiq aytdi, SQL bilan qaytarildi.
