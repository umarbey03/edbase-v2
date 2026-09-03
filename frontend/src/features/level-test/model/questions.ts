/*
  ══════════════════════════════════════════════════════════════════════════
  DARAJA ANIQLASH TESTI — SAVOLLAR VA BAHOLASH QOIDALARI
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN BU TEST BOR: landing'ga kelgan odamning boshidagi eng
    birinchi savol "men qaysi guruhga tushaman?" degan savol emas —
    "men umuman uddalay olamanmi?" degan savol. Test ikkalasiga ham
    javob beradi va buni ariza qoldirishdan OLDIN, hech narsaga majbur
    qilmasdan qiladi.

  ★ NEGA ARIZAGA ULANGAN: test tugagach natija ariza formasiga o'zi
    yoziladi (`LevelTestModal` dagi `apply` hodisasi). Ya'ni menejer
    qo'ng'iroq qilishdan OLDIN odamning taxminiy darajasini biladi va
    suhbat "alifboni bilasizmi?" dan emas, guruh tanlashdan boshlanadi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 MAZMUN — SOF TILSHUNOSLIK                                       │
  └────────────────────────────────────────────────────────────────────┘
  Alifbo, fonetika, so'z tuzilishi, so'z shakllari va gap qurilishi.
  Diniy termin, matn va misollar ISHLATILMAYDI — kurs chet tili kursi
  sifatida sotiladi (ayni qaror interfeysning qolgan matnlarida ham
  qo'llangan).

  ┌────────────────────────────────────────────────────────────────────┐
  │ ★ ADAPTIV MANTIQ                                                   │
  └────────────────────────────────────────────────────────────────────┘
  Savollar beshta blokka bo'lingan va blok oxirida "darvoza" (`GATES`)
  ishlaydi. O'quvchi blokdan o'tolmasa test SHU YERDA to'xtaydi va unga
  o'sha daraja beriladi.

  Ya'ni noldan boshlovchi 16 emas, 4 ta savolga javob beradi — va
  bilmagan savollari bilan o'zini kamsitilgan his qilmaydi. Bu shunchaki
  qulaylik emas: uzun test yarmida tashlab ketiladi, tashlab ketilgan
  test esa ariza keltirmaydi.
*/

/** Test bloklari — savollar shu tartibda beriladi. */
export type BlockId = 'harf' | 'talaffuz' | 'soz' | 'sarf' | 'nahv'

/**
 * Yakuniy natija kaliti.
 *
 * ★ HARFLAR CEFR DARAJASI EMAS: `A` kaliti `A0` darajasini beradi.
 * Ular ataylab ajratilgan — kelajakda daraja nomlari o'zgarsa,
 * darvozalardagi mantiq tegilmay qolaveradi.
 */
export type ResultKey = 'A' | 'B' | 'C' | 'D' | 'E'

export interface QuestionOption {
  /** O'zbekcha variant matni. */
  readonly text?: string
  /**
   * Arab tilidagi variant.
   *
   * ★ `text` bilan ALMASHINADI, qo'shilmaydi: ba'zi savollarda variant
   * o'zi arabcha (masalan o'zak harflari), o'shanda o'zbekcha tarjima
   * savolning ma'nosini oldindan aytib qo'yardi.
   */
  readonly arabic?: string
}

export interface Question {
  readonly block: BlockId
  /** Savol ustida ko'rsatiladigan arab matni (bo'lmasligi mumkin). */
  readonly arabic?: string
  readonly question: string
  readonly options: readonly QuestionOption[]
  /** To'g'ri javobning `options` ichidagi ASL indeksi. */
  readonly correct: number
  /** Javobdan keyingi tushuntirish — natija ekranidagi tahlilda chiqadi. */
  readonly explanation: string
}

export interface BlockInfo {
  readonly name: string
  /** Blokdagi savollar soni — progress va baho shunga bo'linadi. */
  readonly total: number
}

export const BLOCKS: Readonly<Record<BlockId, BlockInfo>> = {
  harf: { name: 'Alifbo va yozuv', total: 4 },
  talaffuz: { name: 'Talaffuz', total: 3 },
  soz: { name: 'Soʻz tuzilishi', total: 2 },
  sarf: { name: 'Soʻz shakllari', total: 4 },
  nahv: { name: 'Gap qurilishi', total: 3 },
}

