import type { MascotMood } from '@/features/mascot'
import { COURSE_FACTS } from '@/shared/config/course-facts'
import type { IconName } from '@/shared/ui'

/*
  ══════════════════════════════════════════════════════════════════════════
  LANDING SAHIFANING MATNI — HAMMASI SHU FAYLDA
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN BAZADAN EMAS, FAYLDAN:
    1) Bu MARKETING matni, biznes ma'lumoti emas. Bazada kurs, tarif va
       ustoz yozuvlari bor, lekin ularda landing uchun kerak bo'ladigan
       narsalar YO'Q: sotuv taklifi, "nima kiradi", savol-javob.
    2) Baza ma'lumotini ko'rsatish uchun ANONIM endpoint kerak bo'lardi.
       Hozir `/api/v1/*` ning hammasi (kirish oqimidan boshqasi)
       autentifikatsiya talab qiladi.

  🔴 QIYMATLARNI SHU YERDA TAHRIRLANG. Sahifaning o'zida
     (`LandingPage.vue`) birorta ham "qotib qolgan" matn yo'q.

  ══════════════════════════════════════════════════════════════════════════
   2026-08-29 — SAHIFA PLATFORMADAN KURSGA QAYTA YO'NALTIRILDI
  ══════════════════════════════════════════════════════════════════════════

  Ilgari bu sahifa PLATFORMANI sotardi: "jonli dars", "davomat",
  "to'lov nazorati", "hisobot". Ya'ni LMS imkoniyatlari ro'yxati.

  Muammo: landingga kelgan odam LMS izlamaydi — u ARAB TILINI o'rganmoqchi.
  "Davomat tizimi bor" degan gap uni qiziqtirmaydi; "8 oyda kitobni
  mustaqil o'qiysiz" degani qiziqtiradi.

  Matn endi loyiha egasi bergan sotuv skriptidan olinadi
  (`Kurs uchun offer scripti.docx`) — narx, tuzilma, kitob yetkazib
  berish va guruh hajmi AYNAN o'sha hujjatdagi qiymatlar.

  ⚠️ NARX VA SHARTLAR HAQIQIY. Ularni o'zgartirishdan oldin o'quv
     bo'limi bilan tasdiqlang — bu sahifa ommaga ochiq va u yerdagi
     raqam ustida odam qaror qabul qiladi.
*/

/* ─────────────────────────── BOT VA ALOQA ─────────────────────────── */

export const BOT_USERNAME = (import.meta.env.VITE_TELEGRAM_BOT_USERNAME ?? '')
  .trim()
  .replace(/^@/, '')

export const BOT_LINK: string | null =
  BOT_USERNAME.length > 0 ? `https://t.me/${BOT_USERNAME}` : null

/*
  🔴 HAQIQIY RAQAM (2026-08-30 da loyiha egasi berdi). Ilgari bu yerda
  o'rin egallovchi `+998 90 000 00 00` turardi — ya'ni sahifadagi
  "Telefon" bo'limi ishlamas raqamni ko'rsatib turardi.

  ★ `phoneHref` — `tel:` havolasi uchun: BO'SHLIQSIZ, qavssiz va
    tirelarsiz. Ba'zi telefonlarning terish ilovasi formatlangan satrni
    ochib bera olmaydi.
*/
export const CONTACT = {
  phone: '+998 (78) 777-77-17',
  phoneHref: '+998787777717',
  /*
    ⚠️ 2026-09-03 — «Du – Sha» EMAS, «Du – Ya» (loyiha egasi aniqlashtirdi).

    Bu — CALL CENTRE ish vaqti, ya'ni menejerlar qo'ng'iroqqa javob
    beradigan vaqt. U dars jadvali bilan bog'liq EMAS: darslar
    haftasiga 5 kun (`WEEK_DAYS`), aloqa esa yetti kun.
  */
  workingHours: 'Dushanba – Yakshanba, 09:00 – 19:00',
} as const

/*
  IJTIMOIY TARMOQLAR — loyiha egasi bergan havolalar (2026-08-29).

  ⚠️ 2026-08-30 — HAQIQIY BREND BELGILARI QO'YILDI.

  Ilgari bu yerda ma'noga eng yaqin UMUMIY ikonkalar turardi
  (`send`, `play`, `camera`), chunki `AppIcon` da brend logolari yo'q edi.
  Amalda bu Telegram'ni "qog'oz samolyot", Instagram'ni esa
  "videokamera" qilib ko'rsatardi — odam tarmoqni belgisidan
  tanimasdi. Endi uchalasi ham o'z logosi bilan
  (`shared/ui/brand-icon-paths.ts`).
*/
export interface Social {
  label: string
  href: string
  icon: IconName
}

export const SOCIALS: readonly Social[] = [
  {
    label: 'Telegram',
    href: 'https://t.me/zinnurakademiyasi_onlayn',
    icon: 'telegram',
  },
  {
    label: 'YouTube',
    href: 'https://www.youtube.com/@zinnur_onlayn',
    icon: 'youtube',
  },
  {
    label: 'Instagram',
    href: 'https://www.instagram.com/zinnur_onlayn',
    icon: 'instagram',
  },
]

/* ─────────────────────────────── HERO ─────────────────────────────── */

/*
  ⚠️ 2026-08-30 — VA'DA ANIQLASHTIRILDI.

  Ilgari sarlavha "arab tilidagi kitobni mustaqil o'qiysiz" der edi va
  matnda "mazmunini tushunadigan darajaga chiqasiz" degan gap bor edi.
  Loyiha egasi buni NOTO'G'RI deb belgiladi: 8 oyda o'quvchi kitob
  MAZMUNINI emas, HARAKATLI (harakatlari qo'yilgan) matnni to'g'ri
  O'QISHNI o'rganadi. Bu ikki xil va'da, va kattarog'i keyin
  norozilikka olib keladi.
*/
export const HERO = {
  /*
    ⚠️ 2026-09-03 — HERO NISHONI VA E'LON PANELI AJRATILDI.

    🔴 MUAMMO: «Yangi guruhga qabul ochiq» endi IKKI joyda edi — eng
       tepadagi e'lon panelida va uning ostidagi hero nishonida. Bir
       ekranda ikki marta takrorlangan gap kuchini yo'qotadi va sahifa
       o'zini takrorlayotgandek ko'rinardi.

    ★ `badge` E'LON PANELIDA QOLDI (u yerda u tanqislik signali),
      hero'ga esa markazning O'ZI kim ekani chiqdi. Birinchi ekran
      endi ikki savolga javob beradi: bu kim, va u nima va'da qiladi.
  */
  eyebrow: 'Arab tili akademiyasi',
  badge: 'Yangi guruhga qabul ochiq',
  /*
    ⚠️ 2026-09-03 — BIRLIKDAN KO'PLIKKA: «kitobni» -> «kitoblarni».

    Loyiha egasining tuzatishi: fonetikani tugatgan o'quvchi BITTA
    kitobni emas, harakatli kitoblarning HAMMASINI o'qiy oladi.
    Birlik shakl va'dani o'zi bilmagan holda kichraytirib qo'yardi —
    odam "qaysi kitob ekan?" deb o'ylardi.
  */
  title: '8 oyda arab tilidagi harakatli kitoblarni',
  titleAccent: 'mustaqil o‘qiysiz',
  /*
    ⚠️ 2026-10-29 — LEAD QAYTA YOZILDI.

    🔴 ESKI GAP IKKI XATO QILARDI: 1) grammatik jihatdan buzilgan edi —
       "jonli darslar … va barcha kitoblar … pochtaga yetkaziladi" bo'lib
       chiqardi; 2) support teacher va kitoblarni HAMMAGA va'da qilardi.
       Ular faqat Milliy tarifda va u yerda ham «qo'shimcha» (narxga
       kirmaydi). Mustaqil xaridori birinchi ekranda o'zi olmaydigan
       narsani o'qirdi.

    ★ ENDI LEAD IKKALA YO'LNI AYTADI: ustoz bilan jonli guruh yoki
      qulay vaqtda video darslar. Tafsilot `PRICE` da.
  */
  lead:
    'Quruq qoida yodlash emas — har dars amaliyot. Ustoz bilan jonli '
    + 'guruh darslari yoki o‘zingizga qulay vaqtda video darslar: '
    + 'o‘zingizga mos shaklni tanlang.',
} as const

