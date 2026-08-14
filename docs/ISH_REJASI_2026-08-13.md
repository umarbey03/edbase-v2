# Ish rejasi — o'quvchi va ustoz panellari (2026-08-13 talablari)

> **Manba:** loyiha egasining 2026-08-13 kungi 37 ta talabi (17:38–18:42).
> **Holat:** 7 ta tadqiqot agenti butun kod bazasini audit qildi. Quyidagi
> har bir qator KODDAN tasdiqlangan — taxmin emas.
>
> **Bu hujjat nima uchun:** talablarning bir qismi ALLAQACHON bajarilgan,
> bir qismi bir necha satrlik ish, bir qismi esa yozma me'moriy qarorlarni
> BEKOR QILADI. Ularni ajratmasdan boshlash — eng qimmat xato bo'lardi.

---

## 0. QISQA XULOSA

| Toifa | Soni | Izoh |
|---|---|---|
| ✅ **Allaqachon bajarilgan** | 5 | Kod yozilmaydi, faqat tasdiqlanadi |
| 🟢 **Arzon (faqat frontend, migratsiyasiz)** | 11 | Bugun kechasi bajarilishi mumkin |
| 🟡 **O'rta (backend, migratsiyasiz yoki kichik)** | 9 | 1–3 kunlik ishlar |
| 🔴 **Katta (entity + migratsiya + testlar)** | 8 | Har biri alohida to'lqin |
| ⛔ **QAROR KUTILMOQDA** | 2 | Kod yozilmaydi — loyiha egasi hal qiladi (Q3 va Q4 hal qilindi va bajarildi) |

**Eng muhim uchta topilma:**

1. **Reyting talablarining yarmi allaqachon bor.** Ball jadvali umuman yo'q —
   reyting har so'rovda oy bo'yicha hisoblanadi. "Har oy yangi natija" —
   bugundan ishlayapti va integratsion test bilan qulflangan. "Streak" ham
   bor va bosh sahifada ko'rinib turibdi.

2. **Vazifa/testlarni modul ichiga ko'chirish MIGRATSIYA TALAB QILMAYDI.**
   `Assignment.ModuleLessonId` va `Test.ModuleLessonId` allaqachon mavjud,
   indekslangan va student DTO'larida uzatilyapti. Bu ~85% frontend ishi.

3. **Bildirishnoma infratuzilmasi qurilgan va testlangan, lekin BITTA ham
   biznes hodisasi uni ishga tushirmaydi.** Outbox, worker, retry, rate-limit,
   idempotentlik — hammasi tayyor. Yagona chaqiruv — Telegram botining javobi.

---

## 0b. HOLAT — 2026-08-14 sessiyasi yakuni

> **Tekshirilgan:** toza `--no-incremental` build · **1316 test, 0 yiqilgan**
> (689 unit + 627 integratsion) · frontend `typecheck` va `lint` toza.
> Boshlang'ich baza `HEAD` da 1214 test edi — ya'ni **+102 yangi test**.

### ✅ BAJARILDI (28 / 40)

| Guruh | Talablar |
|---|---|
| Loyiha egasining 4 qarori | R26 telefon orqali kirish · R2 avtomatik yozuv · R39 chat retention · R11 o'quv markaz reytingi |
| Jonli dars | R1 qo'l ko'tarish · R3 orqaga tugmasi |
| O'quvchi | R6 modul ichida vazifa/test + video · R9 progress · R10 `BaseSheet` · R12 arxiv · R14 streak |
| Chat | R15 ichki tablar · R16a emoji · R17 yozish paneli · R28 ustoz pariteti |
| Ustoz | R20 guruhlar jadvali · R21a filtr · R22 qidiruv · R23 davomat · R25 kalendar · R31 darslar jadvali · R32 vazifa yaratish |
| Maxfiylik | R27 kontakt · R8 suv belgisi |
| Ko'ndalang | R4 tasdiqlash · R19 brend rangi |
| Allaqachon mavjud | R7 · R13 · R18 · R34a |

### ❌ QOLDI (12 / 40) — hammasi EF MIGRATSIYA talab qiladi

Shu sababli ular ketma-ket bajarilishi kerak: hammasi AYNI
`ApplicationDbContextModelSnapshot.cs` faylini o'zgartiradi.

