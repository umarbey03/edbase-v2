import { HttpTransportType, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { onBeforeUnmount, ref } from 'vue'
import type { Ref } from 'vue'

import { getAccessToken, toUserMessage } from '@/shared/api'
import { env, hubUrlFor } from '@/shared/config/env'
import { GroupChatHubEvent, GroupChatHubMethod } from '@/shared/types'
import type {
  GroupChatAccessDto,
  GroupChatChannelName,
  GroupChatMessageDto,
  HubStatus,
} from '@/shared/types'

/**
 * ============================================================================
 *  GURUH CHATI HUB'I — `/hubs/group-chat`
 * ============================================================================
 *
 * `features/live-hub/model/useLiveHub.ts` bilan BIR XIL uslub: `skipNegotiation`
 * + WebSockets, `accessTokenFactory` (qayta ulanishda YANGI token oladi),
 * `withAutomaticReconnect`, va `onBeforeUnmount` da qat'iy tozalash.
 *
 * ★ IKKI TARIXIY XATO SHU YERDA TAKRORLANMAYDI:
 *
 *  1) `JoinThread` javobi — OBYEKT (`{groupId, groupName, channel,
 *     availableChannels}`), massiv EMAS. Jonli hub bilan tekshirildi:
 *     `Array.isArray(result) === false`. Jonli dars hub'ida aynan shu joyda
 *     `Array.isArray` tekshirilardi va u DOIM `false` bo'lgani uchun
 *     ishtirokchilar ro'yxati hech qachon to'ldirilmasdi — xato ham
 *     chiqmagani uchun uzoq vaqt sezilmagan. Shuning uchun bu yerda javob
 *     shakli `isRecord` bilan MAYDONMA-MAYDON tekshiriladi.
 *
 *  2) Hodisa ishlovchisi HECH NARSA QAYTARMASLIGI kerak. SignalR 8 klienti
 *     ishlovchi qiymat qaytarsa konsolga xato yozadi:
 *     "Result given for 'groupchatmessage' method but server is not expecting
 *      a result."
 *     Bu jonli sinovda AYNAN kuzatildi (ishlovchi `arr.push(...)` yozgani
 *     uchun — `push` son qaytaradi). Quyidagi `handleMessage` ataylab
 *     `void` qaytaradi va oxirgi ifodasi qiymat bermaydi.
 */

/**
 * Hub istisnosidan FOYDALANUVCHIGA ko'rsatiladigan matnni ajratadi.
 *
 * NEGA KERAK: SignalR klienti server xatosini o'z qobig'iga o'raydi va
 * `error.message` shunday bo'ladi (jonli sinovda AYNAN kuzatildi):
 *
 *   "An unexpected error occurred invoking 'SendMessage' on the server.
 *    HubException: Juda tez yozyapsiz. Bir necha soniyadan keyin urinib ko'ring."
 *
 * Bu matnni o'zgarishsiz ko'rsatsak, o'quvchi o'zbekcha jumla oldidan
 * inglizcha texnik qatorni o'qirdi. Server o'zi tayyorlagan o'zbekcha qism
 * `HubException: ` dan KEYIN turadi — faqat o'shanisi olinadi.
 */
export function hubErrorText(error: unknown): string {
  const raw = error instanceof Error ? error.message : String(error)
  const marker = 'HubException: '
  const index = raw.indexOf(marker)
  if (index >= 0) return raw.slice(index + marker.length).trim()
  return toUserMessage(error)
}

/** `withAutomaticReconnect` kechikishlari — jonli dars hub'idagidek. */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000, 20_000]

/** Birinchi ulanish muvaffaqiyatsiz bo'lsa — o'z backoff'imiz. */
const INITIAL_RETRY_DELAYS_MS = [1_000, 3_000, 6_000, 12_000, 20_000]

const SERVER_TIMEOUT_MS = 60_000
const KEEP_ALIVE_MS = 15_000

export interface UseGroupChatHubOptions {
  /**
   * Yangi xabar kelganda. ★ Chaqiruvchi `id` bo'yicha DEDUPE qilishi SHART:
   * yuboruvchi o'z xabarini ham broadcast orqali oladi (jonli tasdiqlangan).
   */
  onMessage: (message: GroupChatMessageDto) => void
}

