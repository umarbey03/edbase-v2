import { http } from '@/shared/api'
import type { ChatMessageDto } from '@/shared/types'

/**
 * SPEC 5: `GET /api/v1/live-sessions/{id}/messages?take=50`.
 * Darsga kirganda oxirgi xabarlar tarixini bir marta yuklaydi; qolgani SignalR orqali.
 */
export function fetchRecentMessages(
  sessionId: number,
  take = 50,
  options?: { signal?: AbortSignal },
): Promise<ChatMessageDto[]> {
  return http.get<ChatMessageDto[]>(`/api/v1/live-sessions/${sessionId}/messages`, {
    query: { take },
    signal: options?.signal,
  })
}
