import {
  checkItem,
  fetchAudioTicket,
  markBlockDone,
  revealSample,
  startTest,
  submitTest,
  unitAudioUrl,
} from '@/entities/interactive-unit'
import type { AudioTicketDto } from '@/entities/interactive-unit'

import type { LessonAdapter } from './context'

/** Chipta muddati tugashidan shuncha oldin yangisi olinadi. */
const TICKET_MARGIN_MS = 60_000

/**
 * Haqiqiy server adapteri (2026-10-29).
 *
 * ★ Audio chiptasi BIRLIKKA bitta (60 ta fayl uchun 60 so'rov emas) va
 *   ~15 daqiqa yashaydi — muddati tugashidan 1 daqiqa oldin yangilanadi.
 *   Bir vaqtda kelgan so'rovlar bitta chipta so'rovini kutadi.
 */
export function createApiAdapter(unitId: number): LessonAdapter {
  let ticket: AudioTicketDto | null = null
  let inflight: Promise<AudioTicketDto> | null = null

  async function token(): Promise<string> {
    const fresh = ticket !== null && new Date(ticket.expiresAt).getTime() - Date.now() > TICKET_MARGIN_MS
    if (fresh && ticket !== null) return ticket.token

    inflight ??= fetchAudioTicket(unitId).finally(() => {
      inflight = null
    })
    ticket = await inflight
    return ticket.token
  }

  return {
    check: (itemKey, body) => checkItem(unitId, itemKey, body),
    blockDone: (blockId) => markBlockDone(unitId, blockId),
    reveal: async (blockId, n) => (await revealSample(unitId, blockId, n)).namuna,
    startTest: () => startTest(unitId),
    submitTest: (answers) => submitTest(unitId, answers),
    audioUrl: async (handle) => unitAudioUrl(unitId, handle, await token()),
  }
}
