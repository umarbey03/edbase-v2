import { onBeforeUnmount, readonly, ref } from 'vue'
import type { DeepReadonly, Ref } from 'vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARSLIK AUDIO PLEERI — butun sahifaga BITTA `<audio>` (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ Bir vaqtda bitta tovush (prototipdagi `current` qoidasi): yangi tugma
 *   bosilsa oldingisi to'xtaydi.
 * ★ Sekin rejim — 0.78× (birlik rendereridagi qiymat), butun birlik uchun.
 * ★ Ketma-ket tinglash (`playSeq`) — juftliklar va suhbat satrlari; joriy
 *   belgi `current` da, komponent uni ajratib ko'rsatadi.
 * ★ Xato toast emas — tugmaning o'zida ("qayta urinish"), `failed` orqali.
 * 🔴 Manba manzilida chipta bor (`?t=`); u ~15 daqiqa yashaydi, adapter
 *   eskirganini o'zi yangilaydi.
 */
export interface AudioPlayer {
  current: DeepReadonly<Ref<string | null>>
  failed: DeepReadonly<Ref<string | null>>
  slow: Ref<boolean>
  play(handle: string): Promise<void>
  playSeq(handles: (string | null)[]): Promise<void>
  stop(): void
}

export const SLOW_RATE = 0.78

export function useAudioPlayer(resolve: (handle: string) => Promise<string>): AudioPlayer {
  const current = ref<string | null>(null)
  const failed = ref<string | null>(null)
  const slow = ref(false)

  let element: HTMLAudioElement | null = null
  let token = 0

  function ensure(): HTMLAudioElement {
    if (element === null) {
      element = new Audio()
      element.preload = 'none'
    }
    return element
  }

  function stop(): void {
    token++
    element?.pause()
    current.value = null
  }

  async function playOne(handle: string, run: number): Promise<boolean> {
    const audio = ensure()
    current.value = handle
    failed.value = null

    try {
      const src = await resolve(handle)
      if (run !== token) return false

      audio.src = src
      audio.playbackRate = slow.value ? SLOW_RATE : 1
      await audio.play()

      await new Promise<void>((done, fail) => {
        audio.onended = () => done()
        audio.onerror = () => fail(new Error('audio'))
        audio.onpause = () => done()
      })
      return run === token
    } catch {
      if (run === token) failed.value = handle
      return false
    } finally {
      if (run === token && current.value === handle) current.value = null
    }
  }

  async function play(handle: string): Promise<void> {
    if (current.value === handle) {
      stop()
      return
    }
    element?.pause()
    const run = ++token
    await playOne(handle, run)
  }

  async function playSeq(handles: (string | null)[]): Promise<void> {
    element?.pause()
    const run = ++token
    for (const handle of handles) {
      if (handle === null) continue
      const ok = await playOne(handle, run)
      if (!ok) return
    }
  }

  onBeforeUnmount(() => {
    stop()
    element = null
  })

  return { current: readonly(current), failed: readonly(failed), slow, play, playSeq, stop }
}
