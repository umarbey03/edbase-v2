# Yangi talablar — TO'LIQ REJA (2026-08-10)

> **Manba:** loyiha egasining 2026-08-10 dagi topshirig'i (3 ta ekran surati + matn).
> **Bajarish tartibi:** shu hujjatdagi BLOK tartibida, ketma-ket.
> **Bog'liq hujjatlar:** `DAVOM_ETTIRISH.md` (holatni tiklash, tuzoqlar) ·
> `DIZAYN_KOCHIRISH_REJASI.md` (dizayn pariteti) · `PROGRESS.md` (jurnal) ·
> `SPEC.md` (shartnoma) · `ROADMAP.md`
>
> **Repo:** `~/Documents/Projects/zinnur-v2` · oxirgi commit `78a1e37` ·
> working tree TOZA (yangi ish shu nuqtadan boshlanadi).

---

## 0. TALABNI O'QISH — nima o'zgardi

### 0.1. 🔴 Dizayn qarori TESKARISIGA o'zgardi (ochiq aytiladi)

`DIZAYN_KOCHIRISH_REJASI.md` ning butun asosi shu edi:

> *"dizayn deyarli bir xil bo'lib ko'chmasa eski appni foydalanuvchilari juda
> qiynaladi tushunib olgani"* — ya'ni eski **navy + oltin, qorong'i** tema
> aynan ko'chirilgan (uchala rolda bit-to-bit token pariteti bilan).

Yangi talab: **iOS uslubidagi yorug', minimalistik ranglar** (ekran suratlaridagi
oq sirt + indigo accent + pastel nishonlar).

**Bu ikkisi to'qnashadi va yangi talab ustun** — qaror loyiha egasining.
Lekin parite hujjatining **qolgan qismi kuchda qoladi**, faqat rang almashadi:

| Parite mezoni | Holati |
|---|---|
| Matnlar aynan (bo'lim nomlari, bo'sh holat jumlalari, tugma yozuvlari) | ✅ **saqlanadi** |
| Tartib aynan (menyu, tablar ketma-ketligi) | ✅ **saqlanadi** (istisno: 4.1 — o'quvchilar tabi birinchiga chiqadi, bu ATAYLAB so'ralgan) |
| Karkas (yon menyu, 5 tabli Mini App, 85% drawer) | ✅ saqlanadi + drawer qo'shiladi |
| **Rang tokenlari** | 🔄 **BUTUNLAY ALMASHADI** |

Ya'ni foydalanuvchi "qayerda nima turishini" qayta o'rganmaydi — faqat
ko'rinish yangi bo'ladi. Xavf shu bilan cheklanadi.

### 0.2. Ranglarni almashtirish qayerdan boshqariladi — TOPILDI

Savolga javob: **`frontend/src/style.css` — YAGONA fayl.**

Tailwind v4 utility'lari qiymatni ichiga yozmaydi, o'zgaruvchiga murojaat
qiladi (`.bg-ink-900 { background: var(--color-ink-900) }`). Shuning uchun
`@theme` bloki almashtirilsa **butun loyiha** birdan o'zgaradi — 200+ komponentga
tegilmaydi. Bu arxitektura oldingi sessiyalarda ataylab shunday qurilgan.

Faqat **4 ta qoldiq** qo'lda tuzatiladi (audit qilindi, ro'yxati A3 da):

| Qoldiq | Soni | Joyi |
|---|---|---|
| Qotib qolgan HEX (izohlardan tashqari) | **12** | 6 ta fayl |
| `bg-black/NN`, `bg-white/NN` | **10** | modal fon, video ustidagi qatlam |
| `color-scheme: dark`, `<html class="dark">`, `meta theme-color` | **3** | `style.css`, `index.html`, `AppShell.vue` |
| `::selection { color:#fff }`, scrollbar `#3a6248` | **2** | `style.css` |

---

## 1. BLOK A — iOS uslubidagi yorug' dizayn tizimi

**Nima uchun birinchi:** keyingi hamma blok yangi komponent (drawer, icon-tugma,
loader) qo'shadi. Ular avval yangi tokenlarda yozilsa, ikki marta ishlanmaydi.

### A0. Palitra (ekran suratlaridan olindi)