| Talab | Nima kerak |
|---|---|
| R24 | `LessonGrade` entity — `Attendance` ning aynan nusxasi |
| R5 + R29 + R30 | Yozuv ko'rinishi + sifat nazorati + dars tahlili — **BITTA migratsiyada** |
| R21b + R38 | Guruh kategoriyasi (lookup jadval) + chat filtri |
| R35 + R36 | Bildirishnoma (outbox tayyor, trigger/hub/UI yo'q) |
| R33 + R40 | Dinamik tekshiruvchi + savollar — **BITTA dizayn** |
| R16b + R37 | Chat va vazifada fayl biriktirish |

### 🔴 LOYIHA EGASI HAL QILISHI KERAK

| # | Savol | Nega muhim |
|---|---|---|
| 1 | ✅ **HAL QILINDI (2026-08-14) — video talabi YOQILDI** | Egasining qarori: *"yangi serverga qo'yamiz … noldan ishlatiladi"*, ya'ni qulflanib qoladigan o'quvchi YO'Q. `VideoContentModelled` doimiysi olib tashlandi; "videosi bor" fakti endi `LessonAssets` (`Kind = Video`) dan `EXISTS` bilan keladi. So'rovlar soni O'ZGARMADI (N+1 yo'q), kesh va uni bekor qilish hodisalari TEGILMADI. Batafsil sabab — `GatingService.LessonFactsQuery` izohida |
| 2 | Arxivlangan guruh reytingi | Bitirgan o'quvchi o'z tarixini KO'RA OLMAYDI: `PrimaryGroupAsync` `IsActive` bo'yicha filtrlaydi, ya'ni `groupId: null` qaytadi va 403 ga ham yetmaydi. ⚠️ 2026-08-14 da qo'shilgan `GET /progress/lesson-grades` ham AYNI cheklovga bo'ysunadi (davomat bilan bir xil qoida — ular birga hal qilinishi kerak) |
| 3 | `SharesGroupAsync` muddati | Ustoz O'ZI O'QITGAN har bir o'quvchining profilini MANGU ko'radi. Tavsiya: TEGILMASIN — izohlar CRUD'i ayni shu darvozadan o'tadi, cheklov qo'yilsa ustoz O'ZI yozgan izohni tahrirlay olmay qoladi |
| 4 | ✅ **HAL QILINDI (2026-08-14) — `livekit.url` paneldan boshqariladi** | Yagona to'siq `LiveKitHealthCheck` ning `IConfiguration` dan to'g'ridan-to'g'ri o'qishi edi; endi u ham `IRuntimeOptions<LiveKitOptions>` ni o'qiydi, ya'ni probe va token BITTA kesimning BITTA maydonidan oziqlanadi. `livekit.public_url` ATAYLAB muhitda qoldi — u sertifikat/DNS bilan juftlashgan |
| 5 | ✅ **HAL QILINDI (2026-08-14) — `LessonsTab` legendasi** | `assistant` KO'K (`sky` — dizayn tizimida "kurator" rangi) bo'ldi va legendaga ALOHIDA qator qo'shildi. Endi to'rt ohangning to'rttasi ham legendada va rang ↔ ma'no birma-bir |

**Yopilmagan yarim ish yopildi (2026-08-14):** R24 da o'quvchi o'z dars
bahosini KO'RA OLMASDI (faqat reytingdagi yig'ma `lessonPercent`). Qo'shildi:
`GET /api/v1/progress/lesson-grades` (o'z-o'ziga qamrovli — `studentId`
tokendan) + reyting varaqasidagi ro'yxat (faqat `row.isMe` da).

### ⚠️ ISHLATISHDAN OLDIN SHART

1. **Chat retention O'CHIQ holda keladi** (`chat.retention_enabled = false`) —
   ataylab: yoqiq holda deploy qilinsa birinchi tikda 3 oydan eski hamma
   yozishma o'chib ketardi. Tayyor bo'lganda paneldan yoqing.
2. **Telefon orqali kirishga o'tish TARTIBI** (`§1 Q1` dagi ro'yxat):
   `?phoneMissing=true` va `?telegramLinked=false` bo'yicha HAR BIR xodim
   rolini tekshiring va IKKALA ro'yxat BO'SHAGUNCHA deploy qilmang. Aks holda
   — qaytarib bo'lmaydigan lockout, faqat `psql` bilan tiklanadi.
3. **`.env` dagi `TELEGRAM_BOT_TOKEN` / `TELEGRAM_WEBHOOK_SECRET`** hozir
   DEV uchun soxta qiymat — prod'da HAQIQIY bo'lishi shart.
4. 🔴 **BRAUZERDA HECH NARSA KO'RILMAGAN.** Barcha "yashil" — kompilyator,
   linter va testlar. Joylashuv, o'lcham, ko'rinish TEKSHIRILMAGAN.

---

## 1. ⛔ QARORLAR — kod yozilmasdan oldin

Bu to'rttasidan **Q3 va Q4 hal qilindi va bajarildi** (quyida); qolgan ikkitasi hal qilinmaguncha tegishli ishlar BOSHLANMAYDI.

### Q1. ✅ HAL QILINDI — email login olib tashlandi (2026-08-13)

