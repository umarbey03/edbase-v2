import { http } from '@/shared/api'
import type { AbsenteeParams, AbsenteeReportDto } from '@/shared/types'

const BASE = '/api/v1/absentees'

/**
 * DARSGA KIRMAGANLAR XARITASI (2026-08-18) — kunlik, guruh kesimida.
 *
 * Sana berilmasa server KECHANI oladi: loyiha egasi aynan *"bir kun
 * avval darsga kirmagan"* larni so'radi, va bugungi darslarning ko'pi
 * hali o'tmagan bo'ladi.
 */
export function fetchAbsentees(
  params: AbsenteeParams = {},
  options?: { signal?: AbortSignal },
): Promise<AbsenteeReportDto> {
  return http.get<AbsenteeReportDto>(BASE, {
    // Maydonlar OSHKOR sanaladi (`fetchUsers` naqshi) — interfeys
    // `Record<string, QueryValue>` ga majburan tiplanmasin.
    query: {
      date: params.date,
      groupId: params.groupId,
      teacherId: params.teacherId,
      includePartial: params.includePartial,
      minStreak: params.minStreak,
      search: params.search,
    },
    signal: options?.signal,
  })
}
