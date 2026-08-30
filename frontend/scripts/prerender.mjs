import { readFile, rm, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'

/*
  ══════════════════════════════════════════════════════════════════════════
  PRERENDER — `dist/index.html` GA LANDING MATNINI YOZADI
  ══════════════════════════════════════════════════════════════════════════

  `npm run build` ketma-ketligi:

     1) vite build                 -> dist/          (brauzer uchun bundle)
     2) vite build --ssr           -> dist-ssg/      (Node uchun render)
     3) node scripts/prerender.mjs -> SHU FAYL

  Natijada `dist/` da IKKITA HTML bo'ladi:

     index.html  — landing matni ICHIGA yozilgan (faqat `/` uchun)
     app.html    — bo'sh qobiq (qolgan BARCHA marshrutlar uchun)

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 NEGA IKKITA FAYL, BITTASI EMAS                                  │
  └────────────────────────────────────────────────────────────────────┘
  nginx SPA fallback'i har qanday noma'lum manzilga AYNI HTML ni beradi.
  Agar to'ldirilgan `index.html` hamma joyga berilsa, foydalanuvchi
  `/login` ni ochganda brauzer avval LANDING ni chizib, keyin uni kirish
  sahifasiga almashtirardi — ya'ni bir zumga butunlay boshqa sahifa
  chaqnab ketardi.

  Shuning uchun nginx'da: `/` -> index.html, qolgani -> app.html
  (`nginx.conf` dagi "PRERENDER" bloki).

  ★ `/` da ALMASHTIRISH KO'RINMAYDI: brauzer avval prerender qilingan
    landing'ni chizadi, keyin Vue AYNI komponentni AYNI ma'lumot bilan
    qaytadan chizadi — piksellar bir xil.
*/

const distDir = new URL('../dist/', import.meta.url)
const ssgDir = new URL('../dist-ssg/', import.meta.url)

/** Zaxira qiymat — `index.html` ichida ham shu manzil yozilgan. */
const FALLBACK_ORIGIN = 'https://zinnuronline.uz'

/** `app.html` sarlavhasi — kalit so'zsiz, faqat brend nomi. */
const BRAND_NAME = 'ZIN-NUR ONLINE'

const rawOrigin = (process.env['VITE_SITE_URL'] ?? '').trim().replace(/\/+$/, '')
const origin = rawOrigin.length > 0 ? rawOrigin : FALLBACK_ORIGIN

const shellPath = new URL('index.html', distDir)
const shell = await readFile(shellPath, 'utf8')

/*
  MANZILNI ALMASHTIRISH.

  `index.html` da kanonik manzil va Open Graph teglari QO'LDA yozilgan
  (build'siz ochilganda ham to'g'ri bo'lsin uchun). Bu yerda ular
  `VITE_SITE_URL` qiymatiga keltiriladi — ya'ni haqiqiy manba `.env`.

  ⚠️ `replaceAll` — teg bittadan ko'p: canonical, og:url, og:image va
     JSON-LD ichidagi `@id` lar.
*/
function withOrigin(html) {
  return origin === FALLBACK_ORIGIN ? html : html.replaceAll(FALLBACK_ORIGIN, origin)
}

/*
  ══════════════════════════════════════════════════════════════════════════
  SARLAVHA DRIFT'IGA QARSHI TEKSHIRUV
  ══════════════════════════════════════════════════════════════════════════

  Bosh sahifa sarlavhasi ikki joyda yozilgan: `index.html` dagi `<title>`
  (qidiruv tizimi shuni ko'radi) va `shared/config/seo.ts` dagi
  `LANDING_TITLE` (router ilova ichidan qaytganda shuni tiklaydi).

  Takrorlangan matn ertami-kechmi ajralib ketadi. Ajralganda esa hech
  narsa "buzilmaydi" — shunchaki tabdagi va qidiruvdagi sarlavha boshqa
  bo'lib qoladi va buni hech kim sezmaydi. Shuning uchun build ularni
  SOLISHTIRADI va farq bo'lsa YIQILADI.

  ⚠️ `.ts` faylni regex bilan o'qiymiz — bu qo'pol, lekin bu yerda
     to'g'ri tanlov: prerender Node'da ishlaydi va TypeScript'ni
     kompilyatsiya qilmasdan bitta konstantani o'qish uchun butun
     boshli asboblar zanjirini ko'tarish nomutanosib bo'lardi.
*/
const seoConfigPath = new URL('../src/shared/config/seo.ts', import.meta.url)
const seoConfig = await readFile(seoConfigPath, 'utf8')

const landingTitleMatch = seoConfig.match(/LANDING_TITLE\s*(?::[^=]*)?=\s*'([^']*)'/)

if (landingTitleMatch === null) {
  throw new Error(
    'prerender: `shared/config/seo.ts` dan LANDING_TITLE o\'qib bo\'lmadi. '
    + 'Konstanta nomi yoki yozilishi o\'zgargan bo\'lsa, shu yerdagi '
    + 'regexni ham yangilang.',
  )
}

/*
  ⚠️ REGEX QATOR BOSHIGA BOG'LANGAN (`^...$` + `m`) — ATAYLAB.

  Sodda `/<title>[\s\S]*?<\/title>/` BU YERDA ISHLAMAYDI: `index.html`
  dagi IZOHLARDA ham `<title>` so'zi matn sifatida uchraydi ("Jonli
  saytda `<title>` shunchaki..."). Sodda regex o'sha izohdan boshlab,
  haqiqiy `</title>` gacha bo'lgan hamma narsani ushlab olardi.

  (Aynan shu xato 2026-08-30 da qilinib, birinchi build'da topildi:
   drift tekshiruvi izoh matnini "sarlavha" deb hisoblab, yolg'on
   ogohlantirish bilan build'ni yiqitdi.)

  Haqiqiy teg — alohida qatorda va faqat bo'shliq bilan o'ralgan.
  Guruh 1 — chekinish (almashtirishda saqlanadi), guruh 2 — sarlavha.
*/
// `\r` — Windows'da tahrirlangan fayl CRLF bilan saqlansa, `$` dan
// oldin `\r` turadi va tekshiruv yolg'ondan yiqilardi.
const TITLE_TAG = /^([ \t]*)<title>([^<]*)<\/title>[ \t\r]*$/m

