import { formatDateWithYear } from '@/shared/lib/datetime'
import { lookup } from '@/shared/lib/lookup'
import type { PaymentAgingBucketName, PaymentMonthSummaryDto } from '@/shared/types'

/**
 * MOLIYA DASHBOARD'INING KO'RINISH QOIDALARI.
 *
 * Ranglar eski ilovadan AYNAN ko'chirilgan:
 * `Zinnur-platform/app/templates/academic.html`
 *   • KPI kartochkalari — 2668–2674-qatorlar;
 *   • "Qarz yoshi" guruhlari — 2675-qator (`const AG={...}`);
 *   • "Oxirgi 12 oy" ustunlari — 2691–2693-qatorlar.
 *
 * ★ NEGA TEMA TOKENI EMAS, ANIQ HEX: bular DIAGRAMMA ranglari — ular
 * ma'noni (reja / yig'ilgan / qarz yoshi) kodlaydi va tema o'zgarganda
 * o'zgarmasligi kerak. Tema tokeni (`brand-500` va h.k.) sirtlar va tugmalar
 * uchun; ularni bu yerga aralashtirsak, o'quv bo'limi xodimi eski paneldagi
 * "yashil = yig'ilgan, qizil = qarz" odatini yo'qotardi.
 */

/** Eski `kpiCard(...)` chaqiruvlaridagi ranglar, o'sha tartibda. */
export const KPI_COLORS = {
  /** "Rejadagi tushum" — eskisida `#3b9eff`. */
  planned: '#3b9eff',
  /** "Yig'ilgan" — eskisida `#22c55e`. */
  collected: '#22c55e',
  /** "Yig'ilish foizi" — eskisida `#f2c84b` (o'quv bo'limi urg'u rangi). */
  rate: '#f2c84b',
  /** "Umumiy qarz" — eskisida `#f43f5e`. */
  debt: '#f43f5e',
  /** "Chegirmalar" — eskisida `#a855f7`. */
  discounts: '#a855f7',
  /** "Balansdagi pul" — eskisida `#14b8a6`. */
  balance: '#14b8a6',
  /**
   * "Kechirilgan" — eskisida KPI kartochkasi YO'Q edi (backend bu raqamni
   * endi beradi). Rang "Qarz yoshi" ning 61–90 guruhidan olindi: kechirim
   * ham yo'qotilgan pul, lekin qarzdek qizil emas.
   */
  waived: '#fb923c',
} as const

/** Eski `const AG={'0-30':…}` — qarz qancha eskirsa, rang shuncha xavfli. */
const AGING_COLORS: Record<PaymentAgingBucketName, string> = {
  '0-30': '#22c55e',
  '31-60': '#f2c84b',
  '61-90': '#fb923c',
  '90+': '#f43f5e',
}

/**
 * Guruh rangi. `lookup` ATAYLAB: server kelajakda beshinchi guruh qo'shsa
 * (masalan `180+`) UI qulamasin — noma'lum guruh eng xavfli rangda chiziladi,
 * chunki u faqat ESKIROQ qarz bo'lishi mumkin.
 */
export function agingColor(bucket: string): string {
  return lookup(AGING_COLORS, bucket, '#f43f5e')
}

/** Eski `${k} kun` — "0-30 kun", "90+ kun". */
export function agingLabel(bucket: string): string {
  return `${bucket} kun`
}

/* ------------------------------------------------------- "Oxirgi 12 oy" --- */

/** Reja ustuni — eskisida `rgba(59,158,255,.35)` (KPI ko'kining shaffofi). */
export const TREND_PLANNED_COLOR = 'rgba(59,158,255,.35)'

/** Yig'ilgan ustuni — eskisida to'q yashil `#22c55e`. */
export const TREND_COLLECTED_COLOR = '#22c55e'

/**
 * Ustun balandligi FOIZDA.
 *
 * ★ NOLGA BO'LISH: bo'sh bazada hamma qiymat `0` bo'ladi va `val/max`
 * `NaN` berardi — CSS'da `height: NaN%` yaroqsiz va ustunlar to'liq
 * balandlikda "yopishib" qolardi. Shuning uchun `max <= 0` da 0 qaytadi
 * (eski ilovadagi `Math.max(..., 1)` hiylasi bilan bir xil natija, lekin
 * niyati ko'rinib turadi).
 *
 * 100 dan yuqorisi KESILADI: guruh kesimida to'lov rejadan oshib ketishi
 * mumkin (oldingi oy qarzi yopilsa) va ustun konteynerdan chiqib ketardi.
 */
export function barPercent(value: number, max: number): number {
  if (max <= 0 || !Number.isFinite(value) || !Number.isFinite(max)) return 0
  return Math.min(100, Math.round((value / max) * 100))
}

/**
 * 12 oylik diagrammaning eng baland qiymati.
 *
 * Reja va yig'ilgan BIR XIL o'lchovda chizilishi shart — aks holda 300 000
 * lik ustun 3 000 000 lik ustundan baland ko'rinib, dinamika teskari
 * o'qilardi. Shu sababli maksimum IKKALASIDAN olinadi (eski ilovadagi
 * `trMax` bilan bir xil).
 */
export function trendMax(months: readonly PaymentMonthSummaryDto[]): number {
  let max = 0
  for (const month of months) {
    if (month.billed > max) max = month.billed
    if (month.collected > max) max = month.collected
  }
  return max
}

/** `2026-07` -> `07`; eski ilovadagi `r.period.slice(5)` — ustun tagidagi yorliq. */
export function monthTick(period: string): string {
  return period.slice(5)
}

/* -------------------------------------------------------------- sanalar --- */

/**
 * `2026-07-01` -> `1-iyul 2026`.
 *
 * ★ `T00:00:00` QO'SHILADI: `new Date('2026-07-01')` — spetsifikatsiya
 * bo'yicha UTC yarim tuni, `new Date('2026-07-01T00:00:00')` esa MAHALLIY.
 * Server sanani mahalliy (Asia/Tashkent) deb yuboradi, shuning uchun uni
 * mahalliy o'qish kerak — aks holda manfiy zonali qurilmada hisobot bir kun
 * oldin boshlangandek ko'rinardi.
 */
export function isoDateLabel(iso: string): string {
  return formatDateWithYear(`${iso}T00:00:00`)
}
