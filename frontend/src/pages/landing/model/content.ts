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

export const CONTACT = {
  phone: '+998 90 000 00 00',
  phoneHref: '+998900000000',
  workingHours: 'Dushanba – Shanba, 09:00 – 19:00',
} as const

/*
  IJTIMOIY TARMOQLAR — loyiha egasi bergan havolalar (2026-08-29).

  ★ `icon` maydoni `AppIcon` dagi mavjud nomlardan tanlanadi. Telegram,
    YouTube va Instagram uchun ALOHIDA belgi yo'q, shuning uchun ma'noga
    eng yaqin umumiy ikonkalar olindi (`send`, `play`, `camera`) va yonida
    tarmoq NOMI matn bilan yoziladi — belgi o'zi tanitmasa ham, nom
    tanitadi.
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
    icon: 'send',
  },
  {
    label: 'YouTube',
    href: 'https://www.youtube.com/@zinnur_onlayn',
    icon: 'play',
  },
  {
    label: 'Instagram',
    href: 'https://www.instagram.com/zinnur_onlayn',
    icon: 'camera',
  },
]

/* ─────────────────────────────── HERO ─────────────────────────────── */

export const HERO = {
  badge: 'Yangi guruhga qabul ochiq',
  title: '8 oyda arab tilidagi kitobni',
  titleAccent: 'mustaqil o‘qiysiz',
  lead:
    'Quruq grammatika va qoida yodlash emas — darslarning asosi amaliyot. '
    + 'Jonli darslar, shaxsiy kuratorlik va kitoblar uyingizgacha yetkazib '
    + 'beriladi.',
} as const

/*
  RAQAMLAR — sotuv skriptidagi eng kuchli to'rt fakt.

  ★ "540 000" va "18-20" ATAYLAB shu yerda: ular e'tirozga BIRINCHI
    javob beradi ("qimmatmi?", "guruh katta-ku?"). Ularni pastga
    yashirish sahifaning ishonch qismini zaiflashtirardi.
*/
export const STATS: readonly { value: string, label: string }[] = [
  { value: '8 oy', label: 'to‘liq kurs' },
  { value: '4 kun', label: 'haftasiga dars' },
  { value: '18–20', label: 'kishilik guruh' },
  { value: '540 000', label: 'so‘m / oy' },
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
  youtubeId: '7HS_W3amolU',
  href: 'https://youtu.be/7HS_W3amolU',
} as const

/* ──────────────────────────── NATIJA ──────────────────────────────── */

export interface Outcome {
  icon: IconName
  title: string
  text: string
}

export const OUTCOMES: readonly Outcome[] = [
  {
    icon: 'book',
    title: 'Kitobni mustaqil o‘qiysiz',
    text:
      'Kursni to‘liq yakunlaganingizdan keyin arab tilidagi istalgan '
      + 'kitobni lug‘atsiz ochib, mazmunini tushunadigan darajaga chiqasiz.',
  },
  {
    icon: 'message-circle',
    title: 'Talaffuzingiz to‘g‘rilanadi',
    text:
      'Kuratorlar audio orqali talaffuzingizni eshitadi va xatolaringizni '
      + 'aynan qayerda ekanini ko‘rsatib to‘g‘rilaydi.',
  },
  {
    icon: 'graduation',
    title: 'Grammatikani amalda ishlatasiz',
    text:
      'Qoidalarni yodlab qo‘yish emas — har mavzu darhol mashq, test va '
      + 'jonli suhbatda mustahkamlanadi.',
  },
]

/* ─────────────────────── HAFTALIK TUZILMA ─────────────────────────── */

export interface WeekBlock {
  icon: IconName
  days: string
  title: string
  text: string
}

