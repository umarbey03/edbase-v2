import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { ref } from 'vue'
import type { Ref } from 'vue'
import { useRouter } from 'vue-router'

import { fetchGroupSchedule } from '@/entities/group'
import { startLiveSession } from '@/entities/session'
import { toUserMessage } from '@/shared/api'

/**
 * Guruhning butun dars jadvali.
 *
 * Kalit `TeacherGroupPage` dagi bilan BIR XIL (`['group', id, 'schedule']`) —
 * "keyingi dars" banneri, kalendar va sahifaning o'zi bitta so'rovdan
 * foydalanadi (TanStack Query dedupe qiladi).
 */
export function useGroupSchedule(groupId: number) {
  return useQuery({
    queryKey: ['group', groupId, 'schedule'],
    queryFn: ({ signal }) => fetchGroupSchedule(groupId, { signal }),
  })
}

export interface SessionStartControls {
  start: (sessionId: number) => void
  openRoom: (sessionId: number) => void
  pendingId: Ref<number | null>
  error: Ref<string | null>
}

/**
 * "Darsni boshlash" va "Darsga qaytish" amallari.
 *
 * Boshlash MUVAFFAQIYATLI bo'lsa darhol jonli xonaga o'tiladi — eski ilova
 * ham shunday qilardi (`window.location.href = '/live/' + id`). Xatoda
 * sahifada QOLAMIZ va sababni ko'rsatamiz (masalan 409: "dars vaqti
 * kelmagan").
 */
export function useSessionStart(groupId: number): SessionStartControls {
  const router = useRouter()
  const queryClient = useQueryClient()

  const error = ref<string | null>(null)
  const pendingId = ref<number | null>(null)

  const mutation = useMutation({
    mutationFn: (sessionId: number) => startLiveSession(sessionId),
    onSuccess: (session) => {
      error.value = null
      void queryClient.invalidateQueries({ queryKey: ['group', groupId, 'schedule'] })
      void queryClient.invalidateQueries({ queryKey: ['live-sessions'] })
      void router.push({ name: 'live-room', params: { sessionId: String(session.id) } })
    },
    onError: (mutationError: Error) => {
      error.value = toUserMessage(mutationError)
    },
    onSettled: () => {
      pendingId.value = null
    },
  })

  return {
    start(sessionId: number): void {
      pendingId.value = sessionId
      mutation.mutate(sessionId)
    },
    openRoom(sessionId: number): void {
      void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
    },
    pendingId,
    error,
  }
}
