# Moslashuvchan dizayn (desktop · planshet · telefon)

> **Sana:** 2026-08-13 · **Holat:** 0–5 bosqichlar bajarildi, brauzer QA'si qoldi
>
> Talab (loyiha egasi): *"student panel and all other panels too should be
> fully responsive that could work in desktop mobile, ipad versions too"*.

Bu hujjat IKKI narsani qayd etadi: (1) endi kuchga kirgan chegara tizimi va
(2) eski ilova bilan dizayn pariteti shartnomasi talab qilgan
**chekinishlar ro'yxati**. Shartnomaning 4-bandi: chekinish
o'zboshimchalik bilan qilinmaydi — sababi bilan yoziladi, qarorni loyiha
egasi qabul qiladi.

---

## 1. AUDIT NIMA TOPDI

Ilova "faqat desktop uchun yozilgan" EMAS edi. Unda o'ylab qilingan mobil
qatlam bor: pastdan chiquvchi varaqalar, gamburger menyu, jadval→kartochka
almashuvi, `dvh`, xavfsiz maydon, 52 joyda `tap-target`. Muammo boshqacha va
aniqroq edi:

> **Tizim amalda IKKI bosqichli edi — `<640px` va `≥640px`. Planshet bosqichi
> YO'Q edi, katta monitor uchun kenglik chegarasi yo'q edi, JS tomonda
> ekran o'lchamidan xabardorlik umuman yo'q edi.**

| Chegara | Ishlatilishi | Ulushi |
|---|---|---|
| `sm:` (640px) | 150 | 71% |
| `md:` (768px) | 34 | 16% |
| `lg:` (1024px) | 31 | 15% |
| `xl:` (1280px) | 7 | 3% |
| `2xl:` (1536px) | **0** | 0% |

158 ta `.vue` faylning **95 tasida** birorta ham moslashuvchan variant yo'q edi.
`src/style.css` da `@media` ham, `--breakpoint-*` ham UMUMAN e'lon qilinmagan
edi — bosqichlar Tailwind sukutidan tasodifan kelardi.

---

## 2. ENDI QANDAY: TO'RT BOSQICHLI TIZIM

Qiymatlar `src/style.css` `@theme` blokida ochiq yozilgan. **Tailwind sukut
qiymatlari O'ZGARTIRILMADI** — 222 ta mavjud ishlatilish aynan shularga
tayanadi, birortasini surish butun ilovada jimgina siljish berardi.

| Token | Qiymat | Ma'nosi |
|---|---|---|
| `xs` | 560px | Katta telefon. Ilgari `min-[560px]:` deb 4 joyda qo'lda yozilgan edi |
| `sm` | 640px | Modal varaqadan dialogga o'tadi |
| `md` | 768px | iPad TIK holati |
| `lg` | **1024px** | **Yon menyu VA jadval — IKKALASI shu yerda** |
| `xl` | 1280px | Uch ustunli setkalar |
| `2xl` | 1536px | `AppShell` kontent chegarasi |

### 🔴 Asosiy strukturaviy qaror: `md` emas, `lg`

Ilgari jadvallar `md:` (768px) da desktop ko'rinishiga o'tardi, yon menyu esa
`lg:` (1024px) da ochilardi. Natijada **iPad tik holatida (aynan 768px)**
foydalanuvchi to'liq desktop jadvalini yon menyusiz, gamburger tugmali
joylashuvda ko'rardi — 10 ustunli jadval o'zi uchun mo'ljallanmagan karkasda.

Qaror (loyiha egasi): **jadvallar `lg:` ga ko'chirildi**, ikkala o'tish bitta
nuqtada. iPad tik holati endi kartochka + gamburger.

### Yangi primitiv: `useBreakpoint()`

`src/shared/lib/useBreakpoint.ts`. Auditgacha ilovada `matchMedia`,
`ResizeObserver`, `window.innerWidth`, `orientationchange` UMUMAN yo'q edi.

`hidden lg:block` naqshi IKKALA daraxtni ham quradi — telefon desktop
jadvalini mount qilib, ma'lumot bilan to'ldirib, keyin yashirardi. 12 ta
sahifada shunday edi. Endi ular `v-if` bilan HAQIQIY tarmoqlanadi.

★ **Qoida:** `useBreakpoint()` — tarmoqlanish XULQIY bo'lganda (`v-if`, boshqa
komponent). Sof VIZUAL chegara uchun CSS ishlatiladi. Karkasda ikkalasi ham
ishlatilsa, foydalanuvchi brauzer shriftini kattalashtirganda JS (`1024px`) va
CSS (`64rem`) bir-biridan uzilib, yarim-desktop holat paydo bo'lardi.

