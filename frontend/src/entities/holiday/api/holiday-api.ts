import { http } from '@/shared/api'
import type { CreateHolidayRequest, HolidayDto, HolidayImpactDto } from '@/shared/types'

const BASE = '/api/v1/holidays'

/**
 * BAYRAM KALENDARI (2026-08-16) — umumiy sanalar, O'quv bo'limi/admin
 * boshqaradi. Har sana BARCHA guruhlarning o'sha kundagi darsini bekor
 * qiladi va jadvalni avtomatik oldinga suradi (`HolidayService.CreateAsync`).
 */

export function fetchHolidays(options?: { signal?: AbortSignal }): Promise<HolidayDto[]> {
  return http.get<HolidayDto[]>(BASE, { signal: options?.signal })
}

export function createHoliday(body: CreateHolidayRequest): Promise<HolidayImpactDto> {
  return http.post<HolidayImpactDto>(BASE, body)
}

export function deleteHoliday(id: number): Promise<void> {
  return http.delete<void>(`${BASE}/${id}`)
}
