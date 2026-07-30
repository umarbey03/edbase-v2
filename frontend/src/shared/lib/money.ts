/**
 * PUL.
 *
 * Serverda summa `decimal` (`PaymentDtos.cs` dagi izoh: `double` ishlatilganda
 * `540000.00000000006` chiqib, qarz hech qachon aniq nolga tushmasdi). JSON'da
 * u oddiy son bo'lib keladi (`450000.00` -> `450000`) va TypeScript'da
 * IEEE-754 `number` ga aylanadi.
 *
 * ★ SHU SABABLI IKKI QAT'IY QOIDA:
 *
 *  1) EKRANGA CHIQADIGAN summa SERVERDAN kelgan holicha chiziladi. Qarz,
 *     balans, "to'lovdan keyingi qarz" — hammasini server hisoblab beradi
 *     (`outstanding`, `debtAfter`, `balance`), mijoz ularni QAYTA hisoblamaydi.
 *     Aks holda ikki joyda ikki xil raqam chiqib, kassir qaysiga ishonishini
 *     bilmasdi.
 *
 *  2) Mijozda qo'shish MAJBUR bo'lganda (masalan "shu sahifadagi qarz")
 *     faqat `sumMoney` ishlatiladi: u qiymatlarni TIYINGA (butun songa)
 *     o'girib qo'shadi. `0.1 + 0.2 = 0.30000000000000004` muammosi
 *     butun sonlarda umuman paydo bo'lmaydi.
 */

/** 1 so'm = 100 tiyin. Butun sonli arifmetika shu ko'lamda bajariladi. */
const MINOR_UNITS = 100

/**
 * Guruh ajratgichi — ODDIY BO'SHLIQ EMAS, uzilmas bo'shliq (U+00A0).
 *
 * Oddiy bo'shliqda `540 000` jadval ustuni torayganda `540` va `000` ga
 * bo'linib, ikki xil raqamdek o'qilardi. Eski ilova `ru-RU` lokalidan
 * foydalanardi va u ham aynan U+00A0 qo'yadi — ko'rinish o'zgarmaydi.
 */
const GROUP_SEPARATOR = '\u00A0'

/** O'nlik ajratgich — vergul (eski ilovadagi `ru-RU` bilan bir xil). */
const DECIMAL_SEPARATOR = ','

/**
 * Ikki xonagacha yaxlitlaydi va suzuvchi nuqta "dumini" kesadi.
 *
 * `Math.round(value * 100) / 100` to'g'ridan-to'g'ri ishlatilmaydi: `x * 100`
 * ning o'zi xato kiritadi (`1.005 * 100 = 100.49999999999999`). `toFixed`
 * o'nlik satr orqali yaxlitlaydi va bunday holatlar uchun barqarorroq.
 */
export function roundMoney(value: number): number {
  if (!Number.isFinite(value)) return 0
  return Number(value.toFixed(2))
}

/**
 * Summalarni TIYINDA qo'shadi.
 *
 * Har bir qiymat avval butun songa o'giriladi, qo'shish butun sonlarda
 * bajariladi va natija oxirida so'mga qaytariladi — oraliqda suzuvchi nuqta
 * arifmetikasi UMUMAN ishlatilmaydi.
 */
export function sumMoney(values: Iterable<number>): number {
  let minor = 0
  for (const value of values) {
    if (!Number.isFinite(value)) continue
    minor += Math.round(value * MINOR_UNITS)
  }
  return minor / MINOR_UNITS
}

/**
 * `540000` -> `540 000`; `450000.5` -> `450 000,50`.
 *
 * Butun summada kasr qismi UMUMAN chizilmaydi: markazda hamma narx butun
 * so'mda va `540 000,00` ustunni ikki barobar kengaytirib, o'qishni
 * qiyinlashtirardi.
 *
 * `Intl.NumberFormat` ATAYLAB ishlatilmadi: `uz-*` lokali brauzerlarda to'liq
 * emas va ba'zilarida guruh ajratgichi vergul bo'lib chiqadi — o'sha paytda
 * `540,000` "besh yuz qirq" deb o'qilardi. Qo'lda formatlash har qurilmada
 * bir xil natija beradi (`shared/lib/datetime.ts` dagi oy nomlari bilan bir xil sabab).
 */
export function formatMoney(value: number): string {
  if (!Number.isFinite(value)) return '—'

  const rounded = roundMoney(value)
  const negative = rounded < 0
  const absolute = Math.abs(rounded)

  const whole = Math.trunc(absolute)
  // Kasr qismi ham TIYIN orqali olinadi (`absolute % 1` suzuvchi dum qoldiradi).
  const cents = Math.round(absolute * MINOR_UNITS) - whole * MINOR_UNITS

  const grouped = String(whole).replace(/\B(?=(\d{3})+(?!\d))/g, GROUP_SEPARATOR)
  const fraction = cents === 0 ? '' : `${DECIMAL_SEPARATOR}${cents < 10 ? '0' : ''}${cents}`

  return `${negative ? '−' : ''}${grouped}${fraction}`
}

/** `540 000 so'm` — jadvaldan tashqarida, o'lchov birligi kerak bo'lganda. */
export function formatSum(value: number): string {
  return `${formatMoney(value)} so‘m`
}

/**
 * Formadagi matnni summaga o'giradi.
 *
 * Kassir `540 000` yoki `540000` deb ham yozadi, mobil klaviaturada vergul
 * chiqadi — uchalasi ham qabul qilinadi. Yaroqsiz kiritma `null` beradi
 * (0 EMAS: "bo'sh" va "nol" ni farqlash kerak, aks holda bo'sh forma
 * serverga 0 yuborib, 400 olardi).
 */
export function parseMoneyInput(raw: string): number | null {
  // `\s` uzilmas bo'shliqni (U+00A0) ham qamrab oladi, lekin uni ATAYLAB
  // alohida yozamiz: `formatMoney` aynan shu belgini qo'yadi va nusxa-joylash
  // orqali maydonga qaytib tushadi.
  const normalized = raw.replace(/[\s\u00A0]/g, '').replace(',', '.')
  if (normalized.length === 0) return null
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null
  const parsed = Number(normalized)
  return Number.isFinite(parsed) ? parsed : null
}