/*
  ══════════════════════════════════════════════════════════════════════════
   HERO VIZUALI — SUZUVCHI KARTALAR (2026-09-03)
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN QO'SHILDI: hero'ning o'ng yarmi bo'sh edi va birinchi
    ekran faqat MATNDAN iborat bo'lardi. Bu kartalar kursning to'rtta
    ajratuvchi tomonini bir qarashda ko'rsatadi — o'qimasdan, ko'z bilan:
    jonli dars bor, talaffuz tekshiriladi, kitob yo'lda, guruh kichik.

  ⚠️ 2026-10-29 — KARTALAR HAMMA TARIFGA TEGISHLI QILINDI. Ilgari
     «Talaffuz tekshirildi · Support teacher» va «Kitoblar yo'lda» turardi —
     ikkalasi faqat Milliy va «qo'shimcha». Guruh hajmi kartasi esa
     o'rnini Nuriga berdi (u raqamlar lentasida baribir bor).

  🔴 KARTALARDA YANGI VA'DA YO'Q. To'rtalasi ham sahifada pastroqda
     matn bilan aytilgan (`WEEK`, `OUTCOMES`, `BOOKS`, `STATS`). Ular
     ANONS, ya'ni takror — va aynan shuning uchun ekran o'qigich uchun
     `aria-hidden` ostida turadi.

  ★ `kind` — kartadagi kichik jonli element turi: avatarlar, tovush
    to'lqini yoki progress chizig'i. Shakl ma'noga bog'langan
    (`avatars` = odamlar, `wave` = ovoz, `progress` = yo'ldagi narsa),
    shuning uchun u ma'lumotda turadi, shablonda emas.
*/
export interface HeroCard {
  title: string
  text: string
  kind: 'avatars' | 'wave' | 'progress' | 'plain'
}

export const HERO_CARDS: readonly HeroCard[] = [
  {
    title: 'Jonli dars ketmoqda',
    text: `Asosiy ustoz · ${COURSE_FACTS.lessonDuration}`,
    kind: 'avatars',
  },
  {
    title: 'Ovozli vazifa tekshirildi',
    text: 'Izoh bilan · 9/10',
    kind: 'wave',
  },
  {
    title: 'Keyingi dars ochildi',
    text: 'O‘quv yo‘li · 7-dars',
    kind: 'progress',
  },
]

/*
  Hero vizualining markazidagi harf.

  ★ NEGA AYNAN «ع»: bepul dars ham shu harf haqida (`FREE_LESSON`).
    Ya'ni birinchi ekrandagi belgi pastdagi videoga ishora qiladi va
    ikkisi bir-birini kuchaytiradi.
*/
export const HERO_LETTER = {
  glyph: 'ع',
  caption: '«Ayn» harfi',
} as const

/*
  SAHIFA FONIDAGI XIRA HARFLAR — 2026-09-03.

  ★ NEGA AYNAN «ع ب ن»: ular brend nomining o'zagi (ZIN-NUR emas —
    «ayn», «bo», «nun» arab alifbosining tanish harflari) va sahifaning
    mavzusini bir qarashda aytadi. Tasodifiy harflar bo'lsa, bu
    shunchaki naqsh bo'lib qolardi.

  🔴 SHAFFOFLIK 4.5% — MATNGA XALAQIT BERMASLIGI SHART. Harflar 200px
     gacha katta va biroz quyuqroq bo'lsa, ular ustidagi tana matnining
     kontrasti tushib ketardi. Bu qiymatni oshirmang.
*/
export const DECOR_LETTERS: readonly string[] = ['ع', 'ب', 'ن']

/*
  RAQAMLAR — sotuv skriptidagi eng kuchli to'rt fakt.

  ★ "540 000" va "18-20" ATAYLAB shu yerda: ular e'tirozga BIRINCHI
    javob beradi ("qimmatmi?", "guruh katta-ku?"). Ularni pastga
    yashirish sahifaning ishonch qismini zaiflashtirardi.

  🔴 NARX YONIDAGI YOZUV "so'm / oy" EMAS, "so'm / 8 ta dars" (2026-08-30).
     Sabab `PRICE` izohida: oy — dars soni O'ZGARIB turadigan birlik.
*/
/*
  ⚠️ 2026-10-29 — «5 kun haftasiga dars» → «2 + 3». 5 kunning 3 kuni
     support teacher, ya'ni Milliy tarifdagi «qo'shimcha» (narxga
     kirmaydi). Bitta «5» uni asosiy dars bilan teng ko'rsatardi.
*/
export const STATS: readonly { value: string, label: string }[] = [
  { value: COURSE_FACTS.courseDuration, label: 'Fonetika kursi' },
  { value: '2 + 3', label: 'kun: ustoz + support teacher' },
  { value: COURSE_FACTS.groupSize, label: 'kishilik guruh' },
  { value: COURSE_FACTS.price, label: `so‘m / ${COURSE_FACTS.lessonsPerPayment} · Milliy` },
]

/* ──────────────────────── BEPUL DARS (VIDEO) ──────────────────────── */

/*
  LID MAGNIT — bepul video dars.

  ★ NIMA UCHUN SAHIFADA YUQORIDA: odam pul to'lashdan oldin "bu ustoz
    qanday tushuntiradi?" degan savolga javob izlaydi. Bepul dars shu
    savolga eng tez javob beradi va u obuna yoki ariza talab qilmaydi.

  ★ `youtubeId` ALOHIDA maydon: `LandingPage.vue` undan `youtube-nocookie`
    o'rnatma manzilini yasaydi. To'liq havola esa "YouTube'da ochish"
    tugmasi uchun kerak.
*/
export const FREE_LESSON = {
  eyebrow: 'Bepul dars',
  title: '«Ayn» harfini 15 daqiqada o‘rganing',
  text:
    'Arab tilidagi eng qiyin tovushlardan biri. Ustozimiz uni bosqichma-'
    + 'bosqich, talaffuz mashqlari bilan tushuntiradi — ro‘yxatdan '
    + 'o‘tmasdan, hoziroq ko‘ring.',
  /*
    QISQA FAKTLAR — videoning yonida, pilyuska shaklida (2026-08-30).

    ★ NIMA UCHUN QO'SHILDI: yuqoridagi `text` ayni narsani aytadi, lekin
      GAP ICHIDA. Landingni odam o'qimaydi — SKANERLAYDI, va "bepulmi?",
      "ro'yxatdan o'tishim kerakmi?", "qancha vaqt oladi?" degan uchta
      savolga javob gapning o'rtasida qolib ketardi.

    🔴 BU YERDA YANGI VA'DA YO'Q — uchalasi ham `text` da allaqachon
       aytilgan. Pilyuskalar faqat SHAKLNI o'zgartiradi, mazmunni emas.
  */
  facts: [
    'Ro‘yxatdan o‘tish shart emas',
    'To‘liq bepul',
    '15 daqiqa',
  ],
  youtubeId: '7HS_W3amolU',
  href: 'https://youtu.be/7HS_W3amolU',
} as const

/* ──────────────────────────── NATIJA ──────────────────────────────── */

export interface Outcome {
  icon: IconName
  title: string
  text: string
}

/*
  ⚠️ 2026-10-29 — 2- VA 3-KARTALAR ALMASHDI.

  ★ Sarlavha «8 oydan keyin» emas, «ATF kursidan keyin»: 8 oy — Milliy
    guruhining tempi, Mustaqilda esa tempni o'quvchining o'zi belgilaydi.

  🔴 2-karta «Support teacher talaffuzingizni eshitadi» der edi — bu faqat
     Milliy tarifda va u yerda ham «qo'shimcha». Endi va'da HAMMA tarifga
     tegishli: ovozli vazifa tekshiriladi.
  🔴 3-karta «Grammatikani amalda ishlatasiz» edi. ATF — o'qish va
     talaffuz kursi, Grammatika esa ALOHIDA kurs (`COURSE_PATH`). Hero ham
     «quruq qoida emas» deydi — karta sahifaning o'zi bilan ziddiyatda edi.
*/
export const OUTCOMES_SECTION = {
  eyebrow: 'Natija',
  title: 'ATF kursidan keyin nima o‘zgaradi',
} as const

