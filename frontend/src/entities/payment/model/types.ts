import { formatMoney } from '@/shared/lib/money'
import { monthNameCapitalized } from '@/shared/lib/datetime'
import type {
  DiscountKindName,
  PaymentBlockScopeName,
  PaymentMethodName,
  PaymentStatusName,
  PaymentTransactionKindName,
  StudentDiscountDto,
  TariffDto,
} from '@/shared/types'

/** `BaseBadge` ning `tone` prop'i bilan bir xil to'plam. */
export type PaymentTone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger'

/* ================================================================ holat === */

/*
  Matnlar eski ilovadagi `PAY_ST` dan AYNAN: `due -> Qarzdor` (qizil),
  `paid -> To'langan` (yashil), `waived -> Kechirilgan` (kulrang).

  `Partial` eskisida YO'Q edi — o'sha yerda qisman to'lov ham "paid" bo'lib
  qolardi va markaz jimgina pul yo'qotardi. Yangi holat sariq nishon bilan
  ALOHIDA ko'rsatiladi, aks holda ko'chirish o'sha xatoni qaytarardi.
*/
const STATUS_LABELS: Record<PaymentStatusName, string> = {
  Due: 'Qarzdor',
  Partial: 'Qisman to‘langan',
  Paid: 'To‘langan',
  Waived: 'Kechirilgan',
}

const STATUS_TONES: Record<PaymentStatusName, PaymentTone> = {
  Due: 'danger',
  Partial: 'warning',
  Paid: 'success',
  Waived: 'neutral',
}

export function paymentStatusLabel(status: PaymentStatusName): string {
  return STATUS_LABELS[status]
}

export function paymentStatusTone(status: PaymentStatusName): PaymentTone {
  return STATUS_TONES[status]
}

/**
 * ★ QARZ SIFATIDA HISOBLANADIGAN summa.
 *
 * `PaymentDto.outstanding` — bu SHUNCHAKI `amount − paidAmount` va u
 * HOLATGA QARAMAYDI. Kechirilgan oyda ham u noldan katta bo'lib qoladi:
 * jonli tekshiruvda kechirilgan 540 000 lik oy `status: "Waived",
 * outstanding: 540000` bo'lib qaytdi.
 *
 * Uni to'g'ridan-to'g'ri "qarz" deb chizsak, ekranda ikki xil raqam chiqardi:
 * jadval qatorida "qarz 540 000", o'quvchi hisobida esa "Joriy qarz 0"
 * (server `debt` ni faqat `Due` va `Partial` bo'yicha yig'adi, `onlyDebt`
 * filtri ham shu ikki holatni oladi). Kassir qaysi raqamga ishonishini
 * bilmasdi va kechirilgan oy uchun yana pul so'rardi.
 *
 * Shuning uchun EKRANGA CHIQADIGAN qarz doim shu funksiyadan o'tadi —
 * server qoidasi bilan bir xil bo'lsin.
 */
export function debtAmount(payment: {
  status: PaymentStatusName
  outstanding: number
}): number {
  return payment.status === 'Due' || payment.status === 'Partial' ? payment.outstanding : 0
}

/** Ro'yxat filtridagi variantlar (bo'sh qiymat — "barcha holatlar"). */
export const PAYMENT_STATUS_OPTIONS: ReadonlyArray<{ value: PaymentStatusName; label: string }> = [
  { value: 'Due', label: STATUS_LABELS.Due },
  { value: 'Partial', label: STATUS_LABELS.Partial },
  { value: 'Paid', label: STATUS_LABELS.Paid },
  { value: 'Waived', label: STATUS_LABELS.Waived },
]

/* ================================================================= usul === */

const METHOD_LABELS: Record<PaymentMethodName, string> = {
  Cash: 'Naqd',
  Card: 'Karta',
}

export function paymentMethodLabel(method: PaymentMethodName | null): string {
  return method === null ? '—' : METHOD_LABELS[method]
}

/**
 * To'lov usullari — ATAYLAB IKKITA.
 *
 * Eski ilovada to'rtta edi (`Naqd`, `Karta`, `Click`, `Payme`) va usul erkin
 * satr sifatida saqlanardi. Backend qarori (2026-07-30) uni `enum` ga
 * aylantirdi: markaz amalda faqat naqd va karta qabul qiladi. Bu yerga
 * uchinchisini qo'shsak, server 400 qaytaradi — shuning uchun ro'yxat
 * server enum'iga QAT'IY bog'langan.
 */
export const PAYMENT_METHOD_OPTIONS: ReadonlyArray<{ value: PaymentMethodName; label: string }> = [
  { value: 'Cash', label: METHOD_LABELS.Cash },
  { value: 'Card', label: METHOD_LABELS.Card },
]