> **QAROR:** loyiha egasi — *"email butunlay o'chirilsin tasdiqlayman,
> faqat telefon orqali bo'lsin"*. **A variant + o'lik halqa yopilgan.**
>
> **BAJARILDI.** Quyidagi tahlil TARIXIY yozuv sifatida qoldirilgan —
> u nima uchun bu qaror xavfli bo'lganini va har bir xavf QANDAY
> yopilganini ko'rsatadi.
>
> | Qadam | Holat |
> |---|---|
> | `POST /auth/login` va parol yo'li | ✅ olib tashlandi |
> | `POST /auth/phone/request-code` + `/verify` | ✅ qurildi |
> | Xodimni Telegram'ga bog'lash (X-1 gate'lari) | ✅ ochildi, izohlar qayta yozildi |
> | 🔴 O'lik halqa (bot tokeni) | ✅ `Telegram__BotTokenOverride` |
> | `DbInitializer` telefonsiz admin | ✅ `Bootstrap__AdminPhone` (majburiy) |
> | Xodim telefoni majburiyligi | ✅ API + CSV import |
> | Cutover hisoboti | ✅ `?phoneMissing=true` + `?telegramLinked=false` |
> | Testlar | ✅ 1284 yashil |
>
> **Batafsil operator tartibi:** `docs/DEPLOY_UBUNTU.md` 7.1.1.

<details>
<summary>Qaror qabul qilingunicha bo'lgan tahlil (tarixiy)</summary>


**Bugungi holat:** 5 ta roldan FAQAT BITTASI (o'quvchi) parolsiz kira oladi.
Telegram kirishi ikki mustaqil qatlamda o'quvchi bilan cheklangan
(`AuthService.cs:93`, `TelegramUpdateHandler.cs:297`) — sabab audit X-1:
eski tizimda Telegram orqali ISTALGAN rol olinardi va admin akkaunti
egallanardi.

**🔴 O'LIK HALQA (eng muhim xavf):** bot tokeni va webhook siri BAZADA,
runtime sozlama sifatida saqlanadi va ularni faqat `Admin` roli
o'zgartira oladi (`SettingsController.cs:39`). Demak:

1. Bot tokeni almashsa/xato bo'lsa → hamma Mini App kirishi 503
2. Admin kira olmaydi → tokenni tuzata olmaydi
3. Tokenni tuzatadigan yagona UI o'sha kirish ortida

Bu — faqat `psql` bilan tiklanadigan to'liq ishdan chiqish. Bugun bu holat
xavfsiz, chunki `/auth/login` bor va 503 ekrani foydalanuvchini AYNAN o'sha
yerga yo'naltiradi.

**Yana ikkita mina:**
- `DbInitializer` admin'ni **telefonsiz va Telegram'siz** yaratadi → yangi
  o'rnatishda administrator UMUMAN bo'lmaydi.
- Xodim telefoni hech qayerda majburiy emas, migratsiya esa dublikat
  raqamlilarga `PhoneNormalized = NULL` qoldirgan — ular CRM'da normal
  ko'rinadi, lekin hech qachon bog'lana olmaydi.
- ~25 ta integratsion test fayli `LoginAsAdminAsync` orqali kiradi.

**Variantlar:**

| # | Variant | Foyda | Xavf |
|---|---|---|---|
| **A** | Hamma uchun olib tashlash | Talab so'zma-so'z bajariladi; parol yo'q | X-1 himoyasi yo'q qilinadi; o'lik halqa; yangi deploy'da admin yo'q; xodimlar uchun desktop kirish yo'li QURILISHI kerak (Telegram Login Widget yo'q) |
| **B** | Faqat o'quvchi uchun olib tashlash | ~Bugungi holat; 3 qatorlik ish | So'zma-so'z talabni bajarmaydi |
| **C** | A + "break-glass" admin yo'li | Talab bajariladi, o'lik halqa yopiladi | Baribir parol qoladi; audit va alarm kerak |

**★ Tartib xavfi:** xodimlarni bog'lash AVVAL yoqilishi va TUGALLANGANI
tasdiqlanishi kerak, keyin login o'chiriladi. Teskarisi — qaytarib
bo'lmaydigan lockout.

</details>

🔴 **YUQORIDAGI "TARTIB XAVFI" HAMON KUCHDA.** Kod ikkala o'zgarishni
BIRGA olib keladi, ya'ni himoya kodda emas, DEPLOY TARTIBIDA:
xodimlar botga ulanmasdan turib yangi image'ni prod'ga chiqarish —
qaytarib bo'lmaydigan lockout. To'liq tartib: `docs/DEPLOY_UBUNTU.md` 7.1.1.

---

### Q2. ✅ HAL QILINDI — Avtomatik dars yozuvi (talab: *"recording should be automatic"*)

> **QAROR QABUL QILINDI (2026-08-13, loyiha egasi):** *"automatic recordingni
> ham tasdiqlayman, avtomatik yozilishi kerak, biz uni o'zimizni serverda emas
> cloudflareda saqlaymiz, shuning uchun bu muammo emas, cloudflareni ham
> ulanish joylarini admin panel orqali boshqaradigan qilib ket"*.
>
> **BAJARILDI.** Kanonik qaror yozuvi — `IRecordingService.cs` izohi (u eski
> qarorni bekor qiladi va uning to'rt dalilining har biriga javob beradi).
> Quyida faqat XULOSA.

**Eski qaror nima edi:** `IRecordingService.cs` — *"★★ QAROR: YOZUV QO'LDA
BOSHLANADI … AVTOMATIK EMAS"*. Asosiy dalil — **rozilik**: eski tizim hech
qanday indikatorsiz avtomatik yozardi, izohda ishtirokchilar *"ko'pincha
bolalar"* deb yozilgan; host tugmasining O'ZI indikator hisoblanardi.

**Nima qilindi:**

| Savol | Javob |
|---|---|
| Avtomatik yozuvga o'tiladimi? | **Ha.** Guruh kaliti — `Group.RecordEnabled` (migratsiyasiz, ilgari hech kim o'qimasdi) |
| Trigger qayerda? | `LiveSessionService.StartAsync` — darsning `Live` ga o'tadigan YAGONA joyi |
| Egress dars boshlash yo'lida chaqiriladimi? | **Yo'q.** Faqat `Requested` qatori yoziladi (AYNI tranzaksiyada), Egress'ga watchdog boradi. Eski qarorning 2-dalili ("yozuv nosozligi darsni to'xtatmasin") bekor QILINMADI — u yechimning asosiy cheklovi bo'ldi |
| Kechikish? | ≤ `RecordingWatchdogSettings.Interval`. Shu sababli u 60 s → **15 s** ga tushirildi |
| O'quvchi indikatori | **Qo'shildi va u qarorning SHARTI.** `GET /live-sessions/{id}/recording-status` (yagona rolsiz yozuv endpointi) + `RecordingIndicator.vue` yuqori panelda, HAMMAGA |
| Qo'lda tugma qoladimi? | **Ha, OVERRIDE sifatida.** To'xtatish — roziligning yagona chiqishi; bundan tashqari yozuvi o'chiq guruhning bitta darsi va sozlama tuzatilgandan keyingi holat |
| Watchdog yangi yozuv boshlaydimi? | **Yo'q, avvalgidek.** U guruhlarni skanerlamaydi va `RecordEnabled` ni umuman o'qimaydi — faqat MAVJUD navbat qatorini bajaradi |

**Saqlash hajmi — dalil TUSHDI.** Loyiha egasi Cloudflare'da saqlanishini va
hajm muammo emasligini aytdi. ⚠️ **Retention hamon YO'Q** va bu ongli qoldiq:
`MaxDuration = 4 soat` chegarasi saqlandi (unutilgan xona kunlab yozmasin),
lekin eski yozuvlarni tozalaydigan vazifa qurilmadi — hajm cheklov bo'lmagani
uchun u endi shoshilinch emas.

---

### Q3. ✅ HAL QILINDI — chat tarixini avtomatik o'chirish (talab: *"3 oy default"*)

> **Qaror (loyiha egasi, 2026-08-13):** *"chat tarixi rostan ham belgilangan
> muddatdan keyin o'chirilishi kerak, ya'ni masalan 3 oy belgilansa guruhdagi
> 3 oy oldingi yozishmalar doimiy o'chirilib borishi kerak, bu funksiya
> telegramda bor ya'ni automatic deletion funksiyasi"*
>
> Ya'ni: **QATTIQ o'chirish**, **BARCHA guruhlarda** (faqat arxivlanganlarda
> emas), **SURILIB boruvchi oyna** (bir martalik tozalash emas), admin
> paneldan sozlanadi, standart muddat — 3 oy.

**Bajarildi.** `ChatRetentionJob` (`Application/Jobs/ChatRetentionJob.cs`) +
ikkita sozlama + `IX_GroupChatMessages_SentAt` indeksi (migratsiya
`20260813163233_AddGroupChatMessageSentAtIndex`).

#### Bekor qilingan yozma qaror

`GroupChatMessage.cs` dagi *"tahrirlash va o'chirish yo'q"* qarori QAYTA
KO'RILDI va sinf izohining o'zida yangilandi — kod bilan hujjat zid bo'lib
qolmasin. Ajratish shunday:

| | Holat |
|---|---|
| Foydalanuvchi bitta xabarni tanlab o'chirishi | ❌ **HAMON YO'Q** (qaror kuchida) |
| Tahrirlash endpointi | ❌ **HAMON YO'Q** |
| Muddat bo'yicha avtomatik tozalash | ✅ **BOR** (yangi) |

*"Yozma iz"* argumenti TANLAB o'chirishga qarshi edi — *"savolimni
o'chirdim"* degan imkoniyatga. Muddatli tozalash esa tanlamaydi: u hammaga,
hamma guruhga va faqat VAQT bo'yicha tegishli, ya'ni nizoda tomonlardan biri
dalilni yo'qota olmaydi. Nizo amalda yaqin o'tmish ustida bo'ladi.

#### Qabul qilingan qarorlar

| Savol | Qaror | Sabab |
|---|---|---|
| (a) hamma guruhmi, (b) faqat nofaolmi | **(a)** | Egasi *"guruhdagi 3 oy oldingi yozishmalar"* dedi — guruh holati haqida gap yo'q |
| Qamrov | **Faqat `GroupChatMessages`** | `DirectMessages` (kurator ↔ o'quvchi) va `ChatMessages` (jonli dars) TEGILMAYDI — batafsili quyida |
| Standart holat | **O'CHIQ** (`chat.retention_enabled = false`) | 🔴 Yoqilgan holda yetkazilsa, yangilanish chiqqan kuni birinchi yurish 3 oydan eski BUTUN yozishmani o'chirardi — hech kim so'ramagan holda. Yoqish — paneldagi bitta bosish |
| Eng qisqa muddat | **1 oy** (registrda `Minimum`, vazifada `Math.Clamp`) | `0` kesimni joriy onga tenglashtirib, keyingi yurishda BUGUNGI savollarni ham o'chirardi |
| Tezlik | **`SentAt` ga indeks**, `Id` monotonligiga tayanish EMAS | Ikkinchi yo'l yozilmagan taxminga tayanadi va buzilganda tozalash JIMGINA to'xtaydi (sabab vazifa izohida, sinovda ham yuz bergan) |

#### Nima uchun shaxsiy yozishmalar tegilmaydi

`DirectMessages` — ko'pincha xodimning o'quvchi bilan ISHI haqidagi yagona iz
(*"ota-onasiga qo'ng'iroq qilindi"*, *"to'lov kelishuvi"*). Uni *"chat
tarixi"* degan umumiy so'z ostida o'chirish talabdan KENGROQ bo'lardi.
`ChatMessages` esa sessiyaga bog'langan va dars yozuvi bilan birga o'sha
darsning hujjati. Kerak bo'lsa ular uchun ALOHIDA kalit qo'shiladi —
teskarisi mumkin emas.

#### 🔴 Tiklash yo'li

Ilova orqali YO'Q (loyihada soft-delete infratuzilmasi umuman yo'q,
`IsDeleted` — 0 ta natija). Yagona manba — tungi `pg_dump`
(`infra/scripts/backup-db.sh`, 03:15, 14 kun). Tartib `ChatRetentionJob`
izohida yozilgan.

---

### Q4. ✅ HAL QILINDI — "Umumiy reyting" (talab: *"o'z guruhi bo'yicha, va umumiy"*)

**Egasining qarori (2026-08-13):** *"leaderboardda butun o'quv markaz
bo'yicha va guruh bo'yicha bo'lishi kerak. biz bu loyihani kengaytirib bir
nechta o'quv markazlar sotishimizni hisobga olganda umumiy rating faqat
o'quv markaz uchun amal qilishi kerak, ya'ni jami tizim foydalanuvchilari
uchun emas"*.

**Bekor qilingan qaror.** `ILeaderboardService.cs:8-21` va
`LeaderboardController.cs:14-15` da qamrov ATAYLAB guruh ichida deb
yozilgan edi. Ikki sabab: **maxfiylik** (o'quvchi begona odamlarning ism va
ballini ko'radi) va **adolat** (turli kurs, ustoz, sur'at solishtirilmaydi).
★ **Ikkala izoh ham qayta yozildi** — kod va hujjat bir-biriga zid bo'lib
qolmasin.

**Nima qilindi:**

| Savol | Javob |
|---|---|
| "Umumiy" nimani anglatadi? | **(a) butun o'quv MARKAZ.** Kurs yoki ustoz kesimi TANLANMADI — egasi aynan markazni so'radi |
| 🔴 Markaz tushunchasi kodda bormi? | **YO'Q.** Bugun bitta deployment = bitta markaz, ya'ni "markaz" va "tizimdagi hamma" AYNI to'plam |
| Unda kelajak qanday himoyalandi? | **Nomlangan CHOK:** `ILearningCenterScope` + `SingleCenterScope` (`Application/Common/Scope/`). "Ko'ruvchining markaziga qaysi o'quvchilar kiradi?" savoli KODDA BITTA joyda so'raladi. `LearningCenter` qo'shilganda o'zgarish shu faylga tushadi — reyting servisiga, kontrollerga, DTO'ga TEGILMAYDI |
| `LearningCenter` entity'si yaratildimi? | **Yo'q — ATAYLAB.** Bu alohida qaror va egasi uni hali qabul qilmagan |
| Maxfiylik e'tirozi qayerga ketdi? | **Qisman javob oldi:** markaz jadvali TO'LIQ yuborilmaydi — eng yaxshi **100 ta qator + so'rovchining O'Z qatori**. 3000 kishilik markazda o'quvchi 2999 ta begona ismni ko'rmaydi |
| Adolat e'tirozi qayerga ketdi? | **Ball MUTLAQ emas, FOIZ** (uch mezon o'rtachasi, har biri 0..100) — 20 dars o'tgan guruh va 6 dars o'tgan guruh o'quvchisi bir shkalada. Davomat maxraji esa har o'quvchida O'Z GURUHINIKI |
| 🔴 `MaxRows = 500` (409 xatosi) | **Ko'tarilmadi va markazga QO'LLANMAYDI.** U GURUH invarianti bo'lib qoladi (500 kishilik "guruh" — ma'lumotdagi xato). Markaz yo'li `LeaderboardRanking.RankAll` dan o'tadi va javob Top-100 gacha qisqartiriladi. Chegarani ko'tarish muammoni faqat SURARDI: 5000 qatorli JSON ~600 KB |
| Davomat mezoni qanday umumlashtirildi? | **Maxraj — har o'quvchining ASOSIY guruhi** (`GROUP BY group_id`, bitta so'rovda). Umumiy maxraj olinsa, 4 dars o'tgan guruh o'quvchisi 12 ta darsga bo'linib 33% chiqardi. ★ Guruh jadvallarini qo'shib yuborish TANLANMADI — u sovuq keshda O(guruhlar soni) fan-out berardi |
| Kurs darsi vazifalari (GroupId NULL) kiradimi? | **Ha.** Ular chiqarilsa markazda vazifa mezoni deyarli har doim bo'sh chiqardi va bitta o'quvchi bitta oyda guruh jadvalida bir ball, markaz jadvalida BOSHQA ball ko'rardi |
| Endpoint | `GET /api/v1/leaderboard/center` · `GET /api/v1/leaderboard/me?scope=Center` |
| Ruxsat (YANGI qoida) | Markazning **har qanday FAOL foydalanuvchisi**. ★ Chegara ROL emas, MARKAZ — egasining sharti aynan shu edi |
| Kesh | `leaderboard:center:{markaz}:{oy}` (guruhniki `leaderboard:g{id}:{oy}` — o'zgarmadi). 🔴 `ICacheService` da prefiks bo'yicha o'chirish YO'Q, shuning uchun markaz belgisi kalit ICHIDA |
| Murakkablik | Markaz jadvali — **6 ta agregat so'rov**, o'quvchi soniga ham, guruh soniga ham bog'liq EMAS |
| Frontend | `StudentRatingPage.vue` da "Guruhim / O'quv markaz" tanlagichi. Markaz so'rovi DANGASA — tab ochilmaguncha yuborilmaydi |

**⚠️ Qoldiq cheklov.** Markaz jadvali keshda TO'LIQ saqlanadi (o'quvchining
o'z o'rnini ko'rsatish uchun to'liq ro'yxat kerak), ya'ni Redis hajmi
o'quvchilar soniga chiziqli (~120 bayt × o'quvchi). Yuzlab o'quvchida bu
arzimas; **o'n minglab o'quvchida snapshot jadvali kerak bo'ladi** va
o'shanda `LeaderboardService` izohidagi "snapshot qo'shilmadi" qarori qayta
ko'rib chiqilishi kerak.