---

## 3. TUZATILGAN HAQIQIY XATOLAR

| # | Xato | Joy | Ta'siri |
|---|---|---|---|
| 1 | `.zn-input` shrifti 14px | `style.css` | iOS Safari 16px dan kichik maydonga fokusda sahifani O'ZI yaqinlashtiradi va foydalanuvchi gorizontal siljigan holda QOLIB KETADI. **Ilovadagi HAR BIR formada** (113 ta maydon) |
| 2 | `h-[calc(100vh-420px)]` | `GroupChatRoom.vue:45` | **Sukut qiymati** edi — ya'ni HAR BIR ustoz guruh chatida yozish paneli iOS'da ekrandan chiqib ketardi |
| 3 | `minmax(340px,1fr)` | `FinanceDashboard.vue` | 320–375px qurilmada butun SAHIFA gorizontal skrollga tushardi |
| 4 | `flex shrink-0 flex-wrap` | `PageHeader.vue:24` | `shrink-0` konteynerni `max-content` da qotiradi → yonidagi `flex-wrap` hech qachon ishlamasdi. **21 ta ekranga** ta'sir qilardi |
| 5 | `bottom-24` toast | `StudentToast.vue` | Tab paneli 62px + xavfsiz maydon ≈ 96px — toast panel OSTIGA tushib ko'rinmasdi |
| 6 | `sm:grid-cols-2 xl:grid-cols-3` | `StudentTestsPage.vue` | 520px lik ustun ichida VIEWPORT so'rovi — desktopda kartochkalar ~236px, `xl` da ~150px ga qisilardi |
| 7 | `md:hidden` qaytish tugmasi | `InboxThread.vue:129` | 768–1023px da ro'yxat ham, qaytish tugmasi ham yashirinib, foydalanuvchi yozishmadan CHIQA OLMASDI |
| 8 | Davomat avto-skroll kuzatuvchisi | `AttendanceTab.vue` | `v-if` ga o'tgach planshet burilganda ustoz ENG ESKI darsga tushib qolardi |
| 9 | 6 ta `vh` (`dvh` o'rniga) | 6 fayl | Loyihaning O'Z qoidasini buzardi (`style.css:874`) |
| 10 | Xavfsiz maydonsiz `fixed` elementlar | Jonli dars, yozuv toasti | iPhone "uy" chizig'i ostida qolardi |

---

## 4. CHEKINISHLAR RO'YXATI (parite shartnomasi, 8-bo'lim 4-band)

Hech bir MATN, menyu tartibi yoki tab tartibi o'zgartirilmadi. Quyidagilar —
faqat VIZUAL chekinishlar.

### 4.1. Loyiha egasi ATAYLAB tasdiqlagan (2026-08-13)

| # | Chekinish | Izoh |
|---|---|---|
| A | **O'quvchi paneli desktopda yon menyuli to'liq kenglikka o'tdi** | `navigation.ts:22-31` dagi "5 tab muzlatilgan" qarori bilan ziddiyatda — LEKIN faqat `≥1024px` da. Telegram Mini App doim tor oynada ochiladi (Telegram Desktop ham), shuning uchun **Mini App va telefon YO'LI TEGILMAGAN**: qo'shilgan har bir qoida `lg:` prefiksi ostida |
| B | **Jadvallar `md:` → `lg:`** | iPad tik holati endi desktop jadval emas, kartochka ko'rsatadi |

### 4.2. Xato tuzatishning bevosita natijasi

| # | Chekinish | Sabab |
|---|---|---|
| C | Testlar setkasi 520px karkasda endi 1 ustun (ilgari ≥640px viewport'da 2, ≥1280px da 3) | Aynan shu — tuzatilayotgan xato. Panel to'liq kenglikka chiqqanda 2/3 ustun O'ZI qaytadi (`@container`) |
| D | `PageHeader` amallar bloki endi tor ekranda o'raladi | Ilgari o'ralmay sahifani buzardi |

### 4.3. Kichik vizual siljishlar

| # | Chekinish | Qamrov |
|---|---|---|
| E | `@sm:p-4` `sm:p-4` dan 2px oldinroq ishlaydi | 416–640px viewport |
| F | Shaxsiy chat yozish paneli endi ustun pastiga qadaldi | Guruh chati allaqachon shunday edi — izchillik |
| G | Kalendar kataklari bir pog'ona ixchamroq | Faqat ≤360px |
| H | `MediaControlBar` oraliqlari 8→6px, 12→8px | Overflow tuzatish uchun zarur; tugma o'lchami 44px QOLDI |
| I | `AppShell` kontenti 96rem da markazlashdi | Ilgari 2560px monitorda matn butun ekran bo'ylab cho'zilardi |
| J | O'quvchi desktop konteti 960px da cheklandi | 15px matn 2560px da 300+ belgi/qator bo'lardi |

### 4.4. 🔴 Eng katta erkinlik — tasdiqingiz kerak

| # | Chekinish | Qaytarish |
|---|---|---|
| K | **Jonli darsda telefon YOTIQ holatida boshqaruv paneli video USTIDA suzadi** (Zoom/Meet naqshi) | ~60px yutadi. Rozi bo'lmasangiz: `LiveRoomPage.vue:663-667` dagi `absolute` shoxini olib tashlash kifoya |

### 4.5. Mobil ko'rinish YANGIDAN o'ylab topilgan joylar

Bu beshtasida ilgari mobil variant UMUMAN yo'q edi (faqat gorizontal skroll):

| Ekran | Yechim | Nega shunday |
|---|---|---|
| `AttendanceTab` | Dars tanlanadi → o'sha darsning varaqasi | Ustunlar CHEKSIZ (10 → 69), kataklar interaktiv. Ustozning telefondagi vazifasi — "12-sida kim kelmadi". **Yo'qotilgani:** butun oyni bir qarashda ko'rish (1400px skroller ham bunga javob bermasdi) |
| `GradesTab` | O'quvchi kartochkasi + o'ralgan chiplar | `MAX_COLUMNS = 8` — CHEKLANGAN, qator 2–3 satrga sig'adi. Tab faqat O'QISH uchun. **O'rtacha/Soni — o'quvchi bo'yicha**, vazifa o'qiga burilsa jadvalning eng qimmatli ikki ustuni yo'qolardi |
| `BoardTab` | Standart kartochka ro'yxati | 6 ustun, oddiy holat |
| `StudentAccountDialog` (×2) | Kartochka ro'yxati | Modal ichida |
| `LessonsTab` | 7 ustun QOLDI + `min-w-[520px]` skroller ichida | Bu HAQIQIY oy kalendari: ustunlar ma'no tashiydi ("har seshanba dars bor" — ustun bo'ylab o'qiladi). Reflow qilinsa qatorlar HAFTA bo'lishdan to'xtardi |

### 4.6. Chat pariteti (R28, 2026-08-13, 2-to'lqin)

Talab: *"teacher chat qismi student chat qismi qoidalari bilan bir xil
bo'lsin"* — ya'ni o'quvchi chati ETALON, ustoz ekranlari unga keltirildi.
Quyidagilar — shundan kelib chiqqan vizual chekinishlar (matn tartibi va
menyular tegilmadi):

| # | Chekinish | Qamrov | Nega |
|---|---|---|---|
| L | Ustoz "Chatlar" hubi desktopda IKKI USTUN: ro'yxat suhbat ochilganda ham chapda qoladi | FAQAT `≥1024px` | Ilgari ro'yxat `v-if` bilan DOM'dan chiqardi — 1600px ekranning chap yarmi bo'shab qolardi, har almashuv "Orqaga" bosishni talab qilardi |
| M | O'sha hubda bo'sh o'ng ustun uchun YANGI matn: *"Suhbat tanlanmagan"* | FAQAT `≥1024px` | Bu holat telefonda MAVJUD EMAS (u yerda ro'yxatning o'zi ko'rinadi), ya'ni Mini App matni o'zgarmadi |
| N | "Savollar" ro'yxati ustuni 320px → **340px** | `≥1024px` | Uchala chat ekranida bitta kenglik (`6.3` dagi setka) |
| O | "Savollar" da tanlangan qator endi brend chegara + fon (ilgari `bg-ink-800`) va `aria-current` bilan | `≥1024px` | Hover fonidan deyarli farq qilmasdi; "hozir ochiq" belgisi ranggagina tayanmasligi kerak |
| P | Shaxsiy chatlarda KUN AJRATGICHI paydo bo'ldi (o'quvchi tomonida ham) | Hamma kenglik | Guruh chatida u ALLAQACHON bor edi — bitta foydalanuvchi ikki ekranda ikki xil qoidani ko'rardi |
| Q | Yuborish xatosi qizil bir qator matn o'rniga YOPILADIGAN sariq ogohlantirish (`ChatNotice`) | Hamma kenglik | Guruh chatidagi bilan bitta komponent; yozilgan matn maydonda qolgani uchun ogohlantirish — ma'lumot, harakat emas |
| R | "Savollar" yozish tugmasidagi "Yuborish" yozuvi olib tashlandi (faqat ikonka + `aria-label`) | `≥640px` | Boshqa ikki chatda tugma allaqachon ikonkali |
| S | Reytingda davr `2026-08` o'rniga `avgust 2026` (`periodLabel`) | Hamma kenglik | R12 arxiv tanlagichi bilan bitta yorliq; ilovadagi boshqa hamma joyda davr shu ko'rinishda |

