import { http } from '@/shared/api'
import { apiUrl } from '@/shared/config/env'

import type {
  AudioTicketDto,
  CheckItemBody,
  CheckItemResult,
  LessonBookDto,
  MiniTestAnswer,
  MiniTestDto,
  MiniTestResult,
  StudentUnitDto,
  UnitProgress,
} from '../model/types'

/**
 * Interaktiv darslik API (2026-10-29) — `InteractiveController`.
 *
 * 🔴 Javob tekshiruvi FAQAT serverda: klientda kalit ham, normallashtirish ham
 * yo'q. Shu sabab har javob — bitta kichik so'rov.
 */
const BASE = '/api/v1/interactive'

export function fetchLessonBook(lessonId: number, options?: { signal?: AbortSignal }): Promise<LessonBookDto> {
  return http.get<LessonBookDto>(`${BASE}/lessons/${lessonId}`, { signal: options?.signal })
}

export function fetchUnit(unitId: number, options?: { signal?: AbortSignal }): Promise<StudentUnitDto> {
  return http.get<StudentUnitDto>(`${BASE}/units/${unitId}`, { signal: options?.signal })
}

export function checkItem(unitId: number, itemKey: string, body: CheckItemBody): Promise<CheckItemResult> {
  return http.post<CheckItemResult>(`${BASE}/units/${unitId}/items/${encodeURIComponent(itemKey)}/check`, body)
}

export function markBlockDone(unitId: number, blockId: string): Promise<UnitProgress> {
  return http.post<UnitProgress>(`${BASE}/units/${unitId}/blocks/${encodeURIComponent(blockId)}/done`)
}

export function revealSample(unitId: number, blockId: string, n: number): Promise<{ namuna: string | null }> {
  return http.post<{ namuna: string | null }>(
    `${BASE}/units/${unitId}/blocks/${encodeURIComponent(blockId)}/reveal`,
    { n },
  )
}

export function startTest(unitId: number): Promise<MiniTestDto> {
  return http.post<MiniTestDto>(`${BASE}/units/${unitId}/test/start`)
}

export function submitTest(unitId: number, answers: MiniTestAnswer[]): Promise<MiniTestResult> {
  return http.post<MiniTestResult>(`${BASE}/units/${unitId}/test/submit`, { answers })
}

export function fetchAudioTicket(unitId: number): Promise<AudioTicketDto> {
  return http.get<AudioTicketDto>(`${BASE}/units/${unitId}/audio-ticket`)
}

/** `<audio src>` manzili: sarlavha yuborib bo'lmaydi — birlik chiptasi `?t=` bilan. */
export function unitAudioUrl(unitId: number, handle: string, token: string): string {
  return `${apiUrl(`${BASE}/units/${unitId}/audio/${encodeURIComponent(handle)}`)}?t=${encodeURIComponent(token)}`
}

export const interactiveKeys = {
  lesson: (lessonId: number) => ['interactive', 'lesson', lessonId] as const,
  unit: (unitId: number) => ['interactive', 'unit', unitId] as const,
  ticket: (unitId: number) => ['interactive', 'ticket', unitId] as const,
}
