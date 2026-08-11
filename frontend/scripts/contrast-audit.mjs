/*
  KONTRAST AUDITI — yorug' (iOS uslubidagi) palitra uchun.

  Nima qiladi: `style.css` dagi token qiymatlarini shu yerga ko'chirib, kodda
  HAQIQATAN uchraydigan "matn ustida fon" juftlarini WCAG 2.1 formulasi bilan
  hisoblaydi. Shaffof tint (`bg-rose-500/10`) avval ota-fon ustiga
  kompozitsiya qilinadi — aks holda hisob yolg'on chiqadi.

  Talab: asosiy matn >= 4.5, ikkilamchi/katta matn >= 3.0, grafik >= 3.0.

  QACHON YURGIZILADI: `style.css` dagi rang tokenlaridan BIRORTASI
  o'zgarganda. Teskari shkala (kichik raqam = to'q) tufayli "bir daraja
  yorug'roq qilaman" degan kichik tuzatish jimgina o'qilmaydigan juftlik
  yaratishi mumkin — skript aynan shuni tutadi.

  Ishga tushirish (loyihada node yo'q bo'lsa Docker orqali):
      cd frontend && node scripts/contrast-audit.mjs
      docker run --rm -v "$PWD":/app -w /app node:22-alpine \
        node scripts/contrast-audit.mjs

  Yiqilgan juftlik bo'lsa chiqish kodi 1 bo'ladi (CI'ga ulash mumkin).

  ★ Qiymatlar SHU YERGA QO'LDA ko'chiriladi, `style.css` dan o'qilmaydi.
  Sabab: CSS'ni to'g'ri tahlil qilish uchun `@theme`, `color-mix()` va
  Tailwind'ning standart qiymatlarini ham hisoblash kerak bo'lardi — ya'ni
  brauzer dvigatelining yarmi. Ikki nusxa xavfi bor, lekin u ARZON:
  nomuvofiqlik bo'lsa audit yiqiladi yoki noto'g'ri "OK" beradi va shu
  faylda bitta qatorni tuzatish kifoya.
*/

import process from 'node:process'

// ----------------------------- palitra ------------------------------------

