import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import type { Ref } from 'vue'

import type { GroupChatMessageDto } from '@/shared/types'

/** Pastdan shu masofa ichida bo'lsa — "pastda turibdi" deb hisoblanadi. */
const BOTTOM_THRESHOLD_PX = 72

/** Yuqoriga shu masofagacha yaqinlashganda eskiroq sahifa so'raladi. */
const TOP_TRIGGER_PX = 96

export interface UseGroupChatScrollOptions {
  scroller: Ref<HTMLElement | null>
  messages: Ref<readonly GroupChatMessageDto[]>
  /** Eskiroq sahifani yuklaydi va qo'shilgan xabarlar sonini qaytaradi. */
  loadOlder: () => Promise<number>
  /** Yana eski xabar bormi (yo'q bo'lsa so'ralmaydi). */
  hasMore: Ref<boolean>
}

export interface UseGroupChatScrollResult {
  /** Foydalanuvchi eng pastda turibdimi. Qirqish shu holatga bog'liq. */
  isAtBottom: Ref<boolean>
  jumpToBottom: () => void
}

/**
 * Suhbat skrolli: pastga yopishtirish + yuqoriga chiqqanda tarixni yuklash.
 *
 * PERFORMANS: `scroll` hodisasi `passive` tinglovchi bilan ulanadi va
 * `requestAnimationFrame` orqali siqiladi — brauzer sekundiga yuzlab hodisa
 * beradi, biz esa kadrga bir marta o'lchaymiz (`scrollHeight` ni o'qish
 * layout'ni majburlaydi).
 */
export function useGroupChatScroll(
  options: UseGroupChatScrollOptions,
): UseGroupChatScrollResult {
  const isAtBottom = ref(true)

  let measureScheduled = false
  let loadingOlder = false

  function distanceFromBottom(element: HTMLElement): number {
    return element.scrollHeight - element.scrollTop - element.clientHeight
  }

  function scrollToBottom(): void {
    const element = options.scroller.value
    if (element === null) return
    // `behavior: 'auto'` — ketma-ket "smooth" skrollar bir-birini bo'g'ib,
    // kadrlarni yo'qotadi.
    element.scrollTop = element.scrollHeight
  }

  /**
   * Eskiroq sahifani yuklaydi va SKROLL JOYINI SAQLAYDI.
   *
   * ★ Bu ishning butun mohiyati shu: ro'yxat boshiga 50 ta xabar qo'shilsa,
   * `scrollHeight` keskin o'sadi va `scrollTop` o'zgarmagani uchun
   * foydalanuvchi bir zumda ancha yuqoriga "uchib" ketardi — o'qiyotgan
   * joyini yo'qotardi. Balandlik FARQINI `scrollTop` ga qo'shib, ko'z
   * oldidagi xabarni JOYIDA qoldiramiz.
   */
  async function loadOlderPreservingPosition(): Promise<void> {
    const element = options.scroller.value
    if (element === null || loadingOlder || !options.hasMore.value) return

    loadingOlder = true
    const heightBefore = element.scrollHeight
    const topBefore = element.scrollTop
    try {
      const added = await options.loadOlder()
      if (added === 0) return
      // DOM yangilangandan KEYIN o'lchaymiz.
      await nextTick()
      const current = options.scroller.value
      if (current === null) return
      current.scrollTop = topBefore + (current.scrollHeight - heightBefore)
    } finally {
      loadingOlder = false
    }
  }

  function measure(): void {
    measureScheduled = false
    const element = options.scroller.value
    if (element === null) return

    isAtBottom.value = distanceFromBottom(element) <= BOTTOM_THRESHOLD_PX

    if (element.scrollTop <= TOP_TRIGGER_PX && options.hasMore.value) {
      void loadOlderPreservingPosition()
    }
  }

  function handleScroll(): void {
    if (measureScheduled) return
    measureScheduled = true
    requestAnimationFrame(measure)
  }

  function jumpToBottom(): void {
    scrollToBottom()
    isAtBottom.value = true
  }

  /*
    Yangi xabar kelganda pastga tushamiz — LEKIN faqat foydalanuvchi
    allaqachon pastda bo'lsa. Aks holda u eski xabarlarni o'qiyotgan paytda
    ekran o'z-o'zidan sakrab ketardi.
  */
  watch(
    () => options.messages.value,
    () => {
      if (isAtBottom.value) scrollToBottom()
    },
    { flush: 'post' },
  )

  /*
    Skroll elementi ALMASHISHI mumkin (suhbat yopilib qayta ochilganda
    `v-if` uni qaytadan yaratadi). Shuning uchun `onMounted` emas, elementning
    O'ZI kuzatiladi — aks holda tinglovchi DOM'dan chiqarilgan eski elementda
    qolib ketardi (`features/chat/model/useChatScroll.ts` dagi bilan bir xil
    sabab).
  */
  watch(
    options.scroller,
    (element, previous) => {
      previous?.removeEventListener('scroll', handleScroll)
      if (element === null) return
      element.addEventListener('scroll', handleScroll, { passive: true })
      isAtBottom.value = true
      scrollToBottom()
    },
    { flush: 'post' },
  )

  onBeforeUnmount(() => {
    options.scroller.value?.removeEventListener('scroll', handleScroll)
  })

  return { isAtBottom, jumpToBottom }
}
