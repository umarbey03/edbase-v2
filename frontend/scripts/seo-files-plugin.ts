import type { Plugin } from 'vite'

/*
  ══════════════════════════════════════════════════════════════════════════
  robots.txt VA sitemap.xml — BUILD PAYTIDA YASALADI
  ══════════════════════════════════════════════════════════════════════════

  ⚠️ 2026-08-30 gacha bu fayllarning IKKALASI HAM YO'Q EDI.

  Va ular shunchaki "yo'q" emas edi — nginx'ning SPA fallback qoidasi
  (`try_files $uri $uri/ /index.html`) `/robots.txt` so'roviga
  `200 OK` + `text/html` bilan javob berardi. Ya'ni qidiruv roboti
  direktiva o'rniga butun boshli HTML sahifani olardi.

  ★ NEGA `public/` DA STATIK FAYL EMAS: ikkala fayl ham saytning to'liq
    manzilini o'z ichiga oladi. Statik fayl bo'lsa, domen o'zgarganda
    (yoki staging'da) u eski/noto'g'ri manzilni ko'rsatib turardi va buni
    hech kim sezmasdi — bu fayllarga odam qaramaydi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 SINOV MUHITI INDEKSGA TUSHMASLIGI UCHUN HIMOYA                  │
  └────────────────────────────────────────────────────────────────────┘
  `VITE_SITE_URL` prod domeniga teng bo'lmasa (staging, sinov nusxasi,
  lokal build), robots.txt `Disallow: /` bilan yasaladi.

  Sabab: staging nusxasi indeksga tushsa, u asosiy sayt bilan AYNI
  kontent uchun raqobatlashadi va ikkalasi ham pastga tushadi. Bu
  xatoni keyin tuzatish oylar oladi — oldini olish esa bir shart.
*/

/** Prod domeni — robots.txt "ochiq" bo'ladigan YAGONA manzil. */
const PRODUCTION_ORIGIN = 'https://zinnuronline.uz'

/**
 * Sitemap'ga tushadigan sahifalar.
 *
 * 🔴 FAQAT OMMAGA OCHIQ VA INDEKSLANADIGAN MANZILLAR. Ilova ichidagi
 * sahifalar (`/ustoz/...`, `/admin/...`) bu yerga TUSHMAYDI: ular
 * autentifikatsiya ortida va robot ularga baribir kira olmaydi.
 *
 * ⚠️ `/login` ham ATAYLAB YO'Q. U ochiq, lekin uning qidiruvda
 *    chiqishidan hech kimga foyda yo'q: kirish sahifasiga kelgan odam
 *    allaqachon mijoz. Sitemap — "mana shu sahifalarni ko'rsating"
 *    degan tavsiya, ro'yxatga hamma narsani tiqish uni zaiflashtiradi.
 */
const PAGES: readonly { path: string, changefreq: string, priority: string }[] = [
  { path: '/', changefreq: 'weekly', priority: '1.0' },
]

function buildRobots(origin: string, isProduction: boolean): string {
  if (!isProduction) {
    return [
      '# SINOV MUHITI — indekslash TAQIQLANGAN.',
      '# Sabab: staging asosiy sayt bilan bir xil kontent uchun',
      '# raqobatlashib, ikkalasini ham pastga tushiradi.',
      'User-agent: *',
      'Disallow: /',
      '',
    ].join('\n')
  }

  return [
    '# ZIN-NUR ONLINE',
    '#',
    '# ⚠️ BU YERGA MAXFIY YO\'LLAR YOZILMAYDI ("Disallow: /admin" kabi).',
    '#    robots.txt — OCHIQ fayl, uni istalgan odam o\'qiy oladi. Maxfiy',
    '#    yo\'lni bu yerda sanash — hujumchiga xarita berish bilan teng.',
    '#    Ilova sahifalari autentifikatsiya bilan himoyalangan, indeksga',
    '#    tushmasligi esa ularning `noindex` metasi bilan hal qilinadi.',
    'User-agent: *',
    'Allow: /',
    '',
    `Sitemap: ${origin}/sitemap.xml`,
    '',
  ].join('\n')
}

function buildSitemap(origin: string): string {
  // Sana — build kuni. Vaqt qismisiz: sitemap uchun kun aniqligi yetarli
  // va har build'da soat o'zgarib turishi robotni chalg'itadi.
  const today = new Date().toISOString().slice(0, 10)

  const urls = PAGES.map(page => [
    '  <url>',
    `    <loc>${origin}${page.path}</loc>`,
    `    <lastmod>${today}</lastmod>`,
    `    <changefreq>${page.changefreq}</changefreq>`,
    `    <priority>${page.priority}</priority>`,
    '  </url>',
  ].join('\n')).join('\n')

  return [
    '<?xml version="1.0" encoding="UTF-8"?>',
    '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
    urls,
    '</urlset>',
    '',
  ].join('\n')
}