const T = {
  // Neytral sirtlar
  'ink-950': '#f4f6fb',
  'ink-900': '#ffffff',
  'ink-850': '#fbfcfe',
  'ink-800': '#f2f4f9',
  'ink-750': '#e9ecf5',
  'ink-700': '#dfe3ee',
  line: '#eceff5',
  'line-strong': '#dde1ec',

  // Matn
  'slate-50': '#0f1117',
  'slate-100': '#1b1d2a',
  'slate-200': '#2b2f40',
  'slate-300': '#4a5060',
  'slate-400': '#656d80',
  'slate-500': '#767f95',
  'slate-600': '#838ca0',
  'slate-700': '#c3c8d6',
  muted: '#656d80',
  dim: '#767f95',

  // Brend (indigo)
  'brand-100': '#17164f',
  'brand-200': '#26248a',
  'brand-300': '#3331b4',
  'brand-400': '#4240d2',
  'brand-500': '#4f4de8',
  'brand-600': '#3f3dc0',
  'brand-700': '#302e94',
  'brand-800': '#22216d',
  'brand-900': '#1a1a55',
  'on-brand': '#ffffff',

  // rose / red
  'rose-50': '#3f0f09',
  'rose-100': '#55160f',
  'rose-200': '#7a271a',
  'rose-300': '#912018',
  'rose-400': '#b42318',
  'rose-500': '#d92d20',
  'rose-600': '#a52117',
  'rose-700': '#85180f',
  'rose-800': '#fecdca',
  'rose-900': '#fee4e2',
  'rose-950': '#fef3f2',

  // amber
  'amber-50': '#3d1707',
  'amber-100': '#5c220a',
  'amber-200': '#7a2e0e',
  'amber-300': '#93370d',
  'amber-400': '#b54708',
  'amber-500': '#f79009',
  'amber-600': '#9a3c07',
  'amber-700': '#7f2f08',

  // green / emerald
  'green-50': '#022a1a',
  'green-100': '#05432a',
  'green-200': '#05603a',
  'green-300': '#066b41',
  'green-400': '#067647',
  'green-500': '#0a8055',
  'green-600': '#056038',
  'green-700': '#04482a',

  // sky / cyan
  'sky-50': '#062c41',
  'sky-100': '#0b4a6f',
  'sky-200': '#065986',
  'sky-300': '#026aa2',
  'sky-400': '#0079b8',
  'sky-500': '#0ba5ec',
  'sky-600': '#026aa2',
  'sky-700': '#0b4a6f',

  // violet
  'violet-200': '#4a1fb8',
  'violet-400': '#6938ef',
  'violet-500': '#8b5cf6',

  // teal
  'teal-200': '#0f5f5a',
  'teal-400': '#107569',
  'teal-500': '#14b8a6',

  // orange
  'orange-200': '#93370d',
  'orange-400': '#b93815',
  'orange-500': '#f97316',

  // scrim
  scrim: '#101828',

  /*
    DIAGRAMMA (dataviz) palitrasi — `style.css` dagi `--color-chart-*`.

    Qiymatlar mavjud shkalalarga BOG'LANGAN (`var(--color-sky-400)` va h.k.),
    shuning uchun bu yerda ham AYNAN o'sha HEX'lar takrorlanadi. Nomlar
    alohida: audit jadvalida "grafik: chart-in" degan qator "grafik:
    green-400" dan tushunarliroq va u qaysi ekranni himoya qilayotgani
    ko'rinadi.

    ★ Bu qiymatlar `dataviz` metodikasi bilan hisoblangan (OKLab ΔE, CVD
    simulyatsiyasi) — sabablari `style.css` dagi izohda. Shu skript esa
    faqat WCAG kontrastini tekshiradi: ΔE ni tekshirish uchun
    `dataviz` ko'nikmasidagi `validate_palette.js` yurgiziladi.
  */
  'chart-plan': '#0079b8', // = sky-400
  'chart-in': '#067647', // = green-400
  'chart-out': '#b42318', // = rose-400
  'chart-other': '#6938ef', // = violet-400
  'chart-neutral': '#4a5060', // = slate-300

  // "Qarz yoshi" ordinal shkalasi (bitta ton, yorug'dan to'qqa).
  'chart-age-1': '#d92d20', // = rose-500
  'chart-age-2': '#b42318', // = rose-400
  'chart-age-3': '#7a271a', // = rose-200
  'chart-age-4': '#55160f', // = rose-100
}

/** Jonli dars sahnasi (`[data-surface='stage']`) — to'q sirt. */
const STAGE = {
  'ink-950': '#0f1115',
  'ink-900': '#171a21',
  'ink-850': '#1c2027',
  'ink-800': '#22262f',
  'ink-750': '#2b303a',
  'ink-700': '#363c48',
  line: '#262b34',
  'line-strong': '#3a4150',
  'slate-50': '#ffffff',
  'slate-100': '#eef1f6',
  'slate-200': '#d7dce6',
  'slate-300': '#b6bdcc',
  'slate-400': '#98a1b2',
  'slate-500': '#7d8798',
  'slate-600': '#656f80',
  'slate-700': '#4d5666',
  muted: '#98a1b2',
  dim: '#7d8798',
  'brand-200': '#c7c8fd',
  'brand-300': '#a9aafb',
  'brand-400': '#8a8df8',
  'rose-100': '#fee4e2',
  'rose-200': '#fecdca',
  'rose-300': '#fda29b',
  'rose-400': '#f97066',
  'rose-500': '#f04438',
  'rose-600': '#d92d20',
  'rose-950': '#3f1512',
  'amber-100': '#fef0c7',
  'amber-200': '#fedf89',
  'amber-300': '#fec84b',
  'amber-400': '#fdb022',
  'green-200': '#abefc6',
  'green-400': '#47cd89',
  'emerald-400': '#47cd89',
}

// --------------------------- WCAG hisoblash --------------------------------

function parseHex(hex) {
  const value = hex.replace('#', '')
  const full =
    value.length === 3
      ? value
          .split('')
          .map((char) => char + char)
          .join('')
      : value
  return [
    Number.parseInt(full.slice(0, 2), 16),
    Number.parseInt(full.slice(2, 4), 16),
    Number.parseInt(full.slice(4, 6), 16),
  ]
}

