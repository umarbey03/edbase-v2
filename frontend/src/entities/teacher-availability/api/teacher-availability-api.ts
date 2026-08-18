import { http } from '@/shared/api'
import type {
  FreeTeacherParams,
  FreeTeacherResultDto,
  PagedResult,
  TeacherAvailabilityDetailDto,
  TeacherAvailabilityListParams,
  TeacherAvailabilityRowDto,
  TeacherAvailabilitySummaryDto,
} from '@/shared/types'

const BASE = '/api/v1/teacher-availability'

/**
 * USTOZ KUNLIK TASDIQLASH + O'RINBOSAR (2026-08-17) — o'quv bo'limi paneli.
 * Suhbat mantig'i (savol/javob, o'rinbosar qidirish) BUTUNLAY Telegram bot
 * orqali; bu yerdagi so'rovlar faqat O'QIYDI.
 */

export function fetchTeacherAvailability(
  params: TeacherAvailabilityListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<TeacherAvailabilityRowDto>> {
  return http.get<PagedResult<TeacherAvailabilityRowDto>>(BASE, {
    // Maydonlar OSHKOR sanab o'tiladi (`fetchUsers` bilan AYNI naqsh):
    // shunda `undefined` qiymatlar `buildUrl` da tashlab yuboriladi va
    // interfeys `Record<string, QueryValue>` ga majburan tiplanmaydi.
    query: {
      search: params.search,
      status: params.status,
      from: params.from,
      to: params.to,
      onlyUncovered: params.onlyUncovered,
      sort: params.sort,
      desc: params.desc,
      page: params.page,
      pageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/**
 * Yig'ma ko'rsatkichlar — AYNI filtr bilan, lekin sahifalashsiz.
 * `page`/`pageSize`/`sort` ataylab YUBORILMAYDI: yig'ma butun to'plamni
 * sanaydi va tartib unga ta'sir qilmaydi.
 */
export function fetchTeacherAvailabilitySummary(
  params: Omit<TeacherAvailabilityListParams, 'page' | 'pageSize' | 'sort' | 'desc'> = {},
  options?: { signal?: AbortSignal },
): Promise<TeacherAvailabilitySummaryDto> {
  return http.get<TeacherAvailabilitySummaryDto>(`${BASE}/summary`, {
    query: {
      search: params.search,
      status: params.status,
      from: params.from,
      to: params.to,
      onlyUncovered: params.onlyUncovered,
    },
    signal: options?.signal,
  })
}

export function fetchTeacherAvailabilityDetail(
  checkinId: number,
  options?: { signal?: AbortSignal },
): Promise<TeacherAvailabilityDetailDto> {
  return http.get<TeacherAvailabilityDetailDto>(`${BASE}/${checkinId}`, {
    signal: options?.signal,
  })
}

/**
 * BO'SH USTOZLAR (2026-08-18) — berilgan kun va vaqtda darsi yo'q
 * ustozlar. Individual o'quvchi biriktirishda birinchi qaraladi.
 */
export function fetchFreeTeachers(
  params: FreeTeacherParams = {},
  options?: { signal?: AbortSignal },
): Promise<FreeTeacherResultDto> {
  return http.get<FreeTeacherResultDto>(`${BASE}/free`, {
    query: {
      date: params.date,
      time: params.time,
      durationMinutes: params.durationMinutes,
      includeAssistants: params.includeAssistants,
      onlyFree: params.onlyFree,
      search: params.search,
    },
    signal: options?.signal,
  })
}
