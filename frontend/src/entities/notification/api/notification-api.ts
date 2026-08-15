import { http } from '@/shared/api'
import type {
  NotificationDeleteResultDto,
  NotificationPageDto,
  NotificationReadResultDto,
  NotificationUnreadDto,
} from '@/shared/types'

import { NOTIFICATION_PAGE_SIZE } from '../model/types'

export interface NotificationPageParams {
  /** Undan ESKIROQ qatorlar (pastga scroll). Oldingi sahifadagi `nextBeforeId`. */
  beforeId?: number
  unreadOnly?: boolean
  take?: number
}

/**
 * `GET /api/v1/notifications` — qo'ng'iroqcha ro'yxati.
 *
 * ★ HOLATNI O'ZGARTIRMAYDI: bu so'rov qatorlarni O'QILGAN deb
 * BELGILAMAYDI. Buning uchun alohida `markNotificationsRead` bor — ya'ni
 * ro'yxatni fon rejimida yangilash o'qilmaganlar sanog'ini "yeb
 * qo'ymaydi" (guruh chati bilan AYNI kelishuv).
 */
export function fetchNotifications(
  params: NotificationPageParams = {},
  options?: { signal?: AbortSignal },
): Promise<NotificationPageDto> {
  return http.get<NotificationPageDto>('/api/v1/notifications', {
    query: {
      beforeId: params.beforeId,
      unreadOnly: params.unreadOnly,
      take: params.take ?? NOTIFICATION_PAGE_SIZE,
    },
    signal: options?.signal,
  })
}

/**
 * `GET /api/v1/notifications/unread-count` — faqat nishondagi raqam.
 *
 * ★ ALOHIDA SO'ROV: bu raqam HAR sahifada ko'rinadi, ro'yxat esa faqat
 * qo'ng'iroqcha ochilganda kerak. Bittasini ikkinchisidan olish har
 * sahifa ochilishida 20 ta qator tortib kelardi.
 */
export function fetchUnreadCount(options?: {
  signal?: AbortSignal
}): Promise<NotificationUnreadDto> {
  return http.get<NotificationUnreadDto>('/api/v1/notifications/unread-count', {
    signal: options?.signal,
  })
}

/**
 * `POST /api/v1/notifications/read` — "o'qildi" (idempotent).
 *
 * @param ids Berilmasa yoki bo'sh bo'lsa — BARCHA o'qilmaganlar.
 */
export function markNotificationsRead(
  ids?: number[],
): Promise<NotificationReadResultDto> {
  return http.post<NotificationReadResultDto>('/api/v1/notifications/read', { ids })
}

/**
 * `POST /api/v1/notifications/delete` — BUTUNLAY o'chirish (idempotent).
 *
 * ★ `POST`, `DELETE` EMAS: Id'lar TANADA keladi, `DELETE` so'rovining
 * tanasi esa HTTP da rasman belgilanmagan (ba'zi proksilar uni tashlab
 * yuboradi). `POST .../read` bilan bir xil shakl.
 *
 * 🔴 BO'SH RO'YXAT YUBORILMAYDI: server 400 qaytaradi va bu ATAYLAB —
 * bo'sh massiv "hammasini o'chir" degani emas. Chaqiruvchi tomonda ham
 * shu tekshiruv bor (`NotificationBell`: belgilanmagan holatda tugma
 * umuman ko'rinmaydi).
 *
 * ⚠️ QAYTARIB BO'LMAYDI — chaqirishdan OLDIN tasdiqlash oynasi
 * (`useConfirm`) ko'rsatilishi SHART.
 */
export function deleteNotifications(ids: number[]): Promise<NotificationDeleteResultDto> {
  return http.post<NotificationDeleteResultDto>('/api/v1/notifications/delete', { ids })
}
