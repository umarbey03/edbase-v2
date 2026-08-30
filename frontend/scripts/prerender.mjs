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

// --- 1. Bo'sh qobiq: barcha ilova marshrutlari uchun ---------------------
await writeFile(new URL('app.html', distDir), withOrigin(shell), 'utf8')

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
