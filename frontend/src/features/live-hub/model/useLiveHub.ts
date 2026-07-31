import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { computed, onBeforeUnmount, ref, shallowRef } from 'vue'
import type { ComputedRef, Ref, ShallowRef } from 'vue'

import {
  CHAT_RATE_MAX_MESSAGES,
  CHAT_RATE_WINDOW_MS,
  MAX_RENDERED_MESSAGES,
  messageKey,
  newClientId,
  normalizeBody,
} from '@/entities/message'
import type { ChatMessage } from '@/entities/message'
import { roleWeight } from '@/entities/user'
import { getAccessToken, hubErrorText, toUserMessage } from '@/shared/api'
import { env } from '@/shared/config/env'
import { HubEvent, HubMethod } from '@/shared/types'
import type { HubStatus, PresenceEntry, UserRoleName } from '@/shared/types'

/**
 * ============================================================================
 *  JONLI DARS HUB'I — 200 FOYDALANUVCHI UCHUN OPTIMALLASHTIRILGAN
 * ============================================================================
 *
 * Asosiy muammo: 200 kishilik darsda sekundiga o'nlab `ChatMessage` va
 * `PresenceChanged` hodisasi keladi. Har bir hodisa uchun Vue'ning reaktivligini
 * ishga tushirish = sekundiga o'nlab to'liq qayta render = interfeys qotib qoladi.
 *
 * Shu sababli quyidagi 5 ta usul qo'llanilgan (har biri kod ichida izohlangan):
 *   1) `shallowRef` — xabarlar massivi chuqur proksilanmaydi.
 *   2) `requestAnimationFrame` bilan paketlash — kadrga BITTA yangilanish.
 *   3) DOM cheklovi — ko'pi bilan 200 ta xabar tirik qoladi.
 *   4) Presence oddiy `Map` da (reaktiv emas) + sekundiga ~2 marta render.
 *   5) Rollar alohida keshda — har xabar uchun qidiruv O(1).
 */

/** Presence ro'yxatini qayta chizish oralig'i (ms). */
const PRESENCE_RENDER_INTERVAL_MS = 400

/** `withAutomaticReconnect` uchun kechikishlar. */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000, 20_000]

/** Birinchi ulanish muvaffaqiyatsiz bo'lsa — o'z backoff'imiz. */
const INITIAL_RETRY_DELAYS_MS = [1_000, 3_000, 6_000, 12_000, 20_000]

/** Server bilan aloqa uzilgan deb hisoblash chegarasi (default 30s — 200 ta klientda kam). */
const SERVER_TIMEOUT_MS = 60_000
const KEEP_ALIVE_MS = 15_000

export interface UseLiveHubOptions {
  sessionId: number
  /** `SessionEnded` kelganda chaqiriladi (video ulanishini ham yopish uchun). */
  onSessionEnded?: () => void
}

export interface UseLiveHubResult {
  status: Ref<HubStatus>
  /** DOM'da turgan xabarlar (ko'pi bilan `MAX_RENDERED_MESSAGES` ta). */
  messages: ShallowRef<ChatMessage[]>
  participants: ShallowRef<PresenceEntry[]>
  participantCount: Ref<number>
  raisedHands: ShallowRef<PresenceEntry[]>
  /** Xabar rozetkalari uchun: userId -> rol (chiqib ketganlar ham qoladi). */
  roleByUserId: ShallowRef<ReadonlyMap<number, UserRoleName>>
  sessionEnded: Ref<boolean>
  lastError: Ref<string | null>
  notice: Ref<string | null>
  cooldownRemainingMs: Ref<number>
  canSend: ComputedRef<boolean>
  isSending: Ref<boolean>
  handRaised: Ref<boolean>
  start: () => Promise<void>
  retry: () => Promise<void>
  /** `clientId` — optimistik ko'rsatish uchun barqaror kalit (ixtiyoriy). */
  sendMessage: (body: string, clientId?: string) => Promise<boolean>
  raiseHand: (raised: boolean) => Promise<void>
  seedMessages: (history: readonly ChatMessage[]) => void
  dismissNotice: () => void
}

