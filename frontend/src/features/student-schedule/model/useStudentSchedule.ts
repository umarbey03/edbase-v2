import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import type { ComputedRef, Ref } from 'vue'

import { fetchLiveSessions } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import type { LiveSessionDto, SessionTypeName } from '@/shared/types'

/**
 * O'quvchining dars jadvali — bosh sahifa, kalendar va appbar chipi UCHALASI
 * ham shu yerdan oziqlanadi.
 *
 * BITTA SO'ROV: `queryKey` uchala joyda bir xil, TanStack Query esa bir xil
 * kalitli so'rovni birlashtiradi — 5 ta tab orasida yurganda server har safar
 * qayta so'ralmaydi.
 *
 * ★ SERVER CHEGARASI (`LiveSessionService.ListForUserAsync`):
 *   • `GET /api/v1/live-sessions` HECH QANDAY parametr qabul qilmaydi;
 *   • javobda FAQAT `scheduledEnd >= now - 6 soat` bo'lgan darslar bo'ladi;
 *   • `Cancelled` darslar umuman kelmaydi; ro'yxat 100 ta bilan cheklangan.
 * Ya'ni O'TGAN OYLAR jadvali bu endpointdan OLINMAYDI. Kalendar shuni
 * foydalanuvchiga ochiq aytadi, o'ylab topilgan ma'lumot chizmaydi.
 */

/** Eski ilovadagi `lessonState()` ning aynan mantiqi. */
export type SessionState = 'live' | 'upcoming' | 'past'

/** Eski ilova darsni boshlanishidan 5 daqiqa oldin "jonli" deb ko'rsatardi. */
const LIVE_LEAD_MS = 5 * 60 * 1000

/** Eski ilovada `canEnter`: kirish tugmasi 15 daqiqa qolganda ochiladi. */
const JOIN_WINDOW_MS = 15 * 60 * 1000

export function sessionState(session: LiveSessionDto, now: Date): SessionState {
  if (session.status === 'Live') return 'live'
  if (session.status === 'Ended' || session.status === 'Cancelled') return 'past'

  const start = new Date(session.scheduledStart).getTime()
  const end = new Date(session.scheduledEnd).getTime()
  if (Number.isNaN(start) || Number.isNaN(end)) return 'upcoming'

  const ms = now.getTime()
  if (ms >= start - LIVE_LEAD_MS && ms <= end) return 'live'
  if (ms > end) return 'past'
  return 'upcoming'
}

/** Darsga kirish tugmasi faolmi (eski ilovadagi 15 daqiqalik oyna). */
export function canJoin(session: LiveSessionDto, now: Date): boolean {
  if (sessionState(session, now) === 'live') return true
  const start = new Date(session.scheduledStart).getTime()
  if (Number.isNaN(start)) return false
  return start - now.getTime() <= JOIN_WINDOW_MS
}

export interface ScheduleGroup {
  id: number
  name: string
}

export interface StudentSchedule {
  sessions: ComputedRef<LiveSessionDto[]>
  /** Guruh tanlash chiplari uchun — jadvalda uchragan guruhlar (takrorsiz). */
  groups: ComputedRef<ScheduleGroup[]>
  /** Ustoz darsi bo'yicha keyingisi (jonli bo'lsa — jonlisi birinchi). */
  nextTeacher: ComputedRef<LiveSessionDto | null>
  /** Kurator darsi bo'yicha keyingisi. */
  nextAssistant: ComputedRef<LiveSessionDto | null>
  /** Appbar chipi ko'rsatadigan dars: jonli bo'lsa jonlisi, aks holda eng yaqini. */
  nextAny: ComputedRef<LiveSessionDto | null>
  isPending: ComputedRef<boolean>
  isFetching: ComputedRef<boolean>
  error: ComputedRef<string | null>
  refetch: () => void
}

function byStartAsc(a: LiveSessionDto, b: LiveSessionDto): number {
  return new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime()
}

export function useStudentSchedule(now: Ref<Date>): StudentSchedule {
  const query = useQuery({
    queryKey: ['live-sessions'],
    queryFn: ({ signal }) => fetchLiveSessions({ signal }),
    /*
      60 soniya "yangi" hisoblanadi. Tabdan tabga o'tish sekundlar ichida
      bo'ladi — har o'tishda serverga borish mobil internetda sezilarli
      kechikish berardi, jadval esa daqiqalar davomida o'zgarmaydi.
    */
    staleTime: 60_000,
  })

  const sessions = computed(() => query.data.value ?? [])

  const groups = computed<ScheduleGroup[]>(() => {
    const seen = new Map<number, string>()
    for (const item of sessions.value) {
      if (!seen.has(item.groupId)) seen.set(item.groupId, item.groupName)
    }
    return [...seen].map(([id, name]) => ({ id, name }))
  })

  /** Hali tugamagan darslar, boshlanish vaqti bo'yicha. */
  const future = computed(() =>
    sessions.value.filter((item) => sessionState(item, now.value) !== 'past').sort(byStartAsc),
  )

  function pick(type: SessionTypeName): LiveSessionDto | null {
    const ofType = future.value.filter((item) => item.type === type)
    // Eski ilovadagidek: jonli dars boshqalardan ustun.
    return ofType.find((item) => sessionState(item, now.value) === 'live') ?? ofType[0] ?? null
  }

  const nextTeacher = computed(() => pick('Teacher'))
  const nextAssistant = computed(() => pick('Assistant'))
  const nextAny = computed(
    () =>
      future.value.find((item) => sessionState(item, now.value) === 'live')
      ?? future.value[0]
      ?? null,
  )

  return {
    sessions,
    groups,
    nextTeacher,
    nextAssistant,
    nextAny,
    isPending: computed(() => query.isPending.value),
    isFetching: computed(() => query.isFetching.value),
    error: computed(() =>
      query.error.value !== null ? toUserMessage(query.error.value) : null,
    ),
    refetch: () => {
      void query.refetch()
    },
  }
}