function luminance([r, g, b]) {
  const channel = (raw) => {
    const c = raw / 255
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b)
}

function ratio(fg, bg) {
  const a = luminance(parseHex(fg))
  const b = luminance(parseHex(bg))
  const [hi, lo] = a > b ? [a, b] : [b, a]
  return (hi + 0.05) / (lo + 0.05)
}

/** Shaffof qatlamni ota-fon ustiga qo'yish (`bg-rose-500/10`). */
function over(fgHex, alpha, bgHex) {
  const f = parseHex(fgHex)
  const b = parseHex(bgHex)
  const mix = f.map((channel, index) => Math.round(channel * alpha + b[index] * (1 - alpha)))
  return `#${mix.map((c) => c.toString(16).padStart(2, '0')).join('')}`
}

// --------------------------- tekshiruv ro'yxati ----------------------------

/**
 * Har qator: [tavsif, matn tavsifi, fon tavsifi, minimal talab].
 *
 * Fon: token nomi yoki `['token', alpha, 'ota-token']` (tint).
 * Matn: token nomi yoki `['token', alpha]` — SHAFFOF MATN (masalan
 *       `text-white/75`); u avtomatik ravishda O'SHA QATORNING foni ustiga
 *       kompozitsiya qilinadi, ya'ni ota-fonni ikkinchi marta yozish
 *       kerak emas va ular bir-biriga mos kelmasligi mumkin emas.
 */