/** Bloklar tartibi — test aynan shu ketma-ketlikda boradi. */
export const BLOCK_ORDER: readonly BlockId[] = [
  'harf',
  'talaffuz',
  'soz',
  'sarf',
  'nahv',
]

export const QUESTIONS: readonly Question[] = [
  // ─────────────────────────────────────────── 1. Alifbo va yozuv ──
  {
    block: 'harf',
    arabic: 'ع',
    question: 'Bu harfning nomi nima?',
    options: [{ text: '«Ayn»' }, { text: '«Gʻayn»' }, { text: '«Ho»' }, { text: '«Xo»' }],
    correct: 0,
    explanation:
      '«ع» — ayn. Uning nuqtali shakli «غ» — gʻayn. «ح» — ho, «خ» — xo. '
      + 'Bu toʻrt harf shaklan oʻxshash, farqi nuqtada.',
  },
  {
    block: 'harf',
    arabic: 'مَدْرَسَة',
    question: 'Bu soʻzda nechta harf bor?',
    options: [{ text: '4 ta' }, { text: '5 ta' }, { text: '6 ta' }, { text: '7 ta' }],
    correct: 1,
    explanation:
      'م-د-ر-س-ة — 5 ta harf. Harakatlar (fatha, sukun) harf hisoblanmaydi. '
      + 'Soʻz maʼnosi — «maktab».',
  },
  {
    block: 'harf',
    arabic: 'بُ',
    question: 'Bu yerda qaysi harakat ishlatilgan?',
    options: [{ text: 'Fatha' }, { text: 'Kasra' }, { text: 'Damma' }, { text: 'Sukun' }],
    correct: 2,
    explanation:
      'Damma «u» tovushini beradi: بُ = «bu». Fatha — بَ «ba», kasra — بِ «bi».',
  },
  {
    block: 'harf',
    question: 'Sukun (ــْـ) belgisi nimani bildiradi?',
    options: [
      { text: 'Harf choʻziladi' },
      { text: 'Harf unlisiz, yopiq oʻqiladi' },
      { text: 'Harf ikki marta oʻqiladi' },
      { text: 'Harf umuman oʻqilmaydi' },
    ],
    correct: 1,
    explanation:
      'Sukun — harakatsizlik belgisi, harf unlisiz oʻqiladi. Harfni ikki marta '
      + 'oʻqitadigan belgi esa shadda (ــّـ).',
  },

  // ────────────────────────────────────────────── 2. Talaffuz ──────
  {
    block: 'talaffuz',
    question: 'Qaysi qatordagi harflar lab yordamida talaffuz qilinadi?',
    options: [
      { arabic: 'ب م و ف' },
      { arabic: 'ت د ط ن' },
      { arabic: 'ك ق غ خ' },
      { arabic: 'س ص ز' },
    ],
    correct: 0,
    explanation:
      'ب، م، و، ف — lab undoshlari. ت، د، ط، ن til uchi bilan, ك va ق esa '
      + 'tilning orqa qismi bilan hosil qilinadi.',
  },
  {
    block: 'talaffuz',
    arabic: 'ع',
    question: 'Bu harf qayerda hosil boʻladi?',
    options: [{ text: 'Lab' }, { text: 'Til uchi' }, { text: 'Tomoq' }, { text: 'Burun' }],
    correct: 2,
    explanation:
      'ع va ح — tomoqning oʻrta qismida hosil boʻladi. Arab tilida tomoq '
      + 'undoshlari 6 ta: ء، ه، ع، ح، غ، خ.',
  },
  {
    block: 'talaffuz',
    question: 'Arab tilida choʻziq (uzun) unlini qaysi harflar hosil qiladi?',
    options: [
      { arabic: 'ا و ي' },
      { arabic: 'ب ت ث' },
      { arabic: 'ن ل ر' },
      { arabic: 'س ش ص' },
    ],
    correct: 0,
    explanation:
      'ا، و، ي — choʻziq unli harflari: كِتَاب (kitob), نُور (nur), كَبِير (katta).',
  },

  // ──────────────────────────────────────── 3. Soʻz tuzilishi ──────
  {
    block: 'soz',
    arabic: 'الشَّمْس',
    question: 'Bu soʻz (quyosh) qanday oʻqiladi?',
    options: [
      { text: 'al-shams' },
      { text: 'ash-shams' },
      { text: 'shams' },
      { text: 'al-ams' },
    ],
    correct: 1,
    explanation:
      'ش — shamsiy harf. Shamsiy harflardan oldin artikldagi «ل» oʻqilmaydi, '
      + 'keyingi harf esa shaddalanadi. Qamariy harflarda toʻliq oʻqiladi: '
      + 'الْقَمَر — «al-qamar» (oy).',
  },
  {
    block: 'soz',
    arabic: 'كِتَابٌ',
    question: 'Soʻz oxiridagi «ٌ» (tanvin) nimani bildiradi?',
    options: [
      { text: 'Koʻplikni' },
      { text: 'Soʻz noaniq ekanini' },
      { text: 'Soʻz aniq ekanini' },
      { text: 'Feʼl ekanini' },
    ],
    correct: 1,
    explanation:
      'Tanvin — noaniqlik belgisi: كِتَابٌ «bir kitob». Aniq shakli artikl bilan '
      + 'yasaladi: الْكِتَاب «oʻsha kitob».',
  },

  // ──────────────────────────────────────── 4. Soʻz shakllari ──────
  {
    block: 'sarf',
    arabic: 'كَتَبَ — يَكْتُبُ — كَاتِب — مَكْتَب',
    question: 'Bu soʻzlarning umumiy oʻzagi qaysi?',
    options: [
      { arabic: 'ك ت ب' },
      { arabic: 'ك ا ت' },
      { arabic: 'م ك ت' },
      { arabic: 'ي ك ت' },
    ],
    correct: 0,
    explanation:
      'Arab tilida soʻzlar odatda 3 harfli oʻzakdan yasaladi. ك-ت-ب — «yozish» '
      + 'oʻzagi: yozdi, yozadi, yozuvchi, yozuv stoli.',
  },
  {
    block: 'sarf',
    arabic: 'يَدْرُسُ',
    question: 'Bu feʼl qaysi zamonda?',
    options: [
      { text: 'Oʻtgan zamon' },
      { text: 'Hozirgi-kelasi zamon' },
      { text: 'Buyruq shakli' },
      { text: 'Bu feʼl emas' },
    ],
    correct: 1,
    explanation:
      'Boshidagi «يـ» — hozirgi-kelasi zamon belgisi. Oʻtgan zamon shakli — '
      + 'دَرَسَ («oʻqidi»).',
  },
  {
    block: 'sarf',
    arabic: 'مُعَلِّمٌ جَدِيدٌ',
    question: 'Bu birikmada «جَدِيدٌ» soʻzi qanday vazifada?',
    options: [
      { text: 'Ega' },
      { text: 'Sifat (aniqlovchi)' },
      { text: 'Feʼl' },
      { text: 'Yordamchi soʻz' },
    ],
    correct: 1,
    explanation:
      'Arab tilida sifat otdan keyin keladi va unga moslashadi: '
      + 'مُعَلِّمٌ جَدِيدٌ — «yangi oʻqituvchi».',
  },
  {
    block: 'sarf',
    arabic: 'مَدْرَسَة',
    question: 'Soʻz oxiridagi «ة» harfi odatda nimani bildiradi?',
    options: [
      { text: 'Koʻplikni' },
      { text: 'Ayol jinsini' },
      { text: 'Feʼlni' },
      { text: 'Noaniqlikni' },
    ],
    correct: 1,
    explanation:
      '«ة» (ta marbuta) — koʻpincha ayol jinsi belgisi: طَالِب (talaba) → '
      + 'طَالِبَة (talaba qiz).',
  },

  // ──────────────────────────────────────── 5. Gap qurilishi ───────
  {
    block: 'nahv',
    arabic: 'فِي',
    question: 'Bu soʻz qaysi turkumga kiradi?',
    options: [
      { text: 'Ism' },
      { text: 'Feʼl' },
      { text: 'Harf (yordamchi soʻz)' },
      { text: 'Sifat' },
    ],
    correct: 2,
    explanation:
      'Arab tilida har bir soʻz uch turkumdan biri: ism, feʼl yoki harf. '
      + 'فِي («-da, ichida») — harf, yaʼni yordamchi soʻz (predlog).',
  },
  {
    block: 'nahv',
    arabic: 'ذَهَبْتُ إِلَى الْمَدْرَسَةِ',
    question: '«الْمَدْرَسَةِ» nega kasra («ـِ») bilan tugagan?',
    options: [
      { text: 'Ega boʻlgani uchun' },
      { text: 'Toʻldiruvchi boʻlgani uchun' },
      { text: 'Predlogdan keyin kelgani uchun' },
      { text: 'Koʻplik boʻlgani uchun' },
    ],
    correct: 2,
    explanation:
      'إِلَى — predlog. Undan keyin kelgan ism maxsus holatga oʻtadi va kasra '
      + 'oladi. Gap maʼnosi: «Men maktabga bordim».',
  },
  {
    block: 'nahv',
    arabic: 'الْبَيْتُ كَبِيرٌ',
    question: 'Bu qanday gap?',
    options: [
      { text: 'Feʼliy gap' },
      { text: 'Ismiy gap' },
      { text: 'Soʻroq gap' },
      { text: 'Buyruq gap' },
    ],
    correct: 1,
    explanation:
      'Ismdan boshlangan gap — ismiy gap (ega + kesim). Feʼldan boshlangani '
      + 'feʼliy gap deyiladi: ذَهَبَ الطَّالِبُ. Maʼnosi: «Uy katta».',
  },
]

