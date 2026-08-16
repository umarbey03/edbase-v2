import { http } from '@/shared/api'
import type {
  LiveKitJoinDto,
  LiveSessionDto,
  PagedResult,
  SessionStatsDto,
  SessionStatusName,
} from '@/shared/types'

const BASE = '/api/v1/live-sessions'

/** `GET /api/v1/live-sessions/stats` filtri (R31). */
export interface SessionStatsParams {
  status?: SessionStatusName
  groupId?: number
  page?: number
  pageSize?: number
}

/**
 * R31: darslar jadvali — o'quvchi soni, qatnashganlar soni va davomiylik.
 *
 * 🔴 BU YAGONA YO'L. Har dars uchun `/live-sessions/{id}/attendance` ga
 * borish MUMKIN EMAS: bir guruhda 69 tagacha dars bo'ladi va davomat
 * matritsasi aynan shu sababdan 10 ta ustun bilan cheklangan
 * (`attendance-matrix.ts`). Sanoqlar SERVERDA hisoblanadi.
 *
 * ⚠️ Faqat XODIM uchun — o'quvchi 403 oladi.
 */
export function fetchSessionStats(
  params: SessionStatsParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<SessionStatsDto>> {
  return http.get<PagedResult<SessionStatsDto>>(`${BASE}/stats`, {
    // ★ Nomlar KICHIK harf bilan: endpoint alohida `[FromQuery]`
    //   parametrlarni oladi, `GET /groups` dagidek murakkab model EMAS.
    //   (ASP.NET query nomlarini baribir katta-kichik farqlamasdan
    //   bog'laydi — bu shunchaki serverdagi imzoning aksi.)
    query: {
      status: params.status,
      groupId: params.groupId,
      page: params.page,
      pageSize: params.pageSize,
    },
    signal: options?.signal,
  })
}

/** SPEC 5: `GET /api/v1/live-sessions` */
export function fetchLiveSessions(options?: { signal?: AbortSignal }): Promise<LiveSessionDto[]> {
  return http.get<LiveSessionDto[]>(BASE, { signal: options?.signal })
}

/** SPEC 5: `GET /api/v1/live-sessions/{id}` */
export function fetchLiveSession(
  id: number,
  options?: { signal?: AbortSignal },
): Promise<LiveSessionDto> {
  return http.get<LiveSessionDto>(`${BASE}/${id}`, { signal: options?.signal })
}

/** SPEC 5: `POST /api/v1/live-sessions/{id}/start` — Teacher/Assistant/Admin */
export function startLiveSession(id: number): Promise<LiveSessionDto> {
  return http.post<LiveSessionDto>(`${BASE}/${id}/start`)
}

/** SPEC 5: `POST /api/v1/live-sessions/{id}/end` — Teacher/Assistant/Admin */
export function endLiveSession(id: number): Promise<LiveSessionDto> {
  return http.post<LiveSessionDto>(`${BASE}/${id}/end`)
}

/**
 * `POST /api/v1/live-sessions/{id}/cancel` (2026-08-16) — FAQAT
 * Academic/Admin (loyiha egasi: "buni qo'lda o'quv va admin bo'limi
 * orqali qilinishi kerak bo'ladi"). Guruh jadvali avtomatik qayta
 * tuziladi — o'rnini bosuvchi dars oxiriga qo'shiladi.
 */
export function cancelLiveSession(id: number, reason?: string): Promise<LiveSessionDto> {
  return http.post<LiveSessionDto>(`${BASE}/${id}/cancel`, { reason })
}

/**
 * SPEC 5: `POST /api/v1/live-sessions/{id}/token` -> `LiveKitJoinDto`.
 * Frontend LiveKit'ga aynan shu javob bilan ulanadi.
 */
export function fetchLiveKitJoin(id: number): Promise<LiveKitJoinDto> {
  return http.post<LiveKitJoinDto>(`${BASE}/${id}/token`)
}
