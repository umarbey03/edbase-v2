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
