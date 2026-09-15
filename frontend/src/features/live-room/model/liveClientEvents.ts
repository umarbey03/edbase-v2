import { postLiveClientEvents } from '@/entities/session'
import type { LiveClientEvent, LiveClientInfo } from '@/entities/session'
import { isApiError } from '@/shared/api'

/*
  ════════════════════════════════════════════════════════════════════════
  JONLI DARS KLIENTINING DIAGNOSTIKA HODISALARI (2026-09-14)
  ════════════════════════════════════════════════════════════════════════

  🔴 NIMA UCHUN: LiveKit logida telefondagi o'quvchilar bir darsda ~3 marta
     chiqib-kirishi ko'rinadi (5 kunda 679 ta ortiqcha `CLIENT_REQUEST_LEAVE`),
     lekin SABAB ko'rinmaydi: sahifa yopildimi, ekran qulflandimi, Telegram
     boshqa ilovaga o'tganda sahifani o'ldirdimi yoki internet yo'qoldimi.
     Sababni bilmay turib tuzatish — taxmin bilan tuzatish.

  ★ HODISALAR API LOGIGA TUSHADI, JADVALGA EMAS: loyihaning butun tashxisi
    allaqachon `docker logs` orqali o'qiladi.

  ★ NAVBAT VA PAKET: internet yo'q paytdagi hodisa aynan eng qimmatlisi,
    shuning uchun yuborilmaganlari navbatda qoladi va keyingi urinishda
    ketadi. Sahifa yopilayotganda `keepalive` bilan yuboriladi.

  ⚠️ DIAGNOSTIKA DARSGA HALAQIT BERMASLIGI SHART: har qanday xato jimgina
     yutiladi, navbat cheklangan, yuborish 15 soniyada bir marta.
*/

const FLUSH_INTERVAL_MS = 15_000

/** Server bitta so'rovda 50 tagacha hodisa qabul qiladi. */
const MAX_BATCH = 50

/** Internet uzoq yo'q bo'lsa xotira o'smasin — eng eskisi tashlanadi. */
const MAX_QUEUE = 200

type EventExtra = Partial<Omit<LiveClientEvent, 'type' | 'at'>>

export interface LiveEventReporter {
  report: (type: string, extra?: EventExtra) => void
  dispose: () => void
}

/**
 * Telegram ichidagi brauzermi.
 *
 * Telegram o'z ko'rinishiga `TelegramWebviewProxy` ni qo'shadi; Mini App'da
 * esa `Telegram.WebApp` bor. User-Agent'ga ishonib bo'lmaydi (Android'da
 * oddiy WebView satri), shuning uchun u faqat qo'shimcha belgi.
 */
function isTelegramWebView(): boolean {
  const scope = window as unknown as Record<string, unknown>
  return 'TelegramWebviewProxy' in scope || 'Telegram' in scope || /Telegram/i.test(navigator.userAgent)
}

function networkType(): string | undefined {
  const connection = (navigator as Navigator & { connection?: { effectiveType?: string } }).connection
  return connection?.effectiveType
}

/** Vaqtinchalik xato — hodisalar navbatga qaytariladi. */
function isRetryable(error: unknown): boolean {
  if (!isApiError(error)) return true
  return error.status === 0 || error.status === 429 || error.status >= 500
}

