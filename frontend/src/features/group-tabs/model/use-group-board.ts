import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef } from 'vue'

import { fetchGroupLeaderboard } from '@/entities/leaderboard'
import { isApiError } from '@/shared/api'
import type { LeaderboardRowDto } from '@/shared/types'

/**
 * Guruhning JORIY OYLIK jadvali (`GET /leaderboard/groups/{id}`).
 *
 * Uni IKKI tab o'qiydi — "Reyting" va "Davomat": v2 da guruh kesimidagi
 * o'quvchi-davomat foizini beradigan YAGONA endpoint shu (batafsil
 * `AttendanceTab` izohida).
 *
 * ★ RUXSAT: server faqat guruhning O'Z ustozi/kuratori (`Group.IsStaff`),
 * faol a'zosi va o'quv bo'limiga ruxsat beradi. Kurator guruhi orqali
 * BOG'LANGAN kurator bu ro'yxatda YO'Q va 403 oladi — shuning uchun xato
 * matni alohida yumshatiladi, aks holda ekranda quruq "Ruxsat yo'q"
 * turardi va sabab tushunarsiz bo'lardi.
 */
export interface GroupBoard {
  rows: ComputedRef<LeaderboardRowDto[]>
  period: ComputedRef<string>
  pending: ComputedRef<boolean>
  fetching: ComputedRef<boolean>
  errorMessage: ComputedRef<string | null>
  refetch: () => void
}

export function useGroupBoard(groupId: number): GroupBoard {
  const query = useQuery({
    queryKey: ['leaderboard', 'group', groupId],
    queryFn: ({ signal }) => fetchGroupLeaderboard(groupId, undefined, { signal }),
    // Server natijani 60 sekund keshlaydi — klientda ham shuncha ushlaymiz.
    staleTime: 60_000,
  })

  return {
    rows: computed(() => query.data.value?.rows ?? []),
    period: computed(() => query.data.value?.period ?? ''),
    pending: computed(() => query.isPending.value),
    fetching: computed(() => query.isFetching.value),
    errorMessage: computed(() => {
      const error = query.error.value
      if (error === null) return null
      if (isApiError(error) && error.isForbidden) {
        return `${error.userMessage} Oylik ko‘rsatkichlarni guruhning ustozi, unga bevosita biriktirilgan kurator va o‘quv bo‘limi ko‘ra oladi.`
      }
      return isApiError(error) ? error.userMessage : error.message
    }),
    refetch: () => {
      void query.refetch()
    },
  }
}
