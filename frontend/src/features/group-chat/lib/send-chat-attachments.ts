import { uploadWithProgress } from '@/features/lesson-media'
import type { UploadProgress } from '@/features/lesson-media'
import type { GroupChatChannelName, GroupChatMessageDto } from '@/shared/types'

/**
 * ========================================================================
 * R16b · FAYL BILAN XABAR YUBORISH
 * ========================================================================
 *
 * 🔴 NEGA HUB EMAS, REST: hub metodi `SendMessage(groupId, channel, body)` —
 * uchinchi argumenti SATR. Baytlarni base64 qilib satrga solish mumkin edi,
 * lekin 10 MB fayl 13 MB matnga aylanib, SignalR ning freym chegarasidan
 * oshib ketardi va butun ULANISH uzilardi — ya'ni rasm yuborishga urinish
 * chatning o'zini o'ldirardi. Shuning uchun klient qoidasi bitta va aniq:
 *
 *     biriktirma BOR  -> shu funksiya (REST, `multipart/form-data`)
 *     biriktirma YO'Q -> hub (`useGroupChatRoom.send`)
 *
 * ★ QARSHI TOMON FARQNI SEZMAYDI: server har ikkala yo'lda ham xabarni
 * bazaga yozib, keyin AYNI `GroupChatMessage` hodisasi bilan tarqatadi
 * (commit-then-send). Ya'ni "REST orqali yuborilgan xabar realtime kelmaydi"
 * degan holat yo'q.
 *
 * ★ NEGA `uploadWithProgress` (oddiy `http.post` EMAS): `fetch` da YUKLASH
 * progressi yo'q, 10 MB rasm esa sekin mobil internetda o'nlab sekund
 * ketadi. Ko'rsatkichsiz foydalanuvchi tugmani qayta-qayta bosardi.
 * Mexanizm `features/lesson-media` da BITTA joyda turadi (401 -> token
 * yangilash -> bir marta qayta yuborish, bekor qilish) va bu yerda faqat
 * QAYTA ISHLATILADI.
 *
 * ⚠️ HAMMA FAYL BITTA SO'ROVDA: server ularni bitta xabar va bitta
 * tranzaksiya sifatida yozadi. Shu tufayli "yuklandi, lekin yuborilmadi"
 * degan yetim obyekt umuman paydo bo'lmaydi (server tomondagi asos —
 * `GroupChatAttachment` izohi). Narxi: progress FAYL BOSHIGA emas, butun
 * so'rov bo'yicha ko'rinadi.
 */

/** Bitta xabarga ko'pi bilan shuncha fayl — SERVER chegarasining nusxasi. */
export const CHAT_ATTACHMENT_MAX_FILES = 5

export interface SendChatAttachmentsOptions {
  groupId: number
  files: readonly File[]
  /** Ixtiyoriy izoh. Bo'sh bo'lishi MUMKIN (izohsiz surat). */
  body?: string
  channel?: GroupChatChannelName
  onProgress?: (progress: UploadProgress) => void
  signal?: AbortSignal
}

export function sendGroupChatAttachments(
  options: SendChatAttachmentsOptions,
): Promise<GroupChatMessageDto> {
  const form = new FormData()

  for (const file of options.files) form.append('files', file)

  /*
    `body` va `channel` FAQAT qiymati bo'lganda qo'shiladi.

    🔴 Bo'sh satrni qo'shish XAVFSIZ EMAS: ASP.NET `[FromForm] GroupChatChannel?`
    uchun bo'sh satrni "noto'g'ri qiymat" deb hisoblab **400** qaytaradi
    (`null` deb emas). Ya'ni "server o'zi tanlasin" niyati jimgina xatoga
    aylanardi.
  */
  const body = options.body?.trim() ?? ''
  if (body.length > 0) form.append('body', body)
  if (options.channel !== undefined) form.append('channel', options.channel)

  return uploadWithProgress<GroupChatMessageDto>({
    path: `/api/v1/group-chat/groups/${options.groupId}/messages/attachments`,
    form,
    onProgress: options.onProgress,
    signal: options.signal,
  })
}
