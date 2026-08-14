import { computed } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { isGroupedWith, startsNewDay } from '@/entities/message'
import { formatDayLabel, formatTime } from '@/shared/lib/datetime'
import type { GroupChatAttachmentDto, GroupChatMessageDto } from '@/shared/types'

/** Render uchun tayyor qator. */
export interface GroupChatRow {
  id: number
  senderName: string
  senderRole: string
  body: string
  time: string
  /** Kun ajratgichi matni (`Bugun`, `Kecha`, `12-mart, Seshanba`) yoki `null`. */
  dayLabel: string | null
  /** Avatar + ism ko'rsatilsinmi (ketma-ket xabarlar guruhlanadi). */
  showHeader: boolean
  isOwn: boolean
  /**
   * R16b · biriktirmalar (biriktirmasiz xabarda BO'SH massiv).
   *
   * ★ `?? []` SHU YERDA bir marta bajariladi: DTO'da maydon `| null`
   * (hub orqali eski shakl kelishi mumkin), qator esa render uchun
   * TAYYOR bo'lishi kerak — komponentda har chizishda null-tekshiruv
   * yozilmasin.
   */
  attachments: readonly GroupChatAttachmentDto[]
}

/**
 * Xabarlarni render qatorlariga aylantiradi.
 *
 * ★ `isGroupedWith` va `startsNewDay` `entities/message` dan QAYTA
 * ISHLATILADI, nusxalanmaydi: guruhlash oynasi (5 daqiqa) va kun ajratgichi
 * qoidalari jonli dars chati bilan AYNAN bir xil bo'lishi kerak — bir xil
 * ko'rinadigan ikki chat turli qoida bilan ishlasa, foydalanuvchi buni
 * nosozlik deb qabul qilardi. `GroupChatMessageDto` tuzilish jihatidan
 * `ChatMessage` ni to'liq qamraydi (`id`, `senderId`, `senderName`, `body`,
 * `sentAt`), shuning uchun o'girish kerak emas.
 *
 * PERFORMANS: bu `computed`, `ref` EMAS — natija Vue tomonidan proksilanmaydi
 * va faqat `messages` havolasi almashganda qayta hisoblanadi.
 */
export function useGroupChatRows(
  messages: Ref<readonly GroupChatMessageDto[]>,
  currentUserId: Ref<number | null>,
): ComputedRef<GroupChatRow[]> {
  return computed<GroupChatRow[]>(() => {
    const list = messages.value
    const rows: GroupChatRow[] = []

    for (let index = 0; index < list.length; index += 1) {
      const message = list[index]
      if (message === undefined) continue
      const previous = index > 0 ? list[index - 1] : undefined

      const newDay = startsNewDay(previous, message)
      rows.push({
        id: message.id,
        senderName: message.senderName,
        senderRole: message.senderRole,
        body: message.body,
        time: formatTime(message.sentAt),
        dayLabel: newDay ? formatDayLabel(message.sentAt) : null,
        showHeader: newDay || !isGroupedWith(previous, message),
        /*
          ★ "Mening xabarim" SHU YERDA hisoblanadi: serverdagi DTO'da `mine`
          maydoni YO'Q, chunki obyekt xonadagi hammaga bitta nusxada ketadi
          va server uni har bir qabul qiluvchi uchun alohida bo'yay olmaydi.
        */
        isOwn: message.senderId === currentUserId.value,
        attachments: message.attachments ?? [],
      })
    }

    return rows
  })
}