export const OUTCOMES: readonly Outcome[] = [
  {
    icon: 'book',
    title: 'Harakatli kitoblarni mustaqil o‘qiysiz',
    text:
      'Fonetika kursini yakunlaganingizdan keyin arab tilidagi harakatli '
      + 'kitoblarni ochib, ularni mustaqil, to‘g‘ri o‘qiy olasiz — bittasini '
      + 'emas, hammasini.',
  },
  {
    icon: 'message-circle',
    title: 'Har tovushni to‘g‘ri talaffuz qilasiz',
    text:
      'Ovozli vazifalaringiz tekshiriladi: xato aynan qaysi harfda ekanini '
      + 'bilasiz va uni darhol tuzatasiz.',
  },
  {
    icon: 'graduation',
    title: 'To‘xtamasdan, ravon o‘qiysiz',
    text:
      'Amaliyot modulida matn ustida muntazam mashq qilinadi — o‘qish '
      + 'tezligi va ishonchingiz oshadi.',
  },
]

/* ─────────────────────── PLATFORMA ICHIDAN ────────────────────────── */

/*
  ══════════════════════════════════════════════════════════════════════════
   «PLATFORMA ICHIDAN» KARUSELI — 2026-10-29
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN: sahifa kursni sotadi, lekin onlayn o'quvchining eng katta
    xavotiri — «kabinet qanday, men u yerda adashmaymanmi?». Ilgari
    sahifada kabinetning birorta ham ekrani yo'q edi.

  ★ JOYI — «Natija» dan keyin: va'da → isbot (bepul dars) → natija →
    QANDAY O'QILADI (platforma) → ariza.

  🔴 EKRANLAR HOZIRCHA RASM EMAS, HTML-SXEMA (`ui/PlatformTourScreen.vue`):
     haqiqiy o'quvchi interfeysining soddalashtirilgan nusxasi, O'YLAB
     TOPILGAN demo ma'lumot bilan (ism — faqat ism va familiya bosh
     harfi, telefon yo'q). Login qilmasdan ichki sahifalarni suratga
     olib bo'lmadi, prod bazadan esa skrinshot OLINMAYDI.

  ★ HAQIQIY SKRINSHOTGA O'TISH: slaydga `image` bering
    (`{ src, width, height }`, WebP, ≤120 KB, demo muhitdan olingan) —
    karusel o'sha zahoti sxema o'rniga rasmni ko'rsatadi. `alt` ikkala
    holatda ham ishlatiladi.

  ★ `mood` — slayd ochilganda yo'lboshchi Nurining kayfiyati. Landingda
    faqat ijobiy kayfiyatlar (`concerned` / `sleepy` ishlatilmaydi).

  ⚠️ `plan: 'milliy'` — slayd faqat Milliy tarifga tegishli (jonli dars).
     Teg kartada ko'rinadi, aks holda Mustaqil xaridori jonli darsni
     o'ziga ham tegishli deb o'qirdi.

  🔴 ARABCHA MISOLLAR FAQAT BETARAF MAISHIY SO'ZLAR (كِتَاب, قَلَم, بَاب).
*/
export type TourScreen = 'home' | 'path' | 'lesson' | 'tasks' | 'test' | 'rating' | 'live'

export interface TourSlide {
  id: TourScreen
  title: string
  caption: string
  plan: 'all' | 'milliy'
  mood: MascotMood
  /** Haqiqiy skrinshot. `null` — HTML-sxema chiziladi. */
  image: { src: string, width: number, height: number } | null
  /** Ekran mazmuni — ekran o'qigich uchun. */
  alt: string
}

export const PLATFORM_TOUR = {
  eyebrow: 'Platforma ichidan',
  title: 'Kabinetingiz qanday ko‘rinishini oldindan ko‘ring',
  lead:
    'Darslar, vazifalar, testlar va reyting — hammasi bitta joyda, telefonda '
    + 'ham, kompyuterda ham.',
  note: 'Ekranlarda namunaviy (demo) ma’lumotlar. Ism va raqamlar o‘ylab topilgan.',
  tagAll: 'barcha tariflar',
  tagMilliy: 'Milliy',
  tablistLabel: 'Platforma ekranlari',
  trackLabel: 'Platforma ekranlari. Chapga-o‘ngga suring yoki strelka tugmalaridan foydalaning',
  prev: 'Oldingi ekran',
  next: 'Keyingi ekran',
  slides: [
    {
      id: 'home',
      title: 'Bosh sahifa',
      caption: 'Bugungi dars, progress va ballaringiz — kirgan zahoti bir qarashda.',
      plan: 'all',
      mood: 'happy',
      image: null,
      alt: 'Bosh sahifa: bugungi dars kartasi, kurs progressi va ballar',
    },
    {
      id: 'path',
      title: 'O‘quv yo‘li',
      caption: 'Darslar ketma-ket ochilib boradi: qayerda turganingiz doim ko‘rinadi.',
      plan: 'all',
      mood: 'encouraging',
      image: null,
      alt: 'O‘quv yo‘li: o‘tilgan darslar belgilangan, joriy dars ajratilgan, keyingilari yopiq',
    },
    {
      id: 'lesson',
      title: 'Dars sahifasi',
      caption: 'Video dars, vazifa, test va savol-javob — bitta sahifada.',
      plan: 'all',
      mood: 'thinking',
      image: null,
      alt: 'Dars sahifasi: video pleer, ostida vazifa, test va savol-javob bo‘limlari',
    },
    {
      id: 'tasks',
      title: 'Vazifalar',
      caption: 'Vazifani matn, daftar surati yoki ovozli yozuv bilan topshirasiz — izoh bilan baholanadi.',
      plan: 'all',
      mood: 'happy',
      image: null,
      alt: 'Vazifalar: yuborilgan ovozli yozuv, izoh bilan tekshirilgan vazifa va kutilayotgan vazifa',
    },
    {
      id: 'test',
      title: 'Testlar',
      caption: 'Har mavzudan keyin qisqa test. Natija darhol, xatolar izohi bilan.',
      plan: 'all',
      mood: 'thinking',
      image: null,
      alt: 'Test: savol, uchta arabcha javob varianti va progress chizig‘i',
    },
    {
      id: 'rating',
      title: 'Reyting',
      caption: 'Ball to‘plang va guruhingizda o‘z o‘rningizni ko‘ring.',
      plan: 'all',
      mood: 'celebrate',
      image: null,
      alt: 'Reyting: uchlik podium va o‘quvchilar ro‘yxati, sizning qatoringiz ajratilgan',
    },
    {
      id: 'live',
      title: 'Jonli dars',
      caption: 'Jonli dars saytning o‘zida ochiladi — dastur o‘rnatish shart emas.',
      plan: 'milliy',
      mood: 'encouraging',
      image: null,
      alt: 'Jonli dars: ustoz va ishtirokchilar plitkalari, mikrofon va kamera tugmalari',
    },
  ] satisfies readonly TourSlide[] as readonly TourSlide[],
} as const

/* ─────────────────────── HAFTALIK TUZILMA ─────────────────────────── */

export interface WeekBlock {
  icon: IconName
  /**
   * Kun soni — FAQAT RAQAM ("2", "3"), birliksiz.
   *
   * ⚠️ 2026-08-30 da "2 kun" dan "2" ga o'zgardi. Sabab dizaynda:
   * kartada raqam KATTA shriftda, "kun" so'zi esa uning ostida kichik
   * yozuv bo'lib turadi. Birlik matn ichida qolsa, shablon uni
   * `replace(' kun', '')` bilan qirqishga majbur bo'lardi — ya'ni
   * ko'rinish ma'lumot satrining ichki tuzilishiga bog'lanib qolardi.
   */
  days: string
  title: string
  text: string
}

