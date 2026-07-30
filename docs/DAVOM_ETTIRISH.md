# Yangi sessiyada davom ettirish — TOPSHIRIQ

> Bu fayl **kontekst tugagani uchun** yozildi. Yangi sessiya shu faylni
> o'qib, oldingi suhbatsiz ishni davom ettira oladi.
>
> **Sana:** 2026-07-30 · **Oxirgi commit:** `git log -1` ga qarang

---

## 1. BIRINCHI QADAM — holatni tiklash

```bash
cd ~/Documents/Projects/zinnur-v2
docker compose up -d
docker compose ps          # 5 xizmat healthy bo'lishi kerak
curl -s localhost:5080/health/ready
```

| | |
|---|---|
| Frontend | http://localhost:5173 |
| API + Swagger | http://localhost:5080/swagger |
| Kirish | `admin@zinnur.uz` / `Admin!2345` |
| Eski loyiha (tegilmaydi) | http://localhost:8000 |

**Keyin shu tartibda o'qing:**
1. `docs/PROGRESS.md` — nima bajarilgan (eng muhim)
2. `docs/ROADMAP.md` — qolgan fazalar
3. `docs/SPEC.md` — nom/imzo/port shartnomasi

---

## 2. TUGAGAN FAZALAR

| Faza | Holat |
|---|---|
| 1.1 EF migratsiyalari | ✅ 3 migratsiya, jonli bazada tasdiqlangan |
| 1.2 Testlar + CI | ✅ **298 test** (267 unit + 31 integratsiya) |
| 1.3 Kuzatuv | ✅ Sentry + strukturali log + health checks |
| 2.1 Foydalanuvchilar (CRM) | ✅ 403 himoyasi **amalda isbotlangan** |
| 2.2/2.3 Guruhlar + jadval | ✅ 69 dars generatsiyasi **tasdiqlangan** |
| 3 (Domain) | ✅ 9 entity + testlar |
| 3 (EF + migratsiya) | ✅ `AddLearningProcessTables`, 9 jadval bazada |

---

## 3. ⚠️ TUGALLANMAGAN ISH — birinchi navbatda shu

### 3.1. Faza 3 Application/API — QISMAN bajarilgan

| Qism | Holat |
|---|---|
| Domain (9 entity) | ✅ tayyor va testlangan |
| EF konfiguratsiyalari | ✅ tayyor |
| Migratsiya `AddLearningProcessTables` | ✅ bazaga qo'llangan, 9 jadval |
| `Application/Gating` | 🔶 3 fayl (tugallanmagan) |
| `Application/Assignments` | ❌ **0 fayl** |
| `Application/Tests` | ❌ **0 fayl** |
| `AssignmentsController`, `TestsController` | ❌ yo'q |

Ya'ni **baza qatlami tayyor**, servis va API qatlami qolgan.

**Domain TAYYOR va TESTLANGAN** — faqat Application + WebApi + migratsiya kerak:

| Domain fayl | Nima beradi |
|---|---|
| `Entities/Assignment.cs` | `Validate()`, `EnsureFormatAllowed()`, `IsOverdue()` |
| `Entities/Submission.cs` | `Create()` (birinchi), `Resubmit()`, `Grade()`, `ReopenForResubmit()` |
| `Entities/Test.cs` | `Validate()`, `Publish()`, `EnsureOpenForSubmission()`, `TestQuestion.Score()` |
| `Entities/TestAttempt.cs` | `SubmitAnswers()`, `CloseByTimeout()`, `Deadline()` |
| `Entities/LessonProgress.cs` | `MarkVideoWatched()`, `SetOverride()` |

Testlar `tests/Zinnur.UnitTests/Entities/{Submission,TestAttempt,TestQuestionScoring}Tests.cs`
da — **niyatni ular ko'rsatadi**.

**Bazada allaqachon mavjud unikal kalitlar** (tasdiqlangan):
```
UX_Submissions_AssignmentId_StudentId
UX_TestAttempts_TestId_StudentId
UX_LessonProgress_StudentId_ModuleLessonId
UX_TestAnswers_AttemptId_QuestionId_OptionId   ← uchtalik, TO'G'RI
```

**Kerakli ishlar:**
1. `AssignmentService`, `SubmissionService`, `TestService`, `GatingService` (tugatish)
2. `AssignmentsController`, `TestsController`
3. Fayl yuklash: **chegara STREAMING paytida** tekshirilsin (eski tizimda
   butun fayl RAM'ga o'qilib, keyin tekshirilardi — chegara hech nimani
   himoya qilmasdi)
4. Gating Redis'da keshlansin (~60s), eski tizimda har so'rovda butun kurs
   daraxti qayta hisoblanardi