/* --------------------------- payload validatorlari -------------------------- */

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function asRole(value: unknown): UserRoleName {
  return value === 'Teacher' ||
    value === 'Assistant' ||
    value === 'Academic' ||
    value === 'Admin' ||
    value === 'Student'
    ? value
    : 'Student'
}

function toChatMessage(payload: unknown): ChatMessage | null {
  if (!isRecord(payload)) return null
  const { id, senderId, senderName, body, sentAt, clientId } = payload
  if (typeof id !== 'number' || typeof senderId !== 'number') return null
  if (typeof body !== 'string') return null
  return {
    id,
    senderId,
    senderName: typeof senderName === 'string' ? senderName : '—',
    body,
    sentAt: typeof sentAt === 'string' ? sentAt : new Date().toISOString(),
    // Real vaqtdagi xabarning YAGONA barqaror kaliti — `id` broadcast'da 0.
    clientId: typeof clientId === 'string' ? clientId : null,
  }
}

function toPresenceEntry(payload: unknown): PresenceEntry | null {
  if (!isRecord(payload)) return null
  const { userId, displayName, role, handRaised, joinedAt } = payload
  if (typeof userId !== 'number') return null
  return {
    userId,
    displayName: typeof displayName === 'string' ? displayName : '—',
    role: asRole(role),
    handRaised: handRaised === true,
    joinedAt: typeof joinedAt === 'string' ? joinedAt : new Date().toISOString(),
  }
}

/* ================================ composable =============================== */

