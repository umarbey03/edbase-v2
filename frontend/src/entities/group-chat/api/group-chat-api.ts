import { http } from '@/shared/api'
import type {
  GroupChatChannelName,
  GroupChatMessageDto,
  GroupChatPageDto,
  GroupChatReadResultDto,
  GroupChatThreadDto,
} from '@/shared/types'

import { GROUP_CHAT_PAGE_SIZE } from '../model/types'

/**
 * `GET /api/v1/group-chat/threads` — "Chatlar" ro'yxati.
 *
 * ★ Element (guruh, KANAL) juftligi: o'quvchida bitta guruh ikki qator bo'lib
 * keladi (Ustoz chati + Kurator chati), ustozda esa faqat bittasi — server
 * kimga qaysi kanal ochiqligini o'zi hal qiladi. Klient bu ro'yxatni
 * filtrlamaydi va to'ldirmaydi.
 */
export function fetchGroupChatThreads(options?: {
  signal?: AbortSignal
}): Promise<GroupChatThreadDto[]> {
  return http.get<GroupChatThreadDto[]>('/api/v1/group-chat/threads', {
    signal: options?.signal,
  })
}

export interface GroupChatPageParams {
  /**
   * Berilmasa server BIRINCHI ruxsat etilgan kanalni tanlaydi va tanlovini
   * javobning `channel` maydonida aytadi. Ruxsat etilmagani so'ralsa — 403
   * (jimgina almashtirilmaydi).
   */
  channel?: GroupChatChannelName
  /** Undan ESKIROQ xabarlar (yuqoriga scroll). Oldingi sahifadagi `nextBeforeId`. */
  beforeId?: number
  take?: number
}

/**
 * `GET /api/v1/group-chat/groups/{groupId}/messages` — tarix sahifasi.
 *
 * ★ HOLATNI O'ZGARTIRMAYDI: bu so'rov xabarlarni O'QILGAN deb BELGILAMAYDI.
 * Buning uchun alohida `markGroupChatRead` chaqiriladi — ya'ni ro'yxatni
 * fon rejimida yangilash o'qilmaganlar sanog'ini "yeb qo'ymaydi".
 */
export function fetchGroupChatPage(
  groupId: number,
  params: GroupChatPageParams = {},
  options?: { signal?: AbortSignal },
): Promise<GroupChatPageDto> {
  return http.get<GroupChatPageDto>(`/api/v1/group-chat/groups/${groupId}/messages`, {
    query: {
      channel: params.channel,
      beforeId: params.beforeId,
      take: params.take ?? GROUP_CHAT_PAGE_SIZE,
    },
    signal: options?.signal,
  })
}

/**
 * `POST /api/v1/group-chat/groups/{groupId}/messages` — 201 bilan yangi xabar.
 *
 * ZAXIRA YO'L: odatda xabar HUB orqali ketadi (darhol va bitta uzatishda),
 * bu REST esa hub uzilib qolganda ishlatiladi. Ikkalasi ham BITTA tezlik
 * budjetiga tegishli, shuning uchun "hub ishlamasa REST bilan tezroq
 * yuboraman" degan yo'l YO'Q.
 */
export function sendGroupChatMessage(
  groupId: number,
  body: string,
  channel?: GroupChatChannelName,
): Promise<GroupChatMessageDto> {
  return http.post<GroupChatMessageDto>(`/api/v1/group-chat/groups/${groupId}/messages`, {
    channel,
    body,
  })
}

/**
 * `POST /api/v1/group-chat/groups/{groupId}/read` — o'qilgan chegarasini surish.
 *
 * `upToMessageId` berilmasa server oxirigacha belgilaydi. Javobdagi
 * `changed: false` — chegara allaqachon shu yerda edi (takroriy so'rov zararsiz).
 */
export function markGroupChatRead(
  groupId: number,
  channel?: GroupChatChannelName,
  upToMessageId?: number,
): Promise<GroupChatReadResultDto> {
  return http.post<GroupChatReadResultDto>(`/api/v1/group-chat/groups/${groupId}/read`, {
    channel,
    upToMessageId,
  })
}
