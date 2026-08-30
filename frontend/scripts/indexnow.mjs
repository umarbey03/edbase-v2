/*
  ══════════════════════════════════════════════════════════════════════════
  INDEXNOW — "SAHIFA YANGILANDI" XABARI (Bing va Yandex)
  ══════════════════════════════════════════════════════════════════════════

  Ishga tushirish:   npm run seo:ping

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 NEGA BU KERAK VA U NIMANI HAL QILMAYDI                          │
  └────────────────────────────────────────────────────────────────────┘
  Odatda qidiruv tizimi saytga O'ZI kelguncha kutish kerak — yangi
  saytda bu haftalar oladi. IndexNow buni teskarisiga aylantiradi: biz
  o'zimiz "mana shu manzilni ko'ring" deb xabar beramiz.

  ⚠️ XABAR = TAKLIF, KAFOLAT EMAS. Qidiruv tizimi sahifaga TEZROQ
     keladi, lekin uni indeksga qo'shishni va qaysi o'rinda ko'rsatishni
     baribir o'zi hal qiladi. Bu tezlik vositasi, o'rin vositasi emas.

  ★ AKKAUNT KERAK EMAS — protokolning butun ma'nosi shunda. Google
    Search Console va Yandex Webmaster'ga kirish talab qiladi, IndexNow
    esa faqat sayt ildizidagi kalit fayliga tayanadi (uni build
    qo'yadi — `seo-files-plugin.ts`).

  🔴 GOOGLE INDEXNOW'NI QO'LLAB-QUVVATLAMAYDI. U uchun Search Console
     va sitemap kerak — buni odam qiladi, skript emas.

  QACHON CHAQIRILADI: sahifa MAZMUNI o'zgargandan va yangi versiya
  serverga chiqqandan KEYIN. Deploy'dan oldin chaqirilsa, qidiruv
  tizimi hali eski sahifani ko'radi.
*/

const FALLBACK_ORIGIN = 'https://zinnuronline.uz'

const origin = (process.env['VITE_SITE_URL'] ?? FALLBACK_ORIGIN)
  .trim()
  .replace(/\/+$/, '')

const key = (process.env['VITE_INDEXNOW_KEY'] ?? '').trim()

/*
  Xabar beriladigan manzillar.

  Hozircha bitta — sayt bitta ommaviy sahifadan iborat. Kurs sahifalari
  qo'shilganda ular SHU ro'yxatga ham, `seo-files-plugin.ts` dagi
  sitemap ro'yxatiga ham qo'shiladi.
*/
const urls = [`${origin}/`]

if (key.length === 0) {
  console.error(
    'VITE_INDEXNOW_KEY berilmagan. Kalitni `.env` ga yozing va saytni '
    + 'qayta yig\'ing (kalit fayli sayt ildizida turishi shart).',
  )
  process.exit(1)
}

/*
  🔴 KALIT FAYLI HAQIQATAN OCHIQMI — AVVAL SHUNI TEKSHIRAMIZ.

  Tekshirmasak, xabar `202 Accepted` bilan qabul qilinadi va biz "ishladi"
  deb o'ylaymiz. Aslida qidiruv tizimi keyinroq kalit faylini qidiradi,
  topa olmaydi va xabarni JIMGINA tashlab yuboradi. Ya'ni xato bir necha
  kun ko'rinmaydi.

  Eng ko'p uchraydigan sabab: kalit `.env` da o'zgartirilgan, lekin sayt
  qayta yig'ilmagan.
*/
const keyUrl = `${origin}/${key}.txt`
const keyResponse = await fetch(keyUrl).catch(() => null)

if (keyResponse === null || !keyResponse.ok) {
  console.error(
    `Kalit fayli ochilmadi: ${keyUrl} `
    + `(${keyResponse === null ? 'ulanib bo\'lmadi' : keyResponse.status})\n`
    + 'Sayt shu kalit bilan qayta yig\'ilganini va serverga chiqqanini '
    + 'tekshiring.',
  )
  process.exit(1)
}

const keyBody = (await keyResponse.text()).trim()

if (keyBody !== key) {
  console.error(
    `Kalit fayli ichidagi qiymat mos kelmadi.\n`
    + `  kutilgan: ${key}\n`
    + `  fayldan:  ${keyBody}\n`
    + 'Server eski versiyani berayotgan bo\'lishi mumkin.',
  )
  process.exit(1)
}

/*
  ★ BITTA SO'ROVDA BARCHA MANZILLAR (`urlList`): har manzilga alohida
    so'rov yuborish tezlik chegarasiga urilishi mumkin va protokol
    aynan to'plamli xabarni tavsiya qiladi.

  ★ `api.indexnow.org` — umumiy nuqta: u xabarni ishtirokchi qidiruv
    tizimlariga (Bing, Yandex, Seznam, Naver) O'ZI tarqatadi. Har biriga
    alohida murojaat qilish shart emas.
*/
const response = await fetch('https://api.indexnow.org/indexnow', {
  method: 'POST',
  headers: { 'Content-Type': 'application/json; charset=utf-8' },
  body: JSON.stringify({
    host: new URL(origin).host,
    key,
    keyLocation: keyUrl,
    urlList: urls,
  }),
})

// 200 va 202 — ikkalasi ham muvaffaqiyat. 202 "qabul qilindi, navbatda"
// degani va aynan u eng ko'p qaytadi.
if (response.ok) {
  console.log(`IndexNow: ${urls.length} ta manzil yuborildi (${response.status})`)
  for (const url of urls) console.log(`  · ${url}`)
} else {
  console.error(`IndexNow rad etdi: ${response.status} ${response.statusText}`)
  console.error(await response.text())
  process.exit(1)
}
