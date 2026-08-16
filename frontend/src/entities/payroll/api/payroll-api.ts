import { http } from '@/shared/api'
import type {
  CreatePayrollAdjustmentRequest,
  CreateTeacherRateRequest,
  PayrollAdjustmentDto,
  PayrollDetailDto,
  PayrollPeriodActionRequest,
  PayrollSummaryDto,
  TeacherRateDto,
  UpdateTeacherRateRequest,
} from '@/shared/types'

const BASE = '/api/v1/payroll'

/** `GET /payroll/summary` — davr bo'yicha har xodim uchun yig'indi. */
export function fetchPayrollSummary(
  params: { period?: string } = {},
  options?: { signal?: AbortSignal },
): Promise<PayrollSummaryDto> {
  return http.get<PayrollSummaryDto>(`${BASE}/summary`, {
    query: { period: params.period },
    signal: options?.signal,
  })
}

/** `GET /payroll/{userId}/detail` — bitta xodimning dars-dars tafsiloti. */
export function fetchPayrollDetail(
  userId: number,
  params: { period?: string } = {},
  options?: { signal?: AbortSignal },
): Promise<PayrollDetailDto> {
  return http.get<PayrollDetailDto>(`${BASE}/${userId}/detail`, {
    query: { period: params.period },
    signal: options?.signal,
  })
}

export function fetchTeacherRates(options?: { signal?: AbortSignal }): Promise<TeacherRateDto[]> {
  return http.get<TeacherRateDto[]>(`${BASE}/rates`, { signal: options?.signal })
}

export function createTeacherRate(body: CreateTeacherRateRequest): Promise<TeacherRateDto> {
  return http.post<TeacherRateDto>(`${BASE}/rates`, body)
}

/** ★ TO'LIQ ALMASHTIRISH — `UpdateTeacherRateRequest` dagi izohga qarang. */
export function updateTeacherRate(id: number, body: UpdateTeacherRateRequest): Promise<TeacherRateDto> {
  return http.put<TeacherRateDto>(`${BASE}/rates/${id}`, body)
}

export function deleteTeacherRate(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/rates/${id}`)
}

/* ------------------------------------------------------------ tuzatish */

export function createPayrollAdjustment(
  body: CreatePayrollAdjustmentRequest,
): Promise<PayrollAdjustmentDto> {
  return http.post<PayrollAdjustmentDto>(`${BASE}/adjustments`, body)
}

export function deletePayrollAdjustment(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/adjustments/${id}`)
}

/* ------------------------------------------------------- tasdiqlash/to'lov */

export function approvePayrollPeriod(body: PayrollPeriodActionRequest): Promise<void> {
  return http.post<void>(`${BASE}/approve`, body)
}

export function markPayrollPeriodPaid(body: PayrollPeriodActionRequest): Promise<void> {
  return http.post<void>(`${BASE}/mark-paid`, body)
}
