import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import {
  defaultRecordingRange,
  fetchRecordings,
  isRecordingInProgress,
  recordingItemTitle,
  validateRecordingRange,
} from '@/entities/recording'
import type { RecordingListItem } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'

/**
 * ============================================================================
 *  YOZUVLAR RO'YXATI — `GET /api/v1/recordings`
 * ============================================================================
 *
 * ★ NIMA UCHUN FILTRLARNING BIR QISMI MIJOZDA.
 * Server FAQAT sana oralig'ini biladi (`from`, `to`) — guruh yoki matn
 * bo'yicha filtr YO'Q (jonli tekshirilgan swagger shartnomasi). Eski ilovada
 * esa uchala filtr ham bor edi: guruh ro'yxati serverga borardi, qidiruv va
 * dars turi esa AYNAN mijozda ishlardi (`filterGlobalRecordings()`).
 * Shuning uchun bu yerda guruh filtri ham mijozga o'tkazildi: bitta oyna
 * ichidagi yozuvlar soni o'nlab, yuzlab emas — sahifalash shart emas.
 *
 * ★ DARS TURI FILTRI (eski "Ustoz darsi / Yordamchi darsi") YO'Q: v2 ro'yxat
 * qatorida dars TURI umuman kelmaydi. Ishlamaydigan tanlagichni chizishdan
 * ko'ra uni ko'rsatmagan ma'qul.
 */

/** Yozuv davom etayotgan bo'lsa ro'yxat shu oraliqda o'zi yangilanadi. */
const IN_PROGRESS_REFETCH_MS = 15_000

export interface UseRecordingListOptions {
  /**
   * Guruh bo'yicha OLDINDAN cheklash (guruh sahifasidagi "Yozuvlar" tabi).
   * `null` — barcha guruhlar, foydalanuvchi o'zi tanlaydi.
   */
  fixedGroupId?: number | null
}

export interface UseRecordingListResult {
  from: Ref<string>
  to: Ref<string>
  search: Ref<string>
  groupId: Ref<number | null>
  /** Sana oralig'i xatosi (serverga so'rov YUBORILMAYDI). */
  rangeError: ComputedRef<string | null>
  items: ComputedRef<RecordingListItem[]>
  /** Filtrlardan OLDINGI ro'yxatdagi guruhlar — "Barcha guruhlar" tanlagichi uchun. */
  groupOptions: ComputedRef<{ id: number; name: string }[]>
  isPending: ComputedRef<boolean>
  isFetching: ComputedRef<boolean>
  errorMessage: ComputedRef<string | null>
  refetch: () => void
}

export function useRecordingList(options: UseRecordingListOptions = {}): UseRecordingListResult {
  const initial = defaultRecordingRange()
  const from = ref(initial.from)
  const to = ref(initial.to)
  const search = ref('')
  const groupId = ref<number | null>(options.fixedGroupId ?? null)

  const range = computed(() => ({ from: from.value, to: to.value }))
  const rangeError = computed(() => validateRecordingRange(range.value))

  const recordingsQuery = useQuery({
    queryKey: ['recordings', 'list', range],
    queryFn: ({ signal }) => fetchRecordings(range.value, { signal }),
    /*
      Buzuq oraliq serverga YUBORILMAYDI. Aks holda `<input type="date">` da
      yilni tahrirlash paytida ("202" -> "2026") har bosishda 400 kelardi va
      ekranda xato miltillab turardi.
    */
    enabled: computed(() => rangeError.value === null),
    /*
      Yozuv tugagach `status` "Active" dan "Completed" ga o'tadi. Buni faqat
      qayta so'rov ko'radi — bunday o'zgarish uchun SignalR hodisasi yo'q.

      Ma'lumot `query.state.data` dan olinadi, `recordingsQuery.data` dan EMAS:
      o'z o'zgaruvchisiga uning e'lonidan oldin murojaat qilib bo'lmaydi (TDZ).
      Hech narsa yozilmayotgan bo'lsa taymer umuman ishlamaydi — ochiq turgan
      sahifa serverni bekorga bezovta qilmaydi.
    */
    refetchInterval: (query) =>
      (query.state.data ?? []).some((item) => isRecordingInProgress(item.recording))
        ? IN_PROGRESS_REFETCH_MS
        : false,
  })

  const all = computed(() => recordingsQuery.data.value ?? [])

  const groupOptions = computed(() => {
    const map = new Map<number, string>()
    for (const item of all.value) {
      if (map.has(item.groupId)) continue
      map.set(item.groupId, item.groupName ?? `#${item.groupId}`)
    }
    return [...map.entries()]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name))
  })

  const items = computed(() => {
    const query = search.value.trim().toLocaleLowerCase()
    const selectedGroup = groupId.value

    return all.value.filter((item) => {
      if (selectedGroup !== null && item.groupId !== selectedGroup) return false
      if (query.length === 0) return true
      // Eski ilovada qidiruv nom VA ustoz bo'yicha edi; ustoz maydoni yo'q,
      // shuning uchun uning o'rnida GURUH nomi qidiriladi.
      const haystack =
        `${recordingItemTitle(item)} ${item.groupName ?? ''}`.toLocaleLowerCase()
      return haystack.includes(query)
    })
  })

  const errorMessage = computed(() =>
    recordingsQuery.error.value !== null ? toUserMessage(recordingsQuery.error.value) : null,
  )

  return {
    from,
    to,
    search,
    groupId,
    rangeError,
    items,
    groupOptions,
    isPending: computed(() => recordingsQuery.isPending.value),
    isFetching: computed(() => recordingsQuery.isFetching.value),
    errorMessage,
    refetch: () => {
      void recordingsQuery.refetch()
    },
  }
}
