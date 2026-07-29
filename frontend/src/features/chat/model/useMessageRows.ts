import { computed } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { isGroupedWith, startsNewDay } from '@/entities/message'
import type { ChatMessage } from '@/entities/message'
import { formatDayLabel, formatTime } from '@/shared/lib/datetime'

export interface MessageRow {
  id: number
  senderId: number
  senderName: string
  body: string
  time: string
  /** Kun ajratgichi ko'rsatilsinmi. */
  dayLabel: string | null
  /** Avatar + ism ko'rsatilsinmi (ketma-ket xabarlar guruhlanadi). */
  showHeader: boolean
  isOwn: boolean
}

/**
 * Xabarlar ro'yxatini render uchun tayyor "qatorlar"ga aylantiradi.
 *
 * PERFORMANS: bu `computed`, `ref` emas — natija Vue tomonidan proksilanmaydi va
 * faqat `messages` havolasi o'zgarganda (ya'ni kadrga ko'pi bilan bir marta)
 * qayta hisoblanadi. 200 ta element ustidagi bitta o'tish — bir necha mikrosekund.
 */
export function useMessageRows(
  messages: Ref<readonly ChatMessage[]>,
  currentUserId: Ref<number | null>,
): ComputedRef<MessageRow[]> {
  return computed<MessageRow[]>(() => {
    const list = messages.value
    const rows: MessageRow[] = new Array<MessageRow>(list.length)

    for (let index = 0; index < list.length; index += 1) {
      const message = list[index]
      if (message === undefined) continue
      const previous = index > 0 ? list[index - 1] : undefined

      const newDay = startsNewDay(previous, message)
      rows[index] = {
        id: message.id,
        senderId: message.senderId,
        senderName: message.senderName,
        body: message.body,
        time: formatTime(message.sentAt),
        dayLabel: newDay ? formatDayLabel(message.sentAt) : null,
        showHeader: newDay || !isGroupedWith(previous, message),
        isOwn: message.senderId === currentUserId.value,
      }
    }

    return rows
  })
}
