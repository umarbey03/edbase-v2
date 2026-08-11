/**
 * TELEGRAM MINI APP ADAPTERI — brauzer platformasi bilan ishlaydigan yupqa qatlam.
 *
 * Bu yerda BIZNES MANTIQ YO'Q: faqat "Telegram ichidamizmi", "`initData` qayerda",
 * "SDK'ni qanday yuklaymiz" va bir nechta bezak chaqiruvi. Kirish oqimining o'zi
 * `features/telegram-auth` da, karkas sozlamalari esa `app/providers` da.
 * Shu sababli modul `shared/lib` da: uni `features`, `widgets` va `app` —
 * uchalasi ham ishlatadi (`shared/lib/download.ts` bilan bir turkum).
 *
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ 1) SDK NEGA DINAMIK YUKLANADI                                            │
 * └──────────────────────────────────────────────────────────────────────────┘
 * Rasmiy `telegram-web-app.js` — TASHQI manba (telegram.org). Uni `index.html`
 * ga qo'ysak, HAR bir foydalanuvchi — jumladan ustoz va o'quv bo'limi xodimi,
 * ular Telegram'ni umuman ochmasa ham — sahifa yuklashida telegram.org ga
 * bloklovchi so'rov yuborardi. npm paketi sifatida olsak esa u `vendor`
 * bo'lagiga tushib, hammaga qo'shimcha kilobayt bo'lardi.
 *
 * Loyihada bu masala ALLAQACHON hal qilingan namuna bor — Sentry
 * (`app/providers/sentry.ts`): kerak bo'lmasa 0 bayt, kerak bo'lsa alohida
 * bo'lakda, fon rejimida. Bu yerda ham AYNAN shunday: skript FAQAT Telegram
 * muhiti aniqlangandan keyin, `<script>` tegi orqali qo'shiladi. Natijada
 * bundle o'lchami O'ZGARMAYDI (`vendor` 72 KB da qoladi) va Telegram'dan
 * tashqarida bitta ham tashqi so'rov bo'lmaydi.
 *
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ 2) `initData` NEGA MANZIL FRAGMENTIDAN O'QILADI                          │
 * └──────────────────────────────────────────────────────────────────────────┘
 * Telegram Mini App'ni ochganda manzilga `#tgWebAppData=...` fragmentini
 * qo'shadi — rasmiy SDK ham `initData` ni AYNAN shu yerdan oladi, boshqa
 * manbadan emas.
 *
 * Muammo: SDK tarmoqdan yuklanguncha (~100 ms va undan ko'p) `vue-router`
 * allaqachon `/` dan `/login` ga o'tib, fragmentni manzildan olib tashlagan
 * bo'lishi mumkin — u holda kech kelgan SDK bo'sh `initData` ko'radi va
 * kirish sababsiz ishlamay qolardi. Poyga natijasi tarmoq tezligiga bog'liq,
 * ya'ni xato "goh bor, goh yo'q" bo'lardi.
 *
 * Yechim: fragment MODUL YUKLANGAN ZAHOTI (router ishga tushishidan oldin)
 * bir marta suratga olinadi. Bu qat'iy tartib — poyga umuman bo'lmaydi.
 *
 * ★ `initData` PARCHALANMAYDI. Fragmentdan faqat `tgWebAppData` parametri
 * AJRATIB olinadi va bir marta URL-dekodlanadi — bu transport o'ramini
 * ochish, imzo ostidagi satrning O'ZI daxlsiz qoladi. Uni maydonlarga bo'lib
 * qayta yig'ish `hash` ni buzardi va server 401 qaytarardi.
 *
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ 3) MAXFIYLIK                                                             │
 * └──────────────────────────────────────────────────────────────────────────┘
 * `initData` — imzolangan shaxsiy ma'lumot. U bu modulda HECH QACHON
 * `console` ga chiqarilmaydi va `localStorage` ga yozilmaydi; faqat xotirada
 * turadi va to'g'ridan-to'g'ri serverga yuboriladi.
 */

