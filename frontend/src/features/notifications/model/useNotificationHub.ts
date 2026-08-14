import { HttpTransportType, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import type { HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/vue-query'
import type { QueryClient } from '@tanstack/vue-query'
import { onBeforeUnmount, ref } from 'vue'
import type { Ref } from 'vue'

import { getAccessToken } from '@/shared/api'
import { env, hubUrlFor } from '@/shared/config/env'
import { NotificationHubEvent } from '@/shared/types'
import type { HubStatus, NotificationDto, NotificationKindName } from '@/shared/types'

import {
  NOTIFICATIONS_ROOT_KEY,
  STUDENT_ASSIGNMENTS_KEY,
} from './notification-queries'

/**
 * ============================================================================
 *  BILDIRISHNOMA HUB'I — `/hubs/notifications` (R35/R36)
 * ============================================================================
 *
 * `features/group-chat/model/useGroupChatHub.ts` bilan BIR XIL uslub:
 * `skipNegotiation` + WebSockets, `accessTokenFactory` (qayta ulanishda
 * YANGI token oladi), `withAutomaticReconnect` va qat'iy tozalash.
 *
 * ── GURUH CHATI HUB'IDAN UCHTA FARQ ────────────────────────────────────────
 *
 *  1) OBUNA YO'Q. Server ulanish egasini TOKENDAN aniqlaydi
 *     (`Clients.User`), ya'ni `JoinThread` ga o'xshash chaqiruv kerak emas
 *     va `onreconnected` da QAYTA OBUNA BO'LISH ham shart emas — guruh
 *     a'zoligi yo'q, ya'ni yo'qoladigan narsa yo'q.
 *
 *  2) HODISA RO'YXATGA QO'SHILMAYDI, KESH BEKOR QILINADI. Guruh chatida
 *     xabar to'g'ridan-to'g'ri ekrandagi massivga qo'shiladi (u yerda
 *     tarix komponent holatida). Bu yerda manba — TanStack Query keshi,
 *     ya'ni yagona to'g'ri harakat "qayta so'ra" deyish. Aks holda hub
 *     yo'li va REST yo'li ikki xil haqiqat yaratardi.
 *
 *  3) ★ HODISA SANOG'I EMAS, TURTKI: hodisa tanasi ATAYLAB ishlatilmaydi
 *     (faqat shakli tekshiriladi). Sabab quyidagi "portlash" izohida.
 *
 * ── IKKI TARIXIY TUZOQ SHU YERDA HAM TAKRORLANMAYDI ────────────────────────
 *
 *  • Hodisa ishlovchisi HECH NARSA QAYTARMASLIGI kerak — aks holda
 *    SignalR 8 klienti konsolga "Result given for '...' method but server
 *    is not expecting a result" yozadi. Quyidagi `handleCreated` ataylab
 *    `void` qaytaradi.
 *
 *  • Payload MAYDONMA-MAYDON tekshiriladi (`Array.isArray` kabi bitta
 *    tekshiruvga tayanilmaydi): guruh chati hub'ida aynan shu joyda
 *    DOIM `false` bo'lgan shart uzoq vaqt sezilmay turgan edi.
 */

/** `withAutomaticReconnect` kechikishlari — mavjud ikki hub bilan bir xil. */
const RECONNECT_DELAYS_MS = [0, 2_000, 5_000, 10_000, 20_000]

/** Birinchi ulanish muvaffaqiyatsiz bo'lsa — o'z backoff'imiz. */
const INITIAL_RETRY_DELAYS_MS = [1_000, 3_000, 6_000, 12_000, 20_000]

const SERVER_TIMEOUT_MS = 60_000
const KEEP_ALIVE_MS = 15_000

/**
 * ══════════════════════════════════════════════════════════════════════════
 * 🔴 PORTLASHGA QARSHI HIMOYA — BAHOLASH TO'PLAM-TO'PLAM BO'LADI
 *
 * Ustoz 50 ta ishni bir o'tirishda tekshiradi. Telegram tomonini Redis
 * token-chelagi qo'riqlaydi (25/s), ILOVA ICHIDAGI yo'lda esa hech qanday
 * chegara YO'Q: server har baho uchun bitta hodisa yuboradi.
 *
 * Bitta o'quvchiga odatda shu 50 tadan 1-3 tasi tegadi (qolganlari guruh
 * bo'ylab tarqaladi), lekin bir vazifani KO'P BOSQICHDA tuzatish yoki bir
 * o'quvchining bir necha ishini ketma-ket baholash real holat. Har hodisa
 * darhol `invalidateQueries` chaqirsa, klient ketma-ket 3-5 ta bir xil
 * so'rov yuborardi va oxirgisidan boshqasi behuda bo'lardi.
 *
 * YECHIM — ORQA FRONTLI (trailing) DEBOUNCE. Ketma-ket kelgan hodisalar
 * BITTA yangilashga yig'iladi. 400 ms tanlandi: odam uchun bu "darhol"
 * (idrok chegarasi ~100 ms emas, chunki bu passiv yangilanish, bosilgan
 * tugmaga javob emas), tarmoq uchun esa butun to'plamni yutishga yetadi.
 *
 * ★ NEGA OLDINGI FRONTLI (leading) EMAS: birinchi hodisa kelganda ma'lumot
 *   hali YOZILMAGAN bo'lishi mumkin emas (server commit'dan keyin
 *   yuboradi), lekin KEYINGI hodisalar e'tiborsiz qolardi va oxirgi
 *   bahoni ko'rish uchun yana sahifa yangilash kerak bo'lardi — ya'ni
 *   aynan shikoyat qilingan holat qaytardi.
 * ══════════════════════════════════════════════════════════════════════════
 */
const INVALIDATE_DEBOUNCE_MS = 400

export interface UseNotificationHubResult {
  status: Ref<HubStatus>
}

/* --------------------------- payload validatorlari -------------------------- */

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function asKind(value: unknown): NotificationKindName | null {
  return value === 'SubmissionGraded' ? value : null
}

/**
 * Hodisa tanasini tekshiradi.
 *
 * ★ NATIJA HOZIR ISHLATILMAYDI (yuqoridagi izohga qarang — biz keshni
 * bekor qilamiz, ro'yxatga qo'shmaymiz), LEKIN TEKSHIRUV BARIBIR BOR:
 * u shartnoma buzilganini ERTA ko'rsatadi. Server tanani o'zgartirsa
 * (masalan `kind` raqamga aylansa), bu funksiya `null` qaytaradi va
 * yangilash umuman ishga tushmaydi — jimgina noto'g'ri ma'lumot
 * ko'rsatishdan ko'ra yaxshiroq.
 */
export function toNotification(payload: unknown): NotificationDto | null {
  if (!isRecord(payload)) return null

  const { id, title, body, entityId, read, createdAt } = payload
  const kind = asKind(payload['kind'])

  if (typeof id !== 'number' || kind === null) return null
  if (typeof title !== 'string' || typeof body !== 'string') return null

  return {
    id,
    kind,
    title,
    body,
    entityId: typeof entityId === 'number' ? entityId : null,
    read: read === true,
    createdAt: typeof createdAt === 'string' ? createdAt : new Date().toISOString(),
  }
}

/* ------------------------------ yagona ulanish ----------------------------- */

/*
  ★ ULANISH MODUL DARAJASIDA, KOMPONENT ICHIDA EMAS.

  Qo'ng'iroqcha ikki joyda chizilishi mumkin (xodim karkasida — mobil
  sarlavhada va yon menyuda). Har komponent o'z ulanishini ochsa, bitta
  foydalanuvchi ikkita WebSocket ushlab turardi va har hodisa ikki marta
  kelardi. Shuning uchun ulanish YAGONA, komponentlar esa unga
  hisoblagich (refcount) orqali obuna bo'ladi.
*/
let connection: HubConnection | null = null
let mounted = 0
let disposed = false
let startAttempt = 0
let retryTimer: number | null = null
let invalidateTimer: number | null = null
let client: QueryClient | null = null

const status = ref<HubStatus>('idle')

function clearRetryTimer(): void {
  if (retryTimer === null) return
  window.clearTimeout(retryTimer)
  retryTimer = null
}

function clearInvalidateTimer(): void {
  if (invalidateTimer === null) return
  window.clearTimeout(invalidateTimer)
  invalidateTimer = null
}

/**
 * Keshni bekor qiladi — yig'ilgan holda (yuqoridagi debounce izohi).
 *
 * IKKI kalit bekor qilinadi va ikkinchisi TALABNING YARMI:
 *   • `['notifications']` — qo'ng'iroqcha ro'yxati va nishondagi raqam;
 *   • `['assignments','mine']` — o'quvchining vazifalar ro'yxati, ya'ni
 *     BAHONING O'ZI. Bugungacha ustoz baholaganda buni HECH KIM bekor
 *     qilmasdi va o'quvchi sahifani qo'lda yangilashga majbur edi.
 */
function scheduleInvalidate(): void {
  if (disposed || client === null) return

  clearInvalidateTimer()

  invalidateTimer = window.setTimeout(() => {
    invalidateTimer = null

    const target = client
    if (target === null) return

    void target.invalidateQueries({ queryKey: NOTIFICATIONS_ROOT_KEY })
    void target.invalidateQueries({ queryKey: STUDENT_ASSIGNMENTS_KEY })
  }, INVALIDATE_DEBOUNCE_MS)
}

/**
 * ★ QAYTISH TURI ATAYLAB `void`: SignalR 8 klienti ishlovchidan qiymat
 * kelsa konsolni xato bilan to'ldiradi (jonli tekshirilgan tuzoq).
 */
function handleCreated(payload: unknown): void {
  if (toNotification(payload) === null) return

  scheduleInvalidate()
}

function buildConnection(): HubConnection {
  const built = new HubConnectionBuilder()
    .withUrl(hubUrlFor('notifications'), {
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

  built.on(NotificationHubEvent.NotificationCreated, handleCreated)

  built.onreconnecting(() => {
    if (disposed) return
    status.value = 'reconnecting'
  })

  built.onreconnected(() => {
    if (disposed) return
    status.value = 'connected'

    /*
      ★ QAYTA OBUNA YO'Q (guruh chati hub'idan farq): xona a'zoligi
      umuman ishlatilmaydi, server ulanish egasini tokendan aniqlaydi.

      LEKIN KESH BARIBIR BEKOR QILINADI: uzilib turgan vaqtda kelgan
      bildirishnomalar YO'QOLGAN. Ularni "qayta yuborish" mexanizmi yo'q
      va kerak ham emas — ro'yxat baribir serverdan o'qiladi. Bu bir
      qatorlik chaqiruv "uzilishdan keyin nishon eski raqamda qotib
      qolish" xatosini butunlay yopadi.
    */
    scheduleInvalidate()
  })

  built.onclose(() => {
    if (disposed) return
    status.value = 'disconnected'
    scheduleRetry()
  })

  return built
}

function scheduleRetry(): void {
  if (disposed || retryTimer !== null) return

  const delay =
    INITIAL_RETRY_DELAYS_MS[Math.min(startAttempt, INITIAL_RETRY_DELAYS_MS.length - 1)] ?? 20_000
  startAttempt += 1

  retryTimer = window.setTimeout(() => {
    retryTimer = null
    void ensureStarted()
  }, delay)
}

async function ensureStarted(): Promise<void> {
  if (disposed) return

  // Token yo'q bo'lsa ulanishga urinish MA'NOSIZ: server 401 qaytaradi va
  // biz backoff'ni bekorga yeb qo'yardik. Sessiya tiklanganda karkas qayta
  // mount bo'ladi va bu funksiya yana chaqiriladi.
  if (getAccessToken() === null) return

  const target = connection ?? buildConnection()
  connection = target

  if (target.state !== HubConnectionState.Disconnected) return

  clearRetryTimer()
  status.value = 'connecting'

  try {
    await target.start()
    if (disposed) {
      void target.stop()
      return
    }
    status.value = 'connected'
    startAttempt = 0
  } catch {
    if (disposed) return
    status.value = 'disconnected'

    // Xato foydalanuvchiga KO'RSATILMAYDI: qo'ng'iroqcha ikkinchi darajali
    // funksiya va uning ulanish xatosi ekranda "nimadir buzildi" degan
    // shovqin bo'lardi. Ro'yxat REST orqali baribir ochiladi.
    scheduleRetry()
  }
}

/**
 * ★ TOZALASH MAJBURIY VA TO'LIQ: tinglovchini olib tashlash -> ulanishni
 * yopish. Aks holda karkas almashganda (o'quvchi -> xodim) eski WebSocket
 * tirik qolib, allaqachon yo'q komponent uchun kesh bekor qilinaverardi.
 */
async function dispose(): Promise<void> {
  disposed = true
  clearRetryTimer()
  clearInvalidateTimer()

  const target = connection
  connection = null
  client = null
  status.value = 'idle'

  if (target === null) return

  target.off(NotificationHubEvent.NotificationCreated, handleCreated)

  try {
    await target.stop()
  } catch {
    /* e'tiborsiz — sahifa yoki karkas baribir yopilyapti */
  }
}

/* ================================ composable =============================== */

/**
 * Bildirishnoma kanalini ochadi. HAR KARKAS (AppShell / StudentShell) uni
 * BIR MARTA chaqiradi.
 *
 * ★ QO'NG'IROQCHA KOMPONENTI BUNI CHAQIRMAYDI — u faqat TanStack Query
 * keshidan o'qiydi. Shu tufayli qo'ng'iroqcha bir necha joyda chizilsa ham
 * ulanish YAGONA bo'lib qoladi.
 */
export function useNotificationHub(): UseNotificationHubResult {
  const queryClient = useQueryClient()

  mounted += 1
  disposed = false
  client = queryClient

  void ensureStarted()

  onBeforeUnmount(() => {
    mounted -= 1

    // Oxirgi iste'molchi ketgandagina yopamiz: ikkita karkas qisqa vaqt
    // birga turishi mumkin (marshrut o'tishida eskisi hali unmount
    // bo'lmagan, yangisi allaqachon mount bo'lgan).
    if (mounted <= 0) {
      mounted = 0
      void dispose()
    }
  })

  return { status }
}
