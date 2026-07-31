# Ma'lumot ko'chirish — eski Zin-Nur (Python/FastAPI) → v2 (.NET)

Bu hujjat `tools/migration` dagi **`zinnur-migrate`** vositasini ishlatish
tartibini belgilaydi: tayyorgarlik, zaxira, yurgizish, tekshirish va
**orqaga qaytarish**.

> 🔴 **ENG MUHIM BO'LIM — [6. Yo'qoladigan va taxmin qilinadigan ma'lumot](#6-yoqoladigan-va-taxmin-qilinadigan-malumot).**
> Ko'chirishga ruxsat berishdan oldin loyiha egasi o'sha ro'yxatni
> o'qib chiqishi va har bandini **ataylab qabul qilishi** shart. Vosita
> texnik jihatdan to'g'ri ishlagan holda ham u yerdagi ma'lumot v2 ga
> **o'tmaydi** — bu vosita xatosi emas, sxema farqi.

---

## 0. Vosita nima qiladi va nima QILMAYDI

| | |
|---|---|
| **Manbaga yozadimi?** | **YO'Q.** Ulanish ochilgach darhol `SET default_transaction_read_only = on` qo'yiladi — vosita ichidagi istalgan xato bazaning O'ZI darajasida to'siladi. |
| **Migratsiya yaratadimi?** | **YO'Q.** v2 sxemasi allaqachon tayyor; vosita faqat mavjud jadvallarga yozadi va sxema mos kelishini `SchemaGuard` bilan tekshiradi. |
| **Qayta yurgizsa bo'ladimi?** | **HA.** Har `INSERT` — `ON CONFLICT DO NOTHING`. Uzilgan ko'chirish BOSHIDAN qayta ishga tushiriladi. |
| **Eski `id` lar saqlanadimi?** | **HA** (sabablari 3-bo'limda). |
| **Chiqish kodlari** | `0` — hisobot toza; `1` — mos kelmovchilik topildi; `2` — vosita ishga tusha olmadi. |

### Buyruq

```bash
dotnet run --project tools/migration/src/Zinnur.Migration/Zinnur.Migration.csproj -- \
  --source="Host=...;Database=zinnur_legacy;Username=...;Password=..." \
  --target="Host=...;Database=zinnur;Username=...;Password=..." \
  --only=all
```

| Argument | Ma'nosi |
|---|---|
| `--only=preflight` | Faqat tekshiradi, **hech narsa yozmaydi**. Ish kunida yurgizish uchun. |
| `--only=all` | Tayyorgarlik + ko'chirish + tekshirish. |
| `--allow-nonempty-target` | Maqsad bazada qator bo'lsa ham davom etadi (**faqat uzilgan ko'chirishni davom ettirish uchun**). |
| `--allow-orphan-modules` | Kursga bog'lanmagan modullar bo'lsa ham davom etadi (ular va butun daraxti KO'CHMAYDI). |
| `--batch=N` | Bitta `INSERT` dagi qatorlar soni (standart 1000). |

Ulanish satrlari **faqat oshkor** beriladi (argument yoki `ZINNUR_LEGACY_DB` /
`ZINNUR_V2_DB` muhit o'zgaruvchisi). Vosita hech qanday `.env` faylini
o'qimaydi va **standart ulanish satri yo'q** — aks holda kimdir uni
bexosdan ishlab turgan bazaga qaratib yuborishi mumkin edi.

---

## 1. Tayyorgarlik (ko'chirishdan **1–2 hafta oldin**, ish kunida)

Tayyorgarlik bosqichi **faqat o'qiydi**, shuning uchun uni ishlab turgan
tizimda, ish vaqtida bemalol yurgizish mumkin. Maqsad — to'siqni
ko'chirishning o'rtasida emas, **oldindan** topish.

```bash
zinnur-migrate --source=<eski> --target=<v2> --only=preflight
```

Vosita ettita narsani tekshiradi:

1. **Kerakli jadvallar bormi** (24 ta).
2. **Vaqt ustunlari `timestamptz` mi.** Agar naive `timestamp` bo'lsa,
   barcha dars vaqtlari 5 soatga siljishi mumkin — bu faraz emas,
   bazaning o'zidan tekshiriladi.
3. **Telefon dublikatlari** — v2 da `PhoneNormalized` bo'yicha filtrlangan
   unikal indeks bor.
4. **Elektron pochta dublikatlari** (kichik harfga o'tgandan keyin).
5. **Kvitansiya raqami dublikatlari.**
6. **Kursga bog'lanmagan modullar.**
7. **Erkin satrli ustunlardagi tanilmagan qiymatlar.**

### Nimani BARTARAF ETISH SHART (aks holda ko'chirish to'xtaydi)

| Holat | Nima uchun to'xtatadi | Yechim |
|---|---|---|
| **Pochta dublikati** | v2 da pochta kichik harfda va UNIKAL. Faqat eng kichik `id` ko'chadi, qolganlari **butun daraxti bilan** (guruh a'zoligi, davomat, to'lovlar) tushib qoladi. | Eski bazada pochtani qo'lda tuzating. |
| **Kursga bog'lanmagan modul** | v2 da `Modules.CourseId` MAJBURIY. Modul bilan birga uning darslari, vazifalari, testlari, progressi ham ko'chmaydi. | Eski bazada modullarga kurs biriktiring. |
| **Vaqt ustuni `timestamptz` emas** | Barcha dars vaqtlari 5 soat siljishi mumkin. | Qaysi mintaqada yozilganini aniqlang. |

### Nimani KO'RIB CHIQISH kerak (to'xtatmaydi, lekin hisobotga chiqadi)

- **Telefon dublikatlari.** Eng kichik `id` normallashtirilgan raqamni
  oladi; qolganlarida `PhoneNormalized` `NULL` bo'ladi, lekin **`Phone`
  ko'rinishda saqlanadi** (xodim uni panelda ko'radi va qidiruvdan
  tashqari qolmaydi). Hech kim yo'qolmaydi.
- **Tanilmagan qiymatlar** (`chat_messages.channel`, `payments.method`
  va h.k.). Yechim: eski bazada qiymatni tuzatish **yoki**
  `Mapping/LegacyMap.cs` ga bitta qator qo'shish.

---

## 2. Zaxira — ko'chirish kechasi, birinchi qadam

> Zaxirasiz ko'chirish **boshlanmaydi**. Orqaga qaytarish rejasi (5-bo'lim)
> to'liq shu zaxiraga tayanadi.

```bash
# 1. Eski baza — TO'LIQ mantiqiy zaxira
pg_dump --format=custom --no-owner --no-privileges \
        --file=zinnur_legacy_$(date +%F_%H%M).dump "<eski ulanish satri>"

# 2. v2 baza — ko'chirishdan OLDINGI holat (odatda bo'sh, lekin baribir)
pg_dump --format=custom --no-owner --no-privileges \
        --file=zinnur_v2_before_$(date +%F_%H%M).dump "<v2 ulanish satri>"

# 3. Zaxira HAQIQATAN o'qilishini tekshiring (fayl hajmi YETARLI DALIL EMAS)
pg_restore --list zinnur_legacy_*.dump | wc -l
```

Zaxirani **boshqa mashinaga** nusxalang. Bir xil diskda turgan zaxira —
zaxira emas.

---

## 3. Ko'chirish (to'xtash oynasida)

### Tartib

1. **Eski tizimni yoping** (foydalanuvchilar uchun). Ochiq qolsa
   ko'chirish paytida yozilgan qatorlar v2 ga **tushmaydi**.
2. Zaxira oling (2-bo'lim).
3. v2 bazasi **bo'sh** ekaniga ishonch hosil qiling. Vosita buni o'zi
   tekshiradi va bo'sh bo'lmasa **to'xtaydi** — bu ishlab turgan bazaga
   bexosdan yozib yuborishdan himoya.
4. Vositani yurgizing:

   ```bash
   zinnur-migrate --source=<eski> --target=<v2> --only=all | tee kochirish_$(date +%F_%H%M).log
   ```

5. **Chiqish kodini tekshiring.** `0` bo'lmasa — 5-bo'lim (orqaga qaytarish).

### Nima uchun eski `id` lar saqlanadi

1. **Tashqi havolalar ishlashda davom etadi** — R2 obyekt kalitlari,
   Telegram chuqur havolalari, chop etilgan kvitansiyalar.
2. **Idempotentlik bepul bo'ladi**: "bu qator ko'chganmi?" degan savolga
   birlamchi kalitning o'zi javob beradi (`ON CONFLICT DO NOTHING`) —
   alohida xarita jadvali kerak emas.
3. **Tekshiruv oddiylashadi**: manba va maqsaddagi `id` to'plamlari aynan
   solishtiriladi.

**Narxi:** identity ketma-ketliklari to'g'rilanishi SHART. Buni vosita
ko'chirish oxirida avtomatik bajaradi (`IdentitySequences`) va tekshiruv
bosqichida qayta tasdiqlaydi — busiz birinchi yangi foydalanuvchi
ro'yxatdan o'tolmasdi.

---

## 4. Tekshirish

Vosita oxirida beshta **mustaqil** tekshiruv bajaradi. Ularning birortasi
ham "vosita nima deb o'ylayapti" ga tayanmaydi — hammasi **bazaning
o'zidan** o'qiladi.

| # | Tekshiruv | Nimani ushlaydi |
|---|---|---|
| 1 | **Sanoq** — `manba = ko'chgan + o'tkazilgan`, `maqsad = ko'chgan` | Jimgina tushib qolgan qatorlar |
| 2 | **Pul** — `manba = ko'chgan + ko'chmagan/tuzatilgan`, `maqsad = ko'chgan` | Yo'qolgan yoki ikki marta sanalgan so'm |
| 3 | **Hafta kunlari** — haqiqiy dars SANALARI guruh jadvaliga mos keladimi | Python(Dushanba=0) → .NET(Yakshanba=0) bir kunlik siljish |
| 4 | **Chat oqimlari** — har `(guruh, kanal)` juftligi ALOHIDA | Ustoz va kurator oqimlarining qo'shilib ketishi |
| 5 | **Identity** — ketma-ketliklar `MAX(Id)` dan oldindami | Birinchi yangi qator yozilmasligi |

Bittasi ham buzilsa vosita **xato kodi bilan** tugaydi va hisobotda
sababi ko'rsatiladi.

### Hisobot qanday o'qiladi

Hisobotda **ikki ro'yxat ataylab ajratilgan**:

- **O'TKAZIB YUBORILGAN QATORLAR** — qator maqsad bazaga **tushmadi**
  (haqiqiy yo'qotish);
- **TUZATILGAN QIYMATLAR** — qator tushdi, lekin qiymati **o'zgardi**
  (taxmin, kesish, tozalash).

Ular bir ro'yxatda bo'lsa "necha qator yo'qoldi" degan savolga javob
berib bo'lmasdi.

> **Pul jadvalidagi `ko'chmagan/tuzat.` ustuni manfiy ham bo'lishi
> mumkin.** U yo'qotish emas, manba va maqsad yig'indilari o'rtasidagi
> **ayirma**. Masalan eski tizim qaytarilgan pulni manfiy summa bilan
> yozgan, v2 esa musbat summa + `Refund` turi bilan yozadi.

### Qo'lda tekshirish ro'yxati (hisobotdan tashqari)

Ko'chirishdan keyin panelda **ko'z bilan** tasdiqlang:

- [ ] Bir necha guruhning **dars jadvali kunlari** to'g'rimi (dushanba
      guruhi dushanbada turibdimi).
- [ ] Bitta o'quvchining **ustoz chati** va **kurator chati** alohida
      ko'rinyaptimi.
- [ ] Bir necha o'quvchining **qarzi va balansi** eski tizimdagi bilan
      bir xilmi.
- [ ] Kurator guruhining o'quvchilari ko'rinyaptimi (bog'liq ustoz
      guruhlaridan kelishi kerak).
- [ ] **Yangi** foydalanuvchi ro'yxatdan o'ta oladimi (identity
      ketma-ketligi).

---

## 5. Orqaga qaytarish rejasi

> Ko'chirish **v2 bazasiga yozadi, eski bazaga UMUMAN tegmaydi**.
> Shuning uchun orqaga qaytarish oddiy: **v2 ni tashlab, eski tizimni
> qayta yoqish**.

### 5.1. Qaror mezoni

Orqaga qaytariladi, agar:

- vosita `1` yoki `2` kodi bilan tugagan **va** sabab to'xtash oynasi
  ichida bartaraf etilmasa;
- yoki 4-bo'limdagi qo'lda tekshirish ro'yxatidan bittasi ham
  o'tmasa.

### 5.2. Qadamlar

```bash
# 1. v2 ilovasini to'xtating (hech kim yozmasin)
docker compose stop api web

# 2. v2 bazasini ko'chirishdan OLDINGI holatga qaytaring
dropdb zinnur && createdb zinnur
pg_restore --dbname=zinnur --no-owner --no-privileges zinnur_v2_before_*.dump

# 3. Eski tizimni foydalanuvchilar uchun qayta oching
#    (eski baza TEGILMAGAN — vosita unga yozmagan)
```

**Eski bazani tiklash SHART EMAS**, chunki unga hech narsa yozilmagan.
Zaxira (2-bo'lim) faqat "hech qanday holatda ma'lumot yo'qolmasin"
prinsipi uchun olinadi.

### 5.3. Yarim ko'chgan holat

Vosita yarim yo'lda uzilsa **orqaga qaytarish shart emas**: uni
`--allow-nonempty-target` bilan **boshidan** qayta yurgizing. Allaqachon
yozilgan qatorlar `ON CONFLICT DO NOTHING` tufayli ikkinchi marta
yozilmaydi.

Bu **sintetik ma'lumotda tekshirilgan**: vosita ketma-ket ikki marta
yurgizilganda maqsad bazaning to'liq ma'lumot nusxasi (`pg_dump
--data-only`) **bayt-ma-bayt bir xil** bo'lib qoldi (722 `INSERT` →
722 `INSERT`, birorta yangi qator yaratilmadi).

---

## 6. Yo'qoladigan va taxmin qilinadigan ma'lumot

> 🔴 **Bu ro'yxat loyiha egasi uchun.** Quyidagi ma'lumot v2 ga
> **o'tmaydi** yoki **taxmin qilinadi**. Bu vosita xatosi emas — v2
> sxemasida mos maydon yo'q. Har bir band bo'yicha qaror kerak:
> *(a)* qabul qilish, *(b)* v2 ga maydon qo'shish, *(c)* eski bazani
> arxiv sifatida saqlab qo'yish.

### 6.1. Butunlay ko'chmaydigan jadvallar (18 ta)

| Eski jadval | Nima yo'qoladi |
|---|---|
| `absence_reasons` | Darsga kelmaslik sabablari |
| `app_settings` | Eski tizim sozlamalari (v2 da o'z `AppSettings` i bor) |
| `bot_pending_actions` | Telegram botining kutilayotgan amallari |
| `breakout_rooms`, `breakout_assignments` | Kichik guruh (breakout) xonalari va taqsimoti |
| `grades` | **Eski baholar jadvali** — tekshirilsin, `submissions.grade_value` bilan ustma-ust tushmasligi mumkin |
| `leaderboard_snapshots` | Reyting kesimlari (tarix) |
| `lesson_confirmations` | Darsni tasdiqlash yozuvlari |
| `lesson_leave_reasons` | Darsdan chiqish sabablari |
| `lesson_videos`, `video_sources` | **Kurs video havolalari va manbalari** |
| `message_outbox`, `notification_log` | Yuborilgan xabarlar navbati va jurnali |
| `message_templates` | Xabar shablonlari |
| `refresh_tokens` | **Barcha foydalanuvchilar qayta login qiladi** (ataylab: eski tokenlar v2 formatiga mos emas) |
| `student_free_lessons` | Bepul dars huquqlari |
| `student_notes` | **Xodimlarning o'quvchi haqidagi izohlari** |
| `telegram_user_settings` | Telegram til sozlamalari |

### 6.2. Ko'chadigan jadvallardagi yo'qoladigan ustunlar

| Eski jadval | Ko'chmaydigan ustunlar | Izoh |
|---|---|---|
| `users` | `birth_date`, `gender`, `region`, `district`, `settlement`, `address`, `marital_status`, `sons_count`, `daughters_count` | **Butun shaxsiy anketa** — v2 `User` da bu maydonlar yo'q |
| `lessons` | `recording_status`, `recording_note`, `recording_error`, `egress_id`, `analysis_status`, `analysis_text`, `analysis_error`, `analyzed_at`, `is_free`, `bo_timer_per_sec`, `bo_timer_started_at` | Yozuv holati va **AI tahlil matnlari** |
| `module_lessons` | `video_url`, `is_exam`, `exam_image` | **Kurs darslarining video havolasi** |
| `group_members` | `archive_reason`, `archived_at`, `moved_to_group_id` | Guruhdan chiqish sababi va qayerga ko'chgani |
| `payment_transactions` | `payment_id`, `lesson_id`, `lessons_count` | Jurnal yozuvi **qaysi oyga/darsga** tegishli ekani |
| `chat_messages` | `lesson_id` | Xabar qaysi jonli darsda yozilgani |
| `assignments` | `lesson_id` | Vazifa qaysi jonli darsda berilgani |
| `tariffs` | `note`, `created_by` | Tarif izohi va kim yaratgani |
| `courses`, `student_discounts` | `created_by` | Kim yaratgani |
| `groups` | `start_lesson_id`, `taught_upto_lesson_id`, `created_by` | Kurs bo'yicha qayergacha o'tilgani |

> `groups.curator_group_id` bu ro'yxatda **YO'Q** — u alohida ikkinchi
> qadamda (`Migrator.LinkCuratorGroups`) ko'chiriladi, chunki `Groups`
> ning o'ziga havola qiladi.

### 6.3. Taxmin qilinadigan (o'ylab topiladigan) qiymatlar

| Qayerda | Nima taxmin qilinadi | Nima uchun |
|---|---|---|
| `Modules`, `ModuleLessons`, `TestQuestions`, `TestOptions` | `CreatedAt` — **ko'chirish vaqti** qo'yiladi | Eski jadvallarda `created_at` ustuni umuman yo'q |
| `Attendances` | `CreatedAt` = `joined_at`, yo'q bo'lsa ko'chirish vaqti | Aynan shu sabab |
| `GroupChatMessages` | `SenderRole` — foydalanuvchining **ko'chirish paytidagi** roli | Eski tizim rol tarixini saqlamagan. Rol keyin o'zgargan bo'lsa eski xabar yangi rol bilan ko'rinadi |
| `SubmissionFiles` (eski `file_url` dan) | Fayl **turi kengaytmadan**, `SizeBytes` | Eski ustun bitta matn edi — tur ham, hajm ham saqlanmagan |
| `Payments` | `BaseAmount = Amount + DiscountAmount` | Eski `base_amount` ixtiyoriy edi. `Amount` (o'quvchi haqiqatan qarzdor summa) **o'zgarmaydi** |
| `LiveSessions` | Takrorlangan xona nomi `mig-l{id}` ga almashtiriladi | v2 da `RoomName` unikal; eski tizimda takror bo'lishi mumkin edi |

### 6.4. Ataylab ko'chirilmaydigan qatorlar

| Qayerda | Nima | Nima uchun |
|---|---|---|
| `payment_transactions` `type='due'` | Jurnal yozuvi | Pul **harakati emas**, "oy ochildi" belgisi. v2 da bu holat `Payments.Status = Due` qatorining o'zida turadi. Ko'chirilsa **kunlik kassa hisoboti tushmagan pulni ko'rsatardi** |
| Manfiy balansli `StudentAccounts` | Qator | v2 `CK_StudentAccounts_Balance_NonNegative` rad etadi. Summa hisobotda ko'rsatiladi — **qarz sifatida qo'lda kiritilishi kerak** |
| `PaidAmount > Amount` bo'lgan to'lovlar | Ortiqcha qism | v2 da ortiqcha pul `StudentAccounts.Balance` ga boradi. Kesilgan summa hisobotda ko'rinadi — **qo'lda balansga o'tkazilishi kerak** |
| Foizi 100 dan katta chegirmalar | Qator | v2 `CK_StudentDiscounts_Value_Range` rad etadi |

### 6.5. Ma'no o'zgaradigan (teskari) ustunlar

| Eski | v2 | Izoh |
|---|---|---|
| `attendance.auto_marked` | `Attendances.IsManual` | **TESKARI**: `IsManual = NOT auto_marked`. To'g'ridan-to'g'ri ko'chirilsa `Finalize()` qo'lda qo'yilgan baholarni qayta hisoblab yuborardi |
| `groups.status` (`active`/`archived`) | `Groups.IsActive` (bayroq) | v2 da alohida "arxiv" holati yo'q |
| `chat_messages.channel='assistant'` | `GroupChatChannel.Curator` | Nom o'zgardi, ma'no bir xil |
| Manfiy `payment_transactions.amount` | `Kind=Refund` + **musbat** summa | v2 da yo'nalish **turda**, summada emas |

---

## 7. Sinov holati — nima tekshirilgan, nima tekshirilmagan

### ✅ Sintetik ma'lumotda **tekshirilgan**

Alohida, bo'sh bazada eski sxema qurilib (`db/*.sql` + `models.py` dan
olingan DDL), realistik ma'lumot to'ldirilgan: 38 foydalanuvchi,
5 guruh, 34 dars, 210 davomat, 68 chat xabari (ikkala oqim),
51 to'lov, 31 moliya jurnali yozuvi, turli telefon formatlari va
**ataylab qo'yilgan dublikatlar**.

- Hafta kunlari **bir kun siljimaydi** — 32 dars sanasining hammasi
  guruh jadvaliga to'g'ridan-to'g'ri mos keldi. Yakshanba chegara holati
  (`python 6 → .NET 0`) ham tekshirildi.
- Chat ikki oqimi **qo'shilib ketmaydi** — har `(guruh, kanal)` juftligi
  alohida mos keldi.
- Telefon dublikatlari **jimgina yutilmaydi** — hisobotga `id` va asl
  ko'rinishi bilan chiqadi, birorta foydalanuvchi yo'qolmaydi.
- Pul yig'indilari **tiyinigacha** mos keladi (5 ta mustaqil tenglik).
- **Idempotentlik**: ikki marta yurgizilganda maqsad baza bayt-ma-bayt
  o'zgarmadi.
- **Salbiy nazorat**: hafta kuni konvertatsiyasi va chat kanali
  xaritalashi ataylab buzilganda vosita **chiqish kodi 1** bilan
  to'xtadi va 10 ta aniq xato ko'rsatdi. Ya'ni tekshiruvlar
  **haqiqatan ishlaydi**, shunchaki "yashil" chizmaydi.

### ⚠️ Tekshiril**MA**gan

> **Vosita HAQIQIY prod ma'lumotida hech qachon yurgizilmagan va
> sinalmagan.** Barcha yuqoridagi natijalar **sintetik** ma'lumotga
> tegishli.

Shuning uchun **majburiy birinchi qadam**:

```bash
# Prod bazasining NUSXASIDA (prodning O'ZIDA EMAS!) tayyorgarlikni yurgizing
zinnur-migrate --source=<prod NUSXASI> --target=<bo'sh sinov v2> --only=preflight
```

Prod ma'lumotida albatta sintetik ma'lumotda bo'lmagan holatlar chiqadi:
tanilmagan satr qiymatlari, kutilmagan `NULL` lar, sintetikdan ancha
ko'p dublikatlar. Ular **tayyorgarlik hisobotida** ko'rinadi va
`Mapping/LegacyMap.cs` ga qo'shiladi.

Shuningdek tekshirilmagan:

- **Hajm.** Sinov 734 manba qatorida o'tkazildi (722 ko'chdi, 12 sababi
  bilan o'tkazib yuborildi). Prod hajmida (o'n minglab qator) paket
  o'lchami va yurish vaqti o'lchanmagan.
- **Tarmoq uzilishi** o'rtasida (idempotentlik mantiqan to'g'ri va
  qayta yurgizishda tekshirilgan, lekin haqiqiy uzilish taqlid
  qilinmagan).
- **R2/MinIO fayllari** — vosita faqat bazani ko'chiradi, obyektlar
  saqlagichga tegmaydi. Fayl havolalari eski `id` larga tayangani uchun
  ishlashda davom etishi kutiladi, lekin bu **alohida tekshirilishi
  kerak**.
