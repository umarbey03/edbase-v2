import { onBeforeUnmount, ref, watch } from 'vue'
import type { Ref } from 'vue'

import type { ChatMessage } from '@/entities/message'

/** Pastdan shu masofa ichida bo'lsa — "pastda turibdi" deb hisoblanadi. */
const BOTTOM_THRESHOLD_PX = 72

export interface UseChatScrollResult {
  /** Foydalanuvchi ro'yxatning eng pastida turibdimi. */
  isPinnedToBottom: Ref<boolean>
  /** Pastda bo'lmaganida to'plangan o'qilmagan xabarlar soni. */
  unreadCount: Ref<number>
  /** "Yangi xabarlar" tugmasi bosilganda. */
  jumpToBottom: () => void
}

/**
 * Chat scroll'ini boshqaradi.
 *
 * PERFORMANS:
 *  - `scroll` hodisasi `passive` tinglovchi bilan ulanadi va `requestAnimationFrame`
 *    orqali siqiladi: brauzer sekundiga yuzlab `scroll` hodisasi beradi, biz esa
 *    kadrga bir marta o'lchaymiz (`scrollHeight` o'qish — layout majburlaydi).
 *  - Avtoskroll FAQAT foydalanuvchi pastda turganda. Aks holda o'qiyotgan joyi
 *    "sakrab" ketardi — buning o'rniga "yangi xabarlar" tugmasi ko'rsatiladi.
 *  - Skroll `behavior: 'auto'` — sekundiga bir necha marta "smooth" skroll
 *    animatsiyalari bir-birini bo'g'ib, kadrlarni yo'qotadi.
 */
export function useChatScroll(
  scroller: Ref<HTMLElement | null>,
  messages: Ref<readonly ChatMessage[]>,
): UseChatScrollResult {
  const isPinnedToBottom = ref(true)
  const unreadCount = ref(0)

  let lastSeenId = 0
  let measureScheduled = false

  function lastMessageId(): number {
    const list = messages.value
    return list.length > 0 ? (list[list.length - 1]?.id ?? 0) : 0
  }

  function distanceFromBottom(element: HTMLElement): number {
    return element.scrollHeight - element.scrollTop - element.clientHeight
  }

  function scrollToBottom(): void {
    const element = scroller.value
    if (element === null) return
    element.scrollTop = element.scrollHeight
  }

  function measure(): void {
    measureScheduled = false
    const element = scroller.value
    if (element === null) return

    const pinned = distanceFromBottom(element) <= BOTTOM_THRESHOLD_PX
    isPinnedToBottom.value = pinned
    if (pinned) {
      unreadCount.value = 0
      lastSeenId = lastMessageId()
    }
  }

  function handleScroll(): void {
    if (measureScheduled) return
    measureScheduled = true
    requestAnimationFrame(measure)
  }

  function jumpToBottom(): void {
    scrollToBottom()
    isPinnedToBottom.value = true
    unreadCount.value = 0
    lastSeenId = lastMessageId()
  }

  // `flush: 'post'` — yangi xabarlar DOM'ga qo'yilgandan KEYIN o'lchaymiz.
  watch(
    messages,
    (list) => {
      if (isPinnedToBottom.value) {
        scrollToBottom()
        lastSeenId = lastMessageId()
        unreadCount.value = 0
        return
      }
      // Faqat oxirgi ko'rilgan xabardan keyingilarini sanaymiz.
      let unread = 0
      for (let index = list.length - 1; index >= 0; index -= 1) {
        const message = list[index]
        if (message === undefined || message.id <= lastSeenId) break
        unread += 1
      }
      unreadCount.value = unread
    },
    { flush: 'post' },
  )

  /**
   * Skroll elementi almashishi mumkin (masalan "Ishtirokchilar" tabidan
   * "Suhbat" ga qaytilganda element qaytadan yaratiladi). Shu sababli
   * `onMounted` emas, elementning O'ZINI kuzatamiz — aks holda tinglovchi
   * eski, DOM'dan chiqarilgan elementda qolib ketardi.
   */
  watch(
    scroller,
    (element, previous) => {
      previous?.removeEventListener('scroll', handleScroll)
      if (element === null) return
      // `passive: true` — brauzer skrollni bloklanishini kutmaydi.
      element.addEventListener('scroll', handleScroll, { passive: true })
      lastSeenId = lastMessageId()
      scrollToBottom()
    },
    { flush: 'post' },
  )

  onBeforeUnmount(() => {
    scroller.value?.removeEventListener('scroll', handleScroll)
  })

  return { isPinnedToBottom, unreadCount, jumpToBottom }
}