/*
  HAFTA QANDAY O'TADI — skriptdagi eng aniq va eng ishonarli qism.

  ★ NIMA UCHUN JADVAL SHAKLIDA: "haftasiga 4 kun dars, 3 kun kuratorlik"
    degan gap quloqda ko'p tuyuladi, lekin qaysi kuni nima bo'lishi
    ko'rinmaydi. Bo'lib ko'rsatilganda odam o'z jadvaliga solishtira
    oladi — bu ariza qoldirishdan oldingi oxirgi to'siq.
*/
export const WEEK: readonly WeekBlock[] = [
  {
    icon: 'video',
    days: '2 kun',
    title: 'Ustoz bilan amaliy dars',
    text:
      'Jonli dars, har biri 1.5 soat. Savol berasiz, gapirasiz, '
      + 'ustoz darhol tuzatadi.',
  },
  {
    icon: 'play',
    days: '2 kun',
    title: 'Nazariy dars',
    text:
      'Video va audio darsliklar. Mavzu oxirida uni mustahkamlash '
      + 'uchun test beriladi.',
  },
  {
    icon: 'user-check',
    days: '3 kun',
    title: 'Kurator bilan ishlash',
    text:
      'Asistent mentor siz bilan alohida shug‘ullanadi: talaffuzingizni '
      + 'audio orqali eshitadi va savollaringizga javob beradi.',
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
      'Ustozalarimizning 4–7 yillik tajribasi va arab tili bo‘yicha '
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
    icon: 'user-check',
    title: 'Haftada 3 kun kuratorlik',
    text:
      'Darsdan tashqari shaxsiy yordam. Yarim yo‘lda to‘xtab '
      + 'qolmaysiz — savolingiz javobsiz qolmaydi.',
  },
  {
    icon: 'clipboard',
    title: 'Test va mashqlar',
    text:
      'Har mavzudan keyin test. Natijangiz saqlanadi, qaysi mavzu '
      + 'oqsayotgani ustoz va kuratorga ko‘rinib turadi.',
  },
  {
    icon: 'refresh',
    title: 'Dars yozib olinadi',
    text:
      'Darsga ulgurmasangiz yozuvi kabinetingizda qoladi — '
      + 'istalgan vaqtda qaytib ko‘rasiz.',
  },
  {
    icon: 'send',
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
export const BOOKS = {
  eyebrow: 'Kursga kiritilgan',
  title: 'Kitoblarni uyingizgacha yetkazamiz',
  lead:
    'O‘quv qurollari yoki adabiyot qidirib vaqt sarflamaysiz — kurs uchun '
    + 'kerakli barcha kitoblarni o‘zimiz yuboramiz.',
  points: [
    'Respublikaning istalgan viloyatiga — sizga eng yaqin Uzpost pochtasiga',
    '5–7 kun ichida yetkaziladi',
    'Narxi oylik to‘lov ichida, alohida pul so‘ralmaydi',
    'Telefonda ko‘z og‘ritmaysiz — qog‘oz kitobdan dars qilasiz',
  ],
} as const

/* ──────────────────────────── NARX ────────────────────────────────── */

export const PRICE = {
  eyebrow: 'To‘lov',
  amount: '540 000',
  currency: 'so‘m',
  period: 'har oy',
  daily: 'Kuniga atigi 18 000 so‘m',
  note:
    'Bir umrlik bilim uchun sarmoya. To‘lovni Click yoki Payme orqali '
    + 'amalga oshirasiz.',
  includes: [
    'Haftasiga 2 ta jonli amaliy dars (1.5 soatdan)',
    'Haftasiga 2 ta nazariy dars — video, audio va test',
    'Haftasiga 3 kun kurator yordami',
    'Barcha kitoblar va ularni pochtaga yetkazib berish',
    'Dars yozuvlari va shaxsiy kabinet',
  ],
} as const

/*
  ARIZA FORMASIDAGI «Yo'nalish» ro'yxati.

  ★ HOZIR BITTA QATOR — va bu to'g'ri: markaz ayni paytda bitta kursni
    sotadi. Ilgari bu ro'yxat `COURSES` kartalaridan yig'ilardi, lekin
    o'sha kartalar NAMUNA ma'lumot edi (haqiqiy emas) va sahifadan olib
    tashlandi. Yangi yo'nalish qo'shilsa — shu yerga qator qo'shing.

  Maydon ariza formasida IXTIYORIY: tanlanmasa "Hali tanlamaganman"
  ketadi va o'quv bo'limi qo'ng'iroqda aniqlaydi.
*/
export const COURSE_OPTIONS: readonly string[] = [
  'Arab tili — 8 oylik to‘liq kurs',
]

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
    title: 'Biz bog‘lanamiz',
    text:
      'O‘quv bo‘limi qo‘ng‘iroq qilib, darajangizni aniqlaydi va sizga '
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

/* ─────────────────────────── SAVOLLAR ─────────────────────────────── */

export interface Faq {
  question: string
  answer: string
}

export const FAQ: readonly Faq[] = [
  {
    question: 'Arab tilini noldan boshlasam bo‘ladimi?',
    answer:
      'Ha. Kurs alifbodan boshlanadi va 8 oy davomida bosqichma-bosqich '
      + 'olib boradi. Ariza qoldirganingizdan keyin o‘quv bo‘limi '
      + 'darajangizni aniqlab, mos guruhni tanlaydi.',
  },
  {
    question: 'Darsga ulgurmasam nima bo‘ladi?',
    answer:
      'Jonli dars yozib olinadi va kabinetingizda qoladi — istalgan vaqtda '
      + 'ko‘rasiz. Tushunmagan joyingizni esa kurator bilan alohida '
      + 'muhokama qilasiz.',
  },
  {
    question: 'Kitoblar uchun alohida pul to‘lanadimi?',
    answer:
      'Yo‘q. Barcha kitoblar oylik to‘lov ichiga kiradi. Ularni sizga eng '
      + 'yaqin Uzpost pochtasiga 5–7 kun ichida yetkazib beramiz — '
      + 'respublikaning qaysi viloyatida bo‘lishingizdan qat’i nazar.',
  },
  {
    question: 'Guruhda nechta o‘quvchi bo‘ladi?',
    answer:
      'Atigi 18–20 kishi. Bu ataylab shunday: ustoz har bir o‘quvchiga '
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
