<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { canJoin, sessionState } from '@/features/student-schedule/model/useStudentSchedule'
import type { LiveSessionDto } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

/**
 * Yuqori panel (eski `.appbar`).
 *
 * Chapda "ZIN-NUR" + "TALABA", o'ngda "keyingi dars" chipi va avatar.
 * Eski ilovadagi yordam ("Yo'riqnoma") tugmasi KO'CHIRILMADI — v2 da
 * onboarding tur hali yo'q, ishlamaydigan tugma qo'yish yolg'on bo'lardi.
 */
const props = defineProps<{
  displayName: string
  nextSession: LiveSessionDto | null
  now: Date
}>()

const emit = defineEmits<{ 'open-profile': [] }>()

const router = useRouter()

const initial = computed(() => (props.displayName.trim()[0] ?? '?').toUpperCase())

const isLive = computed(
  () => props.nextSession !== null && sessionState(props.nextSession, props.now) === 'live',
)

function pad2(value: number): string {
  return value < 10 ? `0${value}` : String(value)
}

/**
 * Chipdagi matn — eski `tickMini()` bilan bir xil format:
 * bir kundan ko'p qolgan bo'lsa `3k 04:12`, aks holda `04:12:07`.
 */
const countdown = computed(() => {
  if (props.nextSession === null) return ''
  const diffMs = new Date(props.nextSession.scheduledStart).getTime() - props.now.getTime()
  let seconds = Math.max(0, Math.floor(diffMs / 1000))
  const days = Math.floor(seconds / 86400)
  seconds %= 86400
  const hours = Math.floor(seconds / 3600)
  seconds %= 3600
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  return days > 0
    ? `${days}k ${pad2(hours)}:${pad2(minutes)}`
    : `${pad2(hours)}:${pad2(minutes)}:${pad2(rest)}`
})

/**
 * Eski `goNext()`: jonli bo'lsa darsga kiradi, aks holda bosh sahifaga
 * qaytarib, tepaga suradi (dars kartochkasi o'sha yerda).
 */
function handleNextClick(): void {
  const session = props.nextSession
  if (session === null) return
  if (isLive.value && canJoin(session, props.now)) {
    void router.push({ name: 'live-room', params: { sessionId: String(session.id) } })
    return
  }
  void router.push({ name: 'student-home' })
  window.scrollTo({ top: 0, behavior: 'smooth' })
}
</script>

<template>
  <header
    class="sticky top-0 z-30 flex items-center justify-between px-[18px] pb-3 pt-4 backdrop-blur-[8px]"
    style="background: linear-gradient(180deg, var(--color-ink-950) 72%, transparent)"
  >
    <p class="text-[19px] font-extrabold leading-tight tracking-[-0.4px] text-brand-500">
      ZIN-NUR
      <span class="block text-[10px] font-bold tracking-[2.5px] text-dim">TALABA</span>
    </p>

    <div class="flex items-center gap-2.5">
      <button
        v-if="props.nextSession !== null"
        type="button"
        class="tap-expand flex animate-pop items-center gap-1.5 rounded-[22px] border px-3 py-[7px] text-[13px] font-bold tabular-nums transition-transform active:scale-[0.94]"
        :class="
          isLive
            ? 'animate-pulse-btn border-red-500 bg-red-500 text-white'
            : 'border-brand-500/40 bg-brand-500/15 text-brand-400'
        "
        :aria-label="isLive ? 'Jonli darsga kirish' : 'Keyingi darsgacha qolgan vaqt'"
        @click="handleNextClick"
      >
        <AppIcon
          :name="isLive ? 'chevron-right' : 'clock'"
          :size="15"
        />
        <span v-text="isLive ? 'Jonli' : countdown" />
      </button>

      <button
        type="button"
        class="tap-expand flex size-10 animate-pop items-center justify-center rounded-full text-base font-bold text-white"
        style="background: linear-gradient(135deg, #f5b731, #22d3ee)"
        :title="props.displayName"
        aria-label="Profil"
        @click="emit('open-profile')"
      >
        {{ initial }}
      </button>
    </div>
  </header>
</template>
