import { http } from '@/shared/api'
import type {
  AbsenceNoticeListParams,
  AbsenceNoticeRowDto,
  AbsenceNoticeSummaryDto,
  AbsenceNoticeTarget,
  PagedResult,
  SendAbsenceNoticeRequest,
  SendAbsenceNoticeResultDto,
} from '@/shared/types'

const BASE = '/api/v1/absence-notices'

/**
 * KELMAGANLARGA XABAR (2026-08-18).
 *
 * ★ MAVJUD `broadcasts` DAN FARQI: u GURUHGA yuboradi va bitta qator
 * butun guruhni ifodalaydi. Bu yerda HAR OLUVCHIGA alohida yozuv —
 * "Doniyorga xabar bordimi?" degan savolga javob berish uchun.
 */

function toQuery(params: AbsenceNoticeListParams): Record<string, string | number | undefined> {
  return {
    from: params.from,
    to: params.to,
    groupId: params.groupId,
    studentId: params.studentId,
    delivery: params.delivery,
    search: params.search,
    page: params.page,
    pageSize: params.pageSize,
  }
}

export function fetchAbsenceNotices(
  params: AbsenceNoticeListParams = {},
  options?: { signal?: AbortSignal },
): Promise<PagedResult<AbsenceNoticeRowDto>> {
  return http.get<PagedResult<AbsenceNoticeRowDto>>(BASE, {
    query: toQuery(params),
    signal: options?.signal,
  })
}

/** Yig'ma — AYNI filtr, sahifalashsiz. */
export function fetchAbsenceNoticeSummary(
  params: AbsenceNoticeListParams = {},
  options?: { signal?: AbortSignal },
): Promise<AbsenceNoticeSummaryDto> {
  const { page: _page, pageSize: _pageSize, ...rest } = params

  return http.get<AbsenceNoticeSummaryDto>(`${BASE}/summary`, {
    query: toQuery(rest),
    signal: options?.signal,
  })
}

/**
 * Berilgan darslar bo'yicha ALLAQACHON xabar olganlar — kelmaganlar
 * ro'yxatida "yuborilgan" belgisini chizish uchun (bir odamga ikki
 * marta yozilmasin).
 */
export function fetchSentNoticeTargets(
  sessionIds: number[],
  options?: { signal?: AbortSignal },
): Promise<AbsenceNoticeTarget[]> {
  return http.get<AbsenceNoticeTarget[]>(`${BASE}/sent`, {
    // ★ VERGUL BILAN, massiv sifatida EMAS: `http` mijozi so'rov
    //   parametrida massivni qo'llab-quvvatlamaydi (`QueryValue` —
    //   oddiy qiymat). Uni massivga kengaytirish butun loyihadagi
    //   so'rovlarga ta'sir qilardi, bu yerdagi ehtiyoj esa bitta.
    query: { sessionIds: sessionIds.join(',') },
    signal: options?.signal,
  })
}

/** ⚠️ FAQAT o'quv bo'limi va admin (server 403 qaytaradi). */
export function sendAbsenceNotices(
  body: SendAbsenceNoticeRequest,
): Promise<SendAbsenceNoticeResultDto> {
  return http.post<SendAbsenceNoticeResultDto>(BASE, body)
}
