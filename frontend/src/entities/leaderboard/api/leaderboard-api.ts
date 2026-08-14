import { http } from '@/shared/api'
import type {
  CenterLeaderboardDto,
  GroupLeaderboardDto,
  LeaderboardScopeName,
  MyRankDto,
} from '@/shared/types'

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
 * `GET /api/v1/leaderboard/center` — BUTUN O'QUV MARKAZ bo'yicha jadval.
 *
 * 🔴 "MARKAZ" — "TIZIMDAGI HAMMA FOYDALANUVCHI" DEGANI EMAS. Chegara
 *    serverda (`ILearningCenterScope`) va u mahsulot bir necha o'quv
 *    markazga sotilganda ham o'z ma'nosini saqlaydi.
 *
 * ★ JAVOB QISQARTIRILGAN: `rows` — eng yaxshi `topCount` ta qator.
 *   O'quvchining o'z qatori yuqori yuzlikka kirmasa `rows` ichida
 *   BO'LMAYDI, lekin `me` da haqiqiy o'rni bilan keladi.
 *
 * Ko'ra oladi: markazning har qanday faol foydalanuvchisi — filtr
 * SERVERDA, frontend qoidani takrorlamaydi.
 */
export function fetchCenterLeaderboard(
  period?: string,
  options?: { signal?: AbortSignal },
): Promise<CenterLeaderboardDto> {
  return http.get<CenterLeaderboardDto>(`${BASE}/center`, {
    query: { period },
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/leaderboard/me` — "mening o'rnim", jadvalsiz.
 *
 * Guruh topilmasa `groupId` va `me` — `null` (xato EMAS: o'quvchi hali
 * guruhga qo'shilmagan bo'lishi mumkin).
 *
 * ★ `scope` BERILMASA SERVER `Group` OLADI — bosh sahifa kartochkasi
 *   avvalgidek ishlaydi va qimmat markaz hisobi bexosdan chaqirilmaydi.
 */
export function fetchMyRank(
  period?: string,
  options?: { signal?: AbortSignal; scope?: LeaderboardScopeName },
): Promise<MyRankDto> {
  return http.get<MyRankDto>(`${BASE}/me`, {
    query: { period, scope: options?.scope },
    signal: options?.signal,
  })
}
