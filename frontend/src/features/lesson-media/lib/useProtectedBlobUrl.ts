import { useQuery } from '@tanstack/vue-query'
import { computed, onScopeDispose, ref, watch } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { toUserMessage } from '@/shared/api'
import type { DownloadedFile } from '@/shared/api'

/**
 * ========================================================================
 * HIMOYALANGAN FAYLNI KO'RSATISH (`Authorization` + Blob URL)
 * ========================================================================
 *
 * MUAMMO: media endpointlari (`/lessons/assets/{id}`,
 * `/assignments/attachments/{id}`, `/submissions/files/{id}`) `Authorization`
 * sarlavhasini TALAB qiladi, brauzer esa `<img src>` / `<audio src>`
 * so'rovlarida uni YUBORMAYDI — oddiy havola doim 401 olardi.
 *
 * YECHIM (loyihada allaqachon isbotlangan naqsh — `SubmissionAttachment.vue`):
 * fayl token bilan Blob sifatida olinadi va `URL.createObjectURL` bilan
 * ko'rsatiladi.
 *
 * ★ SHU FAYL NIMA UCHUN BOR: naqsh IKKI YANGI joyda kerak bo'ldi (imtihon
 * rasmi va vazifa sharti biriktirmasi). Uni har joyda qayta yozish —
 * `revokeObjectURL` ni bir joyda unutish demakdir, ya'ni xotira oqishi.
 *
 * ⚠️ TO'G'RI JOYI `shared/lib/useProtectedBlobUrl.ts` BO'LARDI (uni
 * `entities/assignment/ui/SubmissionAttachment.vue` ham ishlatishi kerak),
 * lekin bu topshiriqda `shared/lib/**` ga tegish TAQIQLANGAN. Ko'chirish
 * hisobotda alohida ish sifatida taklif qilingan.
 *
 * 🔴 XOTIRA: `createObjectURL` Blob'ni brauzerda USHLAB TURADI va sahifa
 * yopilgunicha o'zi bo'shalmaydi. Shuning uchun manzil (1) fayl almashganda,
 * (2) skoup yo'q qilinganda darhol bekor qilinadi. Ustoz 50 ta ishni ketma-ket
 * ko'rsa, tozalashsiz seans oxirida brauzer yuzlab megabayt ushlab turardi.
 *
 * 🔴 VIDEO UCHUN ISHLATILMAYDI: 1 GB fayl butunlay xotiraga tushardi va
 * `Range` (seek) ma'nosini yo'qotardi. Video pleyeri qisqa muddatli asset
 * tokeni bilan alohida vazifada quriladi.
 */

export interface ProtectedBlob {
  /** `null` — hali yuklanmagan yoki xato. */
  url: Ref<string | null>
  fileName: ComputedRef<string>
  blob: ComputedRef<Blob | null>
  isPending: ComputedRef<boolean>
  isFetching: ComputedRef<boolean>
  errorMessage: ComputedRef<string | null>
  refetch: () => void
}

export function useProtectedBlobUrl(
  cacheKey: string,
  id: () => number | null,
  fetcher: (id: number, options: { signal?: AbortSignal }) => Promise<DownloadedFile>,
): ProtectedBlob {
  const enabled = computed(() => id() !== null)

  /*
    Blob TanStack Query keshida yashaydi: bitta fayl ikki joyda ochilsa
    QAYTA yuklanmaydi. `staleTime: Infinity` — fayl mazmuni o'zgarmaydi;
    `gcTime` esa ATAYLAB qisqa, chunki kesh Blob'larni ushlab turadi.
  */
  const query = useQuery({
    queryKey: computed(() => [cacheKey, id()]),
    queryFn: ({ signal }) => {
      const current = id()
      // `enabled` tufayli bu holat yuz bermaydi; tur xavfsizligi uchun.
      if (current === null) throw new Error('Fayl tanlanmagan.')
      return fetcher(current, { signal })
    },
    enabled,
    staleTime: Number.POSITIVE_INFINITY,
    gcTime: 2 * 60_000,
    retry: false,
  })

  const url = ref<string | null>(null)

  function release(): void {
    if (url.value === null) return
    URL.revokeObjectURL(url.value)
    url.value = null
  }

  watch(
    () => query.data.value,
    (downloaded) => {
      // Eski manzil AVVAL bekor qilinadi — aks holda oldingi faylning Blob'i
      // xotirada qolib ketardi.
      release()
      if (downloaded !== undefined) url.value = URL.createObjectURL(downloaded.blob)
    },
    { immediate: true },
  )

  onScopeDispose(release)

  return {
    url,
    fileName: computed(() => query.data.value?.fileName ?? 'fayl'),
    blob: computed(() => query.data.value?.blob ?? null),
    isPending: computed(() => enabled.value && query.isPending.value),
    isFetching: computed(() => query.isFetching.value),
    errorMessage: computed(() =>
      query.error.value !== null ? toUserMessage(query.error.value) : null,
    ),
    refetch: () => {
      void query.refetch()
    },
  }
}