export function createLiveEventReporter(
  sessionId: number,
  snapshot: () => { micOn: boolean; cameraOn: boolean },
): LiveEventReporter {
  const client: LiveClientInfo = {
    userAgent: navigator.userAgent,
    telegram: isTelegramWebView(),
    platform: (navigator as Navigator & { userAgentData?: { platform?: string } }).userAgentData?.platform,
  }

  let queue: LiveClientEvent[] = []
  let sending = false
  let disposed = false

  function report(type: string, extra: EventExtra = {}): void {
    if (disposed) return
    const media = snapshot()
    queue.push({
      type,
      at: new Date().toISOString(),
      visibility: document.visibilityState,
      online: navigator.onLine,
      network: networkType(),
      micOn: media.micOn,
      cameraOn: media.cameraOn,
      ...extra,
    })
    if (queue.length > MAX_QUEUE) queue = queue.slice(queue.length - MAX_QUEUE)
  }

  /**
   * ★ `keepalive` YO'LIDA `sending` TEKSHIRILMAYDI: yo'ldagi paket navbatdan
   *   allaqachon olingan, navbatda faqat yuborilmaganlar qolgan — sahifa
   *   yopilayotganda ularni kutib o'tirishga vaqt yo'q.
   */
  function flush(keepalive = false): void {
    if (queue.length === 0) return
    if (sending && !keepalive) return

    const batch = queue.slice(0, MAX_BATCH)
    queue = queue.slice(batch.length)
    if (!keepalive) sending = true

    postLiveClientEvents(sessionId, client, batch, keepalive)
      .catch((error: unknown) => {
        if (!isRetryable(error)) return
        queue = [...batch, ...queue].slice(-MAX_QUEUE)
      })
      .finally(() => {
        if (!keepalive) sending = false
      })
  }

  function onVisibilityChange(): void {
    const hidden = document.visibilityState === 'hidden'
    report(hidden ? 'page-hidden' : 'page-visible')
    // Yashiringan sahifani telefon istalgan lahzada o'ldirishi mumkin.
    if (hidden) flush(true)
  }

  function onPageHide(event: PageTransitionEvent): void {
    report('pagehide', { detail: event.persisted ? 'bfcache' : 'unload' })
    flush(true)
  }

  function onOnline(): void {
    report('online')
    flush()
  }

  function onOffline(): void {
    report('offline')
  }

  const timer = window.setInterval(() => flush(), FLUSH_INTERVAL_MS)
  document.addEventListener('visibilitychange', onVisibilityChange)
  window.addEventListener('pagehide', onPageHide)
  window.addEventListener('online', onOnline)
  window.addEventListener('offline', onOffline)

  function dispose(): void {
    if (disposed) return
    window.clearInterval(timer)
    document.removeEventListener('visibilitychange', onVisibilityChange)
    window.removeEventListener('pagehide', onPageHide)
    window.removeEventListener('online', onOnline)
    window.removeEventListener('offline', onOffline)
    flush(true)
    disposed = true
  }

  return { report, dispose }
}

/*
  ════════════════════════════════════════════════════════════════════════
  EKRAN O'CHMASIN (Screen Wake Lock)
  ════════════════════════════════════════════════════════════════════════

  🔴 NIMA UCHUN: darslarning aksariyati FAQAT OVOZLI — telefonda qaraydigan
     narsa yo'q, ekran 30–60 soniyada o'chadi va qulflanadi. Qulflangan
     telefonda brauzer (ayniqsa Telegram ichidagisi) sahifani to'xtatadi va
     o'quvchi darsdan "o'z-o'zidan" chiqib ketadi.

  ★ Qulf sahifa yashirilganda brauzerning o'zi bo'shatadi, shuning uchun
    sahifa qayta ko'ringanda yana so'raladi. API yo'q brauzerda (eski iOS)
    jimgina hech narsa qilinmaydi — natija diagnostika hodisasida ko'rinadi.
*/

export interface ScreenWakeLock {
  acquire: () => void
  release: () => void
}

export function createScreenWakeLock(onEvent: (detail: string) => void): ScreenWakeLock {
  let sentinel: WakeLockSentinel | null = null
  let wanted = false
  let requesting = false

  const supported = typeof navigator !== 'undefined' && 'wakeLock' in navigator

  function request(): void {
    if (!supported || !wanted || sentinel !== null || requesting) return
    if (document.visibilityState !== 'visible') return
    requesting = true
    navigator.wakeLock
      .request('screen')
      .then((lock) => {
        requesting = false
        if (!wanted) {
          void lock.release().catch(() => undefined)
          return
        }
        sentinel = lock
        lock.addEventListener('release', () => {
          if (sentinel === lock) sentinel = null
        })
        onEvent('acquired')
      })
      .catch((error: unknown) => {
        requesting = false
        onEvent(`error:${error instanceof Error ? error.name : 'unknown'}`)
      })
  }

  function onVisibilityChange(): void {
    if (document.visibilityState === 'visible') request()
  }

  function acquire(): void {
    if (!supported) {
      if (!wanted) onEvent('unsupported')
      wanted = true
      return
    }
    if (!wanted) document.addEventListener('visibilitychange', onVisibilityChange)
    wanted = true
    request()
  }

  function release(): void {
    if (!wanted) return
    wanted = false
    if (supported) document.removeEventListener('visibilitychange', onVisibilityChange)
    const lock = sentinel
    sentinel = null
    if (lock !== null) void lock.release().catch(() => undefined)
  }

  return { acquire, release }
}
