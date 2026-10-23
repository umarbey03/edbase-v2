import { computed, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import type { ComputedRef, Ref, ShallowRef } from 'vue'

import { toUserMessage } from '@/shared/api'
import { showToast } from '@/shared/lib/useToast'

import {
  closeBreakouts,
  fetchBreakoutJoin,
  fetchBreakouts,
  moveBreakoutStudent,
  setBreakoutHelp,
  startBreakouts,
} from '../api/breakout-api'
import type { BreakoutStateDto } from '../api/breakout-api'

/** `useLiveKitRoom` dan kerakli qism — xona almashtirish. */
export interface BreakoutMediaPort {
  switchRoom: (
    key: number | null,
    fetchJoin: ((signal: AbortSignal) => ReturnType<typeof fetchBreakoutJoin>) | null,
  ) => Promise<void>
  currentRoomKey: Ref<number | null>
}

export interface UseBreakoutsResult {
  state: ShallowRef<BreakoutStateDto | null>
  active: ComputedRef<boolean>
  canManage: ComputedRef<boolean>
  /** Taymer qoldig'i (ms); taymer yo'q bo'lsa `null`. */
  remainingMs: ComputedRef<number | null>
  busy: Ref<boolean>
  /** O'quvchi "ustozni chaqirish" ni bosgan. */
  helpRaised: Ref<boolean>
  refresh: () => Promise<void>
  start: (durationMinutes: number | null) => Promise<void>
  move: (studentId: number, room: number) => Promise<void>
  close: () => Promise<void>
  /** Ustoz: guruhga kirish (`null` — asosiy xonaga qaytish). */
  visit: (room: number | null) => Promise<void>
  toggleHelp: () => Promise<void>
}

/** Hub signali yetib kelmagan holat uchun zaxira so'rov oralig'i. */
const POLL_MS = 20_000

/**
 * Ichki guruhlar holati va xona almashtirish.
 *
 * ★ O'QUVCHI O'ZI YURMAYDI: u qaysi xonada bo'lishini SERVER aytadi
 *   (`myRoom`). Holat o'zgarishi bilan bu composable videoni kerakli xonaga
 *   o'tkazadi — guruhlar yopilsa asosiy xonaga qaytaradi.
 * ★ USTOZ ERKIN: u istalgan guruhga kirib-chiqadi (`visit`), holat esa
 *   uni hech qayerga majburan olib bormaydi — faqat guruhlar yopilganda
 *   asosiy xonaga qaytadi.
 */
export function useBreakouts(
  sessionId: number,
  media: BreakoutMediaPort,
  enabled: Ref<boolean>,
): UseBreakoutsResult {
  const state = shallowRef<BreakoutStateDto | null>(null)
  const busy = ref(false)
  const helpRaised = ref(false)
  const nowMs = ref(Date.now())
  let pollTimer: number | null = null
  let clockTimer: number | null = null
  let disposed = false
  let inFlight: Promise<void> | null = null
  let closingOnTimer = false

  const active = computed(() => state.value?.active === true)
  const canManage = computed(() => state.value?.canManage === true)

  const remainingMs = computed<number | null>(() => {
    const endsAt = state.value?.endsAt
    if (!active.value || endsAt === null || endsAt === undefined) return null
    const target = new Date(endsAt).getTime()
    return Number.isNaN(target) ? null : Math.max(0, target - nowMs.value)
  })

  const joinFor = (room: number) => (signal: AbortSignal) => fetchBreakoutJoin(sessionId, room, false, signal)

  async function refresh(): Promise<void> {
    if (disposed || !enabled.value) return
    // Bir vaqtda bitta so'rov: hub signallari ketma-ket kelsa navbat to'planmaydi.
    if (inFlight !== null) return inFlight
    inFlight = (async () => {
      try {
        state.value = await fetchBreakouts(sessionId)
      } catch {
        /* Zaxira so'rov keyingi safar urinadi — darsni xato bilan to'ldirmaymiz. */
      } finally {
        inFlight = null
      }
    })()
    return inFlight
  }

  async function run(work: () => Promise<unknown>): Promise<void> {
    if (busy.value) return
    busy.value = true
    try {
      await work()
    } catch (error) {
      showToast(toUserMessage(error), 'error')
    } finally {
      busy.value = false
    }
  }

  const start = (durationMinutes: number | null): Promise<void> =>
    run(async () => {
      state.value = await startBreakouts(sessionId, durationMinutes)
    })

  const move = (studentId: number, room: number): Promise<void> =>
    run(async () => {
      state.value = await moveBreakoutStudent(sessionId, studentId, room)
    })

  const close = (): Promise<void> =>
    run(async () => {
      await closeBreakouts(sessionId)
      await refresh()
    })

  async function visit(room: number | null): Promise<void> {
    await media.switchRoom(room, room === null ? null : joinFor(room))
  }

  const toggleHelp = (): Promise<void> =>
    run(async () => {
      const next = !helpRaised.value
      await setBreakoutHelp(sessionId, next)
      helpRaised.value = next
      await refresh()
    })

  /* ------------------------ holat -> qaysi xonada bo'lish ------------------ */

  watch(state, (current) => {
    if (current === null) return
    const here = media.currentRoomKey.value

    if (!current.active) {
      helpRaised.value = false
      closingOnTimer = false
      if (here !== null) void media.switchRoom(null, null)
      return
    }

    // Ustoz kirgan guruhi o'chirilgan bo'lsa (masalan, raqami endi yo'q) — asosiyga.
    if (current.canManage) {
      if (here !== null && !current.rooms.some((room) => room.number === here)) {
        void media.switchRoom(null, null)
      }
      return
    }

    const target = current.myRoom
    if (target !== here) void media.switchRoom(target, target === null ? null : joinFor(target))

    // Ustoz kirdi — server belgini o'chirdi; tugma holati ham shunga ergashadi.
    const own = current.rooms[0]
    if (own !== undefined && !own.helpRequested) helpRaised.value = false
  })

  /* ------------------------------- taymer ---------------------------------- */

  watch(remainingMs, (remaining) => {
    if (remaining === null || remaining > 0) return
    // Ustoz klienti guruhlarni yopadi; qolganlar holatni so'raydi — server
    // muddati o'tgan holatni o'zi ham yopadi (ustoz brauzeri yopiq bo'lsa).
    if (canManage.value) {
      if (closingOnTimer) return
      closingOnTimer = true
      void close()
    } else {
      void refresh()
    }
  })

  watch(
    enabled,
    (on) => {
      if (pollTimer !== null) window.clearInterval(pollTimer)
      if (clockTimer !== null) window.clearInterval(clockTimer)
      pollTimer = null
      clockTimer = null
      if (!on) return
      void refresh()
      pollTimer = window.setInterval(() => void refresh(), POLL_MS)
      clockTimer = window.setInterval(() => {
        nowMs.value = Date.now()
      }, 1_000)
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    disposed = true
    if (pollTimer !== null) window.clearInterval(pollTimer)
    if (clockTimer !== null) window.clearInterval(clockTimer)
  })

  return {
    state,
    active,
    canManage,
    remainingMs,
    busy,
    refresh,
    start,
    move,
    close,
    visit,
    toggleHelp,
    helpRaised,
  }
}
