import { http } from '@/shared/api'
import type {
  CreatePayrollAdjustmentRequest,
  GroupPayrollAssignmentDto,
  PayrollAdjustmentDto,
  PayrollDetailDto,
  PayrollPeriodActionRequest,
  PayrollRuleDto,
  PayrollRuleRequest,
  PayrollStudentCoefficientDto,
  PayrollSummaryDto,
  SetGroupPayrollAssignmentRequest,
  SetPayrollStudentCoefficientRequest,
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

/* ------------------------------------------------------------- qoidalar */

export function fetchPayrollRules(options?: { signal?: AbortSignal }): Promise<PayrollRuleDto[]> {
  return http.get<PayrollRuleDto[]>(`${BASE}/rules`, { signal: options?.signal })
}

export function createPayrollRule(body: PayrollRuleRequest): Promise<PayrollRuleDto> {
  return http.post<PayrollRuleDto>(`${BASE}/rules`, body)
}

/** ★ TO'LIQ ALMASHTIRISH (bosqichlar bilan) — izoh: `PayrollRuleRequest`. */
export function updatePayrollRule(id: number, body: PayrollRuleRequest): Promise<PayrollRuleDto> {
  return http.put<PayrollRuleDto>(`${BASE}/rules/${id}`, body)
}

export function deletePayrollRule(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/rules/${id}`)
}

/* ------------------------------------------------------ guruh tayinlash */

export function fetchGroupPayrollAssignments(
  options?: { signal?: AbortSignal },
): Promise<GroupPayrollAssignmentDto[]> {
  return http.get<GroupPayrollAssignmentDto[]>(`${BASE}/group-assignments`, {
    signal: options?.signal,
  })
}

export function setGroupPayrollAssignment(
  groupId: number,
  body: SetGroupPayrollAssignmentRequest,
): Promise<GroupPayrollAssignmentDto> {
  return http.put<GroupPayrollAssignmentDto>(`${BASE}/group-assignments/${groupId}`, body)
}

/* ---------------------------------------------------------- koeffitsient */

/**
 * ★ `percent: 100` — yozuv o'chiriladi va server `204` qaytaradi
 * (`http.put` bo'sh javobda `null` beradi). Sabab: jadval faqat ISTISNONI
 * saqlaydi.
 */
export function setPayrollStudentCoefficient(
  body: SetPayrollStudentCoefficientRequest,
): Promise<PayrollStudentCoefficientDto | null> {
  return http.put<PayrollStudentCoefficientDto | null>(`${BASE}/coefficients`, body)
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