export interface UseGroupChatHubResult {
  status: Ref<HubStatus>
  lastError: Ref<string | null>
  /** Oxirgi muvaffaqiyatli `JoinThread` javobi (kanal tab'lari shundan). */
  access: Ref<GroupChatAccessDto | null>
  /** Suhbatga ulanadi; kanal almashsa avval eskisidan chiqadi. */
  join: (groupId: number, channel: GroupChatChannelName | null) => Promise<void>
  /** Ochiq suhbatdan chiqadi (ulanish tirik qoladi — boshqasiga o'tish uchun). */
  leave: () => Promise<void>
  /** Hub orqali yuboradi. Ulanish yo'q bo'lsa `null` — chaqiruvchi REST'ga tushadi. */
  sendMessage: (body: string) => Promise<GroupChatMessageDto | null>
}

/* --------------------------- payload validatorlari -------------------------- */

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function asChannel(value: unknown): GroupChatChannelName | null {
  return value === 'Teacher' || value === 'Curator' ? value : null
}

function asChannelList(value: unknown): GroupChatChannelName[] {
  if (!Array.isArray(value)) return []
  const result: GroupChatChannelName[] = []
  for (const item of value) {
    const channel = asChannel(item)
    if (channel !== null) result.push(channel)
  }
  return result
}

function toAccess(payload: unknown): GroupChatAccessDto | null {
  if (!isRecord(payload)) return null
  const groupId = payload['groupId']
  const channel = asChannel(payload['channel'])
  if (typeof groupId !== 'number' || channel === null) return null
  const groupName = payload['groupName']
  return {
    groupId,
    groupName: typeof groupName === 'string' ? groupName : '',
    channel,
    availableChannels: asChannelList(payload['availableChannels']),
  }
}

function toMessage(payload: unknown): GroupChatMessageDto | null {
  if (!isRecord(payload)) return null
  const { id, groupId, senderId, senderName, senderRole, body, sentAt } = payload
  const channel = asChannel(payload['channel'])
  if (typeof id !== 'number' || typeof groupId !== 'number' || channel === null) return null
  if (typeof senderId !== 'number' || typeof body !== 'string') return null
  return {
    id,
    groupId,
    channel,
    senderId,
    senderName: typeof senderName === 'string' ? senderName : '—',
    // Rol faqat nishon (badge) uchun — noma'lum qiymat kelsa chizmaymiz.
    senderRole:
      senderRole === 'Student' ||
      senderRole === 'Teacher' ||
      senderRole === 'Assistant' ||
      senderRole === 'Academic' ||
      senderRole === 'Admin'
        ? senderRole
        : 'Student',
    body,
    sentAt: typeof sentAt === 'string' ? sentAt : new Date().toISOString(),
  }
}

/* ================================ composable =============================== */