/**
 * Tasdiqlash kodi ko'rinishidan to'g'rimi.
 *
 * ┌────────────────────────────────────────────────────────────────────┐
 * │ ⚠️ QIYMATNI QO'LDA EKRANLAMAYMIZ — ATAYLAB                         │
 * └────────────────────────────────────────────────────────────────────┘
 * Vite `transformIndexHtml` dan qaytgan `attrs` qiymatlarini O'ZI
 * ekranlaydi. Qo'shimcha ekranlash IKKI MARTA bajarilib, `"` belgisini
 * `&amp;quot;` ga aylantirardi — ya'ni kod jimgina buzilardi.
 * (Bu xato 2026-08-30 da aynan shu yerda qilinib, sinovda topildi.)
 *
 * Shuning uchun bu yerda ekranlash emas, TEKSHIRUV bor: odam `.env` ga
 * butun boshli `<meta ... content="...">` tegini ko'chirib qo'ysa,
 * qiymat "texnik jihatdan xavfsiz" bo'ladi (Vite uni matnga aylantiradi),
 * lekin TASDIQLASH ISHLAMAYDI va sababi hech qayerda ko'rinmaydi.
 * Ogohlantirish aynan shu holatni ushlaydi.
 */
function looksLikeRawTag(value: string): boolean {
  return /[<>"\s]/.test(value)
}

/** `robots.txt`, `sitemap.xml`, IndexNow kaliti va tasdiqlash teglari. */
export function seoFilesPlugin(): Plugin {
  return {
    name: 'zinnur-seo-files',

    // Faqat `vite build` da ishlaydi; dev serverda bu fayllar kerak emas.
    apply: 'build',

    /*
      ══════════════════════════════════════════════════════════════════
       QIDIRUV TIZIMIDA TASDIQLASH TEGLARI
      ══════════════════════════════════════════════════════════════════

      Google Search Console va Yandex Webmaster saytni "sizniki" deb
      tan olishi uchun sahifada ular bergan `<meta>` teg turishi kerak.

      ★ NEGA `.env` DAN, `index.html` GA QO'LDA YOZILMAY: kodni
        o'zgartirish, PR ochish va deploy qilish — bitta kod satri uchun
        uch qadam. `.env` da esa qiymatni qo'yib, saytni qayta yig'ish
        yetarli. Kodga tegilmaydi.

      🔴 QIYMAT BO'SH BO'LSA TEG UMUMAN QO'YILMAYDI. Bo'sh
         `content=""` bilan turgan teg tasdiqlashni ISHLAMAY qoldiradi
         va sababi ko'rinmaydi — odam kodni to'g'ri deb o'ylab, boshqa
         joydan xato qidiradi.
    */
    transformIndexHtml() {
      const tags: { tag: string, attrs: Record<string, string>, injectTo: 'head' }[] = []

      const sources: readonly { env: string, meta: string, label: string }[] = [
        {
          env: 'VITE_GOOGLE_SITE_VERIFICATION',
          meta: 'google-site-verification',
          label: 'Google Search Console',
        },
        {
          env: 'VITE_YANDEX_VERIFICATION',
          meta: 'yandex-verification',
          label: 'Yandex Webmaster',
        },
      ]

      for (const source of sources) {
        const value = (process.env[source.env] ?? '').trim()

        if (value.length === 0) continue

        if (looksLikeRawTag(value)) {
          /*
            ⚠️ `console.warn`, `this.warn` EMAS. `transformIndexHtml` —
            Vite'ning O'Z hook'i va oddiy funksiya ko'rinishida yozilganda
            unga Rollup plagin konteksti (`this`) BERILMAYDI. `this.warn`
            bu yerda `undefined` bo'lib, butun build'ni yiqitadi.
            (Aynan shu xato 2026-08-30 da qilinib, build'da topildi.)
          */
          console.warn(
            `[seo] ${source.env}: butun <meta> tegi emas, FAQAT content="..." `
            + `ichidagi qiymat yozilishi kerak. ${source.label} tasdiqlashi `
            + 'hozirgi qiymat bilan ISHLAMAYDI.',
          )
          continue
        }

        tags.push({
          tag: 'meta',
          attrs: { name: source.meta, content: value },
          injectTo: 'head',
        })
      }

      return tags
    },

    generateBundle(options) {
      /*
        🔴 SSR (prerender) BUILD'IDA O'TKAZIB YUBORILADI.

        `npm run build` Vite'ni IKKI marta chaqiradi: brauzer bundle'i va
        prerender uchun Node bundle'i. Bu qatorsiz robots.txt ikkinchi
        marta `dist-ssg/` ga ham yozilardi — u yerdan hech qayerga
        bormaydi, lekin build chiqishini chalkashtirardi.
      */
      if (options.dir?.includes('dist-ssg') === true) return

      const raw = process.env['VITE_SITE_URL'] ?? ''
      // Oxiridagi "/" olib tashlanadi: manzillar `${origin}${path}` bo'lib
      // yopishtiriladi va ikkita "/" hosil bo'lib qolmasin.
      const origin = raw.trim().replace(/\/+$/, '')

      // Qiymat berilmagan bo'lsa ham SINOV deb hisoblaymiz — noto'g'ri
      // manzilli sitemap chiqargandan ko'ra indekslashni to'xtatgan yaxshi.
      const isProduction = origin === PRODUCTION_ORIGIN

      this.emitFile({
        type: 'asset',
        fileName: 'robots.txt',
        source: buildRobots(origin, isProduction),
      })

      // Sinov muhitida sitemap ham yasalmaydi: u indekslash TAQIQLANGAN
      // saytning manzillarini e'lon qilib, qarama-qarshi signal berardi.
      if (isProduction) {
        this.emitFile({
          type: 'asset',
          fileName: 'sitemap.xml',
          source: buildSitemap(origin),
        })
      }

      /*
        INDEXNOW KALIT FAYLI.

        Protokol shunday ishlaydi: biz qidiruv tizimiga "shu manzilni
        qayta ko'ring" deb xabar yuboramiz va xabarda kalitni beramiz.
        Qidiruv tizimi esa `https://sayt/<kalit>.txt` faylini o'qib,
        ichida O'SHA kalit turganini ko'radi — ya'ni xabar sayt egasidan
        kelganiga ishonch hosil qiladi.

        ★ NEGA UMUMAN QO'SHILDI: Search Console va Webmaster akkaunt
          talab qiladi, IndexNow esa YO'Q. Bu — ro'yxatdan o'tishni
          kutmasdan Bing va Yandex'ga xabar berishning yagona yo'li.

        🔴 KALIT MAXFIY EMAS. U ataylab ochiq faylda turadi va uning
           vazifasi — sirni saqlash emas, xabar yuboruvchi saytga
           kirish huquqiga ega ekanini isbotlash. Uni bilgan begona odam
           faqat "shu sahifani qayta ko'ring" deya oladi, xolos.

        Faqat prod'da: sinov muhitida indekslash baribir taqiqlangan.
      */
      /*
        ══════════════════════════════════════════════════════════════
         YANDEX — TASDIQLASH FAYLI
        ══════════════════════════════════════════════════════════════

        Yandex Webmaster egalikni ikki xil usulda tekshiradi: `<meta>`
        teg yoki sayt ildizidagi `yandex_<token>.html` fayli. Meta teg
        yuqorida (`transformIndexHtml`) qo'yiladi, bu yerda esa FAYL.

        ★ NEGA IKKALASI HAM: ular bir-birini almashtirmaydi, bir-birini
          ZAXIRALAYDI. Yandex panelida qaysi ilova (tab) tanlangani
          muhim — foydalanuvchi "HTML-fayl" ni tanlab qo'ysa, meta teg
          tekshirilmaydi va aksincha. Ikkalasi ham turgani — bitta
          kamroq nosozlik sababi.

        🔴 BU FAYL AYNAN SHUNING UCHUN KERAK: nginx'ning SPA fallback'i
           mavjud bo'lmagan `/yandex_....html` so'roviga `200 OK` bilan
           BUTUN ILOVA sahifasini qaytaradi. Yandex u yerdan
           "Verification: ..." qatorini topa olmay, "HTML-faylda
           noto'g'ri mazmun" deb rad etadi — aynan shu holat
           2026-08-30 da yuz berdi.

        ⚠️ MAZMUN AYNAN YANDEX BERGANIDEK: u `<body>` ichidagi
           "Verification: <token>" qatorini qidiradi. Formatni
           "chiroyliroq" qilib o'zgartirmang.
      */
      const yandexToken = (process.env['VITE_YANDEX_VERIFICATION'] ?? '').trim()

      if (yandexToken.length > 0 && /^[a-z0-9]{8,64}$/i.test(yandexToken)) {
        this.emitFile({
          type: 'asset',
          fileName: `yandex_${yandexToken}.html`,
          source: [
            '<html>',
            '    <head>',
            '        <meta http-equiv="Content-Type" content="text/html; charset=UTF-8">',
            '    </head>',
            `    <body>Verification: ${yandexToken}</body>`,
            '</html>',
            '',
          ].join('\n'),
        })
      }

      const indexNowKey = (process.env['VITE_INDEXNOW_KEY'] ?? '').trim()

      // Format talabi: 8–128 ta belgi, faqat hex. Noto'g'ri kalit bilan
      // xabar jimgina rad etiladi, shuning uchun formatni SHU YERDA
      // tekshiramiz — build logida ko'rinsin.
      if (isProduction && indexNowKey.length > 0) {
        if (!/^[a-f0-9]{8,128}$/i.test(indexNowKey)) {
          this.warn(
            `VITE_INDEXNOW_KEY formati noto'g'ri ("${indexNowKey}"). `
            + '8–128 ta hex belgi bo\'lishi kerak. Kalit fayli yasalmadi.',
          )
        } else {
          this.emitFile({
            type: 'asset',
            fileName: `${indexNowKey}.txt`,
            source: indexNowKey,
          })
        }
      }
    },
  }
}
