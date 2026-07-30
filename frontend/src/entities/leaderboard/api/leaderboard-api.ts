import { http } from '@/shared/api'
import type { GroupLeaderboardDto, MyRankDto } from '@/shared/types'

const BASE = '/api/v1/leaderboard'

/**
 * `GET /api/v1/leaderboard/groups/{groupId}` — guruhning bir oylik jadvali.
 *
 * `period` berilmasa server joriy oyni oladi. Ko'ra oladi: guruhning faol
 * a'zosi, ustoz/kurator, o'quv bo'limi, admin — filtr SERVERDA, frontend
 * qoidani takrorlamaydi.
 */
export function fetchGroupLeaderboard(
  groupId: number,
  period?: string,
  options?: { signal?: AbortSignal },
): Promise<GroupLeaderboardDto> {
  return http.get<GroupLeaderboardDto>(`${BASE}/groups/${groupId}`, {
    query: { period },
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/leaderboard/me` — "mening o'rnim", jadvalsiz.
 *
 * Guruh topilmasa `groupId` va `me` — `null` (xato EMAS: o'quvchi hali
 * guruhga qo'shilmagan bo'lishi mumkin).
 */
export function fetchMyRank(
  period?: string,
  options?: { signal?: AbortSignal },
): Promise<MyRankDto> {
  return http.get<MyRankDto>(`${BASE}/me`, {
    query: { period },
    signal: options?.signal,
  })
}