/*
  ══════════════════════════════════════════════════════════════════════════
  TASODIFIY BOSISHDAN HIMOYA
  ══════════════════════════════════════════════════════════════════════════

  🔴 MUAMMO: hech narsa bilmaydigan odam ham to'rtta variantdan birini
     bosaverib yuqori daraja olishi mumkin. Bunday natija menejerni ham,
     o'quvchining o'zini ham chalg'itadi — u noto'g'ri guruhga tushadi va
     birinchi haftadayoq orqada qoladi.

  Besh qatlam himoya:

    1) VARIANTLAR ARALASHTIRILADI (`use-level-test.ts` dagi Fisher–Yates).
       Aks holda to'g'ri javoblar ma'lum kataklarda to'planib qolardi va
       "doim B ni bosaman" strategiyasi ishlardi.

    2) HAR SAVOLDA «BILMAYMAN» BOR. U tasodifiy tanlash ehtimolini
       pasaytiradi va halol javobga yo'l ochadi.

    3) CHEGARALAR BALAND — `GATES` ga qarang: 4 tadan 3 ta.

    4) KALIT SAVOLLAR (`KEY_QUESTIONS`): alifboni haqiqatan bilgan odam
       ularda xato qilmaydi.

    5) JAVOB VAQTI O'LCHANADI. Savolni o'qishga ulgurmaydigan tezlikda
       javob berilsa natija KO'RSATILMAYDI.
*/

