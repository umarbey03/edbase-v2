import { onBeforeUnmount, ref, watch } from 'vue'
import type { Ref } from 'vue'

import { messageKey } from '@/entities/message'
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

  /**
   * Foydalanuvchi oxirgi ko'rgan xabarning KALITI.
   *
   * ★ ILGARI BU `id` EDI VA BUZUQ ISHLARDI: real vaqtda kelgan xabarda `id`
   * doim 0 bo'ladi, tarixdagi xabarlarda esa haqiqiy (masalan 4460). Ya'ni
   * `message.id <= lastSeenId` sharti YANGI xabar uchun ham rost chiqardi va
   * "N ta yangi xabar" tugmasi hech qachon ko'rinmasdi — tepada o'qiyotgan
   * odam yangi xabar kelganini bilmasdi. Batafsil: `entities/message`.
   */
  let lastSeenKey = ''
  let measureScheduled = false

  function lastMessageKey(): string {
    const list = messages.value
    const last = list.length > 0 ? list[list.length - 1] : undefined
    return last === undefined ? '' : messageKey(last)
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
      lastSeenKey = lastMessageKey()
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
    lastSeenKey = lastMessageKey()
  }

  // `flush: 'post'` — yangi xabarlar DOM'ga qo'yilgandan KEYIN o'lchaymiz.
  watch(
    messages,
    (list) => {
      if (isPinnedToBottom.value) {
        scrollToBottom()
        lastSeenKey = lastMessageKey()
        unreadCount.value = 0
        return
      }
      // Oxirgi ko'rilgan xabardan KEYINGILARINI sanaymiz. Ro'yxat faqat
      // oxiriga o'sadi va boshidan qirqiladi, shuning uchun indeks bo'yicha
      // qidirish yetarli. Kalit topilmasa (eski xabar qirqilib ketgan) —
      // hammasi o'qilmagan hisoblanadi.
      let seenIndex = -1
      for (let index = list.length - 1; index >= 0; index -= 1) {
        const message = list[index]
        if (message !== undefined && messageKey(message) === lastSeenKey) {
          seenIndex = index
          break
        }
      }
      unreadCount.value = list.length - seenIndex - 1
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
      lastSeenKey = lastMessageKey()
      scrollToBottom()
    },
    { flush: 'post' },
  )

  onBeforeUnmount(() => {
    scroller.value?.removeEventListener('scroll', handleScroll)
  })

  return { isPinnedToBottom, unreadCount, jumpToBottom }
}
