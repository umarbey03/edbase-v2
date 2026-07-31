import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
import type {
  CreateDiscountRequest,
  CreateTariffRequest,
  FinanceSettingsDto,
  OpenPeriodRequest,
  OpenPeriodResult,
  PagedResult,
  PaymentBlockDto,
  PaymentBlockScopeName,
  PaymentDto,
  PaymentReceiptDto,
  PaymentStatusName,
  PaymentSummaryDto,
  PaymentTransactionDto,
  RecordPaymentRequest,
  ReversalDto,
  ReversePaymentRequest,
  SetExemptRequest,
  StudentAccountDto,
  StudentDiscountDto,
  TariffDto,
  UpdateDiscountRequest,
  UpdateFinanceSettingsRequest,
  UpdateTariffRequest,
  WaiveRequest,
} from '@/shared/types'

const BASE = '/api/v1/payments'

/* ============================================================== oy ochish === */

/**
 * `POST /payments/periods/open` — joriy (yoki so'ralgan) oy yozuvlarini ochadi.
 *
 * IDEMPOTENT: takror bosilsa yangi qator YARATILMAYDI va xato ham bermaydi —
 * javobdagi `alreadyOpen` nechtasi o'tkazib yuborilganini aytadi. Shuning
 * uchun UI'da tugma "bir marta bosiladigan xavfli amal" deb ko'rsatilmaydi.
 */
export function openPeriod(body: OpenPeriodRequest): Promise<OpenPeriodResult> {
  return http.post<OpenPeriodResult>(`${BASE}/periods/open`, body)
}

/* ==================================================================== pul === */

/** `POST /payments` — to'lov kiritishning YAGONA yo'li. Javob — kvitansiya. */
export function recordPayment(body: RecordPaymentRequest): Promise<PaymentReceiptDto> {
  return http.post<PaymentReceiptDto>(BASE, body)
}

/** `POST /payments/{id}/waive` — to'liq to'langan oy uchun 409 qaytadi. */
export function waivePayment(paymentId: number, body: WaiveRequest): Promise<PaymentDto> {
  return http.post<PaymentDto>(`${BASE}/${paymentId}/waive`, body)
}

/**
 * `POST /payments/reverse` — pulni orqaga qaytaradi.
 *
 * ★ Qisman qaytarish XATO EMAS: qoldiq `unreturned` da qaytadi va UI uni
 * ALOHIDA ko'rsatishi shart (jimgina "qaytarildi" deb yozish hisobni buzardi).
 */
export function reversePayment(body: ReversePaymentRequest): Promise<ReversalDto> {
  return http.post<ReversalDto>(`${BASE}/reverse`, body)
}

/* ================================================================= o'qish === */

export interface PaymentListParams {
  /** `YYYY-MM`. Format buzuq bo'lsa server 400 beradi (`errors.period`). */
  period?: string
  groupId?: number
  studentId?: number
  status?: PaymentStatusName
  /** `true` — faqat qarzi borlar (`Due` va `Partial`). */
  onlyDebt?: boolean
  page?: number
  pageSize?: number
}