export function useGroupChatHub(options: UseGroupChatHubOptions): UseGroupChatHubResult {
  const status = ref<HubStatus>('idle')
  const lastError = ref<string | null>(null)
  const access = ref<GroupChatAccessDto | null>(null)

  /* ------------------------- reaktiv BO'LMAGAN holat ------------------------ */

  let connection: HubConnection | null = null
  let disposed = false
  let startAttempt = 0
  let retryTimer: number | null = null

  /**
   * HOZIR ochiq suhbat. Qayta ulanishda SignalR guruh a'zoligi SAQLANMAYDI —
   * `onreconnected` da AYNAN shu juftlikka qaytadan `JoinThread` qilinadi
   * (jonli dars hub'idagi bilan bir xil qoida).
   */
  let currentGroupId: number | null = null
  let currentChannel: GroupChatChannelName | null = null

  /**
   * Kanal almashtirish uchun navbat raqami.
   *
   * NEGA: `join()` ketma-ket ikki marta chaqirilishi mumkin (foydalanuvchi
   * tab'ni tez bosadi). Kechikkan birinchi javob ikkinchisining ustiga
   * yozilib, ekranda "Ustoz" tab'i tanlangan holda KURATOR tarixi qolib
   * ketardi. Har chaqiruv o'z raqamini oladi va eskirgani natijani
   * tashlab yuboradi.
   */
  let joinToken = 0

  /* ----------------------------- hodisa ishlovchisi ------------------------- */

  /**
   * ★ QAYTISH TURI ATAYLAB `void` (yuqoridagi 2-izohga qarang): SignalR 8
   * klienti ishlovchidan qiymat kelsa konsolni xato bilan to'ldiradi.
   */
  function handleMessage(payload: unknown): void {
    const message = toMessage(payload)
    if (message === null) return

    /*
      ★ KANAL IZOLYATSIYASI — KLIENT TOMONIDAGI SO'NGGI TO'SIQ.

      Server allaqachon faqat tegishli xonaga yuboradi, lekin kanal
      almashtirilganda `LeaveThread` va `JoinThread` orasida qisqa oyna bor:
      shu paytda ESKI kanaldan kelgan xabar yangi tarixga tushib qolishi
      mumkin edi. Guruh va kanalni qayta tekshirish buni imkonsiz qiladi.
    */
    if (message.groupId !== currentGroupId) return
    if (message.channel !== currentChannel) return

    options.onMessage(message)
  }

  /* ------------------------------- ulanish --------------------------------- */

  function buildConnection(): HubConnection {
    const built = new HubConnectionBuilder()
      .withUrl(hubUrlFor('group-chat'), {
        // WebSocket sarlavha qo'llab-quvvatlamagani uchun token query'da ketadi;
        // `accessTokenFactory` uni qayta ulanishda YANGISI bilan almashtiradi.
        accessTokenFactory: () => getAccessToken() ?? '',
        skipNegotiation: true,
        transport: HttpTransportType.WebSockets,
      })
      .withAutomaticReconnect([...RECONNECT_DELAYS_MS])
      .configureLogging(env.isDev ? LogLevel.Warning : LogLevel.Error)
      .build()

    built.serverTimeoutInMilliseconds = SERVER_TIMEOUT_MS
    built.keepAliveIntervalInMilliseconds = KEEP_ALIVE_MS

    built.on(GroupChatHubEvent.GroupChatMessage, handleMessage)

    built.onreconnecting(() => {
      if (disposed) return
      status.value = 'reconnecting'
    })

    built.onreconnected(() => {
      if (disposed) return
      status.value = 'connected'
      // Qayta ulanishda guruh a'zoligi yo'qoladi — suhbatga QAYTA kiramiz.
      const groupId = currentGroupId
      if (groupId !== null) void joinThread(built, groupId, currentChannel, joinToken)
    })

    built.onclose(() => {
      if (disposed) return
      status.value = 'disconnected'
      scheduleRetry()
    })

    return built
  }

  function detachHandlers(target: HubConnection): void {
    target.off(GroupChatHubEvent.GroupChatMessage, handleMessage)
  }

  async function joinThread(
    target: HubConnection,
    groupId: number,
    channel: GroupChatChannelName | null,
    token: number,
  ): Promise<void> {
    try {
      const result: unknown = await target.invoke(
        GroupChatHubMethod.JoinThread,
        groupId,
        channel,
      )
      // Bu chaqiruv eskirgan bo'lsa (foydalanuvchi boshqa kanalga o'tdi) —
      // natijani TASHLAYMIZ.
      if (disposed || token !== joinToken) return

      const parsed = toAccess(result)
      if (parsed === null) {
        lastError.value = 'Suhbat ma’lumoti tushunarsiz shaklda keldi.'
        return
      }
      access.value = parsed
      // ★ Server QAYSI kanalni bergan bo'lsa — o'shanisi yoziladi. Klient
      // "men Teacher so'ragandim" deb turib olmaydi: kanal tanlovi serverniki.
      currentChannel = parsed.channel
      lastError.value = null
    } catch (error) {
      if (disposed || token !== joinToken) return
      // Ruxsat xatosi (boshqa kanal) ham shu yerdan keladi — server matni
      // o'zbekcha va foydalanuvchiga MO'LJALLANGAN, shuning uchun ko'rsatiladi.
      lastError.value = hubErrorText(error)
    }
  }

  function clearRetryTimer(): void {
    if (retryTimer === null) return
    window.clearTimeout(retryTimer)
    retryTimer = null
  }

  function scheduleRetry(): void {
    if (disposed || retryTimer !== null) return
    const delay =
      INITIAL_RETRY_DELAYS_MS[Math.min(startAttempt, INITIAL_RETRY_DELAYS_MS.length - 1)] ?? 20_000
    startAttempt += 1
    retryTimer = window.setTimeout(() => {
      retryTimer = null
      const groupId = currentGroupId
      if (groupId !== null) void join(groupId, currentChannel)
    }, delay)
  }

  async function ensureStarted(): Promise<HubConnection | null> {
    if (disposed) return null

    const target = connection ?? buildConnection()
    connection = target

    if (target.state === HubConnectionState.Connected) return target
    if (target.state !== HubConnectionState.Disconnected) return target

    clearRetryTimer()
    status.value = 'connecting'
    try {
      await target.start()
      if (disposed) {
        void target.stop()
        return null
      }
      status.value = 'connected'
      startAttempt = 0
      lastError.value = null
      return target
    } catch (error) {
      if (disposed) return null
      status.value = 'disconnected'
      lastError.value = toUserMessage(error)
      scheduleRetry()
      return null
    }
  }

  /* --------------------------- suhbatga kirish/chiqish ---------------------- */

  async function leaveCurrent(target: HubConnection): Promise<void> {
    const groupId = currentGroupId
    const channel = currentChannel
    if (groupId === null || channel === null) return
    if (target.state !== HubConnectionState.Connected) return
    try {
      await target.invoke(GroupChatHubMethod.LeaveThread, groupId, channel)
    } catch {
      // Chiqishdagi xato foydalanuvchiga ko'rsatilmaydi: u allaqachon boshqa
      // suhbatga o'tgan va bu xabar faqat chalg'itardi.
    }
  }

  async function join(groupId: number, channel: GroupChatChannelName | null): Promise<void> {
    joinToken += 1
    const token = joinToken

    const target = await ensureStarted()
    if (target === null || disposed || token !== joinToken) return

    // Boshqa suhbatdan kelayotgan bo'lsak — avval eskisidan CHIQAMIZ, aks
    // holda ulanish eski xonada qolib, u yerdagi xabarlarni ham olardi.
    if (currentGroupId !== null && (currentGroupId !== groupId || currentChannel !== channel)) {
      await leaveCurrent(target)
      if (disposed || token !== joinToken) return
    }

    currentGroupId = groupId
    currentChannel = channel
    access.value = null

    await joinThread(target, groupId, channel, token)
  }

  async function leave(): Promise<void> {
    joinToken += 1
    const target = connection
    if (target !== null) await leaveCurrent(target)
    currentGroupId = null
    currentChannel = null
    access.value = null
  }

  /* ------------------------------ yuborish --------------------------------- */

  async function sendMessage(body: string): Promise<GroupChatMessageDto | null> {
    const target = connection
    const groupId = currentGroupId
    if (target === null || groupId === null) return null
    if (target.state !== HubConnectionState.Connected) return null

    const result: unknown = await target.invoke(
      GroupChatHubMethod.SendMessage,
      groupId,
      currentChannel,
      body,
    )
    // Xato bo'lsa `invoke` istisno tashlaydi va u YUQORIGA ketadi: yuborilmagan
    // xabarni jimgina yutib yuborish — foydalanuvchi uchun ma'lumot yo'qolishi.
    return toMessage(result)
  }

  /* ------------------------------- tozalash -------------------------------- */

  /**
   * ★ TOZALASH MAJBURIY VA TO'LIQ: `LeaveThread` -> tinglovchini olib tashlash
   * -> ulanishni yopish. Aks holda sahifadan chiqilgach ham WebSocket tirik
   * qolib, xabarlar allaqachon yo'q komponent uchun kelaverardi.
   */
  async function dispose(): Promise<void> {
    disposed = true
    clearRetryTimer()

    const target = connection
    connection = null
    if (target === null) return

    // TARTIB MUHIM: avval xonadan chiqamiz (ulanish hali tirik), keyin
    // tinglovchini uzamiz, oxirida ulanishni yopamiz.
    await leaveCurrent(target)
    detachHandlers(target)
    try {
      await target.stop()
    } catch {
      /* e'tiborsiz — sahifa baribir yopilyapti */
    }

    currentGroupId = null
    currentChannel = null
  }

  onBeforeUnmount(() => {
    void dispose()
  })

  return { status, lastError, access, join, leave, sendMessage }
}
