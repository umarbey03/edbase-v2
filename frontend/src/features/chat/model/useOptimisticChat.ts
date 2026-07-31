import { computed, onBeforeUnmount, shallowRef, watch } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { messageKey, newClientId, normalizeBody } from '@/entities/message'
import type { ChatMessage } from '@/entities/message'

/**
 * ============================================================================
 *  OPTIMISTIK KO'RSATISH — xabar EKRANGA DARHOL chiqadi
 * ============================================================================
 *
 * ── MUAMMO ─────────────────────────────────────────────────────────────────
 *
 * Ilgari xabar ekranda faqat SERVER BROADCAST'i qaytgach paydo bo'lardi:
 *
 *     Enter -> invoke -> server -> broadcast -> klient -> render
 *
 * Ya'ni har xabar uchun to'liq tarmoq aylanishi kutilardi. Lokal stack'da bu
 * 5-7 ms va sezilmaydi, lekin mobil internetda (RTT 150-400 ms) foydalanuvchi
 * Enter bosgach matn maydonda qotib turardi — aynan "chat sekin" hissiyoti.
 *
 * ── YECHIM ─────────────────────────────────────────────────────────────────
 *
 * Xabar YUBORISHDAN OLDIN ekranga qo'yiladi. Server javobi kutilmaydi.
 *
 * ── ★ TAKRORLANISHNING OLDINI OLISH (eng nozik joy) ────────────────────────
 *
 * Yuboruvchi O'Z xabarining broadcast'ini ham oladi (guruh chatida bu jonli
 * tasdiqlangan, jonli dars hub'ida ham `Clients.Group` — ya'ni yuboruvchi
 * ham xonada). Dedupe qilinmasa xabar EKRANDA IKKI MARTA ko'rinardi.
 *
 * Shuning uchun kalitni KLIENT yasaydi (`newClientId`) va serverga BERADI;
 * server uni broadcast'da o'zgarishsiz qaytaradi. Optimistik nusxa va
 * broadcast nusxasi AYNI kalitga ega bo'ladi, ya'ni:
 *
 *   • kalit `messages` da paydo bo'ldi -> optimistik nusxa olib tashlanadi;
 *   • poyga yo'q: broadcast invoke javobidan OLDIN kelsa ham kalit bir xil.
 *
 * Bu yerda "vaqt + matn bo'yicha taxmin qilish" ATAYLAB ishlatilmaydi — bir
 * xil matnli ikki xabar (masalan "ha") bir-birini yeb qo'yardi.
 *
 * ── XATO BO'LSA ────────────────────────────────────────────────────────────
 *
 * Yuborish rad etilsa (tezlik chegarasi, aloqa yo'q) optimistik nusxa
 * DARHOL olib tashlanadi va `false` qaytadi — kompozitor matnni qaytarib
 * qo'yadi. Ekranda "yuborildi" ko'rinib, aslida ketmagan xabar qolmaydi.
 */

/** Optimistik nusxa qancha vaqt kutadi — undan keyin "yetmadi" deb tashlanadi. */
const CONFIRM_TIMEOUT_MS = 15_000

export interface UseOptimisticChatResult {
  /** Server xabarlari + hali tasdiqlanmagan o'z xabarlarimiz. */
  merged: ComputedRef<readonly ChatMessage[]>
  /** Tasdiqlanmagan xabarlar kalitlari (qatorda "yuborilmoqda" belgisi uchun). */
  pendingKeys: ComputedRef<ReadonlySet<string>>
  /** Kompozitor chaqiradi. `false` — xabar ketmadi, matn qaytarilsin. */
  submit: (body: string) => Promise<boolean>
}