/** Bundan tez berilgan javob "o'qilmagan" deb hisoblanadi. */
export const FAST_ANSWER_MS = 1800

/** Javoblarning shuncha ulushi tez bo'lsa — natija ishonchsiz. */
export const FAST_ANSWER_RATE = 0.7

/**
 * Kalit savollar indekslari — «ع» harfining nomi va damma harakati.
 *
 * ★ NEGA AYNAN SHULAR: ikkalasi ham alifboni bilgan odam uchun bir
 * qarashda javob beriladigan savol. Ularda xato qilingan bo'lsa,
 * qolgan bloklardagi to'g'ri javoblar tasodif bo'lishi ehtimoli yuqori.
 */
export const KEY_QUESTIONS: readonly number[] = [0, 2]

/** Blok bo'yicha to'g'ri javoblar soni. */
export type BlockScore = Readonly<Record<BlockId, number>>

/*
  DARVOZALAR.

  Har biri blok oxirida chaqiriladi va `null` qaytarsa test DAVOM etadi,
  natija kaliti qaytarsa — SHU YERDA tugaydi.

  ⚠️ `harf` darvozasi ballдан tashqari kalit savollarni ham tekshiradi
     (yuqoridagi 4-qatlam).
*/
export const GATES: Partial<
  Record<BlockId, (score: BlockScore, keysOk: boolean) => ResultKey | null>
