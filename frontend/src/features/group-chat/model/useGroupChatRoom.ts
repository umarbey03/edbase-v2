import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { ComputedRef, Ref, ShallowRef } from 'vue'

import {
  fetchGroupChatPage,
  GROUP_CHAT_RATE_LIMIT_MARKER,
  GROUP_CHAT_RATE_WINDOW_SECONDS,
  markGroupChatRead,
  sendGroupChatMessage,
} from '@/entities/group-chat'
import { MAX_RENDERED_MESSAGES } from '@/entities/message'
import { isApiError, toUserMessage } from '@/shared/api'
import type { GroupChatChannelName, GroupChatMessageDto, HubStatus } from '@/shared/types'

import { hubErrorText, useGroupChatHub } from './useGroupChatHub'

/**
 * ============================================================================
 *  SUHBAT EKRANINING MANTIG'I — TanStack Query + SignalR
 * ============================================================================
 *
 * ★ IKKI MANBANI QANDAY BIRLASHTIRAMIZ (vazifa shartidagi savolga javob):
 *
 *   TanStack Query = SURATLAR (snapshot).  SignalR = O'SISHLAR (increment).
 *
 *   • Query FAQAT birinchi sahifani (eng yangi ~50 xabar) oladi va u
 *     keshlanadi, qayta urinadi, bir nechta mount orasida bo'lishiladi.
 *     Suhbat qayta ochilganda ekran BO'SH ko'rinmaydi.
 *   • Hub esa faqat YANGI xabarlarni beradi.
 *   • Ikkisi `messages` degan BITTA mahalliy ro'yxatga quyiladi va u yerda
 *     `id` bo'yicha birlashtiriladi (`seenIds`).
 *
 *   NEGA hub xabari `queryClient.setQueryData` bilan kesh ichiga yozilmadi:
 *   kesh o'zi refetch qilganda (fokus qaytganda, qayta ulanishda) serverdan
 *   kelgan sahifa hub qo'shgan xabarlarni ustidan YOZIB YUBORARDI va ekranda
 *   xabar yo'qolib, keyin qaytib paydo bo'lardi. Mahalliy ro'yxat esa
 *   "birlashtirish" strategiyasini qo'llaydi: query sahifasi ham, hub xabari
 *   ham faqat QO'SHILADI, hech qachon almashtirmaydi.
 *
 * ★ DEDUPE — SHART, ixtiyoriy emas. Xabar yuborilganda u ikki yo'ldan keladi:
 *   `SendMessage` chaqiruvining JAVOBI va xonaga ketgan BROADCAST (yuboruvchi
 *   ham a'zo). Jonli hub bilan tekshirildi: ikkalasida ham `id` bir xil.
 *   `seenIds` bo'lmaganida yuboruvchi o'z xabarini IKKI MARTA ko'rardi.
 */

export interface UseGroupChatRoomOptions {
  groupId: Ref<number | null>
  /** `null` — serverning o'zi tanlasin (birinchi ruxsat etilgan kanal). */
  channel: Ref<GroupChatChannelName | null>
  /**
   * Eng eski xabarlarni DOM'dan chiqarish MUMKINMI.
   *
   * Chaqiruvchi (UI) buni "foydalanuvchi eng pastda turibdi" holatiga
   * bog'laydi: agar u yuqoriga chiqib eski xabarlarni O'QIYOTGAN bo'lsa,
   * ro'yxat boshidan element olib tashlash uning skroll joyini sakratib
   * yuborardi.
   */
  canTrim?: () => boolean
}

export interface UseGroupChatRoomResult {
  messages: ShallowRef<GroupChatMessageDto[]>
  status: Ref<HubStatus>
  isPending: ComputedRef<boolean>
  loadError: ComputedRef<string | null>
  /** Yuborish/ruxsat xatolari (429 ham shu yerda). */
  notice: Ref<string | null>
  groupName: ComputedRef<string>
  /** Kanal tab'lari uchun — FAQAT serverdan (`availableChannels`). */
  availableChannels: ComputedRef<GroupChatChannelName[]>
  /** Server AYNAN qaysi kanalni ko'rsatyapti. */
  activeChannel: ComputedRef<GroupChatChannelName | null>
  hasMore: Ref<boolean>
  isLoadingOlder: Ref<boolean>
  isSending: Ref<boolean>
  /** 429 dan keyin qolgan kutish vaqti (sekund). 0 — bloklanmagan. */
  cooldownSeconds: Ref<number>
  canSend: ComputedRef<boolean>
  loadOlder: () => Promise<number>
  send: (body: string) => Promise<boolean>
  markRead: () => void
  retry: () => void
  dismissNotice: () => void
}