export function useOptimisticChat(
  messages: Ref<readonly ChatMessage[]>,
  currentUserId: Ref<number | null>,
  /** O'z ismimiz (ishtirokchilar ro'yxatidan) — ekranda ko'rinmasa ham DTO to'liq bo'lsin. */
  currentUserName: Ref<string>,
  send: (body: string, clientId: string) => Promise<boolean>,
): UseOptimisticChatResult {
  /**
   * `shallowRef` — `useLiveHub` dagi bilan AYNI sabab: xabarlar o'zgarmas
   * qiymatlar, ularni chuqur proksilash bekorga ishlash.
   */
  const pending = shallowRef<ChatMessage[]>([])

  /** Tirik "yetmadi" taymerlari — komponent yopilganda tozalash uchun. */
  const timers = new Set<number>()

  function drop(key: string): void {
    const next = pending.value.filter((message) => messageKey(message) !== key)
    if (next.length !== pending.value.length) pending.value = next
  }

  /** Server tasdiqlagan kalitlar (broadcast yoki tarixdan kelgan). */
  const confirmedKeys = computed<ReadonlySet<string>>(() => {
    const keys = new Set<string>()
    for (const message of messages.value) keys.add(messageKey(message))
    return keys
  })

  /**
   * Hali tasdiqlanmagan optimistik nusxalar.
   *
   * ★ BU `computed`, `watch` EMAS — VA BU MUHIM. Avvalgi urinishda holat
   * `watch(messages, ...)` ichida yangilanardi va ish vaqtidagi tekshiruvda
   * "yuborilmoqda" belgisi TOZALANMAY qoldi: watch kechikib (yoki umuman)
   * ishlamaganda xabar ekranda abadiy xira bo'lib turardi. Endi to'g'rilik
   * hech qanday rejalashtirish tartibiga bog'liq emas: qiymat manbadan
   * bevosita hisoblanadi.
   */
  const unconfirmed = computed<readonly ChatMessage[]>(() =>
    pending.value.filter((message) => !confirmedKeys.value.has(messageKey(message))),
  )

  const pendingKeys = computed<ReadonlySet<string>>(
    () => new Set(unconfirmed.value.map(messageKey)),
  )

  /** Ko'rsatiladigan ro'yxat: optimistik nusxalar OXIRIGA — ular eng yangilari. */
  const merged = computed<readonly ChatMessage[]>(() =>
    unconfirmed.value.length === 0
      ? messages.value
      : [...messages.value, ...unconfirmed.value],
  )

  /**
   * Tasdiqlanganlarni ro'yxatdan chiqarib tashlaymiz — bu FAQAT xotira
   * tozalash, to'g'rilik yuqoridagi `computed` larda ta'minlangan.
   */
  watch(unconfirmed, (list) => {
    if (list.length !== pending.value.length) pending.value = [...list]
  })

  async function submit(rawBody: string): Promise<boolean> {
    const body = normalizeBody(rawBody)
    if (body.length === 0) return false

    const userId = currentUserId.value
    if (userId === null) {
      // Kim yozayotgani noma'lum bo'lsa optimistik nusxa "o'zimniki" deb
      // belgilanmaydi — bu holda oddiy yo'l bilan yuboramiz.
      return send(body, newClientId())
    }

    const clientId = newClientId()
    const optimistic: ChatMessage = {
      id: 0,
      senderId: userId,
      senderName: currentUserName.value,
      body,
      sentAt: new Date().toISOString(),
      clientId,
    }
    const key = messageKey(optimistic)

    pending.value = [...pending.value, optimistic]

    // Broadcast kelmay qolsa (uzilish, server tarqatishni o'tkazib yuborsa)
    // "yuborilmoqda" belgisi abadiy osilib qolmasin.
    const timer = window.setTimeout(() => {
      timers.delete(timer)
      drop(key)
    }, CONFIRM_TIMEOUT_MS)
    timers.add(timer)

    let ok = false
    try {
      ok = await send(body, clientId)
    } finally {
      if (!ok) {
        window.clearTimeout(timer)
        timers.delete(timer)
        drop(key)
      }
    }

    return ok
  }

  // Tozalash MAJBURIY: yuborish davomida dars xonasidan chiqilsa taymer
  // yopilgan komponentning holatiga tegib turardi.
  onBeforeUnmount(() => {
    for (const timer of timers) window.clearTimeout(timer)
    timers.clear()
  })

  return { merged, pendingKeys, submit }
}