/** Telegram sarlavhasidagi tizim "orqaga" tugmasi. */
export interface TelegramBackButton {
  show: () => void
  hide: () => void
  onClick: (handler: () => void) => void
  offClick: (handler: () => void) => void
}

/**
 * Rasmiy SDK obyektining BIZ ISHLATADIGAN qismi.
 *
 * Ataylab to'liq emas: `@types/telegram-web-app` kabi paket qo'shsak, u
 * SDK bilan versiyadan chiqib ketishi mumkin, biz esa bor-yo'g'i sakkizta
 * chaqiruvdan foydalanamiz. Ixtiyoriy (`?`) maydonlar — eski mijozlarda
 * UMUMAN mavjud bo'lmaganlari.
 */
export interface TelegramWebApp {
  /** Imzolangan kirish ma'lumoti. Bo'sh bo'lishi ham mumkin. */
  readonly initData: string
  readonly version: string
  readonly platform: string
  ready: () => void
  expand: () => void
  close: () => void
  isVersionAtLeast?: (version: string) => boolean
  setHeaderColor?: (color: string) => void
  setBackgroundColor?: (color: string) => void
  openTelegramLink?: (url: string) => void
  disableVerticalSwipes?: () => void
  readonly BackButton: TelegramBackButton
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TelegramWebApp }
  }
}

const SDK_URL = 'https://telegram.org/js/telegram-web-app.js'

/**
 * SDK'ni kutish chegarasi. Mobil internetda skript kechikishi mumkin, lekin
 * ILOVA UNGA BOG'LIQ EMAS (yuqoridagi 2-band): vaqt tugasa bezaklarsiz
 * davom etamiz, kirish esa baribir ishlaydi.
 */
const SDK_TIMEOUT_MS = 5000

/**
 * Ishga tushish fragmenti — MODUL YUKLANGANDA bir marta o'qiladi.
 * Keyin `vue-router` manzilni o'zgartirsa ham bu qiymat saqlanib qoladi.
 */
const launchParams = readLaunchParams()

const launchInitData = launchParams.get('tgWebAppData') ?? ''
const launchPlatform = launchParams.get('tgWebAppPlatform') ?? ''
const launchVersion = launchParams.get('tgWebAppVersion') ?? ''

/**
 * `#tgWebAppData=...&tgWebAppVersion=...` fragmentini o'qiydi.
 *
 * `URLSearchParams` rasmiy SDK'ning `urlParseQueryString` funksiyasi bilan
 * bir xil ishlaydi: `+` -> probel, so'ng `decodeURIComponent`. Shuning uchun
 * chiqadigan `tgWebAppData` qiymati SDK beradigani bilan BAYTMA-BAYT bir xil.
 */