To'liq talablar: `docs/ROADMAP.md` → FAZA 3.

### 3.2. Groups moduli — TUGADI (ikki ish yopildi)
1. ✅ `tests/Zinnur.UnitTests/Scheduling/` yozildi (+61 test)
2. ✅ Xulosa MAVJUD edi — `"schedule"` ichida ichma-ich, ildizda emas:
   `UpdateGroupResponse(GroupDto Group, ScheduleChangeSummary Schedule)`.
   Mening tekshiruvim ildizga qaragani uchun bo'sh chiqqan edi.

### 3.3. ⚠️ `GroupMember.PausedUntil` — EF shadow ustunida
Groups agenti `pausedUntil` ni Domain'ga qo'shmasdan (Domain uning
qamrovida emas edi) EF shadow ustuni sifatida saqladi. Ustun bazada
HAQIQIY (`GroupMembers.PausedUntil date`), lekin `GroupMember` entity'sida
xossa yo'q — `EF.Property<DateOnly?>` orqali o'qiladi.

**Keyingi qadam:** Domain'ga tegilganda `GroupMember.PausedUntil` xossasini
qo'shish. Nom va tur bir xil — **migratsiya kerak bo'lmaydi**.
Sabab `GroupMemberFields.cs` da yozilgan.

---

## 4. BILISH SHART — texnik eslatmalar

### Build (kompyuterda .NET yo'q — hammasi Docker'da)

```bash
cd ~/Documents/Projects/zinnur-v2/backend
docker run --rm -v "$PWD":/src -w /src \
  -v zinnur-nuget-cache:/root/.nuget/packages \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet build Zinnur.sln -v q --nologo
```

**NuGet kesh volume'ini ULASH SHART** — keshsiz har build 4 daqiqa, kesh bilan 3 sekund.

### Testlar (integratsiya testlari ishlab turgan bazani talab qiladi)

```bash
cd ~/Documents/Projects/zinnur-v2/backend
PGU=$(grep '^POSTGRES_USER=' ../.env | cut -d= -f2)
PGP=$(grep '^POSTGRES_PASSWORD=' ../.env | cut -d= -f2)
docker run --rm -v "$PWD":/src -w /src -v zinnur-nuget-cache:/root/.nuget/packages \
  --add-host=host.docker.internal:host-gateway \
  -e TEST_POSTGRES="Host=host.docker.internal;Port=5440;Database=postgres;Username=$PGU;Password=$PGP" \
  -e TEST_REDIS="host.docker.internal:6390" \
  mcr.microsoft.com/dotnet/sdk:9.0 dotnet test Zinnur.sln -v q --nologo
```

> Postgres paroli `zinnur` EMAS — `.env` dan o'qiladi (25 belgili).

### Analizator tuzoqlari (`TreatWarningsAsErrors=true`)

| Kod | Nima qilish |
|---|---|
| `CA1848` | `logger.LogX("...")` TAQIQ — `[LoggerMessage]` (namuna: `ApiLog.cs`) |
| `CA1305` | Har `ToString()`/`Parse` ga `CultureInfo.InvariantCulture` |
| `CA2249` | `IndexOf(...) >= 0` → `Contains(...)` |
| `CA1711` | Tur nomi `Queue`/`Flags` bilan tugamasin |
| `CA1716` | Zaxiralangan so'zlar (`Module` → `CourseModule`) |
| `CA1822` | Instance ma'lumotiga tegmasa `static` qilinsin |

`backend/.editorconfig` `**/Migrations/*.cs` va `tests/**` uchun yumshatilgan —
undan ortiq yumshatmang.

### API konvensiyalari
- Enum'lar JSON'da **SATR**: `"role": "Academic"`, `"weekdays": ["Monday"]`
- Xatolar RFC 7807 ProblemDetails + `traceId`
- `Domain` da `TashqiBog'liqlik = 0` — buzilmasin

---

## 5. ⚠️ MINALAR — ko'chirishda ehtiyot bo'ling

### 5.1. `DayOfWeek` konvensiyasi
Eski Python: **dushanba = 0**. .NET: **yakshanba = 0**.

```
dotnet = (python + 1) % 7
```

Ma'lumot ko'chirish skriptida konvertatsiya **MAJBURIY**, aks holda barcha
darslar bir kun siljiydi. `Group.Weekdays` izohida yozilgan.

### 5.2. LiveKit ICE — dev va prod FARQ QILADI

Bu **eng ko'p uchraydigan self-hosted LiveKit nosozligi** va u o'zini
yashiradi: xona ochiladi, ishtirokchilar ro'yxati to'ladi, **ovoz va video
kelmaydi**.