★ QOLDI (ataylab): "Savollar" ekrani hamon KARTOCHKA ichida
(`h-[60dvh]` + `max-h-[74dvh]`), ya'ni ekranni to'ldiruvchi ustun
(`ChatFillColumn`) naqshiga o'tkazilmadi — bu filtrlar, bo'limlar va
kurator ko'rsatkichlari bilan birga qayta o'lchashni talab qiladi.

---

## 5. QABUL MEZONI — QOLGAN ISH

- [x] `npm run typecheck` toza (butun repo)
- [x] `npm run lint --max-warnings 0` toza (butun repo)
- [x] Ishlab chiqarish build'i (`docker compose build web`) — o'tdi, konteyner
      `healthy`, `/` va `/api/health` 200 qaytaradi
- [x] Yig'ilgan CSS tekshirildi (`index-*.css`, 83 KB): oltita chegara
      (`35/40/48/64/80rem` + bir martalik `820px`), to'rtta konteyner so'rovi
      (`18/24/36/56rem`), `@media(pointer:coarse){.zn-input{font-size:16px}}`,
      `prefers-reduced-motion` ×2, `hover:hover` ×4, `max-md:` tablet shoxi.
      ★ **`dvh` 28 ta, `vh` 0 ta** — sweep to'liq.
      ★ Lightning CSS `width>=` sintaksisini `min-width` ga tushirgan — ya'ni
      brauzer qamrovi manbadagidan KENGROQ (Safari 16.4 dan pastda ham ishlaydi).
