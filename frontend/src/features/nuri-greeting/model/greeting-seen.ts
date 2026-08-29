/**
 * ════════════════════════════════════════════════════════════════════════
 * «NURI» SALOMLASHUVI — QACHON KO'RSATILADI (2026-08-30)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasining talabi: kirishdan keyin o'quvchi darhol bosh sahifaga
 * tushmasin, avval maskot uni shaxsan kutib olsin.
 *
 * ★ HAR KIRISHDA EMAS, KUNIGA BIR MARTA. Sabab o'lchovli: o'quvchilarning
 *   asosiy kanali — Telegram Mini App, va u HAR OCHILISHDA qaytadan
 *   kiradi (`TelegramAuthScreen` avtomatik `initData` bilan). Ya'ni
 *   "har kirishda" degani amalda "kuniga 5-10 marta" degani bo'lardi va
 *   salomlashuv bir kunda bezorga aylanardi. Kuniga bir marta esa u
 *   RITUAL bo'lib qoladi.
 *
 * ★ NEGA `localStorage`, `sessionStorage` EMAS: `sessionStorage` YORLIQQA
 *   (tab) bog'langan va Telegram Mini App har ochilishda YANGI webview
 *   yaratadi — ya'ni belgi hech qachon saqlanmasdi va yuqoridagi qoida
 *   umuman ishlamasdi.
 *
 * 🔴 BELGIDA SHAXSIY MA'LUMOT YO'Q: faqat `userId` va sana. Ism, matn yoki
 *    salomlashuv sababi SAQLANMAYDI — qurilma boshqa odam qo'liga
 *    tushganda ham u yerdan hech nima o'qib bo'lmasin.
 */

/**
 * ★ BITTA KALIT, ICHIDA BITTA YOZUV (ro'yxat emas): bitta qurilmada
 * ko'pincha bitta o'quvchi ishlaydi. Aka-uka bir telefondan foydalansa
 * belgi mos kelmaydi va salomlashuv ikkalasiga ham ko'rsatiladi — bu
 * TO'G'RI xatti-harakat, chunki har biri o'z salomini eshitishi kerak.
 */
const STORAGE_KEY = 'zinnur:nuri-salom'

interface GreetingMark {
  userId: number
  /** MAHALLIY sana, `YYYY-MM-DD`. */
  date: string
}

/**
 * Mahalliy kun kaliti.
 *
 * 🔴 `toISOString().slice(0, 10)` ATAYLAB ISHLATILMAYDI: u UTC beradi va
 *    Toshkentda (UTC+5) kun soat 05:00 da almashardi — ertalab 06:00 da
 *    kirgan o'quvchi "yangi kun" ni allaqachon o'tkazib yuborgan bo'lardi,
 *    kechqurun 23:00 da kirgani esa "kechagi" belgini olardi.
 */
function localDayKey(now: Date): string {
  const month = `${now.getMonth() + 1}`.padStart(2, '0')
  const day = `${now.getDate()}`.padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

/**
 * Saqlangan belgi. Xotira yopiq bo'lsa yoki yozuv buzilgan bo'lsa — `null`.
 *
 * ★ HAR XATO "belgi yo'q" deb talqin qilinadi, ya'ni eng yomon holatda
 * salomlashuv ORTIQCHA ko'rsatiladi. Teskarisi (xatoni "ko'rsatilgan" deb
 * hisoblash) funksiyani jimgina o'chirib qo'yardi va buni hech kim
 * sezmasdi.
 */
function readMark(): GreetingMark | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw === null) return null

    const saved = JSON.parse(raw) as { userId?: unknown, date?: unknown }
    if (typeof saved.userId !== 'number' || typeof saved.date !== 'string') return null

    return { userId: saved.userId, date: saved.date }
  } catch {
    return null
  }
}

/** Bugun shu o'quvchiga salomlashuv ko'rsatilishi kerakmi. */
export function shouldGreetToday(userId: number, now: Date = new Date()): boolean {
  const mark = readMark()
  if (mark === null) return true
  return mark.userId !== userId || mark.date !== localDayKey(now)
}

/**
 * Shu o'quvchi ILGARI umuman salomlashgan bo'lganmi.
 *
 * ★ "Yangi o'quvchi" matnini tanlash uchun kerak. Belgi YO'Q bo'lishi
 * qurilma yangi ekanini bildiradi, o'quvchi yangi ekanini emas — shuning
 * uchun bu qiymat YOLG'IZ ishlatilmaydi: `pickGreeting` uni davomat
 * ma'lumoti bilan BIRGA tekshiradi (hali bitta ham dars o'tmagan bo'lsa
 * o'quvchi haqiqatan yangi).
 */
export function hasGreetedBefore(userId: number): boolean {
  const mark = readMark()
  return mark !== null && mark.userId === userId
}

/**
 * "Ko'rsatildi" deb belgilaydi.
 *
 * ★ EKRAN OCHILGANDA yoziladi, tugma bosilganda EMAS. Belgining ma'nosi —
 * "bugun ko'rsatildi", "bugun o'qib chiqildi" emas. Foydalanuvchi ilovani
 * salomlashuv o'rtasida yopsa, keyingi kirishda uni QAYTA ko'rish faqat
 * bezor qilardi.
 */
export function markGreeted(userId: number, now: Date = new Date()): void {
  try {
    const mark: GreetingMark = { userId, date: localDayKey(now) }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(mark))
  } catch {
    // Xotira to'lgan yoki saqlash o'chirilgan. Salomlashuv baribir
    // ko'rsatildi — faqat keyingi kirishda yana chiqadi.
  }
}