const shellTitleMatch = shell.match(TITLE_TAG)

if (shellTitleMatch === null) {
  throw new Error('prerender: index.html ichida <title> topilmadi.')
}

if (shellTitleMatch[2] !== landingTitleMatch[1]) {
  throw new Error(
    'prerender: bosh sahifa sarlavhasi IKKI joyda boshqa-boshqa.\n'
    + `  index.html:  ${shellTitleMatch[2]}\n`
    + `  seo.ts:      ${landingTitleMatch[1]}\n`
    + 'Ikkalasini bir xil qiling (sabab — seo.ts izohida).',
  )
}

/*
  ══════════════════════════════════════════════════════════════════════════
  1. BO'SH QOBIQ (`app.html`) — SEO TEGLARISIZ VA `noindex` BILAN
  ══════════════════════════════════════════════════════════════════════════

  🔴 2026-08-30 GACHA BU YERDA `shell` NING TO'LIQ NUSXASI YOZILARDI va
     bu jimgina ikkita muammo tug'dirardi:

     1) KANONIK MANZIL HAMMA JOYDA. `/login`, `/ustoz/...`, `/admin/...`
        va har qanday noma'lum manzil `<link rel="canonical" href="…/">`
        bilan kelardi, ya'ni har biri o'zini BOSH SAHIFA deb ko'rsatardi.
        Ustiga router JS orqali `noindex` qo'yardi.

        `noindex` + BOSHQA manzilga ko'rsatuvchi `canonical` — zid
        signal. Qidiruv tizimi sahifalarni kanonik manzilga
        birlashtirganda `noindex` ni O'SHA MANZILGA ko'chirishi mumkin.
        Ya'ni xavf shundaki, ilova sahifalari BOSH SAHIFANI indeksdan
        tushirib yuborardi.

     2) `noindex` FAQAT JS'DAN QO'YILARDI. Google JS'ni ishlatadi,
        Yandex esa ancha zaif — va `entry-ssg.ts` izohida yozilganidek,
        O'zbekistonda Yandex ulushi katta. Ya'ni himoya aynan kerak
        bo'lgan joyda ishonchsiz edi.

  Endi ikkalasi ham HTML'ning O'ZIDA hal qilinadi: SEO bloki kesiladi,
  o'rniga statik `noindex` qo'yiladi. JS umuman talab qilinmaydi.

  ★ `follow` SAQLANADI: sahifa indekslanmasin, lekin undagi havolalar
    kuzatilsin — aks holda ilova ichidan bosh sahifaga qaytadigan
    havolalar ham "o'lik" bo'lib qolardi.
*/
const SEO_BLOCK = /[ \t]*<!-- SEO:START -->[\s\S]*?<!-- SEO:END -->\n?/

if (!SEO_BLOCK.test(shell)) {
  // Jimgina o'tkazib yubormaymiz: belgilar yo'qolsa `app.html` yana
  // kanonik manzil va Open Graph teglarini olib qoladi — ya'ni yuqorida
  // tasvirlangan muammo qaytadi, lekin endi hech kim sezmaydi.
  throw new Error(
    'prerender: index.html ichidan `<!-- SEO:START -->` / `<!-- SEO:END -->` '
    + 'belgilari topilmadi. Ular `app.html` dan nimani kesib tashlashni '
    + 'belgilaydi — o\'chirmang, ko\'chirsangiz juftligini saqlang.',
  )
}

const appShell = withOrigin(shell)
  .replace(
    SEO_BLOCK,
    '    <meta name="robots" content="noindex, follow" />\n',
  )
  // Sarlavha ham almashtiriladi: kalit so'zli landing sarlavhasi kirish
  // sahifasida o'rinsiz va Vue yuklanguncha tabda o'sha turardi.
  // `$1` — chekinish: HTML formatlanishi buzilmasin.
  .replace(TITLE_TAG, `$1<title>${BRAND_NAME}</title>`)

await writeFile(new URL('app.html', distDir), appShell, 'utf8')

// --- 2. To'ldirilgan bosh sahifa ----------------------------------------
const { render } = await import(new URL('entry-ssg.js', ssgDir).href)
const body = await render()

const marker = '<div id="app"></div>'

if (!shell.includes(marker)) {
  // Jimgina o'tib ketmaymiz: prerender ishlamasa SEO ham ishlamaydi, va
  // buni faqat oylar keyin, natijalar tushib ketganda sezish mumkin.
  throw new Error(
    `prerender: index.html ichidan "${marker}" topilmadi. `
    + 'Ilova o\'rami o\'zgargan bo\'lsa, shu belgini yangilang.',
  )
}

await writeFile(
  shellPath,
  withOrigin(shell.replace(marker, `<div id="app">${body}</div>`)),
  'utf8',
)

// --- 3. Oraliq SSR bundle'i kerak emas ----------------------------------
// U `dist/` ga tushmaydi, lekin repo va Docker qatlamida yotib qolmasin.
await rm(ssgDir, { recursive: true, force: true })

console.log(
  `prerender: ${fileURLToPath(shellPath)} tayyor `
  + `(${(body.length / 1024).toFixed(1)} KB matn, origin: ${origin})`,
)