function build(tokens, label) {
  const c = (name) => tokens[name] ?? T[name] ?? name
  const bg = (spec) => (Array.isArray(spec) ? over(c(spec[0]), spec[1], c(spec[2])) : c(spec))

  const rows = [
    // --- neytral matn ---
    ['sarlavha (slate-50) / kartochka', 'slate-50', 'ink-900', 4.5],
    ['asosiy matn (slate-100) / kartochka', 'slate-100', 'ink-900', 4.5],
    ['asosiy matn (slate-100) / sahifa', 'slate-100', 'ink-950', 4.5],
    ['asosiy matn (slate-100) / hover', 'slate-100', 'ink-800', 4.5],
    ['quyi sarlavha (slate-200) / kartochka', 'slate-200', 'ink-900', 4.5],
    ['tana matni (slate-300) / kartochka', 'slate-300', 'ink-900', 4.5],
    ['ikkilamchi (slate-400 / muted) / kartochka', 'slate-400', 'ink-900', 4.5],
    ['ikkilamchi (slate-400) / sahifa', 'slate-400', 'ink-950', 4.5],
    ['ikkilamchi (slate-400) / ichki blok', 'slate-400', 'ink-800', 4.5],
    ['ikonka/placeholder (slate-500)', 'slate-500', 'ink-900', 3],
    ['uchinchi daraja (dim)', 'dim', 'ink-900', 3],
    ['uchinchi daraja (dim) / sahifa', 'dim', 'ink-950', 3],
    ['dekorativ (slate-600)', 'slate-600', 'ink-900', 3],

    // --- brend ---
    ['on-brand / brand-500 (asosiy tugma)', 'on-brand', 'brand-500', 4.5],
    ['on-brand / brand-600 (hover)', 'on-brand', 'brand-600', 4.5],
    ['on-brand / brand-700 (active)', 'on-brand', 'brand-700', 4.5],
    ['text-brand-500 / kartochka', 'brand-500', 'ink-900', 4.5],
    ['text-brand-400 / kartochka', 'brand-400', 'ink-900', 4.5],
    ['text-brand-400 / brand tint 12%', 'brand-400', ['brand-500', 0.12, 'ink-900'], 4.5],
    ['text-brand-300 / brand tint 12%', 'brand-300', ['brand-500', 0.12, 'ink-900'], 4.5],
    ['text-brand-300 / brand tint 16%', 'brand-300', ['brand-500', 0.16, 'ink-900'], 4.5],
    ['text-brand-200 / brand tint 10%', 'brand-200', ['brand-500', 0.1, 'ink-900'], 4.5],

    // --- rose / red (xato) ---
    ['text-rose-400 / kartochka', 'rose-400', 'ink-900', 4.5],
    ['text-rose-400 / sahifa', 'rose-400', 'ink-950', 4.5],
    ['text-rose-400 / rose tint 10%', 'rose-400', ['rose-500', 0.1, 'ink-900'], 4.5],
    ['text-rose-200 / rose tint 10%', 'rose-200', ['rose-500', 0.1, 'ink-900'], 4.5],
    ['text-rose-200 / rose tint 15%', 'rose-200', ['rose-500', 0.15, 'ink-900'], 4.5],
    ['text-rose-100 / rose tint 20%', 'rose-100', ['rose-500', 0.2, 'ink-900'], 4.5],
    ['text-rose-100 / rose-950 (toast)', 'rose-100', 'rose-950', 4.5],
    ['text-rose-500 (moliya raqami)', 'rose-500', 'ink-900', 4.5],
    ['text-rose-300 / rose tint 10%', 'rose-300', ['rose-500', 0.1, 'ink-900'], 4.5],
    ['nishon: text-rose-200 / rose tint 12%', 'rose-200', ['rose-500', 0.12, 'ink-900'], 4.5],
    ['oq matn / bg-red-400 (danger tugma)', 'on-brand', 'rose-400', 4.5],
    ['oq matn / bg-red-500 (danger hover)', 'on-brand', 'rose-500', 4.5],
    ['oq matn / bg-red-600 (danger active)', 'on-brand', 'rose-600', 4.5],

    // --- amber (ogohlantirish) ---
    ['text-amber-400 / kartochka', 'amber-400', 'ink-900', 4.5],
    ['text-amber-400 / amber tint 10%', 'amber-400', ['amber-500', 0.1, 'ink-900'], 4.5],
    ['text-amber-200 / amber tint 10%', 'amber-200', ['amber-500', 0.1, 'ink-900'], 4.5],
    ['text-amber-200 / amber tint 15%', 'amber-200', ['amber-500', 0.15, 'ink-900'], 4.5],
    ['text-amber-300 / amber tint 10%', 'amber-300', ['amber-500', 0.1, 'ink-900'], 4.5],
    ['text-amber-100 / amber tint 10%', 'amber-100', ['amber-500', 0.1, 'ink-900'], 4.5],
    ['nishon: text-amber-200 / amber tint 12%', 'amber-200', ['amber-500', 0.12, 'ink-900'], 4.5],
    ['slate-100 / bg-amber-500 (qo‘l ko‘tarilgan)', 'slate-100', 'amber-500', 4.5],

    // --- green / emerald (muvaffaqiyat) ---
    ['text-green-400 / kartochka', 'green-400', 'ink-900', 4.5],
    ['text-green-400 / green tint 10%', 'green-400', ['green-500', 0.1, 'ink-900'], 4.5],
    ['text-green-200 / green tint 10%', 'green-200', ['green-500', 0.1, 'ink-900'], 4.5],
    ['text-green-300 / kartochka', 'green-300', 'ink-900', 4.5],
    ['text-green-500 (moliya raqami)', 'green-500', 'ink-900', 4.5],
    ['nishon: text-green-200 / green tint 12%', 'green-200', ['green-500', 0.12, 'ink-900'], 4.5],
    ['oq matn / bg-green-400 (success tugma)', 'on-brand', 'green-400', 4.5],
    ['oq matn / bg-green-500 (success hover)', 'on-brand', 'green-500', 4.5],
    ['oq matn / bg-green-600 (success active)', 'on-brand', 'green-600', 4.5],
    ['grafik: stroke-green-500 / kartochka', 'green-500', 'ink-900', 3],

    // --- sky / cyan ---
    ['text-sky-300 / sky tint 15%', 'sky-300', ['sky-500', 0.15, 'ink-900'], 4.5],
    ['text-sky-200 / sky tint 15%', 'sky-200', ['sky-500', 0.15, 'ink-900'], 4.5],
    ['nishon: text-sky-200 / sky tint 12%', 'sky-200', ['sky-500', 0.12, 'ink-900'], 4.5],
    ['text-sky-400 / kartochka (grafik)', 'sky-400', 'ink-900', 3],

    // --- avatar palitrasi (to'q pastel to'ldirish + oq harf) ---
    ['avatar: oq harf / brand-400', 'on-brand', 'brand-400', 4.5],
    ['avatar: oq harf / green-400', 'on-brand', 'green-400', 4.5],
    ['avatar: oq harf / amber-400', 'on-brand', 'amber-400', 4.5],
    ['avatar: oq harf / rose-400', 'on-brand', 'rose-400', 4.5],
    ['avatar: oq harf / sky-400', 'on-brand', 'sky-400', 4.5],
    ['avatar: oq harf / violet-400', 'on-brand', 'violet-400', 4.5],
    ['avatar: oq harf / teal-400', 'on-brand', 'teal-400', 4.5],
    ['avatar: oq harf / orange-400', 'on-brand', 'orange-400', 4.5],

    // --- chegara va grafik elementlar (WCAG 1.4.11) ---
    ['chegara: line-strong / kartochka', 'line-strong', 'ink-900', 1.2],
    ['fokus halqasi: brand-400 / sahifa', 'brand-400', 'ink-950', 3],
    ['jadval th (slate-400) / kartochka', 'slate-400', 'ink-900', 4.5],
    ['input matni (slate-100) / input foni (ink-900)', 'slate-100', 'ink-900', 4.5],
    ['input placeholder (slate-500) / ink-900', 'slate-500', 'ink-900', 3],
    ['dekorativ (slate-600) / sahifa', 'slate-600', 'ink-950', 3],
    ['text-brand-500 / brand tint 16%', 'brand-500', ['brand-500', 0.16, 'ink-900'], 4.5],
    ['nishon neutral: slate-400 / ink-800', 'slate-400', 'ink-800', 4.5],
    ['nishon student: slate-300 / ink-800', 'slate-300', 'ink-800', 4.5],
    ['nishon: text-violet-200 / violet tint 12%', 'violet-200', ['violet-500', 0.12, 'ink-900'], 4.5],
    ['nishon: text-teal-200 / teal tint 12%', 'teal-200', ['teal-500', 0.12, 'ink-900'], 4.5],
    ['nishon: text-orange-200 / orange tint 12%', 'orange-200', ['orange-500', 0.12, 'ink-900'], 4.5],
    ['nishon: text-brand-300 / brand tint 12% (indigo)', 'brand-300', ['brand-500', 0.12, 'ink-900'], 4.5],
    ['nishon: text-sky-200 / sky tint 12% (cyan)', 'sky-200', ['sky-500', 0.12, 'ink-900'], 4.5],
    ['grafik: sky-400 nuqta / ink-900', 'sky-400', 'ink-900', 3],
    ['grafik: brand-500 chegara / ink-900', 'brand-500', 'ink-900', 3],
    ['grafik: rose-500 nuqta / ink-900', 'rose-500', 'ink-900', 3],
    ['grafik: amber-500 nuqta / ink-900', 'amber-500', 'ink-900', 1.5],
    /*
      `BaseBadge` nuqtasi O'Z pastel foni ustida.

      ★ TALAB 1.5, 3.0 EMAS — va bu "yaxshi ko'rinadi" degani emas:
      nuqta `aria-hidden="true"` va DOIM o'z matni yonida turadi ("Jonli",
      "Yordamchi"), ya'ni ma'lumotni YETKAZUVCHI element emas — WCAG 1.4.11
      faqat kontentni tushunish uchun ZARUR grafikaga 3:1 talab qiladi.
      To'yingan sariq/moviy pastel fonda fizik jihatdan 3:1 ga chiqmaydi
      (sariq eng yorug' rang), ularni 3:1 ga majburlash nuqtani to'q
      jigarrang dog'ga aylantirardi va nishon "jonli" belgisini yo'qotardi.
    */
    ['nuqta rose-500 / rose tint 12%', 'rose-500', ['rose-500', 0.12, 'ink-900'], 1.5],
    ['nuqta green-500 / green tint 12%', 'green-500', ['green-500', 0.12, 'ink-900'], 1.5],
    ['nuqta sky-500 / sky tint 12%', 'sky-500', ['sky-500', 0.12, 'ink-900'], 1.5],
    ['nuqta brand-500 / brand tint 12%', 'brand-500', ['brand-500', 0.12, 'ink-900'], 1.5],
    ['nuqta amber-500 / amber tint 12%', 'amber-500', ['amber-500', 0.12, 'ink-900'], 1.5],
    ['nuqta slate-500 / ink-800 (neutral)', 'slate-500', 'ink-800', 1.5],

    /* ---------------- MOLIYA DIAGRAMMASI (dataviz palitrasi) --------------
       Ilgari bu ranglar `finance-view.ts` da qotib qolgan HEX edi va oq
       kartochkada 1.7–2.2:1 berardi (`#f2c84b` 1.71 · `#22c55e` 2.03 ·
       `#fb923c` 2.17) — ustunlar ham, KPI raqamlari ham ko'rinmasdi.

       ★ 3.0 talab qo'yiladi, 4.5 EMAS: bular GRAFIK elementlar (ustun,
       nuqta, kartochkaning 3px chizig'i) — WCAG 1.4.11. Diagrammadagi
       MATN (KPI raqami, guruh nomi, o'q yorliqlari) rang tokenida EMAS,
       SIYOH tokenida (`slate-50` / `slate-100` / `muted`) va u yuqoridagi
       neytral qatorlarda allaqachon tekshiriladi — aynan shuning uchun
       raqamlar rangdan siyohga o'tkazildi.                              */
    ['grafik: chart-plan (reja ustuni) / kartochka', 'chart-plan', 'ink-900', 3],
    ['grafik: chart-in (yig‘ilgan) / kartochka', 'chart-in', 'ink-900', 3],
    ['grafik: chart-out (qarz) / kartochka', 'chart-out', 'ink-900', 3],
    ['grafik: chart-other (chegirma) / kartochka', 'chart-other', 'ink-900', 3],
    ['grafik: chart-neutral (foiz, balans) / kartochka', 'chart-neutral', 'ink-900', 3],
    ['grafik: chart-age-1 (0-30 kun) / kartochka', 'chart-age-1', 'ink-900', 3],
    ['grafik: chart-age-2 (31-60) / kartochka', 'chart-age-2', 'ink-900', 3],
    ['grafik: chart-age-3 (61-90) / kartochka', 'chart-age-3', 'ink-900', 3],
    ['grafik: chart-age-4 (90+) / kartochka', 'chart-age-4', 'ink-900', 3],

    /*
      ORDINAL SHKALANING QADAM ORALIG'I.

      Bu WCAG talabi EMAS — o'z qoidamiz. "Qarz yoshi" shkalasi ordinal
      (bitta ton, yorug'dan to'qqa) va uning ma'nosi QADAMLAR ORASIDAGI
      farqda: agar kimdir keyinchalik `chart-age-2` ni "chiroyliroq" qilib
      `chart-age-1` ga yaqinlashtirsa, to'rt guruh ikkitaga qo'shilib
      ketardi va audit buni SEZMASDI (har biri fon bilan baribir 3:1 dan
      yuqori bo'lardi). Shuning uchun qo'shni qadamlar BIR-BIRIGA nisbatan
      ham tekshiriladi.

      1.25 — hozirgi eng kichik oraliqdan bir oz past chegara; `dataviz`
      ning OKLCH ΔL >= 0.06 talabiga taxminan mos keladigan WCAG ekvivalenti.
    */
    ['ordinal qadam: age-1 ↔ age-2', 'chart-age-1', 'chart-age-2', 1.25],
    ['ordinal qadam: age-2 ↔ age-3', 'chart-age-2', 'chart-age-3', 1.25],
    ['ordinal qadam: age-3 ↔ age-4', 'chart-age-3', 'chart-age-4', 1.25],

    /* ------------- KO'RINMAY QOLGAN ELEMENTLAR (2026-08-11 auditi) ------
       Uchtasi ham "oq ustiga oq" naqshining qoldig'i edi:
         • `FinanceBar` yo'li `bg-white/[0.07]` — 1.02:1, bar doim to'la
           ko'rinardi;
         • `StudentHomePage` davomat halqasi `stroke-ink-800` — 1.06:1;
         • `StudentLearnPage` halqasi allaqachon `ink-750` ga o'tkazilgan.
       Talab 1.15 — `line-strong / kartochka` qatoridagi 1.2 mantig'i bilan
       bir xil: bu SIRT ajratgichi, matn emas; asosiy vazifasi "bor-yo'qligi
       ko'rinsin".                                                        */
    ['yo‘l/halqa: ink-750 / kartochka', 'ink-750', 'ink-900', 1.15],

    /* --------- `BaseButton` `warning` varianti (YANGI, ConfirmDialog) ----
       Hover/active TO'QLASHADI (`amber-300`/`amber-200`), yorug'lashmaydi:
       `amber-500` to'yingan sariq va oq matn u ustida 2.42:1 berardi
       (sabab `BaseButton` izohida).                                      */
    ['oq matn / bg-amber-400 (warning tugma)', 'on-brand', 'amber-400', 4.5],
    ['oq matn / bg-amber-300 (warning hover)', 'on-brand', 'amber-300', 4.5],
    ['oq matn / bg-amber-200 (warning active)', 'on-brand', 'amber-200', 4.5],
    /*
      `ConfirmDialog` ning ton doirasidagi IKONKA (20px, grafik → 1.4.11).
      Ilgari `text-amber-500` edi va o'z tinti ustida 2.12:1 berardi.
      Qizil/indigo da 500 QOLADI (4.03 / 4.97) — istisno faqat sariqda.
    */
    ['ikonka: amber-400 / amber tint 12%', 'amber-400', ['amber-500', 0.12, 'ink-900'], 3],
    ['ikonka: rose-500 / rose tint 12%', 'rose-500', ['rose-500', 0.12, 'ink-900'], 3],
    ['ikonka: brand-500 / brand tint 12%', 'brand-500', ['brand-500', 0.12, 'ink-900'], 3],

    /* ----------------- `RecordingCard` afisha pillari -------------------
       `bg-black/55` va `bg-black/65` edi. Afisha maydoni HAQIQIY video
       kadri emas — `ink-800 -> ink-750` gradienti, ya'ni DOIM yorug';
       qora pill u ustida "yamoq" bo'lib turardi. Endi scrim tokeni.
       Fon sifatida gradientning eng YORUG' uchi (`ink-800`) olinadi —
       eng yomon holat.                                                   */
    ['RecordingCard pill: oq matn / slate-900 70% (ink-800 ustida)', 'on-brand', ['scrim', 0.7, 'ink-800'], 4.5],

    /* -------------- `StudentTabBar` (yorug' temaga o'tkazildi) ----------
       Fon `rgb(5 30 45 / .96)` (eski navy) bo'lib qolgan edi; unda faol
       tab 2.90:1, nofaol 4.26:1 berardi. Endi oq sirt (96%).             */
    ['tabbar faol: brand-500 / ink-900 96%', 'brand-500', ['ink-900', 0.96, 'ink-950'], 4.5],
    /*
      🔴 Nofaol tab `text-dim` edi va bu QATOR SHU AUDITDA YIQILDI: oq fonda
      ham 4.01:1 (navy fonda 4.26:1) — 10px yozuv uchun WCAG AA'dan past.
      `slate-400` bilan 5.20:1.
    */
    ['tabbar nofaol: slate-400 / ink-900 96%', 'slate-400', ['ink-900', 0.96, 'ink-950'], 4.5],
  ]

  const stageRows = [
    ['STAGE asosiy matn (slate-100) / ink-900', 'slate-100', 'ink-900', 4.5],
    ['STAGE ikkilamchi (slate-400) / ink-900', 'slate-400', 'ink-900', 4.5],
    ['STAGE dim / ink-950', 'dim', 'ink-950', 3],
    ['STAGE text-rose-400 / ink-900', 'rose-400', 'ink-900', 4.5],
    ['STAGE text-rose-200 / rose tint 15%', 'rose-200', ['rose-500', 0.15, 'ink-900'], 4.5],
    ['STAGE text-amber-200 / amber tint 15%', 'amber-200', ['amber-500', 0.15, 'ink-900'], 4.5],
    ['STAGE text-brand-200 / brand tint 15%', 'brand-200', ['brand-500', 0.15, 'ink-900'], 4.5],
    ['STAGE emerald-400 (mikrofon) / ink-900', 'emerald-400', 'ink-900', 4.5],
    ['STAGE text-rose-100 / rose-950 (toast)', 'rose-100', 'rose-950', 4.5],
    ['STAGE oq / bg-rose-600 (chiqish)', 'on-brand', 'rose-600', 4.5],
    ['STAGE ink-950 matn / bg-amber-500', 'ink-950', 'amber-500', 4.5],
    /*
      Jonli dars chatida O'Z xabarining vaqti (`ChatMessageRow`).
      `text-white/60` edi — `bg-brand-600` (#3f3dc0) ustida 3.91:1, 10px
      matn uchun WCAG AA'dan past. `/75` bilan o'qiladi.
      ★ `brand-600` sahna temasida QAYTA belgilanmagan, ya'ni qiymat ikki
      sirtda ham bir xil — shuning uchun juftlik bitta jadvalda yetadi.
    */
    ['STAGE o‘z xabari vaqti (oq 75%) / brand-600', ['on-brand', 0.75], 'brand-600', 4.5],
  ]

  const list = label === 'STAGE' ? stageRows : rows
  return list.map(([name, fgSpec, bgSpec, min]) => {
    const bgHex = bg(bgSpec)
    // Shaffof matn (`text-white/75`) fonning USTIGA qo'yiladi — fon
    // yuqorida allaqachon hisoblangani uchun ota-fon takrorlanmaydi.
    const fgHex = Array.isArray(fgSpec) ? over(c(fgSpec[0]), fgSpec[1], bgHex) : c(fgSpec)
    const value = ratio(fgHex, bgHex)
    return { name, fgHex, bgHex, value, min, ok: value >= min }
  })
}