/* ============================================================== jurnal === */

/*
  Eski ilovada server jurnal turlarini shunday atardi: `paid -> To'landi`,
  `waived -> Kechirildi`, `refund -> Pul Qaytarildi`. Shu so'zlar saqlandi.

  `BalanceUse` — YANGI tur: oldindan to'langan puldan oy avtomatik yopilganda
  yoziladi. Eski tizimda bunday yozuv umuman yo'q edi (balans tushunchasi
  yo'q edi), shuning uchun uni ko'chiradigan matn ham yo'q.
*/
const KIND_LABELS: Record<PaymentTransactionKindName, string> = {
  Payment: 'To‘landi',
  Refund: 'Pul qaytarildi',
  Waiver: 'Kechirildi',
  BalanceUse: 'Balansdan yopildi',
}

const KIND_TONES: Record<PaymentTransactionKindName, PaymentTone> = {
  Payment: 'success',
  Refund: 'danger',
  Waiver: 'warning',
  BalanceUse: 'accent',
}

export function transactionKindLabel(kind: PaymentTransactionKindName): string {
  return KIND_LABELS[kind]
}

export function transactionKindTone(kind: PaymentTransactionKindName): PaymentTone {
  return KIND_TONES[kind]
}

/** Jurnalda pul CHIQIMI (qaytarish) minus bilan ko'rsatiladi. */
export function isOutgoingTransaction(kind: PaymentTransactionKindName): boolean {
  return kind === 'Refund'
}

/* ================================================================= blok === */

/** Sozlama formasidagi variantlar — eski `#ps-scope` select'idan AYNAN. */
export const BLOCK_SCOPE_OPTIONS: ReadonlyArray<{
  value: PaymentBlockScopeName
  label: string
}> = [
  { value: 'None', label: 'Hech narsa — faqat ogohlantirish' },
  { value: 'Video', label: 'Video darslar (tavsiya)' },
  { value: 'Live', label: 'Video + jonli darslar' },
  { value: 'Platform', label: 'Butun platforma' },
]

/** Qisqa shakl (eski `scopeTxt`) — matn ichida ishlatiladi. */
const BLOCK_SCOPE_SHORT: Record<PaymentBlockScopeName, string> = {
  None: 'blok yo‘q',
  Video: 'video darslar',
  Live: 'video+jonli',
  Platform: 'butun platforma',
}

export function blockScopeShortLabel(scope: PaymentBlockScopeName): string {
  return BLOCK_SCOPE_SHORT[scope]
}

/* ============================================================= chegirma === */

const DISCOUNT_KIND_LABELS: Record<DiscountKindName, string> = {
  Percent: 'Foiz (%)',
  Amount: 'Summa (so‘m)',
}

export function discountKindLabel(kind: DiscountKindName): string {
  return DISCOUNT_KIND_LABELS[kind]
}

export const DISCOUNT_KIND_OPTIONS: ReadonlyArray<{ value: DiscountKindName; label: string }> = [
  { value: 'Percent', label: DISCOUNT_KIND_LABELS.Percent },
  { value: 'Amount', label: DISCOUNT_KIND_LABELS.Amount },
]

/** `10%` yoki `50 000 so'm` — eski ilovadagi server yorlig'i bilan bir xil. */
export function discountValueLabel(discount: StudentDiscountDto): string {
  return discount.kind === 'Percent'
    ? `${formatMoney(discount.value)}%`
    : `${formatMoney(discount.value)} so‘m`
}

/* ================================================================ tarif === */

/**
 * Tarifning qamrovi — eski ilovada server `Guruh` / `Kurs` / `Umumiy` deb
 * atardi. Bu yerda `specificity` ga EMAS, maydonlarning o'ziga qaraladi:
 * `specificity` — serverning ichki tartiblash raqami va uning ma'nosi
 * o'zgarsa, yorliq jimgina noto'g'ri bo'lib qolardi.
 */
export function tariffScopeLabel(tariff: TariffDto): string {
  if (tariff.groupId !== null) return `Guruh: ${tariff.groupName ?? '—'}`
  if (tariff.courseId !== null) return `Kurs: ${tariff.courseName ?? '—'}`
  return 'Umumiy'
}

/* ================================================================== oy === */

const PERIOD_PATTERN = /^(\d{4})-(\d{2})$/