/*
  HAFTA QANDAY O'TADI — skriptdagi eng aniq va eng ishonarli qism.

  ★ NIMA UCHUN JADVAL SHAKLIDA: "haftasiga 5 kun dars" degan gap quloqda
    ko'p tuyuladi, lekin qaysi kuni nima bo'lishi ko'rinmaydi. Bo'lib
    ko'rsatilganda odam o'z jadvaliga solishtira oladi — bu ariza
    qoldirishdan oldingi oxirgi to'siq.

  ══════════════════════════════════════════════════════════════════════
   ⚠️ 2026-08-30 — HAFTA QAYTA HISOBLANDI: 4 EMAS, 5 KUN DARS
  ══════════════════════════════════════════════════════════════════════

  Ilgari bu yerda UCHTA blok turardi: 2 kun ustoz + 2 kun "nazariy dars"
  + 3 kun kuratorlik. Loyiha egasi aniqlashtirdi: haftada DARS 5 kun —
  2 kuni asosiy ustoz, 3 kuni support teacher.

  ★ "Nazariy dars" bloki O'CHIRILDI, lekin MAZMUNI YO'QOLMADI: video
    darslik va testlar `FEATURES` dagi "Test va mashqlar" va "Dars yozib
    olinadi" kartalarida qoladi. Ular MUSTAQIL ish — hafta jadvalidagi
    "dars kuni" emas, shuning uchun bu yerda sanalmaydi.

  🔴 DARS DAVOMIYLIGI 1 SOAT 20 DAQIQA (1.5 soat EMAS). Bu raqam
     `PRICE.includes` da ham takrorlanadi — biri o'zgarsa, ikkinchisi ham.
*/
export const WEEK: readonly WeekBlock[] = [
  {
    icon: 'video',
    days: '2',
    title: 'Asosiy ustoz darsi',
    text:
      'Jonli dars, har biri 1 soat 20 daqiqa. Savol berasiz, gapirasiz, '
      + 'ustoz darhol tuzatadi.',
  },
  {
    icon: 'user-check',
    days: '3',
    title: 'Support teacher darsi',
    text:
      'Support teacher siz bilan alohida shug‘ullanadi: o‘tilgan mavzuni '
      + 'mustahkamlaydi, talaffuzingizni audio orqali eshitadi va '
      + 'savollaringizga javob beradi.',
  },
]

/*
  ══════════════════════════════════════════════════════════════════════════
   HAFTA KUNLARI — 2026-09-03
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN `WEEK` YETMADI: yuqoridagi ikki blok haftada NECHTA dars
    borligini va ular NIMA ekanini aytadi, lekin QAYSI KUNI ekanini
    aytmaydi. Ishlaydigan yoki o'qiydigan odam uchun aynan shu yetishmas
    edi — u jadvalni o'z haftasiga sola olmasdi.

  🔴 KUNLAR LOYIHA EGASI TASDIQLASHI KERAK. Ular yangi landing maketidan
     olindi (`zin-nur-site.vercel.app`), oldingi kontentda kun nomlari
     UMUMAN yo'q edi. Agar guruhlar turli kunlarda o'qisa — bu ro'yxat
     noto'g'ri va uni o'chirish kerak (yoki guruhga bog'lash).

  ★ IKKI BLOK O'CHIRILMADI: chizma "qachon" ga, bloklar esa "nima
    bo'ladi" ga javob beradi. Faqat chizma qolsa, support teacher darsi
    nimaligi sahifada tushuntirilmay qolardi.
*/
export interface WeekDay {
  /** Qisqartma — chizmada katta harf bilan. */
  label: string
  /** Kun turi: asosiy ustoz / support teacher / dam. */
  kind: 'main' | 'support' | 'off'
  /** Ikki qatorlik izoh (`\n` bilan bo'linadi). */
  text: string
}

export const WEEK_DAYS: readonly WeekDay[] = [
  { label: 'Du', kind: 'main', text: 'Asosiy ustoz\n1 s 20 daq' },
  { label: 'Se', kind: 'support', text: 'Support\nteacher' },
  { label: 'Cho', kind: 'main', text: 'Asosiy ustoz\n1 s 20 daq' },
  { label: 'Pa', kind: 'support', text: 'Support\nteacher' },
  { label: 'Ju', kind: 'support', text: 'Support\nteacher' },
  { label: 'Sha–Ya', kind: 'off', text: 'Mustaqil\nmashq' },
]

/* ────────────────────── KURS BOSQICHLARI ──────────────────────────── */

export interface CourseModule {
  name: string
  /*
    Chizmadagi qisqa nom.

    ★ NEGA `name` DAN KESIB OLINMAYDI: nisbiy chizmadagi eng tor bo'lak
      (Amaliyot I — 1 oy) atigi ~60px keladi va u yerga to'liq nom
      SIG'MAYDI. Avtomatik kesish esa "1-modul — Harf mod…" kabi
      yarim so'z qoldirardi.
  */
  short: string
  duration: string
  /*
    Davomiylik OYDA, raqam bilan — 2026-09-03 da qo'shildi.

    ★ NEGA `duration` YETMAYDI: sahifada modullar endi nisbiy KENGLIKDA
      chiziladi (uzun modul — keng bo'lak). Kenglik `duration` matnidan
      olinsa, uni har safar tahlil qilish kerak bo'lardi — "3 oy" dan
      raqam ajratish esa matn o'zgarishi bilan darhol sinadi.

    ⚠️ `duration` BILAN MOS TURSIN: matn odam uchun, raqam chizma uchun.
  */
  months: number
}

export interface CourseStage {
  name: string
  duration: string
  text: string
  /** Bosqich ichidagi modullar. Bo'lmasa — bosqich bo'linmaydi. */
  modules?: readonly CourseModule[]
  /*
    ══════════════════════════════════════════════════════════════════
     KURSLARNI AJRATUVCHI FAKTLAR — 2026-09-03
    ══════════════════════════════════════════════════════════════════

    🔴 MUAMMO: sahifa Fonetika va Grammatika haqida UMUMIY gapirardi —
       "haftasiga 5 kun dars", "support teacher bor", "test va mashqlar
       bor". Amalda bu faqat FONETIKA uchun to'g'ri:

         Fonetika   — haftasiga 5 kun, support teacher BOR, test BOR
         Grammatika — haftasiga 2 kun, support teacher YO'Q, test YO'Q

       Loyiha egasi: "oradagi farq katta". Umumiy gap odamda noto'g'ri
       kutish yaratardi va u Grammatikaga yozilgach o'zi kutgan
       narsani topmasdi.

    ★ NEGA `boolean` EMAS, MATN: "yo'q" bilan "hozircha yo'q" bir xil
      narsa emas. Grammatika testi rejalashtirilgan, lekin hali
      tayyor emas — buni bayroq bilan ifodalab bo'lmaydi.

    ⚠️ AMALIYOT II DA FAKTLAR YO'Q — ATAYLAB. Uning jadvali loyiha
       egasi tomonidan aytilmagan; o'ylab topilgan raqam yozishdan
       ko'ra maydonni bo'sh qoldirish to'g'riroq.
  */
  facts?: readonly { label: string, value: string }[]
}

