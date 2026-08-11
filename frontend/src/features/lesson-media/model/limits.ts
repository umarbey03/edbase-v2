import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchSettings, SETTINGS_QUERY_KEY } from '@/entities/setting'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatFileSize } from '@/shared/lib/text'
import type { LessonAssetKindName, SettingsPageDto } from '@/shared/types'

/**
 * ========================================================================
 * YUKLASH CHEGARALARI — SOZLAMALARDAN, KODDA QOTMAYDI
 * ========================================================================
 *
 * Server chegarani `AppSetting` registridan oladi:
 *   `lesson.video_max_mb` (standart 1024, maksimum 2048)
 *   `lesson.image_max_mb` (standart 10, maksimum 100)
 * va oshsa **413** qaytaradi. Klient AYNI qiymatni oldindan tekshiradi —
 * shunda 1.5 GB fayl yigirma daqiqa yuklanib, oxirida rad etilmaydi.
 *
 * ── 🔴 TOPSHIRIQDAGI XATO (hisobotda ham qayd etilgan) ─────────────────
 *
 * Brif "ular `GET /api/v1/settings` da `Content` guruhida (`Academic`/`Admin`
 * uchun)" deydi. AMALDA `SettingsController` da sinf darajasida
 * `[Authorize(Roles = "Admin")]` turadi va izohida shunday yozilgan: *"🔴
 * FAQAT `Admin`. `Academic` ham kirmaydi — bu ataylab"* (eski tizimning eng
 * og'ir zaifligi `academic` rolining ortiqcha huquqidan boshlangan). Ya'ni
 * dars kontentini tahrirlay oladigan IKKI roldan BIRI (`Academic`) sozlamani
 * O'QIY OLMAYDI — 403 oladi.
 *
 * SHUNING UCHUN:
 *   • so'rov FAQAT `Admin` uchun yuboriladi (Academic'ga kafolatlangan 403
 *     so'rovini har drawer ochilishida yuborishning ma'nosi yo'q);
 *   • qolgan hollarda registrdagi STANDART qiymat ishlatiladi va UI buni
 *     OSHKORA aytadi ("chegara taxminiy") — jimgina "bilamiz" deb ko'rsatish
 *     administrator chegarani pasaytirgan holatda yolg'on bo'lardi;
 *   • YAKUNIY qaror baribir SERVERDA: klient tekshiruvi faqat ARZON
 *     to'siq, qoidaning nusxasi emas.
 *
 * To'g'ri yechim (backend ishi, hisobotda taklif qilingan): kichik
 * `GET /api/v1/lessons/upload-limits` endpointi yoki `Content` guruhini
 * `Academic` ga ochish.
 */

/** Registrdagi standart qiymatlar (`SettingsRegistry`). */
export const FALLBACK_VIDEO_MAX_MB = 1024
export const FALLBACK_IMAGE_MAX_MB = 10

const VIDEO_KEY = 'lesson.video_max_mb'
const IMAGE_KEY = 'lesson.image_max_mb'

function readMegabytes(page: SettingsPageDto | undefined, key: string, fallback: number): number {
  if (page === undefined) return fallback

  for (const group of page.groups) {
    for (const item of group.items) {
      if (item.key !== key) continue
      const raw = item.value ?? item.defaultValue ?? ''
      const parsed = Number(raw)
      // Buzuq qiymat ilovani yiqitmaydi — standartga qaytadi (server ham
      // aynan shunday qiladi: `LessonAssetService.LimitBytesAsync`).
      return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback
    }
  }
  return fallback
}

export interface UploadLimits {
  videoMaxMb: ComputedRef<number>
  imageMaxMb: ComputedRef<number>
  /** `true` — qiymatlar sozlamalardan EMAS, registr standartidan olingan. */
  isApproximate: ComputedRef<boolean>
  /** Bitta dars faylining chegarasi (bayt). */
  limitBytes: (kind: LessonAssetKindName) => number
  /**
   * Yuborishdan OLDINGI tekshiruv: xato matni yoki `null`.
   * Bo'sh fayl ham shu yerda to'siladi (server `400 file` beradi).
   */
  assetSizeError: (file: File, kind: LessonAssetKindName) => string | null
  /** Vazifa SHARTI biriktirmasi — server u uchun `lesson.image_max_mb` ni qo'llaydi. */
  attachmentSizeError: (file: File) => string | null
}

export function useUploadLimits(): UploadLimits {
  const auth = useAuthStore()

  /*
    Kesh KALITI sozlamalar sahifasi bilan AYNI (`SETTINGS_QUERY_KEY`):
    administrator sozlamalar panelida chegarani o'zgartirsa, u yerda kesh
    yangilanadi va bu drawer ham DARHOL yangi qiymatni ko'radi. Alohida kalit
    olsak, ikki nusxa bir-biridan farq qilib turardi.

    `retry: false` — 403 ni qayta-qayta so'rashning ma'nosi yo'q.
  */
  const settingsQuery = useQuery({
    queryKey: SETTINGS_QUERY_KEY,
    queryFn: ({ signal }) => fetchSettings({ signal }),
    enabled: computed(() => auth.role === 'Admin'),
    staleTime: 5 * 60_000,
    retry: false,
  })

  const videoMaxMb = computed(() =>
    readMegabytes(settingsQuery.data.value, VIDEO_KEY, FALLBACK_VIDEO_MAX_MB),
  )
  const imageMaxMb = computed(() =>
    readMegabytes(settingsQuery.data.value, IMAGE_KEY, FALLBACK_IMAGE_MAX_MB),
  )
  const isApproximate = computed(() => settingsQuery.data.value === undefined)

  function limitBytes(kind: LessonAssetKindName): number {
    const megabytes = kind === 'Image' ? imageMaxMb.value : videoMaxMb.value
    return megabytes * 1024 * 1024
  }

  function sizeError(file: File, limit: number, what: string): string | null {
    if (file.size <= 0) return 'Fayl bo‘sh.'
    if (file.size <= limit) return null
    return (
      `${what} hajmi ${formatFileSize(limit)} dan oshmasligi kerak — `
      + `bu fayl ${formatFileSize(file.size)}. Fayl serverga YUBORILMADI.`
    )
  }

  return {
    videoMaxMb,
    imageMaxMb,
    isApproximate,
    limitBytes,
    assetSizeError: (file, kind) =>
      sizeError(file, limitBytes(kind), kind === 'Image' ? 'Rasm' : 'Video'),
    attachmentSizeError: (file) => sizeError(file, limitBytes('Image'), 'Fayl'),
  }
}