**Neytral sirtlar** — mavjud `ink-*` shkalasi **semantik jihatdan teskari
aylanadi** (eng "to'q" nom = eng yorug' sirt). Nom o'zgarmaydi, chunki 200+
faylda ishlatilgan; faqat qiymat almashadi:

| Token | Hozir (qorong'i) | Yangi (yorug') | Roli |
|---|---|---|---|
| `ink-950` | `#070d09` | `#f4f6fb` | sahifa foni |
| `ink-900` | `#0e1712` | `#ffffff` | kartochka / panel |
| `ink-850` | `#121b16` | `#fbfcfe` | ichki blok |
| `ink-800` | `#15201a` | `#f2f4f9` | hover |
| `ink-750` | `#1c2c22` | `#e9ecf5` | kuchli hover / bosilgan |
| `ink-700` | `#24382b` | `#dfe3ee` | ajratgich to'ldirish |
| `line` | `#1d3326` | `#eceff5` | chegara |
| `line-strong` | `#2c4c37` | `#dde1ec` | kuchli chegara / scrollbar |

**Matn** — `slate-*` shkalasi ham teskari aylanadi (`slate-100` = asosiy matn):

| Token | Yangi | Roli |
|---|---|---|
| `slate-50` | `#0f1117` | eng qora sarlavha |
| `slate-100` | `#1b1d2a` | asosiy matn |
| `slate-200` | `#2b2f40` | quyi sarlavha |
| `slate-300` | `#4a5060` | tanadagi matn |
| `slate-400` / `muted` | `#767f95` | ikkilamchi matn |
| `slate-500` | `#8b93a7` | ikonka, placeholder |
| `slate-600` / `dim` | `#a2a9bb` | uchinchi darajali |
| `slate-700` | `#c3c8d6` | ajratgich matni |

**Brend (indigo)** — ekran suratidagi faol menyu va asosiy tugma rangi:

```
brand-100 #eeefff · 200 #dcdefe · 300 #bcbffc · 400 #8a8df8
brand-500 #5b5bf5  ← AKSENT · 600 #4a49dd · 700 #3a39b0 · 900 #21215e
on-brand  #ffffff
```

**Semantik (pastel nishon: `bg-*/12` + to'q matn)** —
`green-500 #12b76a` · `amber/brand-warn #f79009` · `rose-500 #f04438` ·
`cyan-500 #06aed4` · `violet-500 #8b5cf6`

**Soya tokenlari** (yorug' temada chegara o'rniga soya ishlaydi — YANGI):

```
--shadow-xs  0 1px 2px  rgb(16 24 40 / .05)
--shadow-sm  0 1px 3px  rgb(16 24 40 / .06), 0 1px 2px rgb(16 24 40 / .04)
--shadow-md  0 4px 12px rgb(16 24 40 / .06)
--shadow-lg  0 12px 32px rgb(16 24 40 / .10)
```

**Radius (iOS = keng):** kartochka `1rem`, tugma/input `0.75rem`, nishon pill,
modal `1.25rem`.

**Shrift:** `Plus Jakarta Sans` **saqlanadi** — allaqachon o'zimizda (49 KB,
tashqi so'rovsiz) va o'zbek `oʻ`/`gʻ` glifleri bor. `-apple-system` (SF Pro)
faqat Apple qurilmalarida ishlaydi, Windows/Android'da tushib qolardi.

### A1. 🔴 Uch tema BITTAGA yig'iladi

Hozir `[data-theme='student' | 'teacher' | 'manage']` — uchta to'liq token
to'plami (eski ilovada har panel boshqa navy soyasida edi). "Minimalistik"
talabi bu bo'linishni ma'nosiz qiladi va uni saqlash har rang tuzatishini
**uch joyda** takrorlashga majbur qiladi.

**Qaror:** yagona yorug' palitra `@theme` da. `[data-theme]` mexanizmi
**o'chirilmaydi** (AppShell/StudentShell kodi, `theme-color` meta va teleport
qoidasi joyida qoladi) — lekin bloklar bo'shatiladi, faqat rol uchun
HAQIQATAN farq qiladigan narsa qoladi (o'quvchida kartochka radiusi 16px).
Kelajakda rolni ajratish kerak bo'lsa bitta `--color-brand-500` yozuvi yetadi.

★ **Oltin fon muammosi o'z-o'zidan yo'qoladi:** `on-brand` endi hamma joyda oq
(indigo fonda kontrast 5.9:1). `DAVOM_ETTIRISH.md` 6-bo'limining 10-tuzog'i
("oltin fonda `text-white` ishlatmang") **kuchini yo'qotadi**.

### A2. Karkas darajasidagi tuzatishlar

| Fayl | O'zgarish |
|---|---|
| `frontend/src/style.css` | `html { color-scheme: light }`, fon tokeni, `::selection` tokenga, scrollbar hover tokenga, `.zn-input`/`.zn-table` yorug' variantga (input foni `#fff`, `border` `line`, focus ring `brand-500/25`) |
| `frontend/index.html` | `class="dark"` olinadi, `meta color-scheme: light`, `meta theme-color: #f4f6fb`, `body` klasslari tokenga mos |
| `widgets/app-shell/ui/AppShell.vue` | rol → `theme-color` xaritasi `#f4f6fb` ga |
| `widgets/student-shell/ui/StudentShell.vue` | `STUDENT_THEME_COLOR = '#f4f6fb'` |
| `features/telegram-auth/.../applyMiniAppChrome` | Telegram Mini App chrome rangi |

### A3. Qotib qolgan ranglarni tokenlashtirish (aniq ro'yxat)

| Fayl | Nima |
|---|---|
| `features/student-course/ui/CourseLessonPath.vue:52-54` | oltin gradient + `#3a2600` matn + `#a9760a` soya → brand tokenlari |
| `features/student-schedule/ui/NextLessonCard.vue:59-60` | `#ff9b9b`, `#67e8f9` → `rose-400`, `cyan-500` |
| `pages/student/StudentCalendarPage.vue:248,292` | `#f5b731`/`#22d3ee`, `#fcd34d`/`#67e8f9` → `brand-500`/`cyan-500` |
| `pages/student/StudentLearnPage.vue:276,306` | inline `rgb(...)` + `#67e8f9`, `#c4b5fd` → `bg-cyan-500/12 text-cyan-600` uslubiga |
| `widgets/student-shell/ui/StudentAppBar.vue:104` | avatar gradienti → brand→violet |
| `widgets/student-shell/ui/StudentProfileSheet.vue:50,69,74` | gradient + `#22d3ee` × 2 |
| `shared/ui/BaseModal.vue:85` | `bg-black/65` → `bg-slate-900/35 backdrop-blur-sm` (yorug' temada 65% qora juda og'ir) |
| `widgets/app-shell/ui/AppShell.vue` | mobil menyu ortidagi `bg-black/65` → yuqoridagidek |
| `entities/recording/ui/RecordingCard.vue` | `bg-black/65`, `bg-black/55` — **video posteri ustida QOLADI** (video doim to'q), lekin izoh qo'shiladi |
| `pages/live/LiveRoomPage.vue` | `bg-black/60` — **jonli dars sahnasi QORONG'I QOLADI** (qaror: A5) |
| `pages/auth/LoginPage.vue`, `features/chat`, `features/presence`, `StudentLearnPage` | `bg-white/5` → `bg-ink-800` |

### A4. Komponentlarni yorug' temaga sayqallash

Token almashishi bilan hammasi "ishlaydi", lekin iOS ko'rinishi uchun kichik
tuzatishlar kerak (ekran suratlaridagi ruh):

| Komponent | Tuzatish |
|---|---|
| `BaseCard` | chegara + `shadow-sm`, radius `1rem` |
| `BaseButton` | radius `0.75rem`, primary'da `shadow-xs`, secondary = oq fon + chegara |
| `BaseBadge` | pastel: `bg-{tone}-500/12` + `text-{tone}-600`, pill, 11px medium |
| `BaseAvatar` | ekran suratidagidek **to'q pastel to'ldirish + oq harf**, rang ismdan deterministik (hozir bormi — tekshiriladi) |
| `zn-table` | `th` — `#8b93a7`, 11px, uppercase, `tracking-wide`; qator hover `ink-800`; chegara `line` |
| `AppSidebar` | faol element = to'liq indigo fon + oq matn + radius 12px (surat 1 va 2 dagidek); logotip plitkasi indigo gradient |
| `BaseModal` | radius `1.25rem`, `shadow-lg`, backdrop yengil |

### A5. 🔴 Jonli dars xonasi QORONG'I qoladi (qaror + sabab)

`LiveRoomPage` — video sahna. Yorug' fon video ustida:
1. ko'z charchaydi (kino qoidasi: video atrofi to'q bo'ladi);
2. ekran ulashishda oq ramka video kontrastini yeydi;
3. hech bir jonli dars mahsuloti (Zoom, Meet, LiveKit demo) yorug' emas.

**Yechim:** `LiveRoomPage` ildiziga `data-surface="stage"` qo'yiladi va shu
selektor ostida `ink-*` tokenlari to'q neytralga (`#0f1115` oilasi) qaytariladi.
Bu **rol temasi emas** — sirt temasi, ya'ni A1 qarori buzilmaydi.
Boshqarув tugmalari va chat paneli indigo aksentda qoladi.

### A6. Tekshirish (qabul mezoni)

- [ ] `npm run typecheck` + `eslint --max-warnings 0` toza
- [ ] Kontrast auditi: **hamma matn ≥ 4.5:1**, ikkilamchi ≥ 3:1 (skript bilan,
      19 sahifada — oldingi audit shu qamrovda edi)
- [ ] Brauzerda 5 yuza: kirish · o'quvchi Mini App (390px) · ustoz · o'quv
      bo'limi · jonli dars
- [ ] Qorong'i qoldiq qolmagani: `grep -rE "bg-ink-9|text-slate-1"` emas —
      ko'z bilan, ekran surati bilan solishtirib

> **Qorong'i tema (dark mode) BU BLOKDA YO'Q.** Suratdagi quyosh ikonkasi boshqa
> loyihadan. Tokenlar `:root` da yig'ilgani uchun keyinchalik
> `[data-appearance='dark']` bloki bilan qo'shish ~1 soatlik ish bo'ladi.
> Hozir qilinsa har blokda ikki tema tekshirilishi kerak bo'lardi.

---

## 2. BLOK B — Umumiy UI infratuzilma (qolgan hamma blok shunga tayanadi)

### B1. `BaseDrawer` — o'ngdan chiquvchi 85% panel

Talabda ikki joyda aynan shu so'ralgan (o'quvchi profili, dars tahrirlash):
*"modal ko'rinishida ekranni o'ng tarafidan ekranni 85% egallab chiqishi kerak"*.

`BaseModal` ga yana bir variant qo'shish EMAS, alohida komponent —
`BaseModal` allaqachon uch rejimli (markaz / keng / sheet) va to'rtinchisi uni
o'qib bo'lmas qiladi. Lekin **mexanizm qayta ishlatiladi**: fokus qaytarish,
`body` skroll qulfi, ESC, `Teleport to="body"` — mantiq
`shared/lib/useModalHost.ts` ga chiqariladi va **ikkalasi ham** shundan
foydalanadi (aks holda skroll qulfi bilan bog'liq xato ikki joyda tuzatiladi).

```
Desktop (≥1024px) : o'ngdan surilib chiqadi, width 85vw, max-width 1240px
Planshet (≥640px) : 92vw
Telefon           : to'liq ekran (85% telefonda ma'nosiz), yuqoridan pastga yopiladi
```

- animatsiya: `slide-in-right 0.24s cubic-bezier(.22,1,.36,1)`
- ichida **sticky sarlavha** + skroll qiladigan tana + sticky футер
- `aria-modal`, fokus tuzoq, ichki drawer ochilishi TAQIQLANADI (bir qatlam)

### B2. 🔴 `useConfirm` — har qanday o'zgarish tasdiqlanadi

Talab: *"Platformadagi har qanday edit, delete, change qilingan ma'lumotlar
tasdiqlashni so'rashi kerak"*.

Mavjud `ConfirmDeleteDialog` **saqlanadi** (u 409 sababini oynani yopmasdan
ko'rsatadi — qimmatli xususiyat). Ustiga imperativ qobiq qo'yiladi:

```ts
const confirm = useConfirm()
if (!(await confirm({
  title: 'Guruhdan chiqarish',
  message: 'Aziza Karimova guruhdan chiqariladi. Davomat yozuvlari saqlanadi.',
  confirmLabel: 'Chiqarish',
  tone: 'danger',          // danger | warning | primary
}))) return
```

- host komponent `App.vue` ga bir marta qo'yiladi (`ConfirmHost`)
- Promise qaytaradi → mavjud `useMutation` chaqiruvlariga **bitta qator** bilan
  qo'shiladi, oqim qayta yozilmaydi
- server xatosi (409/400) oynada QOLADI, "Qayta urinish" bilan

★ **"change" ni qanday o'lchash kerak** — hammasiga oyna qo'yilsa interfeys
foydalanishga yaramaydi (har checkbox uchun oyna). Qoida:

| Amal turi | Tasdiq |
|---|---|
| O'chirish, chiqarish, bekor qilish, bloklash, Telegram uzish, pul qaytarish | **HAR DOIM**, `danger` |
| Ma'lumotni almashtiruvchi saqlash (guruh bo'limi, dars, foydalanuvchi, to'lov) | **HAR DOIM**, `primary` — o'zgargan maydonlar ro'yxati bilan |
| Yon ta'siri kattaligi (jadval qayta generatsiyasi, +N/−N dars) | **HAR DOIM**, `warning` + raqamlar bilan |
| Filtr, qidiruv, tab almashish, tartiblash, sahifalash | **YO'Q** (ma'lumot o'zgarmaydi) |
| Formani to'ldirish jarayonidagi har bir maydon | **YO'Q** (saqlashda bir marta) |

### B3. 🔴 Loader — "tugma ishlaganini bilish" talabi

Talab: *"Har bir tugma bosilganda ... shu vaqt davomida loader chiqishi kerak.
Sababiki tugma ishlagani va ma'lumot load qilinayotganini bilish uchun."*

Uch qatlam (uchalasi ham kerak, biri ikkinchisini qoplamaydi):

1. **Tugma ichida** — `BaseButton` da `:loading` allaqachon bor. Ish: **audit** —
   har `useMutation` ni ishga soluvchi tugmada `:loading` borligini tekshirish
   (`grep useMutation` → har biri uchun). Bosilgan tugma **darhol** o'chadi
   (ikki marta yuborish yo'q).
2. **Blok ichida** — `shared/ui/SectionLoader.vue` (skeleton). Drawer bo'limi,
   modal tanasi, tab kontenti yuklanayotganda. `DataStatus` bunga tayyor —
   yetmagan joylarga qo'yiladi.
3. **Global yupqa progress** — `shared/ui/RouteProgress.vue`: sahifa almashishi
   yoki 400 ms dan uzoq har qanday so'rov paytida yuqorida 2px indigo chiziq.
   `vue-query` ning `useIsFetching()` + router hodisalari. 400 ms kechikish
   MAJBURIY: tez javobda chiziq "chaqnab" ketsa interfeys asabiy ko'rinadi.

★ **Loader jonli ma'lumotni O'CHIRMAYDI** — `DataStatus` da `retrying` bor:
qayta yuklashda eski ma'lumot xiralashadi, skeletonga almashmaydi (aks holda
har 30 sekundda jadval "yo'qolib" ko'rinardi).

### B4. `IconButton` + ikonka to'plami

Talab: *"har bir o'quvchi bo'yicha actions buttonlar icon ko'rinishida bo'lgani
ma'qul"*.

- `shared/ui/IconButton.vue`: 36×36 ko'rinadigan, `tap-expand` bilan 44×44
  bosiladigan; `title` + `aria-label` **majburiy prop** (ikonkali tugma
  matnsiz — screen reader uchun nomsiz qolmasligi kerak); `tone`, `:loading`
- `AppIcon` ga yetmagan nomlar qo'shiladi: `pause`, `play`, `arrow-right-left`
  (ko'chirish), `user-x` (chiqarish), `eye`, `upload`, `mic`, `image`, `video`,
  `paperclip`, `link-off`, `wallet`, `note`

---

## 3. BLOK C — Guruhni bo'limlar bo'yicha tahrirlash

Talab: har section **alohida** tahrirlanadi.

### C1. Bo'limlar tarkibi (talabdagi aynan taqsimot)

| Bo'lim | Maydonlar |
|---|---|
| **Asosiy ma'lumotlar** | guruh nomi · guruh turi · ustoz · kurator · kurator guruhi · darslarni yozib olish · guruh statusi |
| **Dars jadvali** | boshlanish sanasi · dars kunlari · boshlanish vaqti · dars davomiyligi · kurs davomiyligi |
| **Kurs** | kurs · **video darslar qaysi qismdan boshlanadi** (YANGI) |

### C2. 🔴 `PUT` to'liq almashtirish bilan bo'limni qanday saqlash

`PUT /groups/{id}` — TO'LIQ ALMASHTIRISH (`DAVOM_ETTIRISH.md` 1-tuzoq). Bitta
bo'limni yuborish **qolgan hamma maydonni `null` ga tushiradi** — kurs uzilib,
butun guruhda gating `NotInCourse` bo'lardi (bu xato bir marta bo'lgan).

**Uch variant ko'rildi:**

| Variant | Baho |
|---|---|
| Har bo'limga alohida `PATCH` endpoint (3 ta) | Eng "to'g'ri", lekin 3 endpoint + 3 validator + 3 test to'plami · jadval qayta generatsiyasi mantig'i ikkiga bo'linadi |
| Umumiy `PATCH` (`JsonPatch`/nullable-wrapper) | .NET'da `Optional<T>` shakli kerak, Swagger buziladi, xato qilish oson |
| **Frontend to'liq `GroupDto` ni saqlab turadi, faqat shu bo'lim maydonlarini o'zgartirib TO'LIQ `PUT` yuboradi** ✅ | Backend **o'zgarmaydi** · semantika bir joyda · UX aynan so'ralgandek |

**Tanlandi: 3-variant.** Shart-sharoitlar (majburiy):

1. Drawer ochilganda `GET /groups/{id}` bilan **yangi** ma'lumot olinadi
   (ro'yxatdagi keshdan emas — u eskirgan bo'lishi mumkin).
2. Har bo'lim saqlangach javobdagi `group` bilan lokal holat **to'liq**
   yangilanadi (uchala bo'lim ham).
3. 🔴 **Optimistik qulf:** javobdagi `updatedAt` saqlanadi; boshqa bo'limni
   saqlashdan oldin `GET` qaytargan `updatedAt` bilan solishtiriladi — farq
   bo'lsa "Guruh boshqa joyda o'zgardi, qayta yuklang" deb ogohlantiriladi.
   Aks holda ikki xodim ikki bo'limni parallel saqlab, biri ikkinchisining
   o'zgarishini bekor qilardi (`PUT` to'liq almashtirish!).
4. **Jadval bo'limi** saqlanishi oldidan `warning` tasdiq: server javobidagi
   `+created / −deleted / preserved` allaqachon bor — lekin u **saqlangandan
   keyin** keladi. Shuning uchun tasdiq oynasida oldindan aytiladi: *"Dars
   jadvali qayta generatsiya qilinadi. Boshlanmagan darslar o'chirilib qayta
   yaratiladi, o'tgan darslar saqlanadi."* Natija raqamlari — saqlangandan keyin
   (hozirgi `scheduleNote` mexanizmi qoladi).

### C3. 🔴 Backend YANGI: "video darslar qaysi qismdan boshlanadi"

Bu **guruh-daraja sozlama** (bir kurs — ko'p guruh, har biri boshqa joydan
boshlashi mumkin: yarim yildan qo'shilgan guruh 1-moduldan boshlamaydi).

```
Group.VideoStartLessonId : long?   -> module_lessons(id), ON DELETE SET NULL
```

`ModuleLesson` tanlanadi (`CourseModule` emas): modul o'rtasidan boshlash
ehtiyoji real — talabda "qaysi qisimdan" deyilgan, dars aniqligi modulni ham
qoplaydi (modulning 1-darsi = modul boshi).

| Ish | Fayl |
|---|---|
| Maydon + `Validate()`: dars guruh kursiga tegishli bo'lishi shart (aks holda 400) | `Domain/Entities/Group.cs` |
| EF sozlash + migratsiya | `Infrastructure/Persistence/...`, `Migrations/` |
| `GroupDto`: `videoStartLessonId`, `videoStartLessonName`, `videoStartModuleName` | `Application/Groups/Dtos` |
| `GroupWriteRequest.VideoStartLessonId` + servisda yozish | `Application/Groups/Services/GroupService.cs` |
| 🔴 **Gating:** shu darsdan OLDINGI darslar guruh o'quvchisiga **ochiq deb ham, talab deb ham hisoblanmaydi** — ular ro'yxatdan chiqadi. `PreviousIncomplete` hisobi yangi boshlanish nuqtasidan yuritiladi, aks holda o'quvchi hech qachon o'tmagan 20 ta darsni "tugatmagan" bo'lib butun kurs qulflanardi | `Application/Gating/Services` |
| Kurs almashsa `VideoStartLessonId` **tozalanadi** (begona kursning darsi bo'lib qolmasin) | `GroupService.UpdateAsync` |
| Testlar: yaroqsiz dars → 400 · gating shu darsdan boshlanishi · kurs almashsa tozalanishi · `null` da eski xatti-harakat | `tests/` |

### C4. Frontend

- `features/group-form/ui/GroupFormDialog.vue` → **`GroupEditDrawer.vue`**
  (`BaseDrawer` ichida uch karta, har birida o'z "Saqlash" tugmasi va o'z
  `isPending` holati)
- **Yaratish** rejimi: bo'limlar bo'yicha ko'rinadi, lekin **bitta** "Yaratish"
  tugmasi (guruh hali yo'q — bo'lak-bo'lak saqlash mumkin emas). Kurs bo'limida
  "video boshlanish darsi" faqat kurs tanlangach faollashadi.
- Kurs bo'limidagi dars tanlagich: `GET /courses/{id}` daraxti (modul → dars),
  qidiruvli select; kurs tanlanmagan bo'lsa o'chiq + sabab
- Har saqlashda `useConfirm` (B2 jadvalidagi tonlar bilan) + tugmada loader

---

## 4. BLOK D — Guruh sahifasi: o'quvchilar birinchi + ikonkali amallar

### D1. Tab tartibi

Talab: *"guruh ichiga kirilganda o'quvchilar ro'yxati birinchi o'rinda"*.

Hozir `features/group-tabs/model/tabs.ts` da eski ilova tartibi:
`Darslar · O'quvchilar · Davomat · Baholar · Vazifalar · Testlar · Reyting · Chat`.

🔴 Bu `DIZAYN_KOCHIRISH_REJASI.md` ning 3-mezoniga ("tartib aynan") **ataylab
qilingan chekinish** — talab aynan shu. Chekinish hujjatga yoziladi.

**Qamrov:** faqat **o'quv bo'limi/admin** guruhga kirganda (ular ro'yxat bilan
ishlaydi). **Ustoz/kuratorda tartib TEGILMAYDI** — ular kunda darsga kiradi,
"Darslar" birinchi bo'lishi ular uchun to'g'ri. Buni tabs modeli rol bo'yicha
tartiblaydi (ikki nusxa emas, bitta ro'yxat + rolga qarab birinchisi).

### D2. Ikonkali amallar

`features/group-members/ui/GroupMembersPanel.vue` — har qator uchun `IconButton`:

| Ikonka | Amal | Tasdiq |
|---|---|---|
| `user` | profilni ochish (BLOK E drawer) | — |
| `pause` / `play` | pauza / tiklash | `warning` |
| `arrow-right-left` | boshqa guruhga ko'chirish | `warning` (mavjud `MoveMemberDialog` qoladi) |
| `user-x` | guruhdan chiqarish | `danger` |
| `wallet` | to'lov holati (BLOK E ning to'lov bo'limiga) | — |

Telefonda ikonkalar qator oxirida "..." menyusiga yig'iladi (5 ta ikonka 360px
ekranga sig'maydi).

---

## 5. BLOK E — O'quvchi profili drawer (o'ngdan 85%)

Eng katta blok: **backend ham, frontend ham**. Talabdagi ro'yxat to'liq
qoplanadi.

### E1. Backend inventari — nima bor, nima yo'q

| Talab qatori | Holat |
|---|---|
| ism, familya, tel, email | ✅ `UserDetailsDto` |
| telegram id | ✅ `User.TelegramId` |
| **telegram username** | 🔴 **YO'Q** — maydon + migratsiya kerak |
| telegram ulanish holati | ✅ hosila (`TelegramId != null`) |
| 🔴 **telegram ulanishini uzish** | 🔴 **YO'Q** — endpoint kerak |
| to'lov ma'lumotlari (qachon, qancha, qanday, qaysi guruh) | ✅ `GET /payments/students/{id}/transactions` |
| **xarajatlari** (qaysi dars/guruh uchun) | ⚠️ qisman — `PaymentTransaction` da guruh bor, **dars kesimi yo'q**. Qaror: E5 |
| to'lov kirgazish | ✅ `POST /payments` |
| to'lovni yechib olish | ✅ `POST /payments/reverse` |
| guruhlar ro'yxati + faol/chiqarilgan/ko'chirilgan | ✅ `GroupMember.Status` (`Active/Paused/Stopped/Moved`) — lekin **o'quvchi kesimidagi endpoint yo'q** |
| test natijalari | ✅ `TestAttempt` — o'quvchi kesimi kerak |
| uy vazifalari natijalari | ✅ `Submission` — o'quvchi kesimi kerak |
| 🔴 **ustoz izohlari** | 🔴 **YO'Q** — eski tizimdagi `student_notes` jadvali v2 ga ko'chirilmagan. Entity + CRUD + migratsiya kerak |

### E2. Backend ishlari

**E2.1. `User.TelegramUsername` (string?, 32)** — Telegram `from.username`.
`TelegramUpdateHandler` bog'lanish paytida yozadi (username o'zgarishi mumkin —
har kirishda yangilanadi). Migratsiya.

**E2.2. `POST /api/v1/users/{id}/telegram/unlink`** — `[Authorize(Roles="Academic,Admin")]`

```
200 -> { telegramId: null, telegramUsername: null }
404 -> foydalanuvchi yo'q
409 -> allaqachon bog'lanmagan
```

🔴 **`TokenVersion` OSHIRILADI** — aks holda o'quvchining Mini App'dagi
access token'i uzilgandan keyin ham 15 daqiqa ishlab turardi, ya'ni "kira
olmaydi" talabi bajarilmagan bo'lardi. Audit yozuvi qoldiriladi (kim uzdi).
Bu qaror `PROGRESS.md` dagi *"Eski bog'lanishni faqat o'quv bo'limi bekor
qiladi"* qarori bilan bir xil chiziqda.

**E2.3. `StudentNote` entity** — ustoz/kurator/o'quv bo'limi izohi

```
id · student_id · author_id · group_id? · body(2000) · created_at · updated_at
```

- `GET /users/{id}/notes` · `POST` · `PUT /notes/{noteId}` · `DELETE /notes/{noteId}`
- Ruxsat: **ustoz/kurator faqat o'z guruhidagi o'quvchiga** yozadi va faqat
  **o'z** izohini tahrirlaydi/o'chiradi; Academic/Admin hammasini ko'radi
- 🔴 O'quvchi **o'z izohlarini KO'RMAYDI** (bu ichki eslatma, "kech qoladi",
  "otasi bilan gaplashildi" kabi yozuvlar bo'ladi) — `Student` rol uchun
  endpoint 403
- Migratsiya + testlar (rol matritsasi)

**E2.4. `GET /api/v1/users/{id}/profile`** — yagona agregat endpoint

Nima uchun bitta endpoint: drawer ochilganda 7 ta parallel so'rov yuborilsa
telefonli internetda 2–3 sekund "bo'sh drawer" ko'rinadi. Bitta so'rov —
bitta loader.

```jsonc
{
  "user":     { id, fullName, email, phone, role, isActive, createdAt },
  "telegram": { linked, telegramId, username, linkedAt },
  "groups":   [ { groupId, groupName, teacherName, status, joinedAt,
                  leftAt, movedToGroupId, movedToGroupName, pausedUntil } ],
  "finance":  { balance, totalPaid, totalDue, blockScope,
                periods:      [ { month, amount, paidAmount, status, groupName } ],
                transactions: [ { id, kind, amount, method, occurredAt,
                                  groupName, periodMonth, note, createdByName } ] },
  "study":    { assignments: [ { title, groupName, lessonName, score, maxScore,
                                 status, submittedAt, isLate } ],
                tests:       [ { title, kind, scorePercent, correct, total,
                                 finishedAt } ],
                attendance:  { present, total, percent } },
  "notes":    [ { id, body, authorName, groupName, createdAt, canEdit } ]
}
```

- 🔴 **Ruxsat:** `Academic/Admin` — hammasi. `Teacher/Assistant` — faqat **o'z
  guruhidagi** o'quvchi, va **`finance` bloki UMUMAN yuborilmaydi** (`null`).
  Ustoz o'quvchining qarzini ko'rishi kerak emas; bu maydonni frontendda
  yashirish yetarli emas — javobda bo'lmasligi kerak.
- `Student` — faqat o'zi, `notes` va `finance.transactions` **null**
- N+1 dan qochish: har blok bitta `Include`/`GroupJoin` bilan
- Katta ro'yxatlar (tranzaksiya) **oxirgi 50 ta** + `hasMore` → to'liqi mavjud
  `/transactions` endpointida (drawer'da "Hammasini ko'rish")
- Testlar: rol matritsasi · begona guruh ustozi → 403 · moliya bloki `null`

**E2.5. `GET /users` ga filtrlar (BLOK F)** — bir joyda qilinadi.

### E3. Frontend — `StudentProfileDrawer`

`BaseDrawer` ichida yopishgan sarlavha (avatar, ism, rol nishoni, holat) +
bo'limlar. Talabdagi tartib saqlanadi:

1. **Shaxsiy** — ism, tel, email, telegram username/id, ulanish holati
   (`Ulangan` yashil nishon + **"Uzish"** ikonkali tugma, `danger` tasdiq:
   *"O'quvchi platformaga kira olmaydi"*)
2. **To'lovlar** — davrlar jadvali + tranzaksiya tarixi (qachon, qancha,
   usul, guruh). Admin uchun: **"To'lov kiritish"** va **"Yechib olish"**
   tugmalari (`payment-actions` mavjud komponentlari qayta ishlatiladi)
3. **Guruhlar** — faol / pauzada / chiqarilgan / ko'chirilgan (qayerga)
   nishonlar bilan, guruh nomiga bosilsa guruh sahifasi
4. **O'quv natijalari** — uy vazifalari (ball, kechikkanmi) · testlar (foiz) ·
   davomat doirasi
5. **Izohlar** — ro'yxat + yangi izoh maydoni; o'z izohini tahrirlash/o'chirish
   (`danger` tasdiq)

★ Rol bo'yicha: `finance` bloki `null` kelsa bo'lim **umuman render qilinmaydi**.
Admin bo'lmaganda pul kiritish/yechish tugmalari yo'q (talab: *"bunisi faqat
admin panelda"*).

### E4. Ro'yxatdan ochilishi

`ManageUsersPage`: qatorga bosilsa drawer (talab: *"har bir o'quvchi ustiga
bosilganda"*). Qator `role="button"`, `tabindex="0"`, Enter/Space bilan ham.
"Tahrirlash" va "Bloklash" ikonkali tugmalarga aylanadi va **hodisa
ko'tarilishi to'xtatiladi** (`@click.stop`) — aks holda tahrirlash bilan birga
drawer ham ochilardi.

### E5. ⚠️ Ochiq savol — "xarajatlari ... qaysi dars uchun"

To'lov modeli **oylik davr** (`BillingPeriod`) asosida: o'quvchi *oy* uchun
to'laydi, *dars* uchun emas. "Qaysi dars uchun to'lov qilgani" degan kesim
hozirgi modelda **yo'q**.

Uch o'qish mumkin:
1. **Oy + guruh** kesimi yetarli (`periodMonth`, `groupName`) — bu **bor**;
2. Bir oyda nechta dars bo'lganini ko'rsatish (`sessionCount`) — arzon,
   hisoblab beriladi;
3. Haqiqiy **dars-bahosi** (per-lesson billing) — bu **moliya modelini
   o'zgartirish**, alohida ish.

**Qaror:** 1 + 2 bajariladi (oy · guruh · o'sha oydagi darslar soni · summa).
3-variant kerak bo'lsa loyiha egasi aytadi — u alohida blok bo'ladi.
Ish 1+2 bilan **to'sib qolinmaydi**.

---

## 6. BLOK F — Foydalanuvchilar panelidagi filtrlar

Talab: mavjudlariga (qidiruv, rol, holat) **qo'shimcha** — guruh bo'yicha va
Telegram ulangan/ulanmagan bo'yicha.

**Backend** `GET /api/v1/users`:

| Parametr | Semantika |
|---|---|
| `groupId: long?` | shu guruhda **`Active`** a'zosi bo'lgan foydalanuvchilar. ★ `Stopped/Moved` KIRMAYDI — "guruh bo'yicha filtr" ro'yxatda chiqarilgan o'quvchilarni ko'rsatsa, xodim ularni hali o'qiyotgan deb o'ylaydi. Kerak bo'lsa keyin `memberStatus` parametri qo'shiladi |
| `telegramLinked: bool?` | `true` → `TelegramId != null` |

- Indeks: `group_members(group_id, status)` bor-yo'qligi tekshiriladi
- Testlar: har filtr alohida + birgalikda + bo'sh natija

**Frontend:** guruh uchun qidiruvli select (1500+ foydalanuvchi bor, guruhlar
ham ko'p — `GET /groups?search=` bilan), Telegram uchun uch holatli select.
Filtrlar `page = 1` ga qaytaradi (mavjud `watch` ga qo'shiladi).

---

## 7. BLOK G — Kurs quruvchi: dars drawer'i (eng katta backend ishi)

Talab: dars yaratish/tahrirlash **drawer**da (o'ng, 85%); ichida dars
nomi/tavsifi · **dars turi (odatiy | imtihon)** · **video yuklash (bir necha
qism)** · imtihonda video o'rniga **rasm** · **uy vazifasi biriktirish**
(shart va javob: text/rasm/audio, bir vaqtda bir nechtasi).

### G1. 🔴 Hozirgi holat: video umuman YO'Q

```csharp
public class ModuleLesson : BaseEntity {
    long ModuleId; string Name; string? Description; int Position; int? DurationMin;
}
```

Video maydoni ham, jadvali ham yo'q. Eski tizimda `lesson_videos` jadvali bor
edi va u **ko'chirilmaydiganlar ro'yxatida** (`MA_LUMOT_KOCHIRISH.md`) — ya'ni
bu funksiya v2 da hali qurilmagan, "tuzatish" emas, **yangi qurilish**.

### G2. Model qarori — bitta `LessonAsset` jadvali

Video va imtihon rasmi uchun ikki jadval EMAS:

```
LessonAsset
  id · lesson_id · kind (Video|Image) · position
  title?            -- "1-qism", "Nazariya" (video qismlari nomlanadi)
  object_key        -- MinIO kaliti (🔴 UI'ga CHIQMAYDI, DAVOM_ETTIRISH 16-tuzoq)
  content_type · size_bytes · duration_sec? · width? · height?
  created_at · created_by_id
```

Sabab: yuklash oqimi, ruxsat tekshiruvi, o'chirish, tartiblash — ikkalasida
**aynan bir xil**. Ikki jadval ikki controller, ikki servis, ikki test to'plami
demakdir va ular asta-sekin bir-biridan uzoqlashadi.

`ModuleLesson.Kind` (`LessonKind { Normal = 0, Exam = 1 }`) — enum, int.
🔴 **Invariant:** `Kind = Normal` → faqat `Video` asset; `Kind = Exam` → faqat
`Image`. Turni almashtirishda mavjud asset bo'lsa **409** (jimgina o'chirish
YO'Q — bir soatlik video jimgina yo'qolishi mumkin emas). Foydalanuvchiga
"avval N ta videoni o'chiring" deb aytiladi.

### G3. Yuklash oqimi — qaror

**Presigned PUT EMAS, API orqali oqim** — bu loyihaning mavjud qarori
(`PROGRESS.md`: *"Fayl o'qish — presigned URL EMAS, API orqali oqim"*).
Video uchun ham shunday: ruxsat har so'rovda tekshiriladi.

```
POST   /api/v1/lessons/{lessonId}/assets      multipart, [FromForm] IFormFile
GET    /api/v1/lessons/assets/{assetId}       oqim (Range so'rovini QO'LLAB-QUVVATLAYDI)
DELETE /api/v1/lessons/assets/{assetId}
PUT    /api/v1/lessons/{lessonId}/assets/reorder   { orderedIds }  (TO'LIQ ro'yxat)
```

🔴 **`Range` MAJBURIY:** `Range` bo'lmasa brauzer videoni oxiriga o'tolmaydi
(seek ishlamaydi) va har ko'rishda butun fayl boshidan oqadi. `206 Partial
Content` + `Accept-Ranges: bytes`.

- Ruxsat: yozish `Academic/Admin`; o'qish — **gating orqali** (o'quvchi
  qulflangan darsning videosini olmaydi; to'lov bloki `Video` bo'lsa ham yo'q).
  Bu tekshiruv mavjud `Gating` servisiga qo'shiladi.
- Chegaralar (`Settings` orqali boshqariladi, kod ichida qotmaydi):
  `lesson.video_max_mb` (standart 1024), `lesson.image_max_mb` (10)
- MIME ro'yxati: video `mp4`, `webm`, `quicktime`; rasm `jpeg`, `png`, `webp`.
  🔴 Kengaytmaga ISHONILMAYDI — magic bytes tekshiriladi (mavjud
  `SubmissionAttachmentReader` da naqsh bor, qayta ishlatiladi)
- Frontendda **progress** (`XMLHttpRequest.upload.onprogress` — `fetch` da
  progress yo'q) + bekor qilish + tarmoq uzilganda qayta urinish
- Katta faylni `IFormFile` bilan qabul qilish `RequestSizeLimit` va Kestrel
  `MaxRequestBodySize` ni oshirishni talab qiladi; nginx'da
  `client_max_body_size` (infra faylida) tekshiriladi

### G4. Uy vazifasi — biriktirmalar

Hozir: `Assignment.imageKey` — **bitta rasm**, `AllowedFormats` esa allaqachon
`[Flags] Text|Image|Audio` (javob formatlari **tayyor**).

Talab: **shart** ham text/rasm/audio bo'lishi mumkin va **bir nechta**.

```
AssignmentAttachment
  id · assignment_id · kind (Image|Audio|Document) · position
  object_key · content_type · size_bytes · duration_sec?
```

- `imageKey` **saqlanadi** (mavjud vazifalar buzilmasin) va migratsiyada
  `AssignmentAttachment` ga ko'chiriladi; DTO'da `imageKey` **deprecated**
  deb belgilanadi, UI faqat `attachments` bilan ishlaydi
- `POST /assignments/{id}/attachments`, `DELETE .../attachments/{id}` —
  G3 dagi oqimning aynan o'zi (umumiy `IAttachmentService` bilan)
- Javob formatlari UI'da **checkbox to'plami** (bir vaqtda bir nechtasi) →
  `"Text, Image, Audio"` satri. ★ Kamida bittasi tanlanishi shart (`None` →
  o'quvchi javob berolmaydi) — 400
- O'quvchi tomonida audio javob: `submissions` yuklash allaqachon
  `IFormFileCollection` qabul qiladi va `AttachmentKind.Audio` enumda bor —
  **tekshiriladi**, kerak bo'lsa MIME ro'yxatiga audio qo'shiladi

### G5. Frontend — `LessonEditDrawer`

`features/course-tree/ui/LessonFormDialog.vue` (173 satr, oddiy modal) →
`BaseDrawer` ichida bo'limlar:

1. **Dars ma'lumotlari** — nomi · tavsifi · davomiyligi (daq.)
2. **Dars turi** — segment tugma (`Odatiy` | `Imtihon`). Almashtirishda asset
   bo'lsa ogohlantirish (409 sababini ko'rsatadi)
3. **Video qismlari** (odatiy) / **Rasmlar** (imtihon):
   - ro'yxat: tartib · nomi · davomiyligi · hajmi · ko'rish · o'chirish
     (`danger` tasdiq)
   - tortib tartiblash (mavjud `CourseTreeEditor` da naqsh bor) → `reorder`
   - yuklash: bir necha fayl, har biri uchun progress qatori
4. **Uy vazifasi** — biriktirilganmi; yo'q bo'lsa "Vazifa qo'shish":
   sarlavha · shart matni · **shart biriktirmalari** (rasm/audio/fayl,
   bir nechta) · maksimal ball · muddat · **qabul qilinadigan javob
   formatlari** (checkbox: matn / rasm / audio)
   — mavjud `AssignmentFormDialog` mantig'i shu bo'limga ko'chiriladi
     (ikki nusxa saqlanmaydi)

Har bo'lim alohida saqlanadi (C2 dagi qoida bilan), har tugmada loader.

### G6. O'quvchi tomoni (parite)

Video darsni **bir necha qism** bilan ko'rish: `StudentLearnPage` /
`student-course` da qismlar ro'yxati, ketma-ket o'ynash, ko'rilganini belgilash
(`LessonProgress` bor). Imtihon darsida rasm(lar) galereyasi.
🔴 Gating va to'lov bloki tekshiruvi **serverda** (E2.4 dagidek) — UI'da
yashirish yetarli emas.

---

## 8. BLOK H — Tasdiqlash va loader auditi (butun platforma)

B2/B3 infratuzilmasi tayyor bo'lgach **to'liq o'tish**:

1. `grep -rn "useMutation" frontend/src` → har biri jadvalga tushadi:
   *fayl · amal · tasdiq kerakmi (ton) · tugmada `:loading` bormi*
2. Yetmagan joylarga qo'shiladi (B2 qoidasi bo'yicha)
3. `grep -rn "useQuery" ` → har biri `DataStatus` yoki `SectionLoader` bilan
   qoplanganmi
4. Natija jadvali `PROGRESS.md` ga yoziladi (keyingi sessiya uchun dalil)

★ Bu blokning qiymati — **bitta ham o'tkazib yuborilmasligi**. Ko'z bilan
"hammasi bordir" deb o'tish shu talabning aynan buzilishi bo'ladi.

---

## 9. BLOK J — Oldingi rejadan qolgan ishlar

`DAVOM_ETTIRISH.md` 3-bo'limi 2026-07-31 holatiga tegishli; keyin
`ed4a571` commit'i bilan uchta punkt yopilgan. **Haqiqiy qoldiq:**

| № | Ish | Izoh |
|---|---|---|
| 1 | Hub xato tarjimasi uchun **regressiya testi** | ishlaydi, lekin test yo'q |
| 2 | Parite bo'shlig'i **#5 xabarlar** (ustoz ↔ o'quvchi DM UI to'liqligi) | |
| 3 | Parite bo'shlig'i **#7 KPI** (o'quv bo'limi bosh sahifa ko'rsatkichlari) | |
| 4 | Parite bo'shlig'i **#8 profil modali** | ⚠️ **BLOK E bilan qoplanadi** — takroriy ish qilinmaydi |
| 5 | Parite bo'shlig'i **#9 tranzaksiya tarixi** | ⚠️ **BLOK E.2 bilan qoplanadi** |
| 6 | **Sozlamalar paneli halol cheklovi** — 27 sozlamadan 13 tasi runtime; qolganini `ISettingsResolver` ga o'tkazish | `PROGRESS.md` da retsept bor |
| 7 | 🔴 **FAZA 7 ko'chirish** — prod bazasi NUSXASIDA `--only=preflight`. Loyiha egasi qarori kerak: 18 jadval ko'chmaydi | eng katta xavf |
| 8 | **Deploy** — staging, prod cutover | |
| 9 | Ochiq biznes savoli: **boshlanmagan eski darslar** (avto-bekor qilish yoki "o'tkazilmadi" holati) | Domain o'zgarishi |

**Tartib qarori:** yangi talablar (A–H) **birinchi** — ular loyiha egasining
bugungi ustuvorligi va ko'rinadigan natija beradi. J bloki shundan keyin.
Istisno: **J.7 (ko'chirish) va J.8 (deploy)** — ular qaror va prod ma'lumotiga
bog'liq, ular haqida alohida gaplashiladi.

---

## 10. BAJARISH TARTIBI

```
A (dizayn)  ──► B (drawer, confirm, loader, ikonka)  ──┬──► C (guruh bo'limlari)
                                                        ├──► D (o'quvchilar tabi + ikonka)
                                                        ├──► E (profil drawer)   ← eng katta
                                                        ├──► F (filtrlar)
                                                        └──► G (dars drawer)     ← eng katta
                                                              │
                                                     H (tasdiq/loader auditi)
                                                              │
                                                        J (qoldiq rejalar)
```

**Nima uchun shu tartib:**
- A birinchi — keyin yozilgan har komponent darhol to'g'ri rangda bo'ladi
- B ikkinchi — C, D, E, G **hammasi** drawer + confirm + loader ustiga qurilади
- D va F kichik (yarim kunlik) — ular E/G orasida "tez g'alaba" beradi
- H oxirida — audit qilish uchun avval hamma yangi mutation yozilgan bo'lishi kerak

**Har blok oxirida (o'zgarmas qoida):**

1. `dotnet build` (backend tegilgan bo'lsa) — `--no-incremental`
2. `dotnet test` — **1034 test yashil** bo'lib qolishi shart, yangi ish yangi
   test qo'shadi
3. `npm run typecheck` + `eslint --max-warnings 0`
4. 🔴 **Образ yangilash** — `docker compose build api web && up -d`
   (`DAVOM_ETTIRISH.md` ogohlantirishi: eski образ 404 qaytarib vaqt yegan)
5. Brauzerda haqiqiy tekshirish (ekran surati bilan)
6. `PROGRESS.md` ga yozuv + shu hujjatdagi belgini yangilash
7. **Bitta mantiqiy commit** (migratsiya bo'lsa — bittasi, zanjir buzilmasin)

---

## 11. XAVFLAR VA QAROR TALAB QILADIGAN JOYLAR

| # | Masala | Holati |
|---|---|---|
| 1 | Rang almashishi eski foydalanuvchilarni chalg'itishi (0.1) | Layout/matn/tartib saqlanadi — xavf past. Qaror loyiha egasining |
| 2 | Qorong'i tema kerakmi | **Hozir yo'q**, tokenlar tayyor (A6) |
| 3 | "Qaysi dars uchun to'lov" (E5) | Oy + guruh + darslar soni bilan qoplanadi; per-lesson billing — alohida ish |
| 4 | Katta video yuklash (1 GB) infra chegaralari | nginx `client_max_body_size`, Kestrel limiti, MinIO diski — G3 da tekshiriladi |
| 5 | O'quvchilar tabi tartibi pariteti buzilishi (D1) | ATAYLAB, talab bo'yicha; ustozda tegilmaydi |
| 6 | `PUT` to'liq almashtirish + parallel tahrirlash (C2.3) | `updatedAt` bilan optimistik qulf |
| 7 | Ustoz o'quvchining moliyasini ko'rmasligi (E2.4) | Javobda `finance: null` — serverda kesiladi |
| 8 | FAZA 7 ko'chirish (J.7) | **Loyiha egasi qarori kutiladi** |

---

## 12. HOLAT JADVALI (ish davomida yangilanadi)

| Blok | Ish | Holat |
|---|---|---|
| A | iOS yorug' dizayn tizimi | ✅ **tugadi** (2026-08-11). `typecheck`/`lint` toza, kontrast auditi **100/100**. Aksent `#4f4de8`. Qoldiq rang xatolari alohida agentda |
| B | Drawer · confirm · loader · IconButton | ✅ **tugadi**. Brauzerda tasdiqlangan (85vw/92vw/to'liq ekran, qatlam steki, sanoqli skroll qulfi) |
| C | Guruh bo'limlari + `VideoStartLessonId` | 🔄 **backend tugadi** (`wave1/group-video-start`, `89720a4`, build 0/0, 1078 test). **UI qolgan** |
| D | O'quvchilar tabi birinchi + ikonkali amallar | ⬜ |
| E | O'quvchi profili drawer (backend + UI) | ⬜ |
| F | Foydalanuvchi filtrlari (guruh, Telegram) | ⬜ |
| G | Dars drawer: tur · video qismlari · vazifa | ⬜ |
| H | Tasdiqlash va loader auditi | ⬜ |
| J | Oldingi rejadan qolganlar | ⬜ |

---

## 13. ISH DAVOMIDA TOPILGAN TUZOQLAR (yangi)

`DAVOM_ETTIRISH.md` 6-bo'limiga qo'shilishi kerak — bu yerda yig'iladi.

### 13.1. BLOK B dan (2026-08-11, brauzerda tasdiqlangan)

18. 🔴 **`body{overflow:hidden}` foydalanuvchi skrollini to'sadi, `window.scrollTo` ni EMAS.**
    Chrome `hidden` konteynerni programma orqali baribir skroll qiladi. Ya'ni
    "skroll qulfi ishlayaptimi" degan testni **g'ildirak hodisasi** bilan
    o'lchash kerak; `scrollTo` bilan o'lchagan test YOLG'ON "buzuq" natija
    beradi. (Bir marta chalg'itgan.)
19. **`strictTemplates: true` + komponentga `data-*` yoki `id`** → tur xatosi.
    E2E selektor kerak bo'lsa **`class` ilgagi** ishlatiladi (masalan
    `js-modal-autofocus`) yoki `defineProps` ga aniq prop qo'shiladi.
20. **Vite 6 dev serveri `host.docker.internal` Host sarlavhasini bloklaydi**
    ("Blocked request") — konteynerdan tekshirganda IP manzil bilan murojaat
    qilinadi (`server.allowedHosts` sozlanmagan).
21. **Puppeteer'da `console.warn` turi `'warn'`**, `'warning'` emas — filtr
    shu sababli bo'sh natija beradi.
22. **`ConfirmDialog` `BaseModal` ustiga qurilMAYDI:** `BaseModal` ESC ni o'zi
    ushlaydi, drawer ustida ochilganda ESC **ikki qatlamni** yopardi. Qatlam
    steki `useModalHost` da.
23. **Enter va "fokus Bekor qilishda" bir vaqtda bo'lmaydi** (fokus tugmada
    bo'lsa Enter O'SHA tugmani bosadi). Qabul qilingan yechim: `danger`/
    `warning` → fokus **Bekor**da, `primary` → fokus **Tasdiq**da; panel
    darajasidagi Enter faqat fokus tugmada bo'lmaganda tasdiqlaydi.
24. **`IconButton` qatorda `gap-3` (12px) dan kichik oraliq bilan
    qo'yilmasin** — `tap-expand` maydonlari ustma-ust tushib qo'shni tugma
    bosiladi.

### 13.2. BLOK C dan (2026-08-11)

25. 🔴 **Git worktree KUZATILMAGAN faylni ko'rmaydi.** Bu hujjatning o'zi
    `main` da `??` holatida edi, shuning uchun worktree'dagi uchala backend
    agenti uni **o'qiy olmadi** va faqat brifdan ishladi. Yangi worktree
    agentiga topshiriq berayotganda **asosiy daraxtdagi absolyut yo'lni**
    bering (`~/Documents/Projects/zinnur-v2/docs/...`) yoki hujjatni oldin
    commit qiling.
26. 🔴 **Gating'da IKKI yo'l bor va ular ajralib ketishi mumkin:** arzon yo'l
    (`GetLessonGateAsync`, bitta dars) va daraxt yo'li (`CourseGate`). Arzon
    yo'lda "shartni ko'rib darhol qaytish" optimizatsiyasi
    `UnlockedOverride` ni chetlab o'tgan edi — o'quv bo'limi qo'lda ochgan
    dars ro'yxatda OCHIQ, bosilganda **403** bo'lardi. Qoida BITTA joyda
    (`LessonGate.Evaluate`) turishi shart. Mosligi endi test bilan qulflangan
    (`LessonGate_CheapPathAgreesWithTheTreeForEveryLesson`) — bunday test
    ilgari umuman yo'q edi.
27. ⚠️ **`CourseGate` keshi yangiligini tor tekshiradi:** faqat `CourseId` va
    `TaughtLessonCount`. Ya'ni guruhning `IsActive` yoki boshqa sozlamasi
    o'zgarsa o'quvchi ~60 sekund **eski qulflar** bilan qoladi.
    `VideoStartLessonId` qo'shildi, qolgani ochiq muammo.
28. ⚠️ **`DAVOM_ETTIRISH.md` dagi test raqamlari eskirgan:** "1034" deb
    yozilgan, `main` da amalda **1055** (621 unit + 434 integratsiya).
29. **`GroupWriteRequest` degan tur backendda YO'Q** — `CreateGroupRequest`
    va `UpdateGroupRequest` alohida. Frontend `shared/types/api.ts` da
    ikkalasiga ham maydon qo'shilishi kerak.
30. **Backend'da kurs progressi endpointi yo'q** — foizni frontend kurs
    daraxtidan yig'adi. Shuning uchun 🔴 **`lockReason === "BeforeGroupStart"`
    bo'lgan darslar progress MAXRAJIDAN chiqarilishi frontend
    javobgarligida**; aks holda progress abadiy pastda qotib qoladi.
    (`CourseGateDto.StartIndex` ataylab qo'shilgan — progress kelajakda
    backend'ga ko'chirilsa maxrajni u beradi.)

### 13.3. BLOK E+F dan (2026-08-11)

31. 🔴 **`GroupMember` ko'chirish tarixini SAQLAMAYDI.** `MoveMemberAsync`
    manba a'zolikni `Moved` qiladi, nishonda yangi a'zolik yaratadi, lekin
    **havola qoldirmaydi**; "qachon chiqdi" ustuni ham yo'q. Ya'ni loyiha
    egasining *"qaysi guruhdan ko'chirilgan"* talabi to'liq bajarilmaydi va
    ma'lumot **hozir har ko'chirishda izsiz yo'qolmoqda**. Kerak:
    `MovedToGroupId` + `LeftAt`. Vaqt bo'yicha taxmin qilish ATAYLAB rad
    etildi (paketli ko'chirishda boshqa guruhni ko'rsatib chalg'itardi).
32. 🔴 **ASP.NET rol atributlari VA bilan birlashadi.** `UsersController` da
    sinf darajasida `[Authorize(Roles="Academic,Admin")]` turgan edi — bunda
    ustoz yoki o'quvchiga ochiq endpoint qo'shishning **iloji yo'q**. Darvoza
    endpoint darajasiga ko'chirildi (naqsh `PaymentsController` dan).
33. **`PaymentTransaction` da davr havolasi YO'Q** — bitta to'lov bir necha
    oyga taqsimlanadi (kvitansiyada `affectedMonths` alohida ro'yxat).
    Shuning uchun tranzaksiya qatorida `periodMonth` bo'lmaydi.
34. **Testda "nechta to'g'ri javob" degan son YO'Q** — har savolning o'z
    `Points` i bor. `score`/`maxScore`/`scorePercent` beriladi; har savol
    1 ball bo'lganda `score` aynan to'g'ri javoblar soni.
35. **Telegram username ISHONCHSIZ identifikator** — bo'shatilgan nom boshqa
    odamga o'tadi. Shu sababli unikal indeks ATAYLAB qo'yilmadi va nom har
    muloqotda qayta yoziladi. UI unga havola ko'rsatishdan boshqa maqsadda
    ishonmasin.
36. ⚠️ **Test sonini faqat TOZA yurishdan oling.** Uzilib qolgan yurishdan
    qolgan ulanishlar `DROP DATABASE` ni to'sib, 14 ta "Test Class Cleanup
    Failure" soxta yozuv qo'shgan edi.
37. **`docs/MIGRATIONS.md` buzuq:** `dotnet ef migrations remove` bazaga
    ulanishni talab qiladi, hujjatdagi buyruq ulanish satrini bermaydi
    (`ConnectionStrings__Postgres` env ham `appsettings` ni bosib o'tmaydi).
    Amaldagi yechim: migratsiya fayllarini qo'lda o'chirib,
    `ApplicationDbContextModelSnapshot.cs` ni `git checkout` bilan tiklash.
38. 🔴 **`Application/Common/Interfaces/IApplicationDbContext.cs` ham UMUMIY
    fayl** — dastlabki taqsimotda ko'rsatilmagan edi. Har yangi entity uni
    ham o'zgartiradi, ya'ni integrator merge qilishi kerak.

### 13.4. BLOK G dan (2026-08-11)

39. 🔴 **`<video src>` `Authorization` sarlavhasini YUBORMAYDI.** Oqim
    endpointi tokenni talab qiladi, ya'ni pleyer to'g'ridan-to'g'ri ishlamaydi.
    `Blob` yechimi (vazifa fayllarida ishlaydigan) 1 GB video uchun **yaramaydi**
    — butun fayl xotiraga tushadi va `Range` ma'nosi yo'qoladi.
    **Qabul qilingan qaror:** `(assetId, userId, exp)` ga bog'langan qisqa
    muddatli token query parametrida; `Authorization` yo'li xodim/API uchun
    qoladi.
40. 🔴 **nginx yuklashni to'sadi:** `client_max_body_size 10m` (server
    darajasida), `/api/` da `proxy_read_timeout 60s` va `proxy_buffering on`,
    `proxy_request_buffering` standart `on`. Ya'ni 1 GB video **API'ga yetib
    bormaydi**. ★ Server darajasidagi 10m **qoladi** — butun API'ni 2 GB ga
    ochish xavfsizlik regressiyasi bo'lardi; chegara faqat yuklash
    `location` ida oshiriladi.
41. 🔴 **Presigned vs proxy — kod bazasida TESKARI qaror bor.**
    `IRecordingStorage` izohida dars yozuvi uchun presigned tanlangan, sababi
    *"trafik LiveKit SFU bilan bitta kanalni bo'lishadi"* va *"proxy `Range` ni
    qo'llamaydi"*. Ikkinchi e'tiroz endi yopildi (`Range` bor), lekin **tarmoq
    narxi e'tirozi qolmoqda**. Hozircha proxy saqlanadi (ruxsat har so'rovda
    tekshiriladi va bekor qilinadi); kanal to'yinganda **CDN yoki alohida
    domen**, presigned emas.
42. ⚠️ **ASP.NET multipart faylni model bog'lashda, ilova kodidan OLDIN
    diskka buferlaydi.** Ya'ni sozlama 100 MB bo'lsa ham 2 GB so'rov avval
    diskka tushib, keyin 413 oladi. Aniq nuqtada to'xtatish uchun
    `IAsyncResourceFilter` kerak. Endpoint faqat `Academic`/`Admin` ga ochiq,
    shuning uchun tahdid past.
43. 🔴 **`DomainException` middleware'da 409 ga xaritalanadi** — ya'ni Domain
    ichidagi **maydon validatsiyasi** "holat ziddiyati" bo'lib chiqadi va
    forma xatoni to'g'ri katakcha ostida ko'rsata olmaydi
    (`AllowedFormats = None` aynan shunday edi). Maydon xatolari servisda
    aniq **400 + `problem.errors[maydon]`** bilan berilishi kerak; Domain
    qo'riqlashi ikkinchi qatlam sifatida qoladi.
44. 🔴 **`RuntimeSettings.StopAsync` suzuvchi nosozlik manbai edi** (tuzatildi):
    yopilgan `CancellationTokenSource` ustida `CancelAsync()` →
    `ObjectDisposedException` → **butun test sinfi "Cleanup Failure"** bilan
    qizil, testlar o'tgan bo'lsa ham. 36-tuzoqdagi 14 soxta yozuvning ildiz
    sababi shu.
45. **O'zbekcha apostrof tuzog'i testlarda:** `JsonSerializer` apostrofni
    `'` ga o'giradi, shuning uchun xom javob matni ustidagi
    `Contain("to'liq emas")` **har doim** yiqiladi — endpoint to'g'ri ishlasa
    ham. `ProblemText.ReadAsync` yordamchisi qo'shildi (JSON'ni tahlil qiladi).
46. **MP4 konteyneri shart biriktirmasida AUDIO deb qabul qilinadi** —
    `ftyp` da audio va video ajratilmaydi. `Audio` ustun qo'yilgan, chunki
    iOS Safari ovoz yozuvini video brendi bilan beradi; teskarisi o'quvchining
    ovozli javobini jimgina rad etardi.
47. **`durationSec`/`width`/`height` KLIENTDAN keladi** (serverda media
    dekoder yo'q) — faqat ko'rsatish uchun, ularga hech qanday qaror
    bog'lanmaydi.
48. **`reorder` konvensiyasi:** loyihada mavjud uchta reorder `POST .../reorder`.
    Yangi media reorder ham **`POST`** ga keltirildi (integrator).

### 13.5. Yorug' tema qoldiqlari + `BaseModal` refaktoridan (2026-08-11)

49. 🔴 **Tailwind v4 `@theme` dagi ISHLATILMAGAN o'zgaruvchini chiqishdan
    OLIB TASHLAYDI.** Tayyor CSS'da tekshirildi: `--color-teal-700`,
    `--color-orange-700`, `--color-brand-800` UMUMAN yo'q (ularga mos utility
    klass hech qayerda ishlatilmagan). Ta'siri: inline `style` ichida
    `var(--color-...)` bilan o'qiladigan token JIMGINA yo'qolib, element
    rangsiz qolishi mumkin. Qoida: bunday tokenlarni `@theme` dan TASHQARIDA
    (`:root` da) e'lon qiling **va** har `var()` ga zaxira qiymat bering —
    `var(--color-rose-100, #55160f)`. Moliya diagrammasining
    `--color-chart-*` bloki aynan shu sababli `:root` da.
50. **`strictTemplates` `data-*` ni ODDIY HTML elementida ham rad etadi**
    (19-tuzoq faqat komponentlar haqida deb yozilgan edi): `<p data-qa="x">`
    → `TS2353: 'data-qa' does not exist in type 'HTMLAttributes &
    ReservedProps'`. Ya'ni E2E ilgagi HAR YERDA `class` bo'ladi.
51. **Tailwind v4 shaffoflik modifikatori (`bg-X/70`) `color-mix(in oklab,
    …)` ga aylanadi**, ya'ni `getComputedStyle(...).backgroundColor`
    `oklab(…)` qaytaradi, `rgba(…)` EMAS — brauzer testidagi
    `bg.startsWith('rgba(')` tekshiruvi shu sababli YOLG'ON yiqiladi.
    ★ `contrast-audit.mjs` shaffoflikni sRGB'da qo'shadi; farq o'lchandi va
    AHAMIYATSIZ: `slate-900/70` uchun brauzer `rgb(83,90,102)`, skript
    `rgb(84,90,103)` (6.95 vs 6.92:1). Ya'ni auditning modeli yaroqli.
52. **Konteynerdagi Chrome'da `localhost` — KONTEYNERNING O'ZI.** Puppeteer
    bilan tekshirganda Chrome'ga `--host-resolver-rules=MAP localhost
    host.docker.internal` beriladi: shunda sahifa manzili
    `http://localhost:5173` bo'lib qoladi (API CORS ro'yxatida AYNAN shu
    origin bor) va API ham topiladi. Aks holda hamma so'rov
    `ERR_CONNECTION_REFUSED` bo'lib, "sahifa bo'sh" degan yolg'on xulosa
    chiqadi.
53. 🔴 **`<Transition>` + `prefers-reduced-motion` da `animation: none`
    YOZILMAYDI.** Vue chiqish o'tishini `animationend`/`transitionend`
    hodisasi bilan tugatadi; animatsiya butunlay o'chirilsa hodisa kelmaydi
    va element DOM'da MUZLAB qoladi (panel yopilmaydi). Davomiylikni
    `0.01ms` ga tushirish kerak.
54. **Programmatik `element.click()` FOKUS BERMAYDI.** "Yopilgach fokus
    chaqirgan tugmaga qaytdimi" testini `page.evaluate(el => el.click())`
    bilan o'lchash yolg'on "buzuq" natija beradi (fokus `body` da bo'lgani
    uchun u yerga qaytadi) — haqiqiy `page.click(selector)` kerak. Aksincha,
    oyna OCHIQ turganda haqiqiy bosish qoraytiruvchi qatlamga tushib oynani
    yopadi — u yerda programmatik `click()` kerak.
55. ⚠️ **Teskari neytral shkalada `750` — `700` dan YORUG'ROQ**
    (`ink-750` #e9ecf5 vs `ink-700` #dfe3ee). Ya'ni "ko'rinmayapti,
    750 ga o'tkazamiz" degan tuzatish ba'zi joyda kontrastni PASAYTIRADI
    (`NotFoundPage` dagi "404" aynan shunday bo'lardi: 1.19 → 1.09).
    Har almashtirishdan keyin auditni yurgizing.