/*
  KURS QANDAY BO'LINADI (2026-08-30 da loyiha egasi berdi).

  ★ NIMA UCHUN SAHIFAGA QO'SHILDI: ilgari landing "8 oylik kurs" deb
    bitta blok sifatida gapirar edi. Amalda markaz UCHTA narsani sotadi
    (ATF, Amaliyot II, Grammatika) va ATF ning o'zi uch moduldan iborat.
    Buni ko'rsatmaslik ikki muammo tug'dirardi: odam "8 oydan keyin
    nima?" degan savolga javob topmasdi, va ariza formasidagi yo'nalish
    ro'yxati sahifadagi hech narsaga bog'lanmasdi.

  🔴 "AMALIYOT I — 1 OY" HISOBLAB CHIQARILGAN, BERILMAGAN.
     Loyiha egasi ATF = 8 oy, Harf = 3 oy, Qoida = 4 oy dedi; uchinchi
     modulning davomiyligi aytilmadi va u qoldiqdan olindi (8−3−4).
     ⚠️ Agar amalda boshqacha bo'lsa — SHU QATORNI to'g'rilang.

  🔴 GRAMMATIKA — DAVOMIYLIGI YO'Q. U ham berilmagan, shuning uchun
     `duration` ataylab bo'sh emas, "alohida kurs" deb yozilgan:
     o'ylab topilgan oy sonini yozish narx va jadval bo'yicha noto'g'ri
     kutish yaratardi.
*/
export const COURSE_PATH: readonly CourseStage[] = [
  {
    /*
      ⚠️ 2026-09-03 — QISQARTMA OCHIB YOZILDI.

      🔴 SABAB (loyiha egasi): "ATF degan yozuvni ko'pchilik tushuna
         olmaydi". Qisqartma faqat ICHKARIDA ma'lum va sahifaga
         birinchi marta kelgan odam uchun u ma'nosiz uch harf edi —
         holbuki aynan shu kurs sotilmoqda.

      ★ TO'LIQ SHAKL BU YERDA, kursning O'Z kartasida: aynan shu joyda
        odam "bu nima?" degan savolni beradi. Sahifaning qolgan
        qismida (FAQ, narx yorlig'i) qisqa «ATF» qoladi — u yergacha
        odam ma'nosini bilib bo'lgan bo'ladi.
    */
    name: 'ATF (Arab tili Fonetika)',
    duration: '8 oy',
    text:
      'Asosiy kurs. Alifbodan boshlanadi va harakatli matnni mustaqil '
      + 'o‘qiy oladigan darajagacha olib boradi.',
    modules: [
      { name: '1-modul — Harf moduli', short: '1-modul · Harf', duration: '3 oy', months: 3 },
      { name: '2-modul — Qoida moduli', short: '2-modul · Qoida', duration: '4 oy', months: 4 },
      { name: '3-modul — Amaliyot I moduli', short: '3-modul', duration: '1 oy', months: 1 },
    ],
    /*
      ⚠️ 2026-10-29 — «Milliy tarifda» ANIQLASHTIRILDI: 2 kun asosiy ustoz
         narxga kiradi, 3 kun support teacher — «qo'shimcha». Mustaqil
         tariflarda ikkalasi ham yo'q.
    */
    facts: [
      { label: 'Milliy tarifda', value: '2 kun ustoz + 3 kun support' },
      { label: 'Support teacher', value: 'Milliy · qo‘shimcha' },
      { label: 'Test va mashqlar', value: 'Bor' },
    ],
  },
  {
    name: 'Amaliyot II',
    duration: '3 oy',
    text:
      'Fonetika tugagandan keyingi davomi. O‘qish ko‘nikmasi matn ustida '
      + 'muntazam amaliyot bilan mustahkamlanadi.',
  },
  {
    name: 'Grammatika',
    duration: 'alohida kurs',
    text:
      'So‘z shakllari va gap qurilishi qoidalari. Arab tilini o‘qish '
      + 'bilan cheklanmasdan, matnni tahlil qila olmoqchi bo‘lganlar uchun.',
    /*
      🔴 GRAMMATIKA FONETIKADAN JIDDIY FARQ QILADI — shuning uchun bu
         uchta qator sahifada ochiq ko'rsatiladi. Ularsiz odam
         yuqoridagi "haftasiga 5 kun, support bor" degan gaplarni
         Grammatikaga ham tegishli deb o'ylardi.

      ⚠️ "Hozircha yo'q" — ATAYLAB shunday yozilgan: loyiha egasi
         testlar keyinchalik tuzilishi mumkinligini aytdi. Test
         qo'shilsa, shu qatorni yangilang.
    */
    facts: [
      { label: 'Haftasiga', value: '2 kun dars' },
      { label: 'Support teacher', value: 'Yo‘q' },
      { label: 'Test va mashqlar', value: 'Hozircha yo‘q' },
    ],
  },
]

/* ──────────────────────── AFZALLIKLAR ─────────────────────────────── */

export interface Feature {
  icon: IconName
  title: string
  text: string
}

export const FEATURES: readonly Feature[] = [
  {
    icon: 'award',
    title: 'Sertifikatli ustozlar',
    text:
      'Ustozlarimizning 4–7 yillik tajribasi va arab tili bo‘yicha '
      + 'CEFR yoki TANAL xalqaro sertifikatlari bor.',
  },
  {
    icon: 'users',
    title: 'Guruhda 18–20 kishi',
    text:
      'Ko‘p onlayn kurslardagidek 50 kishi yig‘ilmaydi. Kichik guruh — '
      + 'ustoz har bir o‘quvchiga vaqt ajrata oladi.',
  },
  {
    /*
      ⚠️ 2026-09-03 — IKKALA KARTAGA HAM "FONETIKA KURSIDA" QO'SHILDI.

      🔴 SABAB: support teacher ham, testlar ham FAQAT fonetika kursida
         bor (Grammatikada ikkalasi ham yo'q — `COURSE_PATH` dagi
         `facts` izohi). Bu kartalar esa "Kursga kiritilgan" bo'limida,
         ya'ni odam ularni HAR kursga tegishli deb o'qirdi va
         Grammatikaga yozilgach o'zi kutgan narsani topmasdi.
    */
    icon: 'user-check',
    title: 'Haftada 3 kun support teacher darsi',
    text:
      'Fonetika kursida asosiy darsdan tashqari shaxsiy yordam. Yarim '
      + 'yo‘lda to‘xtab qolmaysiz — savolingiz javobsiz qolmaydi.',
  },
  {
    icon: 'clipboard',
    title: 'Test va mashqlar',
    text:
      'Fonetika kursida har mavzudan keyin test. Natijangiz saqlanadi, '
      + 'qaysi mavzu oqsayotgani ustoz va support teacher’ga ko‘rinib '
      + 'turadi.',
  },
  {
    icon: 'refresh',
    title: 'Dars yozib olinadi',
    text:
      'Darsga ulgurmasangiz yozuvi kabinetingizda qoladi — '
      + 'istalgan vaqtda qaytib ko‘rasiz.',
  },
  {
    icon: 'telegram',
    title: 'Telegram orqali aloqa',
    text:
      'Eslatmalar, vazifa va e’lonlar Telegramga keladi. '
      + 'Kirish uchun parol ham kerak emas.',
  },
]

/* ───────────────────────── KITOB BONUSI ───────────────────────────── */

/*
  ★ ALOHIDA BLOK, "afzalliklar" ro'yxatiga QO'SHILMADI — ATAYLAB.
    Bu raqobatchilarda yo'q yagona narsa va u ro'yxatning oltinchi
    qatorida ko'zdan qochib ketardi.
*/
/*
  ⚠️ 2026-08-30 — "UYINGIZGACHA" OLIB TASHLANDI.

  Sarlavha "Kitoblarni uyingizgacha yetkazamiz" der edi. Bu VA'DA
  BAJARILMAYDI: kitob eshikkacha emas, o'quvchining uyiga eng yaqin
  Uzpost pochtasiga boradi va uni o'quvchi o'sha yerdan oladi.

  🔴 SHART OCHIQ YOZILDI, YASHIRILMADI: pochtaga borish kerakligini
     sahifada aytmaslik uni kitob kelganda aytishga qoldirardi — ya'ni
     pul to'langandan keyin. Bu esa aynan norozilik chiqadigan joy.
*/
export const BOOKS = {
  eyebrow: 'Milliy tarifda qo‘shimcha',
  title: 'Kitoblarni pochtangizgacha yetkazamiz',
  /*
    ⚠️ 2026-10-29 — «MILLIY TARIFDA» QO'SHILDI: Mustaqil tariflarda kitob
       yo'q. «Uyingizga emas, uyingizga…» takrori ham olib tashlandi.
  */
  lead:
    'Milliy tarifda kurs uchun kerakli kitoblarni o‘zimiz yuboramiz: '
    + 'uyingizga eng yaqin Uzpost bo‘limiga 5–7 kunda yetib boradi, uni '
    + 'o‘sha yerdan olasiz.',
  points: [
    'Respublikaning istalgan viloyatiga — uyingizga eng yaqin Uzpost pochtasiga',
    '5–7 kun ichida yetkaziladi',
    'Milliy tarifda qo‘shimcha — alohida pul olinmaydi',
    // ★ Ekranni yomonlamaydi: Mustaqil+ ning o'zi interaktiv darslik sotadi.
    'Qog‘oz kitob qo‘lingizda — darsni ekransiz ham takrorlaysiz',
  ],
} as const