/** Markaz vaqtidagi joriy oy, `YYYY-MM`. */
export function currentPeriod(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}`
}

/**
 * `2026-07` -> `iyul 2026` (eski ilovadagi `periodLabel` bilan AYNAN bir xil).
 *
 * Oy nomlari `shared/lib/datetime.ts` dan olinadi va kichik harfga o'giriladi:
 * o'sha yerda ular BOSH harf bilan (kalendar sarlavhasi uchun), bu yerda esa
 * gap ichida keladi ("iyul 2026 uchun yozuvlar").
 */
export function periodLabel(period: string): string {
  const match = PERIOD_PATTERN.exec(period)
  if (match === null) return period
  const year = match[1] ?? ''
  const monthIndex = Number(match[2]) - 1
  const name = monthNameCapitalized(monthIndex)
  return name.length > 0 ? `${name.toLowerCase()} ${year}` : period
}

/**
 * Server `period` ni qat'iy tekshiradi va format buzuq bo'lsa 400 beradi
 * (`errors.period`: "Davr formati noto'g'ri: 'bad'"). `<input type="month">`
 * odatda to'g'ri qiymat beradi, lekin qo'lda tozalanganda bo'sh satr qoladi —
 * shuning uchun yuborishdan oldin shu yerda tekshiriladi.
 */
export function isValidPeriod(period: string): boolean {
  const match = PERIOD_PATTERN.exec(period)
  if (match === null) return false
  const month = Number(match[2])
  return month >= 1 && month <= 12
}

/** `<input type="date">` uchun bugungi sana, `YYYY-MM-DD` (MAHALLIY vaqtda). */
export function todayIsoDate(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  const day = now.getDate()
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}-${day < 10 ? '0' : ''}${day}`
}

/**
 * Joriy oyning BIRINCHI kuni, `YYYY-MM-DD` (MAHALLIY vaqtda).
 *
 * Moliya dashboard'ining standart `from` qiymati. Server ham AYNAN shu
 * standartni oladi ("joriy oy boshi..bugun"), lekin maydonlar baribir
 * to'ldirib ko'rsatiladi — kassir qaysi oraliqni ko'rayotganini bilishi kerak.
 *
 * `toISOString()` ISHLATILMAYDI: u UTC beradi va Toshkentda (UTC+5) tunning
 * birinchi yarmida oldingi kunni qaytarardi — 1-iyul kuni ertalab hisobot
 * 30-iyundan boshlanib, o'tgan oy tushumi bugungi kassaga qo'shilib ketardi.
 */
export function monthStartIsoDate(): string {
  const now = new Date()
  const month = now.getMonth() + 1
  return `${now.getFullYear()}-${month < 10 ? '0' : ''}${month}-01`
}

const ISO_DATE_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/

/**
 * `<input type="date">` qiymati serverga yuborishga yaroqlimi.
 *
 * Maydonni qo'lda tozalash mumkin (bo'sh satr qoladi). Buzuq qiymat
 * yuborilsa server 400 qaytarib, butun dashboard o'rniga xato ekrani
 * chiqardi — shuning uchun filtr shu yerda tekshiriladi va yaroqsiz bo'lsa
 * so'rov UMUMAN yuborilmaydi (`isValidPeriod` bilan bir xil yondashuv).
 */
export function isValidIsoDate(value: string): boolean {
  const match = ISO_DATE_PATTERN.exec(value)
  if (match === null) return false
  const month = Number(match[2])
  const day = Number(match[3])
  return month >= 1 && month <= 12 && day >= 1 && day <= 31
}

/**
 * `2026-07` + `2026-07` -> `iyul 2026`;
 * `2026-01` + `2026-07` -> `yanvar 2026 — iyul 2026`.
 *
 * Dashboard'dagi HISOB (accrual) raqamlari qaysi OYLARGA tegishli ekanini
 * aytadi. Bu sana oralig'i (`from..to`) bilan bir xil EMAS: 15-iyuldan
 * 20-avgustgacha tanlansa, hisob oylari iyul va avgust bo'ladi — buni
 * yozmasak, "Rejadagi tushum" tanlangan kunlarga tegishlidek o'qilardi.
 */
export function periodRangeLabel(fromPeriod: string, toPeriod: string): string {
  const from = periodLabel(fromPeriod)
  return fromPeriod === toPeriod ? from : `${from} — ${periodLabel(toPeriod)}`
}

/**
 * `93` -> `93%`, `93.42` -> `93,4%`.
 *
 * Server foizni 0..100 oralig'ida, kasr bilan yuborishi mumkin.
 * `entities/test` dagi `percentLabel` ATAYLAB qayta ishlatilmadi: u boshqa
 * entity'ga tegishli (FSD'da entity entity'dan import qilmaydi) va
 * yaxlitlamaydi — `93.4200000001` kabi qiymat kartochkani cho'zib yuborardi.
 *
 * Kasr ajratgichi vergul — `shared/lib/money.ts` dagi summalar bilan bir xil.
 */
export function collectionRateLabel(rate: number): string {
  if (!Number.isFinite(rate)) return '—'
  const rounded = Math.round(rate * 10) / 10
  return `${String(rounded).replace('.', ',')}%`
}
