import { daysSinceLastMessage, waitingHours } from '@/entities/direct-message'
import type { ConversationDto } from '@/shared/types'

/**
 * "Savollar" ro'yxatining EKRAN mantiqi — eski `renderDmThreads()`.
 *
 * Kutish soatlari va "aloqa yo'q" kunlari `entities/direct-message` da
 * hisoblanadi (ularni Kuratorlik ekrani ham o'qiydi); bu yerda faqat
 * QIDIRUV, GURUH va CHIP filtri qoladi.
 */

export type InboxFilter = 'all' | 'waiting' | 'unread'

export interface InboxFilterOption {
  key: InboxFilter
  label: string
}

/** Chip'lar TARTIBI va NOMI eski ilovadan aynan. */
export const INBOX_FILTERS: readonly InboxFilterOption[] = [
  { key: 'all', label: 'Hammasi' },
  { key: 'waiting', label: 'Javob kutmoqda' },
  { key: 'unread', label: 'O‘qilmagan' },
]

/** Ro'yxatdagi bitta qator uchun oldindan hisoblangan ko'rinish. */
export interface InboxRow {
  conversation: ConversationDto
  /** `null` — javob kutilmayapti. */
  waitingHours: number | null
  /** `null` — ogohlantirish kerak emas (7 kundan kam yoki javob kutilmoqda). */
  staleDays: number | null
}

/** Eski ilovadagi "N kun aloqa yo'q" ogohlantirish chegarasi. */
const STALE_DAYS = 7

export function toRows(conversations: readonly ConversationDto[], now: Date): InboxRow[] {
  return conversations.map((conversation) => {
    const hours = waitingHours(conversation, now)
    const days = daysSinceLastMessage(conversation, now)
    return {
      conversation,
      waitingHours: hours,
      // "Aloqa yo'q" faqat kutish BO'LMAGANDA ma'noli: kutayotgan suhbatda
      // vaqt allaqachon kutish nishonida ko'rsatilgan.
      staleDays: hours === null && days !== null && days >= STALE_DAYS ? days : null,
    }
  })
}

/** Qidiruv + guruh + chip filtri (eski `renderDmThreads()`). */
export function filterRows(
  rows: readonly InboxRow[],
  options: { search: string; groupName: string; filter: InboxFilter },
): InboxRow[] {
  const needle = options.search.trim().toLowerCase()

  return rows.filter((row) => {
    const name = (row.conversation.peerName ?? '').toLowerCase()
    if (needle.length > 0 && !name.includes(needle)) return false
    if (options.groupName.length > 0 && row.conversation.groupName !== options.groupName) {
      return false
    }
    if (options.filter === 'waiting' && row.waitingHours === null) return false
    if (options.filter === 'unread' && row.conversation.unreadCount === 0) return false
    return true
  })
}

/** Filtrdagi `<select>` variantlari — takrorlanmagan guruh nomlari. */
export function groupOptions(conversations: readonly ConversationDto[]): string[] {
  const names = new Set<string>()
  for (const conversation of conversations) {
    if (conversation.groupName !== null && conversation.groupName.length > 0) {
      names.add(conversation.groupName)
    }
  }
  return [...names].sort((a, b) => a.localeCompare(b))
}