/* ──────────────────────────── NARX ────────────────────────────────── */

/*
  ══════════════════════════════════════════════════════════════════════
   🔴 NARX OY BO'YICHA EMAS, DARS BO'YICHA (2026-08-30)
  ══════════════════════════════════════════════════════════════════════

  Ilgari sahifa "540 000 so'm / har oy" der edi. Loyiha egasi buni
  o'zgartirishni so'radi va sabab amaliy:

    oy — dars soni QAT'IY BO'LMAGAN birlik. Haftasiga 2 ta asosiy dars
    bo'lsa, ko'p oyda 8 ta dars chiqadi, lekin BA'ZI oylarda 9 ta.
    "540 000 har oy" degan yozuv o'quvchida "dars soniga qaramay narx
    bir xil" degan kutish yaratadi, va to'qqizinchi dars uchun hisob
    kelganda aynan shu joyda norozilik chiqadi.

  Shuning uchun birlik DARS: 540 000 so'm — asosiy ustozning 8 ta darsi,
  bitta dars 67 500 so'm (540 000 ÷ 8).

  ⚠️ `perLesson` maydoni ilgari `daily` deb atalardi va "kuniga 18 000
     so'm" ni ko'rsatardi. Kunlik bo'lish endi TO'G'RI KELMAYDI: u
     narxni yana oyga bog'lab qo'yardi.
*/
export const PRICE = {
  eyebrow: 'Tariflar',
  /*
    Sarlavha tariflar soniga qarab (offers API dan qancha mustaqil reja
    kelsa). 2026-10-29 da sahifadan shu yerga ko'chirildi.
  */
  titleSingle: 'Kurs to‘lovi',
  titleTwo: 'Ikki xil o‘qish shakli',
  titleThree: 'Uch xil o‘qish shakli',
  planName: 'Milliy',
  /** Milliy kartasidagi kurs nomi (qisqartma + to'liq shakli). */
  planCourse: 'ATF — Arab tili fonetikasi',
  planTagline: 'Asosiy ustoz bilan guruhda jonli darslar va qo‘shimchalar',
  /*
    🔴 NARX FAQAT 8 TA DARS UCHUN (loyiha egasi, 2026-10-29). Support
       teacher, kitoblar, video darslar va boshqalar — QO'SHIMCHA: ular
       narxga kirmaydi va ular uchun alohida pul olinmaydi. Matnda
       «to'lov ichida» degan gap ishlatilmasin.
  */
  /*
    ══════════════════════════════════════════════════════════════════════
     MILLIY KARTASI IKKI BLOKKA BO'LINDI (2026-10-29)
    ══════════════════════════════════════════════════════════════════════

    🔴 MUAMMO: ilgari Milliy kartasida 7-8 qator bor edi va ulardan
       6 tasida «qo'shimcha» pilyuskasi turardi. Narxga kiradigan YAGONA
       qator — 8 ta jonli dars — 4-o'rinda, boshqalardan farqsiz edi.
       Odam «540 000 nima uchun?» degan savolga javobni o'zi qidirardi.

    ★ ENDI: «Narxga kiradi» bloki (bitta qator, ajratilgan) va alohida
      «Qo'shimcha» ro'yxati, uning sarlavhasi ostida BITTA izoh. Ilgari
      shu izoh besh xil shaklda takrorlanardi (`bonusNote`, `booksNote`,
      `booksNoteMilliy`, …) — ular o'chirildi.

    ⚠️ INTERAKTIV DARSLIK MILLIY RO'YXATIDA YO'Q — ATAYLAB. Milliy o'quvchisi
       uni oladimi, loyiha egasi hali aytmagan. Javob kelguncha va'da
       qilinmaydi (egasiga savol).
  */
  includedTitle: 'Narxga kiradi',
  included: ['Asosiy ustoz bilan 8 ta jonli dars — har biri 1 soat 20 daqiqa'],
  extrasTitle: 'Qo‘shimcha',
  extrasNote: 'Narxga kirmaydi va ular uchun alohida pul olinmaydi',
  extras: [
    'Haftasiga 3 kun support teacher darsi',
    'Kitoblar — Uzpost bo‘limiga yetkazib beriladi',
    'Video darslar, vazifa va testlar, kabinet va reyting',
    'Dars yozuvlari va guruh chati',
  ],
  selfTagline: 'Jonli darssiz: video darslar, vazifa va testlar o‘zingizga qulay vaqtda',
  leadThreePlans:
    'Milliy — ustoz bilan guruhda jonli darslar, to‘lov dars soni bo‘yicha. '
    + 'Mustaqil va Mustaqil+ — jonli darssiz, to‘lov oyiga; Mustaqil+ da '
    + 'interaktiv darslik ham bor.',
  /** Mustaqil+ kartasidagi eslatma — darslik hali to'liq emas (2026-10-29). */
  plusNote: 'Interaktiv darslik ATF kursidan boshlab bosqichma-bosqich ochiladi.',
  leadTwoPlans:
    'Milliy — ustoz bilan guruhda jonli darslar, to‘lov dars soni bo‘yicha. '
    + 'Mustaqil — video darslar, vazifa va testlar, to‘lov oyiga.',
  amount: '540 000',
  currency: 'so‘m',
  period: '8 ta dars',
  perLesson: 'Bitta dars — 67 500 so‘m',
  /*
    ⚠️ 2026-09-03 — MATN UCHGA BO'LINDI.

    Ilgari `note` da hammasi bir joyda edi: hisoblash qoidasi, ortiqcha
    dars va to'lov usullari. Bo'lim endi ikki kartadan iborat va bu
    uchta gap uch xil joyga tushadi — biri sarlavha ostiga (nima uchun
    darsga bo'linadi), biri narx kartasiga (qanday hisoblanadi), biri
    esa alohida chiziqqa (Click/Payme). Bitta uzun abzas hammasini
    o'qilmaydigan qilib qo'yardi.
  */
  lead:
    'To‘lov oy bo‘yicha emas, dars bo‘yicha hisoblanadi — siz faqat '
    + 'haqiqiy dars soni uchun to‘laysiz.',
  note:
    '540 000 so‘m — asosiy ustozning 8 ta darsi. Ba’zi oylarda darslar '
    + 'soni 9 taga to‘g‘ri keladi va o‘shanda ortiqcha dars alohida '
    + 'qo‘shiladi.',
  /*
    To'lov usullari — chiziq shaklida.

    ★ NEGA MATNDAN AJRATILDI: «Click» va «Payme» — TANIB OLINADIGAN
      nomlar. Gap ichida ular oddiy so'z bo'lib qolardi; alohida
      belgi bo'lganda odam ularni o'qimasdan, ko'z bilan taniydi.
  */
  methods: ['Click', 'Payme'],
  methodsLabel: 'To‘lov usullari:',
  /*
    ⚠️ 2026-10-29 — KITOB ESLATMASI (`booksNote`) O'CHIRILDI. «Kitob alohida
       pul turadimi?» degan savolga endi Milliy kartasining o'zidagi
       «Qo'shimcha» bloki javob beradi — narx yonida, takrorsiz.
  */
  /** Mustaqil kartalaridagi tugma va uning ostidagi mikromatn. */
  buyCta: 'Sotib olish',
  buyHint: 'Telegram orqali kirasiz, to‘lov kabinetda — Click yoki Payme.',
  newBadge: 'Yangi',
  perMonth: 'oy',
  /*
    TARIFLAR SOLISHTIRMASI — MUSTAQIL KARTALARIDA bir xil qatorlar, har
    birida ✓ yoki ✗. (2026-10-29 dan Milliy kartasi bu ro'yxatni emas,
    yuqoridagi `included` + `extras` bloklarini ko'rsatadi.)

    ★ NEGA BIR XIL RO'YXAT: odam ikki kartani yonma-yon qo'yib solishtiradi.
      Qatorlar bir xil va bir xil tartibda bo'lsa, farq bir qarashda
      ko'rinadi; har kartada o'z ro'yxati bo'lsa, odam nima YO'QLIGINI
      o'zi topishi kerak bo'lardi.
    🔴 Mustaqil tarifda kitob yetkazib berilmaydi (loyiha egasi, 2026-10-29).
  */
  compare: [
    // `self` — Mustaqil, `plus` — Mustaqil+ (interaktiv darslik bilan).
    // `bookOnly` — qator faqat Mustaqil+ sotuvda bo'lsa ko'rinadi.
    { label: 'Video darslar — o‘zingizga qulay vaqtda', self: true, plus: true, bookOnly: false },
    { label: 'Vazifa va testlar — tekshirib beriladi', self: true, plus: true, bookOnly: false },
    { label: 'Shaxsiy kabinet va reyting', self: true, plus: true, bookOnly: false },
    { label: 'Interaktiv darslik: audio, mashqlar, harf yozish, mini-test', self: false, plus: true, bookOnly: true },
    { label: 'Asosiy ustoz bilan jonli darslar', self: false, plus: false, bookOnly: false },
    { label: 'Support teacher darslari', self: false, plus: false, bookOnly: false },
    { label: 'Kitoblar (Uzpost)', self: false, plus: false, bookOnly: false },
  ],
  /*
    ⚠️ 2026-10-29 — `includes` (zaxira «Kurs bilan nimalar beriladi»
       ro'yxati) O'CHIRILDI. U offers API yiqilganda chiqardi va unda
       «qo'shimcha» belgisi yo'q edi, ya'ni narx qoidasiga zid edi. Endi
       zaxira holatda ham o'sha Milliy kartasi (ikki blok bilan) chiqadi.
  */
} as const

