<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { sessionTitle } from '@/entities/session'
import { formatWeekdayDateTime } from '@/shared/lib/datetime'
import type { LiveSessionDto, SessionTypeName } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'

import { canJoin, sessionState } from '../model/useStudentSchedule'

/**
 * "Keyingi dars" kartochkasi (eski `heroCard()` / `.hero2`).
 *
 * Bosh sahifada IKKITA bo'ladi: ustoz darsi (oltin) va kurator darsi (firuza).
 * Dars jonli bo'lsa kartochka qizarib, tugma qizil pulsatsiyaga o'tadi —
 * eski ilovadagi `.hero2.live` + `.btn.live` bilan bir xil.
 */
const props = defineProps<{
  type: SessionTypeName
  session: LiveSessionDto | null
  now: Date
}>()

const router = useRouter()

const isTeacher = computed(() => props.type === 'Teacher')
const typeLabel = computed(() => (isTeacher.value ? 'Ustoz darsi' : 'Kurator darsi'))

const isLive = computed(
  () => props.session !== null && sessionState(props.session, props.now) === 'live',
)
const joinable = computed(() => props.session !== null && canJoin(props.session, props.now))

/** Kartochka foni: oltin / firuza / (jonli bo'lsa) qizil radial gradient. */
const cardStyle = computed(() => {
  if (isLive.value) {
    return {
      borderColor: 'rgb(239 68 68 / 0.5)',
      background:
        'radial-gradient(125% 100% at 100% 0, rgb(239 68 68 / 0.24), transparent 60%), var(--color-ink-900)',
    }
  }
  if (isTeacher.value) {
    return {
      borderColor: 'rgb(245 183 49 / 0.42)',
      background:
        'radial-gradient(125% 100% at 100% 0, rgb(245 183 49 / 0.16), transparent 60%), var(--color-ink-900)',
    }
  }
  return {
    borderColor: 'rgb(34 211 238 / 0.42)',
    background:
      'radial-gradient(125% 100% at 100% 0, rgb(34 211 238 / 0.16), transparent 60%), var(--color-ink-900)',
  }
})

const labelColor = computed(() => {
  if (isLive.value) return '#ff9b9b'
  return isTeacher.value ? 'var(--color-brand-400)' : '#67e8f9'
})

/** Orqaga sanoq: kun / soat / daqiqa / sek (eski `.count.mini`). */
const countdown = computed(() => {
  if (props.session === null) return null
  const diffMs = new Date(props.session.scheduledStart).getTime() - props.now.getTime()
  let seconds = Math.max(0, Math.floor(diffMs / 1000))
  const days = Math.floor(seconds / 86400)
  seconds %= 86400
  const hours = Math.floor(seconds / 3600)
  seconds %= 3600
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  const pad = (value: number): string => (value < 10 ? `0${value}` : String(value))
  return [
    { value: String(days), label: 'kun' },
    { value: pad(hours), label: 'soat' },
    { value: pad(minutes), label: 'daqiqa' },
    { value: pad(rest), label: 'sek' },
  ]
})

function join(): void {
  if (props.session === null) return
  void router.push({ name: 'live-room', params: { sessionId: String(props.session.id) } })
}
</script>

<template>
  <article
    class="flex flex-col overflow-hidden rounded-[18px] border-[1.5px] p-4 pb-3.5"
    :style="cardStyle"
  >
    <p
      class="flex items-center gap-1.5 text-[11px] font-extrabold uppercase tracking-[1px]"
      :style="{ color: labelColor }"
    >
      <span
        v-if="isLive"
        class="size-2 animate-ping-live rounded-full bg-red-500"
        aria-hidden="true"
      />
      <AppIcon
        v-else
        :name="isTeacher ? 'graduation' : 'user-check'"
        :size="15"
      />
      {{ isLive ? `JONLI · ${typeLabel}` : typeLabel }}
    </p>

    <p
      v-if="props.session === null"
      class="mt-3 text-[13px] text-slate-400"
    >
      Rejalashtirilgan dars yo‘q
    </p>

    <template v-else>
      <h3
        class="mb-1 mt-2.5 text-lg font-extrabold leading-tight"
        v-text="sessionTitle(props.session)"
      />
      <p class="text-[12.5px] leading-snug text-slate-400">
        <span v-text="props.session.groupName" /><br>
        <span v-text="formatWeekdayDateTime(props.session.scheduledStart)" />
      </p>

      <!-- Jonli darsda sanoq ortiqcha — darhol kirish tugmasi chiqadi. -->
      <div
        v-if="!isLive && countdown !== null"
        class="mb-2.5 mt-3 flex gap-1.5"
      >
        <div
          v-for="cell in countdown"
          :key="cell.label"
          class="flex-1 rounded-[11px] border border-line bg-black/25 px-1 py-2 text-center"
        >
          <b
            class="block text-[21px] font-extrabold leading-none tabular-nums"
            v-text="cell.value"
          />
          <span
            class="mt-1 block text-[9px] uppercase tracking-wider text-slate-400"
            v-text="cell.label"
          />
        </div>
      </div>

      <BaseButton
        class="mt-auto"
        :class="isLive ? 'animate-pulse-btn' : ''"
        :variant="isLive ? 'danger' : 'secondary'"
        size="lg"
        block
        :disabled="!joinable"
        @click="join"
      >
        <!-- Eski ilovada strelka matndan KEYIN turardi: "Darsga kirish ›". -->
        Darsga kirish
        <AppIcon
          v-if="isLive"
          name="chevron-right"
          :size="18"
        />
      </BaseButton>
    </template>
  </article>
</template>
