import { onBeforeUnmount, ref } from 'vue'
import type { Ref } from 'vue'

import { fetchLessonAssetTicket, lessonAssetTicketUrl } from '@/entities/course'
import { toUserMessage } from '@/shared/api'

/**
 * ============================================================================
 *  DARS VIDEOSI CHIPTASI — `useRecordingLink` BILAN AYNI NAQSH
 * ============================================================================
 *
 * Farqi FAQAT manba turida: yozuv S3 presigned URL bilan ishlaydi (havolaning
 * o'zi muddatli), dars mediasi esa `?ticket=` so'rov parametri bilan
 * (`LessonAssetsController.Ticket` — imzo `assetId`ga bog'langan). Ikkalasi
 * ham ~15 daqiqada eskiradi va ikkalasida ham `expiresAt` kuzatiladi.
 */

const FRESHNESS_MARGIN_MS = 60_000

export interface UseLessonAssetTicketResult {
  url: Ref<string | null>
  pending: Ref<boolean>
  error: Ref<string | null>
  /** Chiptani oladi (kerak bo'lsa qayta so'raydi) va `<video src>` manzilini qaytaradi. */
  load: (assetId: number, force?: boolean) => Promise<string | null>
  reset: () => void
}

export function useLessonAssetTicket(): UseLessonAssetTicketResult {
  const url = ref<string | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  let expiresAtMs = 0
  let loadedFor: number | null = null
  let disposed = false

  function isFresh(assetId: number): boolean {
    if (loadedFor !== assetId || url.value === null) return false
    return Date.now() < expiresAtMs - FRESHNESS_MARGIN_MS
  }

  async function load(assetId: number, force = false): Promise<string | null> {
    if (!force && isFresh(assetId)) return url.value

    pending.value = true
    error.value = null
    try {
      const ticket = await fetchLessonAssetTicket(assetId)
      if (disposed) return null

      const expiry = new Date(ticket.expiresAt).getTime()
      expiresAtMs = Number.isNaN(expiry) ? 0 : expiry
      loadedFor = assetId
      url.value = lessonAssetTicketUrl(assetId, ticket.token)
      return url.value
    } catch (cause) {
      if (disposed) return null
      // 403 — qulflangan dars yoki to'lov qarzi (server sababni yozadi); 404 — o'chirilgan.
      error.value = toUserMessage(cause)
      url.value = null
      loadedFor = null
      return null
    } finally {
      if (!disposed) pending.value = false
    }
  }

  function reset(): void {
    url.value = null
    error.value = null
    pending.value = false
    expiresAtMs = 0
    loadedFor = null
  }

  onBeforeUnmount(() => {
    disposed = true
    reset()
  })

  return { url, pending, error, load, reset }
}