export function useGroupChatRoom(options: UseGroupChatRoomOptions): UseGroupChatRoomResult {
  const queryClient = useQueryClient()

  const messages = shallowRef<GroupChatMessageDto[]>([])
  const notice = ref<string | null>(null)
  const hasMore = ref(false)
  const isLoadingOlder = ref(false)
  const isSending = ref(false)
  const cooldownSeconds = ref(0)

  /** Birlashtirish kaliti — reaktiv EMAS (Vue'ga ko'rsatilmaydi). */
  let seenIds = new Set<number>()
  /** Keyingi "eskiroq" sahifa uchun kursor. */
  let nextBeforeId: number | null = null
  let cooldownTimer: number | null = null
  /** Serverga oxirgi marta shu `id` gacha "o'qildi" deb aytilgan. */
  let lastMarkedId: number | null = null

  const canTrim = options.canTrim ?? ((): boolean => true)

  /* ------------------------------ birinchi sahifa --------------------------- */

  const queryKey = computed(() => [
    'group-chat',
    'page',
    options.groupId.value,
    options.channel.value,
  ])

  const pageQuery = useQuery({
    queryKey,
    queryFn: ({ signal }) => {
      const groupId = options.groupId.value
      // `enabled` buni ta'minlaydi; tekshiruv faqat turlarni qanoatlantiradi.
      if (groupId === null) throw new Error('Guruh tanlanmagan.')
      const channel = options.channel.value
      return fetchGroupChatPage(groupId, channel === null ? {} : { channel }, { signal })
    },
    enabled: computed(() => options.groupId.value !== null),
    /*
      Suhbat OCHIQ turganda yangi xabarlar HUB orqali keladi, shuning uchun
      bu yerda `refetchInterval` YO'Q — u faqat serverga keraksiz yuk
      bo'lardi. Fokus qaytganda bir marta tekshirish esa foydali: kompyuter
      uyquda bo'lganda hub uzilib, bir necha xabar o'tkazib yuborilgan
      bo'lishi mumkin.
    */
    refetchOnWindowFocus: true,
  })

  const groupName = computed(() => pageQuery.data.value?.groupName ?? '')
  const availableChannels = computed(() => pageQuery.data.value?.availableChannels ?? [])
  const activeChannel = computed(() => pageQuery.data.value?.channel ?? null)

  const isPending = computed(() => pageQuery.isPending.value && options.groupId.value !== null)

  const loadError = computed(() =>
    pageQuery.error.value !== null ? toUserMessage(pageQuery.error.value) : null,
  )

  /* ------------------------------ birlashtirish ----------------------------- */

  /** Ro'yxat OXIRIGA qo'shadi (yangi xabarlar). Takrorlar tashlanadi. */
  function appendMessages(incoming: readonly GroupChatMessageDto[]): number {
    const fresh: GroupChatMessageDto[] = []
    for (const message of incoming) {
      if (seenIds.has(message.id)) continue
      seenIds.add(message.id)
      fresh.push(message)
    }
    if (fresh.length === 0) return 0

    const next = messages.value.concat(fresh)
    // `id` bo'yicha o'sish tartibi: hub xabari REST sahifasidan oldin kelib
    // qolishi mumkin (ular alohida kanallar orqali yuradi).
    next.sort((a, b) => a.id - b.id)

    /*
      DOM cheklovi — `entities/message` dagi `MAX_RENDERED_MESSAGES` (200)
      QAYTA ISHLATILADI. O'sha yerdagi sabab bu yerda ham amal qiladi: har
      xabar ~5 ta DOM tuguni, minglab xabar skrollni qotiradi. Tarix serverda
      qoladi va yuqoriga skroll qilinganda qaytadan olinadi.

      ★ FAQAT foydalanuvchi eng pastda turganda qirqamiz — aks holda u
      o'qiyotgan eski xabarlar oyoq ostidan olinib, sahifa sakrab ketardi.
    */
    messages.value =
      next.length > MAX_RENDERED_MESSAGES && canTrim()
        ? next.slice(next.length - MAX_RENDERED_MESSAGES)
        : next

    return fresh.length
  }

  /** Ro'yxat BOSHIGA qo'shadi (yuqoriga skroll — eskiroq sahifa). */
  function prependMessages(incoming: readonly GroupChatMessageDto[]): number {
    const fresh: GroupChatMessageDto[] = []
    for (const message of incoming) {
      if (seenIds.has(message.id)) continue
      seenIds.add(message.id)
      fresh.push(message)
    }
    if (fresh.length === 0) return 0

    // Bu yerda QIRQMAYMIZ: foydalanuvchi eski xabarlarni ATAYLAB so'radi.
    const next = fresh.concat(messages.value)
    next.sort((a, b) => a.id - b.id)
    messages.value = next
    return fresh.length
  }

  function resetThread(): void {
    messages.value = []
    seenIds = new Set<number>()
    nextBeforeId = null
    lastMarkedId = null
    hasMore.value = false
    notice.value = null
  }

  /*
    Query sahifasi kelganda uni mahalliy ro'yxatga QUYAMIZ (almashtirmaymiz).
    `items` — ESKIDAN YANGIGA, ya'ni qo'shimcha saralash shart emas, lekin
    `appendMessages` baribir `id` bo'yicha tartiblaydi (hub xabari oldin
    kelgan bo'lishi mumkin).
  */
  watch(
    () => pageQuery.data.value,
    (page) => {
      if (page === undefined) return
      appendMessages(page.items ?? [])
      /*
        Kursor FAQAT birinchi marta o'rnatiladi: `loadOlder` uni o'zi
        suradi va query refetch bo'lganda uni orqaga tashlash "eski
        xabarlarni qayta yuklash" tsikliga olib kelardi.
      */
      if (nextBeforeId === null) {
        nextBeforeId = page.nextBeforeId
        hasMore.value = page.hasMore
      }
    },
    { immediate: true },
  )

  /* --------------------------------- hub ----------------------------------- */

  const hub = useGroupChatHub({
    onMessage: (message) => {
      const added = appendMessages([message])
      if (added === 0) return
      /*
        Yangi xabar kelganda "Chatlar" ro'yxatidagi oxirgi xabar va
        o'qilmaganlar soni eskiradi — ro'yxat so'rovini bekor qilamiz.
        Ro'yxat ochiq bo'lmasa TanStack uni qayta so'ramaydi, ya'ni bu
        arzon amal.
      */
      void queryClient.invalidateQueries({ queryKey: ['group-chat', 'threads'] })
    },
  })

  /*
    Guruh yoki kanal almashganda: mahalliy ro'yxatni tozalab, hub'da ham
    xonani almashtiramiz. `immediate` — birinchi ochilishda ham ishlaydi.
  */
  watch(
    () => [options.groupId.value, options.channel.value] as const,
    ([groupId, channel]) => {
      resetThread()
      if (groupId === null) {
        void hub.leave()
        return
      }
      void hub.join(groupId, channel)
    },
    { immediate: true },
  )

  /* ------------------------------ eskiroq sahifa ---------------------------- */

  /** Yuklangan xabarlar sonini qaytaradi (0 — yangisi yo'q). */
  async function loadOlder(): Promise<number> {
    const groupId = options.groupId.value
    const beforeId = nextBeforeId
    if (groupId === null || beforeId === null || isLoadingOlder.value || !hasMore.value) return 0

    isLoadingOlder.value = true
    try {
      const channel = activeChannel.value ?? options.channel.value
      const page = await fetchGroupChatPage(groupId, {
        beforeId,
        ...(channel === null ? {} : { channel }),
      })
      const added = prependMessages(page.items ?? [])
      nextBeforeId = page.nextBeforeId
      hasMore.value = page.hasMore
      return added
    } catch (error) {
      notice.value = toUserMessage(error)
      return 0
    } finally {
      isLoadingOlder.value = false
    }
  }

  /* ------------------------------- o'qildi --------------------------------- */

  /**
   * Suhbat ko'rilganini serverga aytadi.
   *
   * ★ ALOHIDA CHAQIRUV KERAK: `GET .../messages` holatni O'ZGARTIRMAYDI va
   * xabarlarni o'qilgan deb belgilamaydi (backend shartnomasi). Ya'ni bu
   * so'rovsiz o'qilmaganlar sanog'i hech qachon nolga tushmasdi.
   *
   * Takroriy chaqiruv zararsiz — server `changed: false` qaytaradi.
   */
  function markRead(): void {
    const groupId = options.groupId.value
    if (groupId === null) return
    const channel = activeChannel.value ?? options.channel.value ?? undefined
    const lastId = messages.value[messages.value.length - 1]?.id
    if (lastId === undefined) return

    /*
      Shu xabargacha allaqachon belgilangan bo'lsa — so'rov YUBORILMAYDI.
      Busiz har kelgan xabar uchun bitta POST ketardi: 30 ta xabar kelgan
      jonli suhbatda 30 ta keraksiz so'rov (server ularni baribir
      `changed: false` bilan qaytarardi).
    */
    if (lastMarkedId === lastId) return
    lastMarkedId = lastId

    void markGroupChatRead(groupId, channel, lastId)
      .then((result) => {
        // Sanoq HAQIQATAN o'zgargandagina ro'yxatni yangilaymiz — aks holda
        // har ochilishda keraksiz so'rov ketardi.
        if (result.changed) {
          void queryClient.invalidateQueries({ queryKey: ['group-chat', 'threads'] })
        }
      })
      .catch(() => {
        // "O'qildi" belgilanmasa suhbatning o'zi ishlashda davom etadi —
        // foydalanuvchiga xato ko'rsatish faqat chalg'itardi.
      })
  }

  /* -------------------------------- yuborish -------------------------------- */

  function clearCooldownTimer(): void {
    if (cooldownTimer === null) return
    window.clearInterval(cooldownTimer)
    cooldownTimer = null
  }

  /**
   * 429 dan keyin tugmani `Retry-After` sekundga bloklaydi.
   *
   * ★ OLDINDAN bloklash YO'Q: server budjeti `(guruh, kanal, foydalanuvchi)`
   * bo'yicha va REST bilan hub uni BO'LISHADI — klient uni aniq hisoblay
   * olmaydi. Shuning uchun faqat serverning haqiqiy javobiga ishonamiz.
   */
  function startCooldown(seconds: number): void {
    cooldownSeconds.value = Math.max(1, seconds)
    clearCooldownTimer()
    cooldownTimer = window.setInterval(() => {
      cooldownSeconds.value -= 1
      if (cooldownSeconds.value <= 0) {
        cooldownSeconds.value = 0
        clearCooldownTimer()
      }
    }, 1000)
  }

  const canSend = computed(() => !isSending.value && cooldownSeconds.value === 0)

  async function send(body: string): Promise<boolean> {
    const groupId = options.groupId.value
    if (groupId === null || body.length === 0 || !canSend.value) return false

    isSending.value = true
    try {
      /*
        AVVAL HUB: xabar bitta uzatishda ketadi va javob darhol qaytadi.
        Hub ulanmagan bo'lsa (`null`) REST'ga tushamiz — chat "aloqa yo'q"
        deb butunlay to'xtab qolmasin.
      */
      let sent = await hub.sendMessage(body)
      if (sent === null) {
        const channel = activeChannel.value ?? options.channel.value ?? undefined
        sent = await sendGroupChatMessage(groupId, body, channel)
      }
      // Javobni ham ro'yxatga qo'shamiz. Broadcast allaqachon kelgan bo'lsa
      // `seenIds` uni tashlab yuboradi — takror ko'rinmaydi.
      if (sent !== null) appendMessages([sent])
      notice.value = null
      void queryClient.invalidateQueries({ queryKey: ['group-chat', 'threads'] })
      return true
    } catch (error) {
      /*
        IKKI XIL XATO MANBASI, IKKI XIL O'QISH USULI:

         • REST (`ApiError`) — status kodi 429 va `Retry-After` sarlavhasi
           bor, ya'ni ANIQ sekundni serverdan olamiz;
         • HUB (`HubException`) — status kodi ham, sarlavha ham YO'Q, faqat
           o'zbekcha matn. Shu sababli chegara matn bo'yicha aniqlanadi va
           kutish vaqti server oynasiga (10 s) tenglashtiriladi.
      */
      if (isApiError(error)) {
        notice.value = error.userMessage
        if (error.status === 429) {
          startCooldown(error.retryAfterSeconds ?? GROUP_CHAT_RATE_WINDOW_SECONDS)
        }
      } else {
        const text = hubErrorText(error)
        notice.value = text
        if (text.includes(GROUP_CHAT_RATE_LIMIT_MARKER)) {
          startCooldown(GROUP_CHAT_RATE_WINDOW_SECONDS)
        }
      }
      return false
    } finally {
      isSending.value = false
    }
  }

  function retry(): void {
    void pageQuery.refetch()
    const groupId = options.groupId.value
    if (groupId !== null) void hub.join(groupId, options.channel.value)
  }

  function dismissNotice(): void {
    notice.value = null
  }

  /*
    Sanoq taymeri komponent bilan birga o'chadi. `useGroupChatHub` o'z
    ulanishini o'zi yopadi (unda alohida `onBeforeUnmount` bor), bu yerda
    esa faqat SHU modul ochgan resurs tozalanadi.
  */
  onBeforeUnmount(clearCooldownTimer)

  return {
    messages,
    status: hub.status,
    isPending,
    loadError,
    notice,
    groupName,
    availableChannels,
    activeChannel,
    hasMore,
    isLoadingOlder,
    isSending,
    cooldownSeconds,
    canSend,
    loadOlder,
    send,
    markRead,
    retry,
    dismissNotice,
  }
}