> = {
  harf: (s, keysOk) => (s.harf >= 3 && keysOk ? null : 'A'),
  soz: s => (s.talaffuz + s.soz >= 3 ? null : 'B'),
  sarf: s => (s.sarf >= 3 ? null : 'C'),
  nahv: s => (s.nahv >= 2 ? 'E' : 'D'),
}

export interface LevelResult {
  /** Ekranda ko'rinadigan daraja belgisi. */
  readonly level: string
  readonly name: string
  readonly text: string
  /** Tavsiya etilgan yo'nalish sarlavhasi. */
  readonly recommendation: string
  readonly recommendationText: string
  /**
   * Ariza formasidagi «Yo'nalish» ro'yxatiga mos boshlanish.
   *
   * 🔴 `content.ts` dagi `COURSE_OPTIONS` bilan MOS BO'LISHI SHART —
   *    forma variantni aynan shu satr bilan boshlanadigan qatordan
   *    qidiradi. Yo'nalish nomlari o'zgarsa, bu yerni ham yangilang.
   */
  readonly courseMatch: string
}

export const RESULTS: Readonly<Record<ResultKey, LevelResult>> = {
  A: {
    level: 'A0',
    name: 'Boshlangʻich daraja',
    text:
      'Arab yozuvi siz uchun yangi. Bu — oʻquvchilarimizning koʻpchiligi '
      + 'boshlaydigan nuqta va kurs aynan shu holatga moʻljallangan.',
    recommendation: 'ATF — 1-modul (Harf moduli)',
    recommendationText:
      '8 oylik asosiy kursning boshidan — alifbo, harf shakllari va '
      + 'harakatlardan boshlaysiz. Harf moduli 3 oy davom etadi.',
    courseMatch: 'ATF',
  },
  B: {
    level: 'A1',
    name: 'Alifboni bilasiz',
    text:
      'Harflar va harakatlarni tanidingiz. Endi tovushlarni toʻgʻri talaffuz '
      + 'qilish va soʻz tuzilishi qoidalari kerak.',
    recommendation: 'ATF — 2-modul (Qoida moduli)',
    recommendationText:
      'Asosiy kursda davom etasiz: talaffuz, soʻz tuzilishi va oʻqish '
      + 'qoidalari. Qoida moduli 4 oy davom etadi.',
    courseMatch: 'ATF',
  },
  C: {
    level: 'A2',
    name: 'Oʻqish asoslari bor',
    text:
      'Alifbo va talaffuz mustahkam. Endi kerak boʻlgani — matn ustida '
      + 'muntazam amaliyot va soʻz shakllarini tanish.',
    recommendation: 'ATF — 3-modul (Amaliyot I)',
    recommendationText:
      'Asosiy kursning yakuniy moduli. Undan keyin 3 oylik Amaliyot II '
      + 'kursiga oʻtishingiz mumkin.',
    courseMatch: 'ATF',
  },
  D: {
    level: 'B1',
    name: 'Mustaqil oʻqiy olasiz',
    text:
      'Harakatli matnni mustaqil oʻqiy olasiz. Endi asosiy vazifa — tezlik, '
      + 'ravonlik va soʻz boyligi.',
    recommendation: 'Amaliyot II — 3 oylik kurs',
    recommendationText:
      'Oʻqish koʻnikmangiz matn ustida muntazam amaliyot bilan '
      + 'mustahkamlanadi.',
    courseMatch: 'Amaliyot II',
  },
  E: {
    level: 'B2',
    name: 'Grammatikaga tayyorsiz',
    text:
      'Oʻqish ham, soʻz shakllari ham sizga tanish. Keyingi bosqich — matnni '
      + 'tahlil qila olish.',
    recommendation: 'Grammatika kursi',
    recommendationText:
      'Soʻz shakllari va gap qurilishi qoidalari. Matnni faqat oʻqib emas, '
      + 'tahlil qilib tushunasiz.',
    courseMatch: 'Grammatika',
  },
}