---

### Kichikroq qarorlar (ishni to'smaydi, lekin aniqlik kerak)

| # | Savol | Variantlar |
|---|---|---|
| q5 | Guruh "kategoriyasi" (ATF, CEFR, IELTS) | `Course` allaqachon shuni modellaydi (izohida misol — "ATF"). Yangi maydonmi yoki `Course`mi? Yangi bo'lsa: enum yoki admin tahrirlaydigan jadval? |
| q6 | "Biriktirilgan kurator" ustuni | Backendda IKKITA kurator tushunchasi bor: `AssistantName` (odam) va `CuratorGroupName` (guruh). Qaysi biri? |
| q7 | Video suv belgisi | DOM qatlami (arzon, devtools'da o'chiriladi) yoki serverda kuydirilgan (ishonchli, hajm ×o'quvchi soni ≈ 80 GB → 1.6 TB/oy) |
| q8 | Telefoni yo'q o'quvchida suv belgisi nima ko'rsatadi? | Ism? ID? `"Ism Familiya · 4821"`? |
| q9 | Kontakt maxfiyligi kuratorga ham tegishlimi? | Kod ustoz va kuratorni AYNAN bir xil ko'radi. ★ Kuratorning "qo'ng'iroq" tugmasi kodda uning ASOSIY amali deb yozilgan |
| q10 | "Teacher vazifa yaratmasin" | (a) qat'iy: ustoz umuman yarata olmaydi; (b) yumshoq: faqat KURS vazifalari o'quv bo'limida — bu allaqachon shunday |
| q11 | Dars bahosi oylik reytingga kiradimi? | Hal qilinmasa — reyting yangi baholarni jimgina e'tiborsiz qoldiradi |
| q12 | Yozuv ko'rinishi: default qiymat | `true` = bugungi xulq saqlanadi; `false` = mavjud yozuvlar o'quvchidan YO'QOLADI |
| q13 | Yozuv ko'rinishi: kim ustun? | O'quv bo'limi yashirsa, ustoz ko'rsatsa — kim g'olib? |

---

## 2. TALABLAR JADVALI

Belgilar: ✅ tayyor · 🟢 frontend · 🟡 backend (migratsiyasiz/kichik) · 🔴 katta · ⛔ qaror kutmoqda

### O'quvchi paneli va jonli dars

| # | Talab | Bugungi holat | Baho | To'lqin |
|---|---|---|---|---|
| R1 | Ustozda "qo'l ko'tarish" bo'lmasin | Rol tekshiruvi UMUMAN yo'q. Ekran ulashish allaqachon host bilan cheklangan — nusxa oladigan naqsh | 🟢 S | 1 |
| R2 | Yozuv avtomatik | **BAJARILDI** (Q2). `Group.RecordEnabled` → `LiveSessionService.StartAsync` → navbat → watchdog. O'quvchi indikatori bilan. Retention hamon yo'q (hajm cheklov emas) | ✅ | 1 |
| R3 | Jonli darsda "orqaga" tugmasi kerakmas | Qizil "chiqish" tugmasi bilan AYNAN bir xil handler — dublikat | 🟢 S | 1 |
| R4 | Barcha edit/delete tasdiqlansin | `useConfirm` allaqachon bor va ishlatiladi — qamrov auditi kerak | 🟢 S–M | 1 |
| R5 | Dars yozuvlari ko'rinishi dinamik | `SessionRecording` da ko'rinish maydoni UMUMAN yo'q | 🔴 M | 3 |
| R6 | Vazifa/test/dars modul ichida | `ModuleLessonId` FK ikkalasida ham BOR | 🟡 L (FE) | 2 |
| R7 | Bir necha video → qismli | `LessonAsset` + `Position` + `Title` — to'liq tayyor | ✅ | 2 |
| R8 | 🔴 Video ustida telefon raqami | Video UMUMAN o'ynatilmaydi (auth yo'q); telefon `/auth/me` da yo'q | ⛔ q7,q8 | 3 |
| R9 | Progress ko'rsatilsin | Backend `completed` yuboradi, FE tipida YO'Q va 3 ta izoh "yo'q" deydi | 🟢 S | 1 |
| R10 | Reyting: yarim ekran varaqa | `BaseModal` `sheet` bermayapti → to'liq ekran. Yangi `BaseSheet` kerak | 🟢 S | 1 |
| R11 | Reyting: guruh + umumiy | **BAJARILDI** (Q4). Qaror bekor qilindi; "umumiy" = bitta O'QUV MARKAZ, qamrov `ILearningCenterScope` chokida | ✅ | — |
| R12 | Natijalar arxivi | `?period=` IKKALA endpointda ISHLAYDI; FE `undefined` yuboradi | 🟡 M | 2 |
| R13 | Har oy yangi natija | **Allaqachon shunday** — test bilan qulflangan | ✅ | — |
| R14 | Streak kunlari | **Bor** — `AttendanceMath.Streak`, bosh sahifada ko'rinadi (dars streak'i) | ✅ / 🟢 S | 1 |
| R15 | Chat: ichki kanal bo'linishi kerakmas | Faqat FE, ~40 qator. `update:channel` — o'lik kod | 🟢 S | 1 |
| R16 | Chat: emoji / rasm / fayl | Emoji — faqat FE. Fayl — yangi entity + migratsiya + domen invarianti | 🟢 S / 🔴 L | 1 / 4 |
| R17 | Yozish paneli joyida qotsin | Ustoz sahifalari `height-class` bermaydi → qat'iy balandlik. O'quvchi sahifasi tuzatilgan | 🟢 S–M | 1 |
| R18 | Chat guruh rejimida | **Allaqachon shunday** | ✅ | — |
| R19 | Brend nomi bir xil rangda | Audit kerak | 🟢 S | 1 |