function readLaunchParams(): URLSearchParams {
  const raw = window.location.hash.replace(/^#/, '')
  if (raw.length === 0) return new URLSearchParams()
  // Fragment ichida yo'l ham bo'lishi mumkin: `#/bosh?tgWebAppData=...`.
  const queryStart = raw.indexOf('?')
  return new URLSearchParams(queryStart >= 0 ? raw.slice(queryStart + 1) : raw)
}

/**
 * Ilova Telegram Mini App ichida ochilganmi.
 *
 * `initData` ning O'ZI emas, MUHIT tekshiriladi: `initData` bo'sh bo'lgan
 * holat ham bor (masalan Mini App kanaldagi inline tugmadan ochilsa) va u
 * "oddiy brauzer" degani emas — bunda foydalanuvchiga email formasi emas,
 * "botdan qayta oching" xabari kerak.
 */
export function isTelegramMiniApp(): boolean {
  return (
    launchInitData.length > 0
    || launchPlatform.length > 0
    || window.Telegram?.WebApp !== undefined
  )
}

/**
 * Serverga yuboriladigan `initData`. Bo'sh satr — "yo'q" degani.
 *
 * SDK yuklangan bo'lsa uning qiymati birinchi o'rinda: u eng ishonchli manba.
 * Aks holda ishga tushish fragmentidagi surat ishlatiladi.
 */
export function readTelegramInitData(): string {
  const fromSdk = window.Telegram?.WebApp?.initData ?? ''
  return fromSdk.length > 0 ? fromSdk : launchInitData
}

/** SDK allaqachon mavjud bo'lsa qaytaradi (kutmaydi). */
export function getTelegramWebApp(): TelegramWebApp | null {
  return window.Telegram?.WebApp ?? null
}

let sdkPromise: Promise<TelegramWebApp | null> | null = null

/**
 * SDK'ni bir marta yuklaydi va obyektni qaytaradi (yoki `null` — Telegram
 * muhiti emas, skript yuklanmadi yoki vaqt tugadi).
 *
 * HECH QACHON `reject` qilmaydi: chaqiruvchi uchun "SDK yo'q" — bu xato emas,
 * shunchaki bezaklarsiz rejim.
 */
export function ensureTelegramWebApp(): Promise<TelegramWebApp | null> {
  if (sdkPromise !== null) return sdkPromise

  const existing = getTelegramWebApp()
  if (existing !== null) {
    sdkPromise = Promise.resolve(existing)
    return sdkPromise
  }

  if (!isTelegramMiniApp()) {
    // Oddiy brauzer: telegram.org ga BITTA ham so'rov yubormaymiz.
    sdkPromise = Promise.resolve(null)
    return sdkPromise
  }

  sdkPromise = new Promise<TelegramWebApp | null>((resolve) => {
    let settled = false
    const finish = (): void => {
      if (settled) return
      settled = true
      window.clearTimeout(timer)
      resolve(getTelegramWebApp())
    }

    const timer = window.setTimeout(finish, SDK_TIMEOUT_MS)

    const script = document.createElement('script')
    script.src = SDK_URL
    script.async = true
    script.addEventListener('load', finish)
    script.addEventListener('error', finish)
    document.head.appendChild(script)
  })

  return sdkPromise
}

/**
 * Eski Telegram mijozlari yangi metodlarni bilmaydi va SDK ular uchun
 * `throw` qiladi. Bu chaqiruvlar BEZAK — ularsiz ham ilova to'liq ishlaydi,
 * shuning uchun xato jimgina yutiladi (konsolni behuda to'ldirmaslik uchun:
 * foydalanuvchi buni tuzata olmaydi).
 */
function tryCall(action: () => void): void {
  try {
    action()
  } catch {
    /* eski mijoz — bezaksiz davom etamiz */
  }
}

/** Mijoz kamida shu Bot API versiyasini qo'llab-quvvatlaydimi. */
function supportsVersion(webApp: TelegramWebApp, version: string): boolean {
  // SDK kech yuklanib fragmentni ko'rmagan bo'lsa, o'z versiyasi o'rniga
  // "6.0" deb o'ylaydi — shuning uchun bizdagi SURAT ham hisobga olinadi.
  if (webApp.isVersionAtLeast?.(version) === true) return true
  if (launchVersion.length === 0) return false
  return compareVersions(launchVersion, version) >= 0
}

/** `7.10` va `7.7` kabi qismlarni SON sifatida solishtiradi (satr sifatida emas). */
function compareVersions(left: string, right: string): number {
  const a = left.split('.')
  const b = right.split('.')
  const length = Math.max(a.length, b.length)
  for (let i = 0; i < length; i += 1) {
    const x = Number(a[i] ?? '0')
    const y = Number(b[i] ?? '0')
    if (!Number.isFinite(x) || !Number.isFinite(y)) return 0
    if (x !== y) return x < y ? -1 : 1
  }
  return 0
}

/**
 * Mini App karkasini sozlaydi.
 *
 * ★ TEMA YO'NALISHI: biz Telegram'dan rang OLMAYMIZ, unga O'Z rangimizni
 * BERAMIZ. `themeParams` (foydalanuvchining Telegram temasi) ataylab umuman
 * o'qilmaydi — ilovaning rangi brend belgisi (yorug' `#f4f6fb` fon, indigo
 * `#4f4de8` urg'u) va u Telegram temasiga qarab o'zgarmasligi kerak.
 * `setHeaderColor`/`setBackgroundColor` esa aksincha ishlaydi: Telegram'ning
 * O'Z sarlavhasi va overscroll foni bizning fonimizga moslanadi, ya'ni ilova
 * "qirqilgan" ko'rinmaydi.
 *
 * @param backgroundColor `#rrggbb` — ilova sahifasining foni.
 */
export function applyMiniAppChrome(webApp: TelegramWebApp, backgroundColor: string): void {
  // Telegram'ga "chizib bo'ldim" deydi va yuklanish pardasini oladi.
  tryCall(() => { webApp.ready() })
  // Ilova to'liq balandlikda ochiladi: o'quvchi paneli — to'liq ilova,
  // yarim ekranli varaq emas.
  tryCall(() => { webApp.expand() })

  if (supportsVersion(webApp, '6.1')) {
    tryCall(() => { webApp.setBackgroundColor?.(backgroundColor) })
  }
  if (supportsVersion(webApp, '6.9')) {
    tryCall(() => { webApp.setHeaderColor?.(backgroundColor) })
  }
  /*
    Pastga tortib yopish — o'quvchi ekranlarining aksari uzun ro'yxat
    (kalendar, reyting, chat). Ro'yxatni yuqoriga aylantirmoqchi bo'lgan
    barmoq ilovani tasodifan yopib qo'yardi. 7.7 dan pastda metod yo'q va
    bunda eski xatti-harakat qoladi.
  */
  if (supportsVersion(webApp, '7.7')) {
    tryCall(() => { webApp.disableVerticalSwipes?.() })
  }
}

/**
 * Mini App'ni yopadi. `false` — yopib bo'lmadi (SDK yo'q), chaqiruvchi
 * zaxira yo'lni ko'rsatishi kerak.
 */
export function closeMiniApp(): boolean {
  const webApp = getTelegramWebApp()
  if (webApp === null) return false
  let closed = false
  tryCall(() => {
    webApp.close()
    closed = true
  })
  return closed
}

/**
 * Telegram ichidagi havolani ochadi (bot chati). `false` — ochib bo'lmadi.
 *
 * `window.open` EMAS: Mini App webview'ida u yangi brauzer oynasini
 * ochishga urinadi va `t.me` havolasi Telegram ilovasiga qaytmaydi.
 */
export function openTelegramLink(url: string): boolean {
  const webApp = getTelegramWebApp()
  if (webApp?.openTelegramLink === undefined) return false
  let opened = false
  tryCall(() => {
    webApp.openTelegramLink?.(url)
    opened = true
  })
  return opened
}

/**
 * Tizim "orqaga" tugmasini boshqaradi.
 *
 * Mini App'da brauzer tugmalari yo'q — ichki sahifadan (masalan test yechish
 * ekrani) chiqishning yagona tizim yo'li shu tugma.
 */
export function setBackButton(visible: boolean): void {
  const webApp = getTelegramWebApp()
  if (webApp === null) return
  tryCall(() => {
    if (visible) webApp.BackButton.show()
    else webApp.BackButton.hide()
  })
}

/** "Orqaga" bosilganda chaqiriladigan ishlovchini ulaydi va uzish funksiyasini qaytaradi. */
export function onBackButtonClick(handler: () => void): () => void {
  const webApp = getTelegramWebApp()
  if (webApp === null) return () => undefined
  tryCall(() => { webApp.BackButton.onClick(handler) })
  return () => {
    tryCall(() => { webApp.BackButton.offClick(handler) })
  }
}
