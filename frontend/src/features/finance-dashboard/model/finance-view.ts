import { formatDateWithYear } from '@/shared/lib/datetime'
import { lookup } from '@/shared/lib/lookup'
import type { PaymentAgingBucketName, PaymentMonthSummaryDto } from '@/shared/types'

/**
 * MOLIYA DASHBOARD'INING KO'RINISH QOIDALARI.
 *
 * Tartib, sarlavhalar va matnlar eski ilovadan ko'chirilgan:
 * `Zinnur-platform/app/templates/academic.html`
 *   • KPI kartochkalari — 2668–2674-qatorlar;
 *   • "Qarz yoshi" guruhlari — 2675-qator (`const AG={...}`);
 *   • "Oxirgi 12 oy" ustunlari — 2691–2693-qatorlar.
 *
 * 🔴 RANGLAR ESA KO'CHIRILMADI — QAYTA QURILDI (2026-08-11).
 *
 * Ilgari shu faylda 12 ta QOTIB QOLGAN HEX turgan (`#3b9eff`, `#22c55e`,
 * `#f2c84b`, `#f43f5e`, `#a855f7`, `#14b8a6`, `#fb923c`, …) va yuqoridagi
 * izoh ularni "tema tokeni emas, ma'no rangi" deb himoya qilardi. Bu
 * fikrning YARMI to'g'ri: ranglar haqiqatan ma'no kodlaydi. Lekin ular
 * QORONG'I navy fon uchun tanlangan edi va ilova yorug' temaga o'tgach
 * oq kartochkada o'qilmay qoldi:
 *     `#f2c84b` (eski oltin) — 1.71:1 · `#22c55e` — 2.03:1 · `#fb923c` — 2.17:1
 * ya'ni "Yig'ilish foizi" raqami ham, yashil ustunlar ham amalda KO'RINMAS
 * edi. Bundan tashqari `#f2c84b` endi brend rangi ham emas.
 *
 * Yangi qiymatlar `src/style.css` dagi `--color-chart-*` tokenlarida va
 * ular "chiroyli" tanlanmagan — `dataviz` metodikasi bo'yicha HISOBLANGAN
 * (OKLab ΔE, CVD simulyatsiyasi, oq sirtda kontrast). Sabablari va
 * o'lchangan raqamlari o'sha token blokining izohida.
 *
 * ★ MA'NO SAQLANDI: "yashil = yig'ilgan, qizil = qarz" — o'quv bo'limi
 * xodimining eski paneldan olib kelgan odati. Faqat ohang boshqa: yorqin
 * yashil o'rniga to'q emerald (`chart-in`), pushti-qizil o'rniga to'q
 * qizil (`chart-out`).
 *
 * ★ QIYMAT SIFATIDA CSS `var()` QAYTARADI, HEX EMAS. Sabab: ranglar
 * inline `style` ichida ishlatiladi (`:style="{ backgroundColor: ... }"`),
 * ya'ni Tailwind klassi bo'lib bo'lmaydi; `var()` esa yagona manbani
 * (`style.css`) saqlaydi va nusxa HEX paydo bo'lishiga yo'l qo'ymaydi.
 */

/**
 * KPI kartochkasining URG'U rangi — kartochkaning 3px yuqori chizig'i.
 *
 * 🔴 RAQAMNING O'ZI ENDI RANGSIZ (siyoh rangida). `dataviz` qoidasi:
 * "qiymat va yorliq SIYOH tokenlarida, rang esa yonidagi BELGIDA" — aks
 * holda 26px raqam o'z rangining kontrastiga qaram bo'lib qoladi. Qorong'i
 * fonda rangli raqam to'g'ri yechim edi (yorqin rang o'qiladigan variant),
 * oq fonda esa to'q siyoh o'qiladi: `slate-50` = 18.9:1, ya'ni har qanday
 * rangdan yaxshiroq.
 *
 * ★ YETTI KO'RSATKICH — TO'RT RANG + NEYTRAL. Bu qisqartirish ataylab:
 * yorug' sirtda 4.5:1 talabi bilan bir vaqtda bir-biridan ajralib turadigan
 * to'rtdan ortiq ton fizik jihatdan yo'q (hisob-kitob `style.css` da).
 * Rangni faqat MA'NOSI bor ko'rsatkich oladi:
 *     havorang = reja/o'lchov · yashil = kelgan pul ·
 *     qizil = yetmagan/qaytgan pul · binafsha = voz kechilgan pul.
 * Qolgani (foiz, balans) — hisob-kitob ma'lumoti, neytral qoladi.
 */