### Ustoz paneli

| # | Talab | Bugungi holat | Baho | To'lqin |
|---|---|---|---|---|
| R20 | Guruhlar jadval ko'rinishida | 7 ustundan **6 tasi API'da BOR** | 🟢 S | 1 |
| R21 | Filtr: status / tur / kategoriya | `Type` (Guruh/Individual) TO'LIQ bor. `IsActive` server tomonda bor. Kategoriya YO'Q | 🟢 S + 🔴 M | 1 / 3 |
| R22 | Qidiruv barcha parametrlar bo'yicha | Server — faqat nom. Ustoz sahifasi — mijozda, faqat birinchi 50 guruh | 🟡 M | 2 |
| R23 | Davomat jadvali professional | Muzlatilgan ustun, legenda, CSV — BOR. Yetishmaydi: sticky sarlavha, jami qatori | 🟢 S | 1 |
| R24 | 🔴 Baho har DARSGA qo'yiladi | Baho = `Submission.Score`, doim VAZIFAGA bog'langan. Darsga bog'lanish YO'Q | 🔴 L | 4 |
| R25 | Kalendar katagi darsni aniqroq ko'rsatsin | Barcha ma'lumot mijozda bor | 🟢 S | 1 |
| R26 | Email login olib tashlansin | **BAJARILDI** — telefon + Telegram kodi | ✅ | — |
| R27 | Student kontakti ustozga ko'rinmasin | AYNAN 3 ta projeksiya, 2 tasi ustozga ochiq. Naqsh bor (`StudentAudience`) | 🟡 M | 2 |
| R28 | Ustoz chati = o'quvchi chati qoidalari | **12 ta aniq farq** topildi, hammasi frontend | 🟡 M | 2 |
| R29 | Yozuvlarda sifat nazorati xulosasi | Hech narsa yo'q. ★ Eski ilovada BOR EDI va ataylab tashlangan | 🔴 M | 3 |
| R30 | "Darslarim"da dars tahlili tugmasi | Ikki o'qilish: R29 bilan bir xil ma'lumotmi yoki statistikami? | 🔴 S/M | 3 |
| R31 | "Darslarim" jadvalida student/qatnashgan/davomiylik | **Uchtasidan 0 tasi** API'da bor. Agregat endpoint kerak | 🟡 M | 2 |
| R32 | Vazifani faqat o'quv bo'limi yaratsin | Ustoz hozir O'Z guruhiga vazifa yarata oladi | ⛔ q10 | 2 |
| R33 | Tekshiruvchi dinamik tayinlansin | Ustoz va kurator kodda AYNAN bir xil ko'riladi | 🔴 M | 4 |
| R34 | Kelgan vazifalar sana bo'yicha | Server tomonda tartiblangan. Yagona kamchilik — vazifalararo navbat yo'q | 🟢 S / 🟡 M | 1 / 3 |
| R35 | Notification | Outbox QURILGAN va TESTLANGAN, lekin 0 ta biznes hodisasi | 🔴 L | 3 |
| R36 | Baho qo'yilganda avtomatik yangilansin | `['assignments','mine']` faqat o'quvchining o'z amalidan keyin yangilanadi | 🟡 M | 3 |
| R37 | Vazifada fayl/rasm ikki tomonlama + katta ko'rish | O'quvchi yuklashi TO'LIQ ishlaydi. Ustoz biriktira olmaydi. Lightbox IKKI joyda bor | 🟡 M | 3 |
| R38 | Chatlarga filtr (tur/kategoriya) | `/threads` da parametr umuman yo'q. ★ 200 ta chegara — mijoz tomonda filtr ma'lumot yashirardi | 🟡 M | 3 |
| R39 | Chat tarixi retention | **BAJARILDI** (Q3). `ChatRetentionJob` + `chat.retention_enabled` / `chat.retention_months` (standart: O'CHIQ, 3 oy) + `IX_GroupChatMessages_SentAt`. 🔴 Qattiq o'chirish, tiklab bo'lmaydi | ✅ | 3 |
| R40 | Savollar: dars savollari, navbat, dinamik ruxsat | ★ `DirectMessage.ModuleLessonId` TO'LIQ qurilgan va 100% UXLAYAPTI | 🔴 M–L | 4 |

---

## 3. TO'LQINLAR

### 1-to'lqin — qarorsiz, migratsiyasiz (BUGUN KECHASI)

Hech biri qaror kutmaydi, hech biri migratsiya talab qilmaydi, hech biri
boshqasini to'smaydi. Parallel agentlarga bo'linadi.

| Guruh | Ishlar |
|---|---|
| **A. Jonli dars** | R1 (qo'l ko'tarish rol gate) · R3 (orqaga tugmasi) |
| **B. O'quvchi** | R9 (progress `completed` + `BeforeGroupStart` xatosi) · R10 (`BaseSheet`) · R14 (streak reytingda) |
| **C. Chat FE** | R15 (ichki tablar) · R17 (yozish paneli) |
| **D. Ustoz jadvallari** | R20 (guruhlar jadvali) · R21a (status/tur filtri) · R23 (davomat sticky+jami) · R25 (kalendar katagi) |
| **E. Ko'ndalang** | R4 (tasdiqlash auditi) · R19 (brend rangi) · R16a (emoji) · R34a (tartib) |

### 2-to'lqin — backend, migratsiyasiz yoki kichik

R6 (modul ichi — eng katta FE ishi) · R12 (arxiv picker) · R22 (qidiruv) ·
R27 (kontakt maxfiyligi) · R28 (ustoz chati pariteti) · R31 (darslar
jadvali agregati) · R32 (q10 dan keyin)

### 3-to'lqin — yangi entity + migratsiya

R5 + R29 + R30 (**bitta migratsiyada** — ikkalasi ham `SessionRecording`/
`RecordingDto` ga tegadi) · R35 + R36 (bildirishnoma: outbox trigger + hub +
in-app) · R37 (ustoz fayllari) · R38 (chat filtri, q5 dan keyin) · R21b
(kategoriya) · R8 (suv belgisi, q7/q8 dan keyin)

### 4-to'lqin — eng katta

R24 (`LessonGrade` entity — `Attendance` ning aynan nusxasi) · R33 + R40
(**bitta dizayn** — pastga qarang) · R16b (chat fayllari)

---

## 4. KO'NDALANG TOPILMALAR

1. **R33 va R40 — bitta qaror ikki marta.** Ikkalasi ham "bu guruh/dars uchun
   ustozmi yoki kuratormi, o'quv bo'limi tanlaydi" degan savol. Bugun ikkala
   ruxsat ham AYNI `Group.TeacherId` / `AssistantId` / `CuratorGroupId`
   uchligidan kelib chiqadi, faqat turli kod yo'llari orqali. Joylashuvni
   BIR MARTA hal qilish kerak.

2. **🔴 R40 da chuqur struktura ziddiyati.** Suhbat kaliti `(StudentId,
   StaffId)` — ya'ni bitta o'quvchida FAQAT BITTA xodim suhbatdoshi bo'ladi.
   Agar 12-dars savoli ustozga, 13-dars savoli kuratorga ketsa — bitta
   o'quvchiga IKKITA suhbat kerak bo'ladi va bu bugungi kalitni buzadi.

3. **R5 + R29 bitta migratsiyada bo'lsin** — aks holda bitta jadval uchun
   ikkita migratsiya va ikkita TS tip o'zgarishi bo'ladi.

4. **🔴 `LoggingMessageSender` — sukut bo'yicha jo'natuvchi.** Telegram
   sozlanmagan bo'lsa bildirishnomalar JIMGINA logga yoziladi va hech kimga
   yetmaydi. R35 dan oldin muhitda `TelegramOptions.IsConfigured` tekshirilsin.

5. **`Clients.User(...)` kod bazasida BIR MARTA ham ishlatilmagan.** Barcha
   hub'lar `Clients.Group`. Foydalanuvchi darajasidagi hub — yangi ish
   (lekin auth allaqachon `/hubs` uchun umumiy).

6. **Yo'l-yo'lakay topilgan XATOLAR** (talabda yo'q, lekin tuzatilishi kerak):
   - Progress `BeforeGroupStart` darslarni maxrajdan chiqarmaydi → kursga
     kech qo'shilgan guruhda progress MANGU qotib qoladi
   - FE `CourseLessonDto` tipida `completed` yo'q, 3 ta izoh "server bermaydi"
     deb yozilgan — server Wave 2 dan beri BERADI
   - `LessonsTab` legendasi nuqta chizadi, kataklar esa matnli pill —
     mos kelmaydi
   - `DirectMessages.ModuleLessonId` da indeks YO'Q

---

## 5. TEKSHIRUV MEZONI

Har bir to'lqin uchun:

- [ ] `npm run typecheck` + `eslint --max-warnings 0` toza
- [ ] `dotnet test` — 1055 test (621 unit + 434 integratsion) yashil
- [ ] Migratsiya bo'lsa: `docs/MIGRATIONS.md` bo'yicha, snapshot yangilangan
- [ ] Brauzerda 320 / 390 / 768 / 1024 / 1600 px
- [ ] Parite chekinishi bo'lsa — `docs/MOSLASHUVCHANLIK.md` 4-bo'limiga yozilsin