// ------------------------------- chiqish -----------------------------------

/*
  `console.log` EMAS: eslint konfiguratsiyasida `no-console` yoqilgan va
  `npm run lint` `--max-warnings 0` bilan yuriladi, ya'ni bitta `console.log`
  butun lint'ni yiqitardi. `process.stdout.write` esa oddiy chiqish oqimi.
*/
function write(line = '') {
  process.stdout.write(`${line}\n`)
}

function print(title, rows) {
  const widths = [
    Math.max(title.length, ...rows.map((row) => row.name.length)),
    9,
    9,
    7,
    6,
    5,
  ]
  const pad = (text, index, right = false) =>
    right ? String(text).padStart(widths[index]) : String(text).padEnd(widths[index])

  write(`\n### ${title}\n`)
  write(
    `| ${pad('Juftlik', 0)} | ${pad('Matn', 1)} | ${pad('Fon', 2)} | ${pad('Nisbat', 3, true)} | ${pad('Talab', 4, true)} | ${pad('Holat', 5)} |`,
  )
  write(
    `|${'-'.repeat(widths[0] + 2)}|${'-'.repeat(widths[1] + 2)}|${'-'.repeat(widths[2] + 2)}|${'-'.repeat(widths[3] + 2)}|${'-'.repeat(widths[4] + 2)}|${'-'.repeat(widths[5] + 2)}|`,
  )
  for (const row of rows) {
    write(
      `| ${pad(row.name, 0)} | ${pad(row.fgHex, 1)} | ${pad(row.bgHex, 2)} | ${pad(row.value.toFixed(2), 3, true)} | ${pad(row.min.toFixed(1), 4, true)} | ${pad(row.ok ? 'OK' : 'YIQ', 5)} |`,
    )
  }
  const failed = rows.filter((row) => !row.ok)
  write(`\n${rows.length} juftlik, ${failed.length} yiqilgan.`)
  return failed
}

const lightFailed = print('Yorug‘ sirt (asosiy tema)', build(T, 'LIGHT'))
const stageFailed = print('Jonli dars sahnasi ([data-surface=stage])', build(STAGE, 'STAGE'))

const total = lightFailed.length + stageFailed.length
if (total > 0) {
  write(`\nJAMI YIQILGAN: ${total}`)
  for (const row of [...lightFailed, ...stageFailed]) {
    write(`  - ${row.name}: ${row.value.toFixed(2)} < ${row.min}`)
  }
  process.exitCode = 1
} else {
  write('\nHAMMASI TALABGA MOS.')
}
