import { Room, RoomEvent } from 'livekit-client'
import type { Participant } from 'livekit-client'
import { onBeforeUnmount, shallowRef, watch } from 'vue'
import type { Ref, ShallowRef } from 'vue'

import { fetchBreakoutJoin } from '../api/breakout-api'

/**
 * USTOZ UCHUN "KIM GAPIRYAPTI" (2026-10-23).
 *
 * Har bir ichki guruhga ko'rinmas tinglovchi ulanishi ochiladi: u hech
 * narsaga OBUNA BO'LMAYDI (`autoSubscribe: false` — trafik deyarli nol),
 * faqat LiveKit'ning `ActiveSpeakersChanged` hodisasini oladi. Ovoz
 * eshitilmaydi — ustoz faqat qaysi guruhda kim gapirayotganini ko'radi.
 *
 * Server tinglovchiga `hidden` token beradi (identity `{id}-monitor`),
 * shuning uchun guruhdagilar uni ko'rmaydi va u ustozning guruhga kirgan
 * asosiy ulanishiga to'qnashmaydi.
 */
export interface UseBreakoutMonitorResult {
  /** Guruh raqami -> hozir gapirayotganlar (`userId`). */
  speaking: ShallowRef<ReadonlyMap<number, ReadonlySet<number>>>
  /** Guruh raqami -> LiveKit xonasiga haqiqatan ulanganlar (`userId`). */
  connected: ShallowRef<ReadonlyMap<number, ReadonlySet<number>>>
}

function userIdOf(participant: Participant): number | null {
  const id = Number.parseInt(participant.identity, 10)
  return Number.isNaN(id) ? null : id
}

export function useBreakoutMonitor(
  sessionId: number,
  rooms: Ref<readonly number[]>,
  enabled: Ref<boolean>,
): UseBreakoutMonitorResult {
  const speaking = shallowRef<ReadonlyMap<number, ReadonlySet<number>>>(new Map())
  const connected = shallowRef<ReadonlyMap<number, ReadonlySet<number>>>(new Map())
  const open = new Map<number, Room>()
  const pending = new Set<number>()
  let disposed = false

  function setFor(
    target: ShallowRef<ReadonlyMap<number, ReadonlySet<number>>>,
    number: number,
    ids: ReadonlySet<number> | null,
  ): void {
    const next = new Map(target.value)
    if (ids === null) next.delete(number)
    else next.set(number, ids)
    target.value = next
  }

  function refreshConnected(number: number, room: Room): void {
    const ids = new Set<number>()
    for (const participant of room.remoteParticipants.values()) {
      const id = userIdOf(participant)
      if (id !== null) ids.add(id)
    }
    setFor(connected, number, ids)
  }

  async function openMonitor(number: number): Promise<void> {
    if (open.has(number) || pending.has(number)) return
    pending.add(number)
    try {
      const join = await fetchBreakoutJoin(sessionId, number, true)
      if (disposed || !enabled.value || !rooms.value.includes(number)) return

      const room = new Room({ adaptiveStream: false, dynacast: false })
      room
        .on(RoomEvent.ActiveSpeakersChanged, (speakers: Participant[]) => {
          const ids = new Set<number>()
          for (const speaker of speakers) {
            const id = userIdOf(speaker)
            if (id !== null) ids.add(id)
          }
          setFor(speaking, number, ids)
        })
        .on(RoomEvent.ParticipantConnected, () => refreshConnected(number, room))
        .on(RoomEvent.ParticipantDisconnected, () => refreshConnected(number, room))

      await room.connect(join.serverUrl, join.token, { autoSubscribe: false })
      if (disposed || !rooms.value.includes(number)) {
        await room.disconnect()
        return
      }
      open.set(number, room)
      refreshConnected(number, room)
    } catch {
      /* Kuzatuv ixtiyoriy — guruhlar ishlashiga ta'sir qilmaydi. */
    } finally {
      pending.delete(number)
    }
  }

  async function closeMonitor(number: number): Promise<void> {
    const room = open.get(number)
    open.delete(number)
    setFor(speaking, number, null)
    setFor(connected, number, null)
    if (room !== undefined) await room.disconnect().catch(() => undefined)
  }

  watch(
    [rooms, enabled],
    ([numbers, on]) => {
      const wanted = new Set(on ? numbers : [])
      for (const number of [...open.keys()]) if (!wanted.has(number)) void closeMonitor(number)
      for (const number of wanted) void openMonitor(number)
    },
    { immediate: true },
  )

  onBeforeUnmount(() => {
    disposed = true
    for (const number of [...open.keys()]) void closeMonitor(number)
  })

  return { speaking, connected }
}