| Muhit | Yechim |
|---|---|
| **Dev** (`docker-compose.yml`) | `NODE_IP=127.0.0.1`, `LIVEKIT_RTC_USE_EXTERNAL_IP=false`, `LIVEKIT_RTC_ENABLE_LOOPBACK_CANDIDATE=true` — brauzer ham LiveKit ham bir mashinada |
| **Prod** (`docker-compose.prod.yml`) | `network_mode: host` + yuqoridagi uchtasi `!reset null` bilan **BEKOR QILINADI** |

⚠️ Prod'da `NODE_IP=127.0.0.1` qolib ketsa media **hech qachon** ishlamaydi.
Overlay buni `!reset` qiladi — tekshirish:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml config | grep -E "NODE_IP|USE_EXTERNAL"
# faqat LIVEKIT_NODE_IP ko'rinishi kerak
```

Konfiguratsiya **bitta faylda** (`infra/livekit/livekit.yaml`) — dev uni env
orqali ustidan yozadi. Ikkinchi yaml yaratmang: fayllar vaqt o'tib
bir-biridan uzoqlashadi va "mening mashinamda ishlaydi" holati tug'iladi.

### 5.3. Test konfiguratsiyasi — `UseSetting`, `ConfigureAppConfiguration` EMAS
Minimal hosting'da `Program.cs` konfiguratsiyani host **qurilayotganda**
o'qiydi; factory callback'lari keyinroq ishlaydi. `AddInMemoryCollection`
kuchga kirmaydi. `ZinnurApiFactory` allaqachon `UseSetting` ishlatadi.

---

## 6. HALI ISBOTLANMAGAN DA'VOLAR

Bularni "ishlaydi" deb hisoblamang:

1. **200 foydalanuvchi.** `tests/load/signalr-load.mjs` yozilgan, lekin
   **yugurtirilmagan**. Butun arxitekturaning asosiy da'vosi — hozircha nazariya.
   ```bash
   cd ~/Documents/Projects/zinnur-v2 && node tests/load/signalr-load.mjs
   ```
2. **Haqiqiy video/audio oqimi.** Signalling tasdiqlangan (LiveKit tokenni
   qabul qiladi, `/rtc/validate` → `success`), lekin ikki brauzer ochib
   media kelishini **hech kim sinamagan**.
3. **Prod deploy.** `docker-compose.prod.yml` va `network_mode: host`
   haqiqiy Ubuntu serverda **ishga tushirilmagan**.
4. **Zaxiradan tiklash.** Skript bor, sinalmagan.
5. **Qidiruv query plan.** `pg_trgm` GIN indeksi yaratilgan, lekin katta
   hajmda `EXPLAIN` bilan tekshirilmagan (3 ta foydalanuvchida planner
   har doim seq scan tanlaydi).

---

## 7. UMUMAN BOSHLANMAGAN — 2 460 satr biznes mantiq

| Modul (eski tizim) | Satr | Izoh |
|---|---|---|
| `payments_svc` + `finance_svc` | **775** | ⚠️ Eng nozik — pul. Har use-case uchun test MAJBURIY |
| `telegram_bot` | 486 | O'quvchilar uchun yagona kirish yo'li. ⚠️ Mini App faqat `student` roli (eski zaiflik X-1) |
| `notifications` | 396 | Outbox + worker. ⚠️ commit-then-send (eski tizimda teskari edi) |
| `points_svc` | 233 | Reyting. ⚠️ `overall` snapshot NULL `group_id` tufayli dublikat berardi |
| `storage` | 167 | R2 fayl yuklash |
| `analysis_svc` | 165 | AI tahlil. ⚠️ butun videoni RAM'ga yuklamang (eski OOM sababi) |
| `backup_svc` | 154 | Zaxira |
| `dm_svc` | 84 | Kurator ↔ o'quvchi chat |

**Frontend:** eski tizimda 4 panel, 14 228 satr. v2 da 4 sahifa
(`LoginPage`, `StudentHomePage`, `LiveRoomPage`, `NotFoundPage`).
Admin CRM paneli (eski `academic.html` = 6 275 satr) **yo'q**.

---

## 8. TAVSIYA ETILGAN KEYINGI QADAM

1. **Faza 3 Application/API** ni tugatish (Domain tayyor — eng arzon ish)
2. **Yuklama testini yugurtirish** — 200 foydalanuvchi da'vosini isbotlash
3. Keyin tanlash: **pul moduli** (eng nozik) yoki **admin CRM paneli** (eng katta)

Agentlar bilan ishlashda: **buyruq bermang, muammoni tushuntiring.** Shu
tunda agent mening `RoleClaimType` taklifimni rad etib, to'g'ri yechimni
topdi — chunki unga "nima uchun" aytilgan edi. Batafsil: `docs/PROGRESS.md`
→ "Koordinator xatosi".