/** `GET /payments` — oylik yozuvlar ro'yxati (qarzdorlar hisoboti uchun ham). */
export function fetchPayments(
  params: PaymentListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<PaymentDto>> {
  return http.get<PagedResult<PaymentDto>>(BASE, {
    query: {
      period: params.period,
      groupId: params.groupId,
      studentId: params.studentId,
      status: params.status,
      // `false` ni ham yuboramiz: server standarti allaqachon `false`, lekin
      // aniq yuborilgan qiymat so'rovni o'qiyotgan odam uchun tushunarli.
      onlyDebt: params.onlyDebt,
      page: params.page,
      pageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/** `GET /payments/students/{id}` — qarz, balans, oylar tarixi, oxirgi jurnal. */
export function fetchStudentAccount(
  studentId: number,
  options?: { signal?: AbortSignal },
): Promise<StudentAccountDto> {
  return http.get<StudentAccountDto>(`${BASE}/students/${studentId}`, { signal: options?.signal })
}

/** `GET /payments/students/{id}/transactions` — sahifalangan moliya jurnali. */
export function fetchStudentTransactions(
  studentId: number,
  params: { page?: number; pageSize?: number } = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<PaymentTransactionDto>> {
  return http.get<PagedResult<PaymentTransactionDto>>(`${BASE}/students/${studentId}/transactions`, {
    query: { page: params.page, pageSize: params.pageSize },
    signal: options?.signal,
  })
}

/* ================================================================= yig'ma === */

/**
 * Dashboard davri.
 *
 * ★ IKKALA CHEGARA HAM KIRADI va sanalar MAHALLIY (Asia/Tashkent).
 * `new Date().toISOString().slice(0, 10)` bilan yasalgan qiymat YUBORILMAYDI:
 * u UTC beradi va Toshkentda 05:00 gacha bo'lgan payt bir kun oldingi sanani
 * ko'rsatib, oy boshidagi tushum hisobotdan tushib qolardi. Shu sababli
 * sanalar `entities/payment` dagi mahalliy yordamchilardan olinadi.
 *
 * Berilmasa server o'zi "joriy oy boshi..bugun" ni oladi; UI ham AYNAN shu
 * standartni ko'rsatadi — maydonlar bo'sh turmasin va kassir qaysi oraliqni
 * ko'rayotganini bilsin.
 */
export interface PaymentSummaryParams {
  /** `YYYY-MM-DD`. */
  from?: string
  to?: string
}

/**
 * `GET /payments/summary` — moliya dashboard'ining YAGONA manbasi.
 *
 * NEGA BITTA SO'ROV: raqamlarni mijozda hisoblash uchun barcha to'lov
 * qatorlarini (72 000 dan ortiq) yuklab olish kerak bo'lardi va natija
 * baribir "bir lahza oldingi" holat bo'lardi. Server buni 21–32 ms da qiladi.
 *
 * ★ `from > to` bo'lsa server 400 beradi va sababni `problem.errors.from` da
 * yozadi — `toUserMessage` uni o'zi o'qiydi, shuning uchun UI'da alohida
 * tahlil qilinmaydi. Maksimal oraliq — 5 yil.
 */
export function fetchPaymentSummary(
  params: PaymentSummaryParams = {},
  options?: { signal?: AbortSignal },
): Promise<PaymentSummaryDto> {
  return http.get<PaymentSummaryDto>(`${BASE}/summary`, {
    query: { from: params.from, to: params.to },
    signal: options?.signal,
  })
}

/**
 * `GET /payments/summary/export` — o'sha hisobotning CSV nusxasi.
 *
 * ★ `http.download` ATAYLAB: javob `Authorization` talab qiladi, brauzer
 * navigatsiyasi (`window.open`, `<a href>`) esa bu sarlavhani YUBORMAYDI va
 * fayl o'rniga 401 kelardi. Bu yo'ldan o'tganda token yangilash ham ishlaydi.
 *
 * CSV UTF-8 BOM va `sep=,` bilan keladi (Excel uchun) — mijoz tomonida
 * mazmunga UMUMAN tegilmaydi, faqat saqlanadi.
 */
export function downloadPaymentSummaryCsv(
  params: PaymentSummaryParams = {},
): Promise<DownloadedFile> {
  return http.download(`${BASE}/summary/export`, 'moliya-hisobot.csv', {
    query: { from: params.from, to: params.to },
  })
}

/* =================================================================== blok === */

/** `GET /payments/students/{id}/block` — 403 ga duch kelmasdan oldin tekshirish. */
export function fetchBlockStatus(
  studentId: number,
  scope: PaymentBlockScopeName,
  options?: { signal?: AbortSignal },
): Promise<PaymentBlockDto> {
  return http.get<PaymentBlockDto>(`${BASE}/students/${studentId}/block`, {
    query: { scope },
    signal: options?.signal,
  })
}

/** `POST /payments/students/{id}/exempt` — bloklashdan istisno (yoki bekor). */
export function setStudentExempt(
  studentId: number,
  body: SetExemptRequest,
): Promise<PaymentBlockDto> {
  return http.post<PaymentBlockDto>(`${BASE}/students/${studentId}/exempt`, body)
}

/* =============================================================== sozlama === */

export function fetchFinanceSettings(options?: {
  signal?: AbortSignal
}): Promise<FinanceSettingsDto> {
  return http.get<FinanceSettingsDto>(`${BASE}/settings`, { signal: options?.signal })
}

/**
 * `PUT /payments/settings`.
 *
 * ★ `enforce` YUBORILMAYDI — u muhit xossasi (`Payments:EnforceBlock`) va
 * serverda faqat o'qiladi. Formada u ko'rsatiladi, lekin O'ZGARTIRILMAYDI.
 */
export function updateFinanceSettings(
  body: UpdateFinanceSettingsRequest,
): Promise<FinanceSettingsDto> {
  return http.put<FinanceSettingsDto>(`${BASE}/settings`, body)
}

/* ================================================================= tarif === */

export function fetchTariffs(
  params: { isActive?: boolean } = {},
  options?: { signal?: AbortSignal },
): Promise<TariffDto[]> {
  return http.get<TariffDto[]>(`${BASE}/tariffs`, {
    query: { isActive: params.isActive },
    signal: options?.signal,
  })
}

/**
 * `GET /payments/tariffs/resolve` — guruhga AYNAN qaysi tarif tushishi.
 *
 * Server mos tarif bo'lmasa `204 No Content` beradi va bu XATO EMAS,
 * shunchaki "sozlanmagan". `http` 204 da `undefined` qaytaradi — uni shu
 * yerda `null` ga aylantiramiz, chaqiruvchi `undefined` bilan
 * `TariffDto | undefined` orasida chalkashmasin.
 */
export async function resolveTariff(
  groupId: number,
  params: { onDate?: string } = {},
  options?: { signal?: AbortSignal },
): Promise<TariffDto | null> {
  const tariff = await http.get<TariffDto | undefined>(`${BASE}/tariffs/resolve`, {
    query: { groupId, onDate: params.onDate },
    signal: options?.signal,
  })
  return tariff ?? null
}

export function createTariff(body: CreateTariffRequest): Promise<TariffDto> {
  return http.post<TariffDto>(`${BASE}/tariffs`, body)
}

/** ★ TO'LIQ ALMASHTIRISH — `UpdateTariffRequest` dagi izohga qarang. */
export function updateTariff(id: number, body: UpdateTariffRequest): Promise<TariffDto> {
  return http.put<TariffDto>(`${BASE}/tariffs/${id}`, body)
}

export function deleteTariff(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/tariffs/${id}`)
}

/* ============================================================== chegirma === */

export function fetchStudentDiscounts(
  studentId: number,
  options?: { signal?: AbortSignal },
): Promise<StudentDiscountDto[]> {
  return http.get<StudentDiscountDto[]>(`${BASE}/students/${studentId}/discounts`, {
    signal: options?.signal,
  })
}

export function createDiscount(
  studentId: number,
  body: CreateDiscountRequest,
): Promise<StudentDiscountDto> {
  return http.post<StudentDiscountDto>(`${BASE}/students/${studentId}/discounts`, body)
}

/** ★ TO'LIQ ALMASHTIRISH — forma barcha maydonlarni qaytaradi. */
export function updateDiscount(
  studentId: number,
  id: number,
  body: UpdateDiscountRequest,
): Promise<StudentDiscountDto> {
  return http.put<StudentDiscountDto>(`${BASE}/students/${studentId}/discounts/${id}`, body)
}

export function deleteDiscount(studentId: number, id: number): Promise<void> {
  return http.delete<void>(`${BASE}/students/${studentId}/discounts/${id}`)
}
