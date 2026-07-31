import { onBeforeUnmount, ref } from 'vue'
import type { Ref } from 'vue'

import { fetchRecordingLink } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'

/**
 * ============================================================================
 *  YOZUV HAVOLASI — MUDDATLI (PRESIGNED) MANZILNI BOSHQARISH
 * ============================================================================
 *
 * ★ NEGA BLOB YOKI `http.download()` EMAS.
 * Loyihada vazifa fayllari uchun API orqali oqim tanlangan, chunki u yerda
 * `Authorization` sarlavhasi majburiy. VIDEO UCHUN QAROR BOSHQACHA va u
 * jonli tekshirilgan: `GET /api/v1/recordings/{id}/link` S3 ning IMZOLANGAN
 * manzilini qaytaradi (`X-Amz-Signature`, `X-Amz-Expires=900`). Bunday manzil
 * sarlavha talab qilmaydi, ya'ni uni to'g'ridan-to'g'ri `<video src>` ga berish
 * mumkin va shu ma'qul:
 *   • 1 GB lik darsni blob'ga yuklab olish brauzer xotirasini to'ldirardi;
 *   • `<video>` faqat kerakli qismni so'raydi (Range) — oldinga tashlash
 *     bir zumda ishlaydi, blob'da esa butun fayl kutilardi.
 * Blob ishlatilmagani uchun `URL.createObjectURL`/`revokeObjectURL` juftligi
 * ham bu yerda YO'Q — bo'shatishni unutish xavfi ham yo'q.
 *
 * ★ EVAZIGA MUAMMO: manzil ESKIRADI. Oyna 20 daqiqa ochiq tursa (uzun darsni
 * ko'rayotgan xodim) manzil kuchini yo'qotadi va video o'rtasida to'xtaydi.
 * Shuning uchun bu yerda `expiresAt` saqlanadi va `isFresh()` bilan har
 * ochishdan oldin tekshiriladi; eskirgan bo'lsa QAYTA so'raladi.
 */

/**
 * Xavfsizlik zaxirasi: muddat tugashiga shuncha qolganda manzil "eskirgan"
 * hisoblanadi. Server 15 daqiqa beradi; 60 soniya — tarmoq sekin bo'lsa ham
 * so'rov ulgurishi uchun yetarli, lekin ortiqcha so'rov ham tug'dirmaydi.
 */
const FRESHNESS_MARGIN_MS = 60_000

export interface UseRecordingLinkResult {
  url: Ref<string | null>
  pending: Ref<boolean>
  error: Ref<string | null>
  /** Havolani oladi (kerak bo'lsa qayta so'raydi) va manzilni qaytaradi. */
  load: (recordingId: number, force?: boolean) => Promise<string | null>
  reset: () => void
}

export function useRecordingLink(): UseRecordingLinkResult {
  const url = ref<string | null>(null)
  const pending = ref(false)
  const error = ref<string | null>(null)

  /* Reaktiv BO'LMAGAN holat: shablon bu qiymatlarni ko'rmaydi. */
  let expiresAtMs = 0
  let loadedFor: number | null = null
  /**
   * Komponent yo'q qilingandan keyin kech kelgan javob `ref` larni
   * yangilamasligi uchun.
   */
  let disposed = false

  function isFresh(recordingId: number): boolean {
    if (loadedFor !== recordingId || url.value === null) return false
    return Date.now() < expiresAtMs - FRESHNESS_MARGIN_MS
  }

  async function load(recordingId: number, force = false): Promise<string | null> {
    if (!force && isFresh(recordingId)) return url.value

    pending.value = true
    error.value = null
    try {
      const link = await fetchRecordingLink(recordingId)
      if (disposed) return null

      const fresh = link.url
      if (fresh === null || fresh.length === 0) {
        // Server 200 qaytarib, manzilni bermasligi — kutilmagan holat.
        // Bo'sh pleyer o'rniga sabab yozamiz.
        error.value = 'Yozuv havolasi bo‘sh keldi. O‘quv bo‘limiga xabar bering.'
        return null
      }

      const expiry = new Date(link.expiresAt).getTime()
      // Server sanani noto'g'ri yuborsa ham havola YAROQSIZ deb belgilanmasin:
      // bunday holda uni bir martalik deb hisoblaymiz (keyingi safar qayta so'raladi).
      expiresAtMs = Number.isNaN(expiry) ? 0 : expiry
      loadedFor = recordingId
      url.value = fresh
      return fresh
    } catch (cause) {
      if (disposed) return null
      /*
        `toUserMessage` shu yerda yagona manba:
          • 403 — qarzdor o'quvchi (`PaymentBlockService`) yoki begona dars:
            server `detail` da SABABNI yozadi va u o'zgarishsiz ko'rsatiladi;
          • 409 — yozuv hali tayyor emas;
          • 503 — fayl ombori sozlanmagan/ishlamayapti;
          • 404 — yozuv o'chirilgan.
        Matnni o'zimiz yig'sak, serverning aniq maslahati yo'qolardi.
      */
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