/*
  ARIZA FORMASIDAGI «Yo'nalish» ro'yxati.

  🔴 RO'YXAT `COURSE_PATH` BILAN BIR XIL BO'LIB TURSIN: odam sahifada
     uchta bosqichni o'qib, formada bittasini ko'rsa — sahifaga
     ishonchi tushadi. Yangi yo'nalish qo'shilsa, IKKALA joyga.

  ★ NEGA QO'LDA YOZILGAN, `COURSE_PATH` dan YIG'ILMAGAN: bu yerdagi
    qatorlar menejer ko'radigan YOZUV (arizaga shu matn tushadi), u
    yerdagilar esa sahifa sarlavhasi. Ular bir xil boshlanadi, lekin
    biri ikkinchisiga bo'ysunmaydi — masalan bu yerda davomiylik ham
    yozilgan, chunki menejer ro'yxatni raqamsiz ajrata olmaydi.

  Maydon ariza formasida IXTIYORIY: tanlanmasa "Hali tanlamaganman"
  ketadi va menejer qo'ng'iroqda aniqlaydi.
*/
export const COURSE_OPTIONS: readonly string[] = [
  'ATF (Arab tili Fonetika) — 8 oylik asosiy kurs',
  'Amaliyot II — 3 oylik kurs',
  'Grammatika',
]

/* ────────────────────────── DARAJA TESTI ──────────────────────────── */

/*
  DARAJA ANIQLASH BO'LIMI — 2026-09-03.

  ★ NIMA UCHUN QO'SHILDI: sahifa narxni va dasturni aytadi, lekin odamning
    boshidagi savolga — "MEN qaysi joydan boshlayman?" — javob bermasdi.
    U savolga javobsiz qolgan odam ariza qoldirish o'rniga "keyinroq
    o'ylab ko'raman" deb ketadi.

  ★ NEGA ARIZADAN OLDIN TURADI: test tugagach natija ariza formasiga
    O'ZI yoziladi. Ya'ni odam testni yechib bo'lgach forma allaqachon
    yarim to'ldirilgan bo'ladi va "ariza qoldirish" alohida qaror
    bo'lmay qoladi — boshlangan ishning davomiga aylanadi.

  🔴 SAVOLLAR BU YERDA EMAS: ular `features/level-test/model/questions.ts`
     da. Bu yerda faqat sahifada ko'rinadigan MARKETING matni.
*/
export const LEVEL_TEST = {
  eyebrow: 'Daraja aniqlash',
  title: 'Qaysi moduldan boshlashingizni bilib oling',
  text:
    '16 ta savol — alifbodan gap qurilishigacha. Test moslashuvchan: bilim '
    + 'darajangizga qarab savollar o‘zgaradi, shuning uchun ko‘pchilik uni '
    + '3 daqiqada tugatadi. Bilmagan savolingizni «Bilmayman» deb '
    + 'belgilang — daraja shunda aniq chiqadi.',
  cta: 'Testni boshlash',
  /*
    ⚠️ MAVZULAR RO'YXATI `questions.ts` DAGI `BLOCKS` BILAN MOS TURSIN.
       Ular ataylab nusxa: bu yerda marketing tili ("harflar, harakatlar"),
       u yerda esa test mantig'i. Blok qo'shilsa — ikkala joyga.
  */
  topics: [
    { name: 'Alifbo va yozuv', hint: 'harflar, harakatlar' },
    { name: 'Talaffuz', hint: 'tovush hosil bo‘lishi' },
    { name: 'So‘z tuzilishi', hint: 'artikl, tanvin' },
    { name: 'So‘z shakllari', hint: 'o‘zak, fe’l shakllari' },
    { name: 'Gap qurilishi', hint: 'ega, kesim, holatlar' },
  ],
  chips: ['3–4 daqiqa', 'Ro‘yxatdan o‘tish shart emas', 'To‘liq bepul'],
  /*
    KO'RGAZMA KARTASI — testni ochmasdan turib nimaligini ko'rsatadi.

    ★ NEGA HAQIQIY SAVOL: o'ylab topilgan "namuna" testning haqiqiy
      qiyinligini yashirardi. Bu — `questions.ts` dagi 2-savolning
      aynan o'zi.
  */
  preview: {
    stage: '1-bosqich · Alifbo va yozuv',
    count: 'Savol 2 / 4',
    question: 'Bu so‘zda nechta harf bor?',
    arabic: 'مَدْرَسَة',
    options: [
      { key: 'A', label: '4 ta', correct: false },
      { key: 'B', label: '5 ta', correct: true },
      { key: 'C', label: '6 ta', correct: false },
    ],
  },
} as const

/* ──────────────────────── QANDAY BOSHLANADI ───────────────────────── */

export interface Step {
  title: string
  text: string
}

export const STEPS: readonly Step[] = [
  {
    title: 'Ariza qoldirasiz',
    text:
      'Ism va telefon raqamingizni yozasiz. Bu bir daqiqalik ish va '
      + 'hech narsaga majbur qilmaydi.',
  },
  {
    /*
      ⚠️ 2026-08-30 — "O'QUV BO'LIMI" O'RNIGA "MENEJERLARIMIZ".

      Loyiha egasining talabi. Sabab: "o'quv bo'limi" — ICHKI bo'lim
      nomi, sahifaga kelgan odam uchun u hech narsani anglatmaydi va
      rasmiy idorani eslatadi. Qo'ng'iroqni amalda menejer qiladi.
      Shu almashtirish `FAQ` va ariza formasida ham qilingan.
    */
    title: 'Menejerlarimiz bog‘lanadi',
    text:
      'Menejerlarimiz qo‘ng‘iroq qilib, darajangizni aniqlaydi va sizga '
      + 'mos guruhni tanlaydi.',
  },
  {
    title: 'Joyingizni band qilasiz',
    text:
      'To‘lovni Click yoki Payme orqali amalga oshirasiz, biz esa '
      + 'kitoblaringizni pochtaga tayyorlaymiz.',
  },
  {
    title: 'Darslar boshlanadi',
    text:
      'Telegram orqali kabinetga kirasiz — jadval, jonli dars havolasi '
      + 'va vazifalar shu yerda.',
  },
]

/* ──────────────────────────── FOOTER ──────────────────────────────── */