- [ ] Brauzerda tekshirish: **320 · 375 · 390 · 768 · 820 · 1024 · 1280 · 2560**,
      tik va yotiq, uchala rolda (kirish 2026-08-13 dan telefon + Telegram
      kodi bilan: admin `+998900000001`, ustoz `+998900000002`,
      o'quvchi `+998900000003`)
- [ ] Telegram Mini App yo'li telefonda o'zgarmaganini tasdiqlash
- [ ] Kontrast auditi (`scripts/contrast-audit.mjs`) — **rang tokenlariga
      tegilmagani uchun kutilmoqda, lekin yurgizilsin**

---

## 6. O'QUVCHI PANELI — DESKTOP JOYLASHUV TILI (2026-08-13, 2-iteratsiya)

Loyiha egasi birinchi desktop urinishini RAD ETDI:

> *"desktop variantida menudan tashqari contentlar to'liq ekran va kenglik
> bo'yicha moslangan holda joylashmayapti, to'liq kenglik bo'yicha professional
> bo'lishi kerak yani shunchaki centerga tartiblab qo'ymang, interaktivroq
> bo'lishi kerak, shuningdek desktop holatida calendar juda kattalashib ketibdi
> shunday katta ekranda ham ekranga sig'mayapti"*

### 6.1. Ildiz sabab

Birinchi urinish o'quvchi panelini **kengaytirilgan telefon** qilib qo'ygan
edi: bitta ustun, 960px da qulflangan, markazda. 2560px monitorda ikki yonida
~700px dan bo'sh joy qolardi.

Kalendar esa BUNDAN BATTAR: kataklar `aspect-square` va setka `w-full`.
Telefonda katak ~50px (to'g'ri), lekin 960px lik ustunda katak **~130px**
bo'lib, oy setkasi yolg'iz o'zi ~800px balandlikka chiqardi. `aspect-square`
ning YUQORI CHEGARASI YO'Q — konteyner qancha keng bo'lsa, kalendar shuncha
baland.

### 6.2. Tamoyil

> **Desktop — kengroq telefon EMAS.** Qo'shimcha kenglik matn qatorini
> cho'zish uchun emas, **IKKINCHI USTUN** uchun ishlatiladi. Shunda har bir
> ustun qulay o'lchovda (~60–75 belgi) qoladi, ekran esa to'la ko'rinadi.

Karkas endi `lg:max-w-[1600px]`. Bo'sh joyni CHIZIQ UZUNLIGI emas, KONTENT
to'ldiradi.

### 6.3. Sahifa bo'yicha desktop setkasi (`lg:` dan boshlab)

| Sahifa | Desktop setkasi | O'ng ustunda nima |
|---|---|---|
| Bosh sahifa | `lg:grid-cols-[minmax(0,1fr)_360px]` | Davomat doirasi + statistika (`sticky`) |
| **Kalendar** | `lg:grid-cols-[minmax(0,600px)_minmax(0,1fr)]` | Tanlangan kun darslari (`sticky`) |
| O'quv | `lg:grid-cols-[minmax(0,1fr)_320px]` | Kurs progressi + tez havolalar |
| Reyting | Podium to'liq kenglik, ro'yxat `lg:grid-cols-2` | — |
| **Chat** | `lg:grid-cols-[340px_minmax(0,1fr)]` | Suhbat DOIM ochiq (ro'yxat almashmaydi) |
| Vazifalar / Testlar / Yozuvlar | Mavjud `@container` setkalari | — |

### 6.3b. Planshet bosqichi (`md:` 768px) — 2-shikoyat

> *"ipad qismlarida ham to'liq ekran holatida ishlamayapti"*

Desktop qatlami `lg:` (1024px) dan boshlanardi, iPad tik holati esa **768px**,
iPad Air **820px** — ikkalasi ham undan past. Ular telefon qoidasiga tushib,
520px lik tasma bo'lib qolardi: ikki yonida ~124px bo'sh joy va ko'rinib
turgan `xs:border-x` chegaralari.

| Kenglik | Ilgari | Endi |
|---|---|---|
| Telefon | 520px + tab paneli | o'zgarmadi |
| **iPad 768–1023px** | **520px tasma** | **840px + tab paneli** |
| Desktop ≥1024px | 960px markazda | 1600px + yon menyu |

★ Karkas ustuni va TAB PANELI kengligi JUFT (`md:max-w-[840px]` ikkalasida).
★ Yon menyu planshetda qo'shilmadi: 768px da u kontentga 538px qoldirardi —
520px dan deyarli farqsiz, ya'ni menyu qo'shib hech narsa yutilmasdi.
★ Xodim panellari (`AppShell`) planshetda ALLAQACHON to'g'ri edi —
`<main>` `mx-auto w-full max-w-[96rem] flex-1`, ya'ni kenglikni to'ldiradi.
Tor chegara faqat o'quvchi karkasida bor edi.

### 6.4. 🔴 Kalendar katagi — QATTIQ CHEGARA

Katak **hech qachon 76px dan katta bo'lmaydi**. Yechim KENGLIKDA emas,
SETKA TREKI ta'rifida: `lg:grid-cols-[repeat(7,minmax(0,76px))]` +
`lg:justify-center`. Trek o'z `max` funksiyasidan osholmaydi, ortiqcha joy
esa kataklarga emas, treklardan TASHQARIGA chiqadi. `aspect-square` shundan
keyin allaqachon chegaralangan kenglikdan balandlik oladi — ya'ni chegara
*qurilish bo'yicha*, tanlangan kenglikka moslab emas.

| Konteyner | 380px | 600px | 1000px | 2000px |
|---|---|---|---|---|
| Ilgari | 50px | 81px | **139px** | **281px** |
| Endi | 50px | **76px** | **76px** | **76px** |

Oy setkasi endi ~481px (ilgari ~800px).

### 6.5. Interaktivlik (talab: *"interaktivroq bo'lishi kerak"*)

- Kartochka va qatorlarda `hover:` holati (Tailwind v4 buni o'zi
  `@media(hover:hover)` ga o'raydi — teginishda yopishmaydi)
- Tanlangan element KO'RINIB turadi (kalendar kuni, chat suhbati)
- Yon ustunlar `lg:sticky lg:top-*` — skrollda kontekst yo'qolmaydi
- `focus-visible` halqasi saqlanadi (`style.css:763`)
- ★ Telefon yo'liga TEGILMAYDI: har bir qoida `lg:` ostida

### Keyingi ishga tavsiyalar (bu to'lqinda QILINMADI)

1. `@custom-variant short-landscape ((orientation: landscape) and (max-height: 500px))`
   — jonli dars yotiq qoidalari hozir JS bog'lanishida; sof CSS'ga o'tsa
   bitta arbitr qolardi.
2. `safe-bottom` utility'si — ilovada 4 joyda inline `style` da `env()` bor.
3. `StudentAccountDialog` jadvali `≥1024px` da HAM gorizontal skroll qiladi:
   chegara VIEWPORT bo'yicha, haqiqiy cheklov esa MODAL kengligi
   (`BaseModal` `wide` = `sm:max-w-3xl` = 768px). Bugungi xulq o'zgarmadi.
