import { http } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'
import type {
  GroupChatChannelName,
  GroupChatMessageDto,
  GroupChatPageDto,
  GroupChatReadResultDto,
  GroupChatThreadDto,
  GroupTypeName,
} from '@/shared/types'

import { GROUP_CHAT_PAGE_SIZE } from '../model/types'

/** R38 · "Chatlar" ro'yxati filtri (guruh turi va yo'nalishi). */
export interface GroupChatThreadParams {
  /**
   * Guruh TURI.
   *
   * ⚠️ `'Curator'` YUBORILMAYDI — server 400 qaytaradi. Kurator turidagi
   * guruhning alohida chati yo'q va u bu ro'yxatda hech qachon ko'rinmaydi
   * (server qoidasi, to'rt joyda takrorlangan). Tanlagichda ham faqat
   * `Group` va `Individual` bo'ladi.
   */
  type?: Exclude<GroupTypeName, 'Curator'>
  /** O'quv yo'nalishi (kategoriya Id'si). */
  categoryId?: number
}

/**
 * `GET /api/v1/group-chat/threads` — "Chatlar" ro'yxati.
 *
 * ★ Element (guruh, KANAL) juftligi: o'quvchida bitta guruh ikki qator bo'lib
 * keladi (Ustoz chati + Kurator chati), ustozda esa faqat bittasi — server
 * kimga qaysi kanal ochiqligini o'zi hal qiladi. Klient bu ro'yxatni
 * to'ldirmaydi.
 *
 * 🔴 R38 · FILTR SERVERGA PARAMETR SIFATIDA YUBORILADI, ro'yxat ustida
 * `Array.filter` bilan EMAS. Server ro'yxatni saralagandan KEYIN 200 qatorda
 * kesadi (`GroupChatService.MaxThreads`), ya'ni mijozdagi filtr kesilgandan
 * keyingi guruhlarni UMUMAN ko'rmasdi: 201-o'rindagi guruh filtrga to'liq mos
 * kelsa ham natijada chiqmasdi. Bu UX nuqsoni emas, MA'LUMOT YO'QOLISHI.
 */
export function fetchGroupChatThreads(
  params: GroupChatThreadParams = {},
  options?: { signal?: AbortSignal },
): Promise<GroupChatThreadDto[]> {
  return http.get<GroupChatThreadDto[]>('/api/v1/group-chat/threads', {
    // ★ Query nomlari camelCase — `GroupChatThreadQuery` maydonlari bilan
    //   AYNAN bir xil. (`fetchGroups` da BOSH HARF ishlatiladi, chunki u
    //   yerdagi Swagger shakli boshqa; ikkalasi ham ASP.NET uchun ishlaydi,
    //   lekin bu yerda server DTO'si bilan bir xil yozuv aniqroq.)
    query: { type: params.type, categoryId: params.categoryId },
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

/* ==========================================================================
   R16b · BIRIKTIRMALAR (rasm / ovoz / hujjat)
   ========================================================================== */

/**
 * `GET /api/v1/group-chat/attachments/{id}` — biriktirmaning BAYTLARI.
 *
 * ★ NAQSH `fetchSubmissionFile` BILAN AYNI: endpoint `Authorization` talab
 * qiladi, brauzer esa `<img src>` / `<audio src>` so'rovlarida uni
 * YUBORMAYDI. Shuning uchun fayl `Blob` sifatida olinadi va
 * `URL.createObjectURL` bilan ko'rsatiladi.
 *
 * 🔴 TO'G'RIDAN-TO'G'RI `src` GA QO'YIB BO'LMAYDI — har safar 401 kelardi.
 */
export function fetchGroupChatAttachment(
  attachmentId: number,
  options?: { signal?: AbortSignal },
): Promise<DownloadedFile> {
  return http.download(
    `/api/v1/group-chat/attachments/${attachmentId}`,
    `fayl-${attachmentId}`,
    { signal: options?.signal, headers: { Accept: '*/*' } },
  )
}