/*
  ⚠️ 2026-10-29 — FOOTER MATNI SHU YERGA KO'CHIRILDI VA TUZATILDI.

  🔴 Ilgari sahifada qotib yozilgan edi: «ATF — 8 oylik asosiy kurs: jonli
     darslar, support teacher va kitoblar Uzpost orqali». Bu «hammasi
     kursga kiradi» deb o'qilardi — holbuki support teacher va kitoblar
     faqat Milliy tarifda va «qo'shimcha» (narxga kirmaydi).
*/
export const FOOTER = {
  about:
    'Arab tili akademiyasi. Asosiy kurs — ATF (Arab tili fonetikasi), 8 oy. '
    + 'Ustoz bilan jonli darslar yoki mustaqil o‘qish.',
  login: 'Tizimga kirish',
  bot: 'Telegram bot',
  rights: 'ZIN-NUR ONLINE. Barcha huquqlar himoyalangan.',
} as const

/* ─────────────────────────── SAVOLLAR ─────────────────────────────── */

export interface Faq {
  question: string
  answer: string
}

export const FAQ: readonly Faq[] = [
  {
    question: 'Arab tilini noldan boshlasam bo‘ladimi?',
    answer:
      'Ha. ATF (Arab tili Fonetika) kursi alifbodan boshlanadi va 8 oy '
      + 'davomida bosqichma-bosqich olib boradi. Ariza qoldirganingizdan '
      + 'keyin menejerlarimiz darajangizni aniqlab, mos guruhni tanlaydi. '
      + 'Darajangizni hoziroq bilmoqchi bo‘lsangiz — sahifadagi bepul '
      + 'testni yeching.',
  },
  {
    question: 'Kurs qanday bosqichlardan iborat?',
    answer:
      'Asosiy kurs — ATF (Arab tili Fonetika), 8 oy va uchta moduldan '
      + 'iborat: Harf moduli (3 oy), Qoida moduli (4 oy) va Amaliyot I '
      + 'moduli. U tugagandan keyin 3 oylik Amaliyot II kursi bor. '
      + 'Alohida Grammatika kursi ham mavjud.',
  },
  /*
    ⚠️ 2026-09-03 DA QO'SHILDI — loyiha egasining ko'rsatmasi bo'yicha.

    🔴 SABAB: sahifa ikkala kurs haqida umumiy gapirardi va odam
       ular bir xil deb o'ylardi. Farq esa katta: dars kunlari,
       support teacher va testlar. Bu savol farqni BIR joyda,
       to'g'ridan-to'g'ri aytadi.

    ⚠️ Bu javob `COURSE_PATH` dagi `facts` bilan MOS TURSIN. Jadval
       o'zgarsa — ikkala joyni ham yangilang.
  */
  {
    question: 'Fonetika va Grammatika kurslarining farqi nima?',
    answer:
      'Fonetika (ATF) — arab yozuvini o‘qishni o‘rgatadigan asosiy kurs: '
      + 'har mavzudan keyin test bor, Milliy tarifda esa haftasiga 2 kun '
      + 'ustoz darsi va 3 kun support teacher bilan qo‘shimcha mashg‘ulot. Grammatika esa alohida kurs va '
      + 'boshqacha quriladi: haftasiga 2 kun dars, support teacher yo‘q va '
      + 'hozircha testlar ham yo‘q. Grammatika o‘qishni emas, matnni '
      + 'tahlil qilishni o‘rgatadi.',
  },
  {
    question: 'Bitta dars qancha davom etadi?',
    answer:
      'Milliy tarifda asosiy ustoz bilan jonli dars — 1 soat 20 daqiqa. '
      + 'Haftasiga bunday dars 2 kun bo‘ladi; qolgan 3 kun support teacher '
      + 'darsi — u qo‘shimcha, narxga kirmaydi.',
  },
  {
    question: 'To‘lov qanday hisoblanadi?',
    answer:
      'Milliy tarifda 540 000 so‘m — asosiy ustozning 8 ta jonli darsi '
      + 'uchun, ya’ni bitta dars 67 500 so‘m. Buni ataylab oyga emas, darsga '
      + 'bog‘laganmiz: ba’zi oylarda darslar soni 9 taga to‘g‘ri keladi va '
      + 'o‘shanda siz faqat haqiqiy dars soni uchun to‘laysiz. Mustaqil va '
      + 'Mustaqil+ tariflarida to‘lov oyiga.',
  },
  /*
    ⚠️ 2026-10-29 — UCHINCHI TARIF (Mustaqil+) QO'SHILDI. Ilgari savol faqat
       ikki tarif haqida edi.

    🔴 «TO'LOV ICHIDA» DEYILMAYDI: Milliy narxi FAQAT 8 ta jonli dars uchun,
       qolgani «qo'shimcha» — narxga kirmaydi va ular uchun alohida pul
       olinmaydi (loyiha egasi, 2026-10-29).
  */
  {
    question: 'Milliy, Mustaqil va Mustaqil+ farqi nima?',
    answer:
      'Milliy — asosiy ustoz bilan guruhda jonli darslar (haftasiga 2 kun); '
      + 'narx 8 ta jonli dars uchun 540 000 so‘m. Support teacher darslari '
      + '(haftasiga 3 kun), kitoblar, video darslar, dars yozuvlari va guruh '
      + 'chati — qo‘shimcha: narxga kirmaydi, ular uchun alohida pul '
      + 'olinmaydi. Mustaqil — jonli darssiz: video darslarni o‘zingizga '
      + 'qulay vaqtda ko‘rasiz, vazifa va testlarni bajarasiz, ular tekshirib '
      + 'beriladi; to‘lov oyiga. Mustaqil+ — shularning ustiga interaktiv '
      + 'darslik: u ATF kursidan boshlab bosqichma-bosqich ochiladi; to‘lov '
      + 'ham oyiga. Mustaqil tariflarni saytning o‘zida sotib olasiz.',
  },
  {
    question: 'Platformani to‘lovdan oldin ko‘rsam bo‘ladimi?',
    answer:
      'Ha — «Platforma ichidan» bo‘limida kabinetning asosiy ekranlari bor: '
      + 'bosh sahifa, o‘quv yo‘li, dars, vazifalar, testlar va reyting. '
      + 'Bepul darsni esa ro‘yxatdan o‘tmasdan ko‘rishingiz mumkin.',
  },
  {
    question: 'Darsga ulgurmasam nima bo‘ladi?',
    answer:
      'Milliy tarifda jonli dars yozib olinadi va kabinetingizda qoladi — istalgan vaqtda '
      + 'ko‘rasiz. Tushunmagan joyingizni esa support teacher bilan '
      + 'alohida muhokama qilasiz (ikkalasi ham qo‘shimcha).',
  },
  {
    question: 'Kitoblar uchun alohida pul to‘lanadimi?',
    answer:
      'Yo‘q. Milliy tarifda kitoblar qo‘shimcha sifatida beriladi — ular uchun '
      + 'alohida pul olinmaydi (Mustaqil tarifda kitob yo‘q). Kitob uyingizgacha emas, '
      + 'uyingizga eng yaqin Uzpost pochtasiga 5–7 kun ichida yetkaziladi '
      + '— respublikaning qaysi viloyatida bo‘lishingizdan qat’i nazar — '
      + 'va uni o‘sha pochtadan olasiz.',
  },
  {
    question: 'Guruhda nechta o‘quvchi bo‘ladi?',
    answer:
      'Milliy tarifda guruh 18–20 kishidan iborat. Bu ataylab shunday: ustoz har bir o‘quvchiga '
      + 'vaqt ajrata olishi uchun guruh hajmi cheklangan. Shuning uchun '
      + 'joylar tez tugaydi.',
  },
  {
    question: 'Darsda qatnashish uchun nima kerak?',
    answer:
      'Brauzer va internet. Alohida dastur o‘rnatish shart emas — dars '
      + 'saytning o‘zida ochiladi. Telefonda ham, kompyuterda ham ishlaydi.',
  },
  {
    question: 'Tizimga qanday kiraman?',
    answer:
      'Parol yo‘q. «Kirish» tugmasini bosasiz, Telegram boti sizni taniydi '
      + 'va 6 xonali kod yuboradi — o‘sha kodni saytga kiritasiz.',
  },
]
