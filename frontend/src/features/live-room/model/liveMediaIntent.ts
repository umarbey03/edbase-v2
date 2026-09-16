/*
  ════════════════════════════════════════════════════════════════════════
  FOYDALANUVCHI NIYATI SAHIFA QAYTA YUKLANISHIDAN OMON QOLADI (2026-09-16)
  ════════════════════════════════════════════════════════════════════════

  🔴 NIMA UCHUN. `useLiveKitRoom` ichidagi `wantMic` / `wantCamera` — oddiy
     modul o'zgaruvchilari, ya'ni ular KOMPOZABL bilan birga o'ladi. Ular
     UZILISHDAN (`reconnect`) omon qoladi, lekin `LiveRoomPage` ning
     UNMOUNT bo'lishidan omon qolmaydi.

     2026-09-15 kechasining o'lchovi (jonli dars klient hodisalari, 7 dars,
     82 foydalanuvchi):

       • bitta ishtirokchi darsga o'rtacha 3.2 marta ulanadi (max 21);
       • 305 ta `page-closed`, ulardan faqat 79 tasi haqiqiy "Chiqish";
       • takrorlanadigan naqsh — `page-closed (mic=true)` dan keyin
         3–7 soniyada `connected (mic=false)`.

     Ya'ni o'quvchi mikrofonini yoqadi, sahifa qandaydir sababdan qayta
     quriladi va u MIKROFONSIZ qaytadi — ustoz "gapir" deydi, o'quvchi esa
     o'zining jim qolganini bilmaydi. Uzilishdan keyin mikrofon tiklanishi
     72% dan 73% ga "yaxshilangani" ham shundan: `restoreMedia()` faqat
     qayta ulanishda ishlaydi, qayta qurilishda esa tiklanadigan holatning
     o'zi qolmaydi.

  ★ NEGA `localStorage`, `sessionStorage` EMAS. Ikki xil yo'qotish bor va
    ikkalasini ham qoplash kerak:
      1) SPA ichidagi unmount/remount — bir tabda, `sessionStorage` yetardi;
      2) Telegram fondagi WebView'ni butunlay o'ldiradi va qaytganda YANGI
         kontekst ochiladi — `sessionStorage` bo'sh keladi, `localStorage`
         esa qoladi.
    O'lchovda ikkinchi holat ham bor (`page-hidden` dan keyin `page-visible`
    SIZ to'g'ridan-to'g'ri `connected`), shuning uchun `localStorage`.

  ★ MUDDAT NEGA KERAK. `localStorage` o'chmaydi. Muddatsiz saqlansa, ertaga
    darsga kirgan o'quvchining mikrofoni o'zidan o'ziga yonardi — bu esa
    "jimgina yozib turish" ga o'xshaydi va ishonchni buzadi. Shuning uchun
    niyat FAQAT `MAX_AGE_MS` ichida amal qiladi: qayta qurilish soniyalar
    ichida bo'ladi, ya'ni chegara bemalol yetadi.

  ★ HAR DARS UCHUN ALOHIDA KALIT (`sessionId`) — bir darsda yoqilgan
    mikrofon boshqasiga o'tib ketmasin.
*/

/** Saqlangan niyat: foydalanuvchi O'ZI nima yoqqan edi. */
export interface LiveMediaIntent {
  mic: boolean
  camera: boolean
}

interface StoredIntent extends LiveMediaIntent {
  /** `Date.now()` — eskirganini aniqlash uchun. */
  at: number
}

const KEY_PREFIX = 'zinnur.live.intent.'

/**
 * Niyat shuncha vaqt amal qiladi. 10 daqiqa — qayta qurilish (soniyalar) va
 * telefon fonda turib qaytishi (o'lchovda p90 = 324 s) uchun yetarli, lekin
 * "ertaga o'zidan yonib qolish" uchun emas.
 */
const MAX_AGE_MS = 10 * 60 * 1000

function keyFor(sessionId: number): string {
  return `${KEY_PREFIX}${String(sessionId)}`
}

/**
 * Private/incognito rejimda `localStorage` ga murojaat `throw` qilishi
 * mumkin — butun dars shuning uchun yiqilmasin.
 */
function safeStorage(): Storage | null {
  try {
    return window.localStorage
  } catch {
    return null
  }
}

/** Saqlangan qiymat haqiqatan bizning shaklimizdami. */
function isStoredIntent(value: unknown): value is StoredIntent {
  if (typeof value !== 'object' || value === null) return false
  const candidate = value as Record<string, unknown>
  return typeof candidate.mic === 'boolean'
    && typeof candidate.camera === 'boolean'
    && typeof candidate.at === 'number'
}

/**
 * Eskirmagan niyatni qaytaradi. Yo'q, buzuq yoki eskirgan bo'lsa — `null`.
 *
 * `now` parametri testlar uchun: vaqtga bog'liq mantiqni soat o'zgarishini
 * kutmasdan tekshirish kerak.
 */
export function readMediaIntent(sessionId: number, now: number = Date.now()): LiveMediaIntent | null {
  const raw = (() => {
    try {
      return safeStorage()?.getItem(keyFor(sessionId)) ?? null
    } catch {
      return null
    }
  })()

  if (raw === null || raw.length === 0) return null

  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }

  if (!isStoredIntent(parsed)) return null

  // Kelajakdagi sana (telefon soati orqaga surilgan) ham ishonchsiz —
  // `now - at` manfiy chiqadi va tekshiruvdan o'tib ketardi.
  const age = now - parsed.at
  if (age < 0 || age > MAX_AGE_MS) return null

  return { mic: parsed.mic, camera: parsed.camera }
}

/**
 * Niyatni saqlaydi. Ikkalasi ham `false` bo'lsa yozuv O'CHIRILADI — bo'sh
 * niyatni saqlab yurishdan ma'no yo'q va u eskirgan qiymatni ustidan
 * yozilmay qolish xavfini tug'dirardi.
 */
export function writeMediaIntent(
  sessionId: number,
  intent: LiveMediaIntent,
  now: number = Date.now(),
): void {
  if (!intent.mic && !intent.camera) {
    clearMediaIntent(sessionId)
    return
  }

  const stored: StoredIntent = { mic: intent.mic, camera: intent.camera, at: now }
  try {
    safeStorage()?.setItem(keyFor(sessionId), JSON.stringify(stored))
  } catch {
    // Saqlab bo'lmasa dars baribir ishlaydi — faqat tiklash bo'lmaydi.
  }
}

/** Foydalanuvchi O'ZI chiqdi yoki niyat bo'shadi — yozuvni o'chiramiz. */
export function clearMediaIntent(sessionId: number): void {
  try {
    safeStorage()?.removeItem(keyFor(sessionId))
  } catch {
    /* e'tiborsiz qoldiriladi */
  }
}