export function useLiveHub(options: UseLiveHubOptions): UseLiveHubResult {
  const { sessionId } = options

  const status = ref<HubStatus>('idle')
  const sessionEnded = ref(false)
  const lastError = ref<string | null>(null)
  const notice = ref<string | null>(null)
  const isSending = ref(false)
  const handRaised = ref(false)
  const participantCount = ref(0)

  /**
   * (1) `shallowRef` — `ref` EMAS.
   * `ref([])` bo'lganida Vue massivdagi HAR BIR xabar obyektini Proxy'ga o'raydi.
   * 200 ta xabar × 5 ta maydon = 1000 ta reaktiv bog'lanish, ular hech qachon
   * o'zgarmaydi (xabar — o'zgarmas qiymat). `shallowRef` da faqat `.value` ning
   * O'ZI kuzatiladi: yangi massiv tayinlansa bitta yangilanish bo'ladi.
   */
  const messages = shallowRef<ChatMessage[]>([])
  const participants = shallowRef<PresenceEntry[]>([])
  const raisedHands = shallowRef<PresenceEntry[]>([])
  const roleByUserId = shallowRef<ReadonlyMap<number, UserRoleName>>(new Map())

  /* ------------------------- reaktiv BO'LMAGAN holat ------------------------ */

  let connection: HubConnection | null = null
  let disposed = false
  let startAttempt = 0
  let retryTimer: number | null = null

  /**
   * (2) Kiruvchi xabarlar buferi — ODDIY massiv, reaktiv emas.
   * Hodisa kelganda shu yerga qo'shiladi va `requestAnimationFrame` rejalashtiriladi.
   * 30 ta xabar bitta kadrda kelsa ham render BIR MARTA bo'ladi.
   */
  let pendingMessages: ChatMessage[] = []
  let flushHandle: number | null = null

  /**
   * Takroriy xabarlarni (tarix + realtime ustma-ust tushishi) filtrlash.
   *
   * ★ KALIT `id` EMAS (TUZATILGAN NOSOZLIK): broadcast'da `id` DOIM 0 keladi
   * (baza raqamini fon xizmati beradi). `id` bo'yicha filtrlanganda birinchi
   * xabardan keyingi HAMMASI "ko'rilgan" deb tashlanardi va chat qotib
   * qolardi. Batafsil: `entities/message` -> `messageKey`.
   */
  let seenKeys = new Set<string>()

  /**
   * (4) Presence — reaktiv BO'LMAGAN `Map`.
   * Har `PresenceChanged` hodisasida Vue'ga tegmaymiz; faqat `Map` yangilanadi va
   * ro'yxatni qayta chizish rejalashtiriladi (sekundiga ~2 marta). 200 kishi bir
   * daqiqada kirsa — 200 ta hodisa, lekin ~10 ta render.
   */
  const presence = new Map<number, PresenceEntry>()

  /**
   * (5) Rollar keshi. Chatdagi rozetkalar uchun kerak, lekin foydalanuvchi chiqib
   * ketganda ham eski xabarlarida rozetka qolishi kerak — shuning uchun bu `Map`
   * dan hech narsa o'chirilmaydi (bir necha yuzta yozuv — arzimas xotira).
   */
  const knownRoles = new Map<number, UserRoleName>()

  let presenceRenderTimer: number | null = null
  let lastPresenceRenderAt = 0

  /** Oynadagi yuborish vaqtlari — server qoidasining klientdagi nusxasi. */
  let sentAtTimes: number[] = []
  const cooldownRemainingMs = ref(0)
  let cooldownTimer: number | null = null

  /* ------------------------------- xabarlar -------------------------------- */

  function scheduleMessageFlush(): void {
    if (flushHandle !== null) return
    flushHandle = requestAnimationFrame(flushMessages)
  }

  function flushMessages(): void {
    flushHandle = null
    if (pendingMessages.length === 0) return

    const incoming = pendingMessages
    pendingMessages = []

    const next = messages.value.concat(incoming)

    // (3) DOM cheklovi: eng eskilarini tashlaymiz. Tarix serverda qoladi.
    messages.value =
      next.length > MAX_RENDERED_MESSAGES ? next.slice(next.length - MAX_RENDERED_MESSAGES) : next

    pruneSeenKeys()
  }

  function pruneSeenKeys(): void {
    // `seenKeys` cheksiz o'smasligi kerak — vaqti-vaqti bilan tirik xabarlar
    // bo'yicha qayta quramiz.
    if (seenKeys.size <= MAX_RENDERED_MESSAGES * 4) return
    const fresh = new Set<string>()
    for (const message of messages.value) fresh.add(messageKey(message))
    for (const message of pendingMessages) fresh.add(messageKey(message))
    seenKeys = fresh
  }

  function pushMessage(message: ChatMessage): void {
    const key = messageKey(message)
    if (seenKeys.has(key)) return
    seenKeys.add(key)
    pendingMessages.push(message)

    // Tab fonda bo'lsa `requestAnimationFrame` ishlamaydi va bufer o'sib ketishi
    // mumkin — baribir 200 tadan ortig'i ko'rsatilmaydi, ortiqchasini tashlaymiz.
    if (pendingMessages.length > MAX_RENDERED_MESSAGES * 2) {
      pendingMessages = pendingMessages.slice(-MAX_RENDERED_MESSAGES)
    }

    scheduleMessageFlush()
  }

  /**
   * Darsga kirganda REST orqali olingan tarixni qo'shadi (takrorlarsiz).
   *
   * TARTIB `id` bo'yicha EMAS, VAQT bo'yicha: tarixda haqiqiy `id` bor, real
   * vaqtda kelganida esa u 0. `id` bo'yicha saralanganda tarix yuklanishi
   * bilan realtime xabarlar ro'yxatning BOSHIGA sakrab chiqib ketardi.
   */
  function seedMessages(history: readonly ChatMessage[]): void {
    const merged = new Map<string, ChatMessage>()
    for (const message of history) merged.set(messageKey(message), message)
    for (const message of messages.value) merged.set(messageKey(message), message)
    for (const message of pendingMessages) merged.set(messageKey(message), message)
    pendingMessages = []

    const sorted = Array.from(merged.values()).sort(
      (a, b) => Date.parse(a.sentAt) - Date.parse(b.sentAt),
    )
    messages.value =
      sorted.length > MAX_RENDERED_MESSAGES
        ? sorted.slice(sorted.length - MAX_RENDERED_MESSAGES)
        : sorted

    seenKeys = new Set(messages.value.map((message) => messageKey(message)))
  }

  /* ------------------------------- presence -------------------------------- */

  function rememberRole(userId: number, role: UserRoleName): void {
    if (knownRoles.get(userId) !== role) knownRoles.set(userId, role)
  }

  function renderPresence(): void {
    lastPresenceRenderAt = performance.now()

    const entries = Array.from(presence.values())
    // Barqaror tartib: avval ustoz/kurator, keyin qo'l ko'targanlar, keyin ism bo'yicha.
    // Tartib barqaror bo'lgani uchun `:key` bilan DOM deyarli qayta tuzilmaydi.
    entries.sort((a, b) => {
      const byRole = roleWeight(a.role) - roleWeight(b.role)
      if (byRole !== 0) return byRole
      if (a.handRaised !== b.handRaised) return a.handRaised ? -1 : 1
      return a.displayName.localeCompare(b.displayName)
    })

    participants.value = entries
    raisedHands.value = entries.filter((entry) => entry.handRaised)
    roleByUserId.value = new Map(knownRoles)
  }

  function schedulePresenceRender(): void {
    if (presenceRenderTimer !== null) return
    const elapsed = performance.now() - lastPresenceRenderAt
    if (elapsed >= PRESENCE_RENDER_INTERVAL_MS) {
      // Yakka hodisa — darhol ko'rsatamiz (leading edge).
      renderPresence()
      return
    }
    // Hodisalar to'p-to'p kelmoqda — oxirida bir marta chizamiz (trailing edge).
    presenceRenderTimer = window.setTimeout(() => {
      presenceRenderTimer = null
      renderPresence()
    }, PRESENCE_RENDER_INTERVAL_MS - elapsed)
  }

  /* ----------------------------- hodisa ishlovchilari ----------------------- */

  function handleChatMessage(payload: unknown): void {
    const message = toChatMessage(payload)
    if (message === null) return
    pushMessage(message)
  }

  function handlePresenceChanged(payload: unknown): void {
    if (!isRecord(payload)) return
    const { userId, displayName, role, joined, count } = payload
    if (typeof userId !== 'number') return

    if (typeof count === 'number') participantCount.value = count

    const normalizedRole = asRole(role)
    rememberRole(userId, normalizedRole)

    if (joined === true) {
      const existing = presence.get(userId)
      presence.set(userId, {
        userId,
        displayName: typeof displayName === 'string' ? displayName : (existing?.displayName ?? '—'),
        role: normalizedRole,
        handRaised: existing?.handRaised ?? false,
        joinedAt: existing?.joinedAt ?? new Date().toISOString(),
      })
    } else {
      presence.delete(userId)
    }

    // Server `count` yubormagan bo'lsa — o'zimizdagi ro'yxatdan hisoblaymiz.
    if (typeof count !== 'number') participantCount.value = presence.size

    schedulePresenceRender()
  }

  function handleHandRaised(payload: unknown): void {
    if (!isRecord(payload)) return
    const { userId, displayName, raised } = payload
    if (typeof userId !== 'number') return

    const existing = presence.get(userId)
    if (existing !== undefined) {
      presence.set(userId, { ...existing, handRaised: raised === true })
    } else {
      // Presence'da yo'q bo'lsa ham ko'rsatamiz (hodisalar tartibi kafolatlanmagan).
      presence.set(userId, {
        userId,
        displayName: typeof displayName === 'string' ? displayName : '—',
        role: knownRoles.get(userId) ?? 'Student',
        handRaised: raised === true,
        joinedAt: new Date().toISOString(),
      })
    }
    schedulePresenceRender()
  }

  function handleSessionEnded(payload: unknown): void {
    if (isRecord(payload) && typeof payload['sessionId'] === 'number') {
      if (payload['sessionId'] !== sessionId) return
    }
    sessionEnded.value = true
    options.onSessionEnded?.()
  }

  /* ------------------------------- ulanish --------------------------------- */

  function buildConnection(): HubConnection {
    const built = new HubConnectionBuilder()
      .withUrl(env.hubUrl, {
        // SPEC 6: WebSocket sarlavha qo'llab-quvvatlamagani uchun token
        // `?access_token=` query parametrida ketadi. `accessTokenFactory` bilan
        // signalr-js buni O'ZI qo'shadi va qayta ulanishda YANGI tokenni oladi.
        accessTokenFactory: () => getAccessToken() ?? '',
        // Negotiation bosqichini o'tkazib yuboramiz: bitta ortiqcha HTTP so'rov
        // yo'qoladi va 200 klient bir vaqtda kirganda serverga yuk kamayadi.
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([...RECONNECT_DELAYS_MS])
      .configureLogging(env.isDev ? LogLevel.Warning : LogLevel.Error)
      .build()

    built.serverTimeoutInMilliseconds = SERVER_TIMEOUT_MS
    built.keepAliveIntervalInMilliseconds = KEEP_ALIVE_MS

    built.on(HubEvent.ChatMessage, handleChatMessage)
    built.on(HubEvent.PresenceChanged, handlePresenceChanged)
    built.on(HubEvent.HandRaised, handleHandRaised)
    built.on(HubEvent.SessionEnded, handleSessionEnded)

    built.onreconnecting(() => {
      if (disposed) return
      status.value = 'reconnecting'
    })

    built.onreconnected(() => {
      if (disposed) return
      status.value = 'connected'
      // MUHIM: qayta ulanishda SignalR guruh a'zoligi saqlanmaydi —
      // `session-{id}` guruhiga qaytadan qo'shilish SHART.
      void joinSession(built)
    })

    built.onclose(() => {
      if (disposed) return
      status.value = 'disconnected'
      scheduleRetry()
    })

    return built
  }

  function detachHandlers(target: HubConnection): void {
    target.off(HubEvent.ChatMessage, handleChatMessage)
    target.off(HubEvent.PresenceChanged, handlePresenceChanged)
    target.off(HubEvent.HandRaised, handleHandRaised)
    target.off(HubEvent.SessionEnded, handleSessionEnded)
  }

  async function joinSession(target: HubConnection): Promise<void> {
    try {
      // SPEC 6.1: to'liq ishtirokchilar ro'yxati FAQAT shu javobda keladi.
      // Keyin faqat delta (`PresenceChanged`) keladi.
      //
      // ★ JAVOB SHAKLI: `JoinSessionResult(Session, Participants, Count)` —
      // ya'ni OBYEKT (`{ session, participants, count }`), massiv EMAS
      // (hub shartnomasi: `LiveClassHub.cs`). Ilgari shu yerda `Array.isArray`
      // tekshirilardi va u DOIM `false` bo'lgani uchun ro'yxat hech qachon
      // to'ldirilmasdi: 20 kishilik darsga kirgan odam bo'sh ishtirokchilar
      // panelini va `0` sanog'ini ko'rardi, faqat undan KEYIN kirganlar delta
      // bilan qo'shilardi. Xato ham chiqmagani uchun ko'zga tashlanmagan.
      const result: unknown = await target.invoke(HubMethod.JoinSession, sessionId)
      const participants = isRecord(result) ? result['participants'] : null

      if (Array.isArray(participants)) {
        presence.clear()
        for (const raw of participants) {
          const entry = toPresenceEntry(raw)
          if (entry === null) continue
          presence.set(entry.userId, entry)
          rememberRole(entry.userId, entry.role)
        }
        // Server `count` ni ham yuboradi, lekin sanoq RO'YXATDAN hisoblanadi:
        // ikki manba ajralib qolsa ekranda "3 ishtirokchi" yozilib, ro'yxatda
        // 2 kishi ko'rinardi.
        participantCount.value = presence.size
        renderPresence()
      }
      lastError.value = null
    } catch (error) {
      lastError.value = toUserMessage(error)
    }
  }

  function clearRetryTimer(): void {
    if (retryTimer === null) return
    window.clearTimeout(retryTimer)
    retryTimer = null
  }

  function scheduleRetry(): void {
    if (disposed || sessionEnded.value || retryTimer !== null) return
    const delay =
      INITIAL_RETRY_DELAYS_MS[Math.min(startAttempt, INITIAL_RETRY_DELAYS_MS.length - 1)] ?? 20_000
    startAttempt += 1
    retryTimer = window.setTimeout(() => {
      retryTimer = null
      void start()
    }, delay)
  }

  async function start(): Promise<void> {
    if (disposed || sessionEnded.value) return
    if (connection !== null && connection.state !== HubConnectionState.Disconnected) return

    clearRetryTimer()
    status.value = 'connecting'

    const target = connection ?? buildConnection()
    connection = target

    try {
      await target.start()
      if (disposed) {
        void target.stop()
        return
      }
      status.value = 'connected'
      startAttempt = 0
      lastError.value = null
      await joinSession(target)
    } catch (error) {
      if (disposed) return
      status.value = 'disconnected'
      lastError.value = toUserMessage(error)
      scheduleRetry()
    }
  }

  async function retry(): Promise<void> {
    startAttempt = 0
    clearRetryTimer()
    await start()
  }

  /* ------------------------------ yuborish --------------------------------- */

  function clearCooldownTimer(): void {
    if (cooldownTimer === null) return
    window.clearInterval(cooldownTimer)
    cooldownTimer = null
  }

  /**
   * SPEC 6.2 — server oyna bo'yicha chegaralaydi (10 soniyada 5 ta). Klient
   * AYNAN shu qoidani takrorlaydi: foydalanuvchi serverdan "rad javob" olib
   * xabarini yo'qotmaydi va serverga keraksiz yuk tushmaydi.
   *
   * ★ ILGARI "2 soniyada 1 ta" edi va HAR xabardan keyin yuborish tugmasi
   * 2 soniyaga o'chib qolardi. Odam ikkinchi qatorini yozib Enter bosganda
   * hech narsa bo'lmasdi — bu foydalanuvchi shikoyat qilgan "kechikish"
   * hissiyotining bir qismi edi.
   */
  function pruneSendWindow(now: number): void {
    sentAtTimes = sentAtTimes.filter((at) => now - at < CHAT_RATE_WINDOW_MS)
  }

  /** Yana qancha kutish kerak (0 — hozir yuborsa bo'ladi). */
  function remainingCooldown(now: number): number {
    pruneSendWindow(now)
    if (sentAtTimes.length < CHAT_RATE_MAX_MESSAGES) return 0
    const oldest = sentAtTimes[0] ?? now
    return Math.max(0, CHAT_RATE_WINDOW_MS - (now - oldest))
  }

  function refreshCooldown(): void {
    cooldownRemainingMs.value = remainingCooldown(Date.now())

    if (cooldownRemainingMs.value <= 0) {
      clearCooldownTimer()
      return
    }
    if (cooldownTimer !== null) return

    cooldownTimer = window.setInterval(() => {
      cooldownRemainingMs.value = remainingCooldown(Date.now())
      if (cooldownRemainingMs.value <= 0) clearCooldownTimer()
    }, 100)
  }

  const canSend = computed(
    () => status.value === 'connected' && cooldownRemainingMs.value <= 0 && !sessionEnded.value,
  )

  /**
   * Xabar yuboradi.
   *
   * `clientId` — chaqiruvchi (kompozitor) yasagan BARQAROR kalit. U xabarni
   * ekranga DARHOL chiqarib, server broadcast'i qaytganda uni AYNI kalit
   * bo'yicha tanib, ikki marta ko'rsatmaydi. Berilmasa — shu yerda yasaladi.
   */
  async function sendMessage(rawBody: string, clientId?: string): Promise<boolean> {
    const body = normalizeBody(rawBody)
    if (body.length === 0) return false

    if (remainingCooldown(Date.now()) > 0) {
      // ★ JIM QAYTMAYDI: ilgari kompozitor shunchaki hech narsa qilmasdi va
      // foydalanuvchi nima uchun xabari ketmaganini bilmasdi.
      notice.value = 'Sekinroq yozing — 10 soniyada 5 tagacha xabar yuborish mumkin.'
      refreshCooldown()
      return false
    }

    const target = connection
    if (target === null || target.state !== HubConnectionState.Connected) {
      notice.value = 'Aloqa yo‘q. Xabar yuborilmadi.'
      return false
    }

    isSending.value = true
    try {
      // Server `SenderName` va `SentAt` ni O'ZI qo'yadi (SPEC 6).
      await target.invoke(HubMethod.SendMessage, sessionId, body, clientId ?? newClientId())
      sentAtTimes.push(Date.now())
      refreshCooldown()
      notice.value = null
      return true
    } catch (error) {
      // `hubErrorText` — server matnining oldidagi inglizcha SignalR qobig'ini
      // olib tashlaydi (dev muhitida u qo'shiladi).
      notice.value = hubErrorText(error)
      return false
    } finally {
      isSending.value = false
    }
  }

  async function raiseHand(raised: boolean): Promise<void> {
    const target = connection
    if (target === null || target.state !== HubConnectionState.Connected) {
      notice.value = 'Aloqa yo‘q. Qayta urinib ko‘ring.'
      return
    }
    // Optimistik yangilanish — tugma darhol javob beradi, server tasdiqlaydi.
    handRaised.value = raised
    try {
      await target.invoke(HubMethod.RaiseHand, sessionId, raised)
    } catch (error) {
      handRaised.value = !raised
      notice.value = toUserMessage(error)
    }
  }

  function dismissNotice(): void {
    notice.value = null
  }

  /* ------------------------------- tozalash -------------------------------- */

  async function dispose(): Promise<void> {
    disposed = true

    clearRetryTimer()
    clearCooldownTimer()
    if (flushHandle !== null) {
      cancelAnimationFrame(flushHandle)
      flushHandle = null
    }
    if (presenceRenderTimer !== null) {
      window.clearTimeout(presenceRenderTimer)
      presenceRenderTimer = null
    }

    const target = connection
    connection = null
    if (target === null) return

    detachHandlers(target)
    try {
      if (target.state === HubConnectionState.Connected) {
        await target.invoke(HubMethod.LeaveSession, sessionId)
      }
    } catch {
      // Chiqishda xato bo'lsa ham ulanishni to'xtatamiz.
    }
    try {
      await target.stop()
    } catch {
      /* e'tiborsiz */
    }

    pendingMessages = []
    presence.clear()
    seenKeys = new Set<string>()
    sentAtTimes = []
  }

  onBeforeUnmount(() => {
    void dispose()
  })

  return {
    status,
    messages,
    participants,
    participantCount,
    raisedHands,
    roleByUserId,
    sessionEnded,
    lastError,
    notice,
    cooldownRemainingMs,
    canSend,
    isSending,
    handRaised,
    start,
    retry,
    sendMessage,
    raiseHand,
    seedMessages,
    dismissNotice,
  }
}