export const KPI_ACCENTS = {
  /** "Rejadagi tushum" va "Sof tushum" — o'lchov bazasi. */
  planned: 'var(--color-chart-plan)',
  /** "Yig'ilgan" va "Kassaga tushgan" — kelgan pul. */
  collected: 'var(--color-chart-in)',
  /**
   * "Yig'ilish foizi" — NEYTRAL. Bu pul emas, nisbat: yashil qilsak
   * "Yig'ilgan" bilan bir xil ma'no berardi, alohida rang bersak esa
   * palitrada beshinchi ton kerak bo'lardi (yo'q — `style.css` ga qarang).
   */
  rate: 'var(--color-chart-neutral)',
  /** "Umumiy qarz" va "Qaytarilgan" — yetmagan yoki qaytgan pul. */
  debt: 'var(--color-chart-out)',
  /** "Chegirmalar" va "Kechirilgan" — ataylab olinmagan pul. */
  discounts: 'var(--color-chart-other)',
  /**
   * "Balansdagi pul" / "Balansdan yopilgan" — NEYTRAL. Bu YANGI tushum
   * emas (oldin to'langan puldan yopilgani), shuning uchun uni yashil
   * qilish hisobotni yolg'on qilardi.
   */
  balance: 'var(--color-chart-neutral)',
  /**
   * "Kechirilgan" — "Chegirmalar" bilan BIR XIL rangda. Ikkisi ham "voz
   * kechilgan pul" oilasi va ular hech qachon yonma-yon turmaydi
   * (biri asosiy raqamlar setkasida, ikkinchisi kassa jurnalida).
   */
  waived: 'var(--color-chart-other)',
} as const

/**
 * "Qarz yoshi" guruhi rangi — ORDINAL shkala (bitta ton, yorug'dan to'qqa).
 *
 * Eski `const AG={...}` da yashil → sariq → to'q sariq → qizil edi. Yorug'
 * fonda u ikki marta yiqiladi: sariq/to'q sariq 3:1 ga chiqmaydi, bizning
 * to'q "matn" qadamlarida esa sariq-to'q sariq-qizil bir-biridan
 * AJRALMAYDI (o'lchangan ΔE 2.9 — amalda bitta rang). Yechim va o'lchovlar
 * `style.css` dagi `--color-chart-age-*` izohida.
 */
const AGING_COLORS: Record<PaymentAgingBucketName, string> = {
  '0-30': 'var(--color-chart-age-1)',
  '31-60': 'var(--color-chart-age-2)',
  '61-90': 'var(--color-chart-age-3)',
  '90+': 'var(--color-chart-age-4)',
}

/** Shkalaning eng to'q (eng xavfli) qadami — noma'lum guruh uchun. */
const AGING_FALLBACK_COLOR = 'var(--color-chart-age-4)'

/**
 * Guruh rangi. `lookup` ATAYLAB: server kelajakda beshinchi guruh qo'shsa
 * (masalan `180+`) UI qulamasin — noma'lum guruh eng to'q rangda chiziladi,
 * chunki u faqat ESKIROQ qarz bo'lishi mumkin.
 */
export function agingColor(bucket: string): string {
  return lookup(AGING_COLORS, bucket, AGING_FALLBACK_COLOR)
}

/** Eski `${k} kun` — "0-30 kun", "90+ kun". */
export function agingLabel(bucket: string): string {
  return `${bucket} kun`
}

/* ------------------------------------------------------- "Oxirgi 12 oy" --- */

/**
 * Reja ustuni.
 *
 * 🔴 Eskisida `rgba(59,158,255,.35)` — 35% SHAFFOF ko'k edi. Qorong'i fonda
 * u "xira reja" bo'lib ko'rinardi, oq kartochkada esa `#baddff` ga
 * aylanib 1.35:1 beradi — ustun UMUMAN ko'rinmasdi va diagrammada faqat
 * yashil "yig'ilgan" qolardi, ya'ni reja bilan solishtirish imkoni yo'q
 * edi. Endi to'liq to'q havorang (4.70:1).
 *
 * ★ "Reja"ni ataylab xira qilmadik: `dataviz` qoidasi bo'yicha ikki qatorli
 * diagrammada ikkisi ham HAQIQIY qator (legenda bor), biri "fon" emas.
 */
export const TREND_PLANNED_COLOR = 'var(--color-chart-plan)'

/** Yig'ilgan ustuni — `chart-in` (oq fonda 5.70:1). Eskisida `#22c55e` (2.03:1). */
export const TREND_COLLECTED_COLOR = 'var(--color-chart-in)'

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
