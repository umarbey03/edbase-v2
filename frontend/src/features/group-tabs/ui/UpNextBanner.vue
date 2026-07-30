<script setup lang="ts">
import { computed } from 'vue'

import { sessionStartState, sessionTypeLabel } from '@/entities/session'
import { formatWeekdayDateTime } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { ScheduledSessionDto } from '@/shared/types'
import { BaseButton } from '@/shared/ui'

import { useGroupSchedule, useSessionStart } from '../model/use-group-schedule'

/**
 * "Keyingi dars" banneri — eski `#upnext-box` (`.upnext`).
 *
 * Jonli dars BOR bo'lsa u birinchi o'ringa chiqadi (eski
 * `renderUpnext()` da ham shunday), aks holda eng yaqin rejadagi dars.
 */
const props = defineProps<{ groupId: number }>()

const now = useNow()
const scheduleQuery = useGroupSchedule(props.groupId)
const { start, openRoom, pendingId, error } = useSessionStart(props.groupId)

const upNext = computed<ScheduledSessionDto | null>(() => {
  const sessions = scheduleQuery.data.value ?? []
  const live = sessions.find((item) => item.status === 'Live')
  if (live !== undefined) return live

  const current = now.value.getTime()
  const upcoming = sessions
    .filter(
      (item) =>
        item.status === 'Scheduled' && new Date(item.scheduledEnd).getTime() >= current,
    )
    .sort(
      (a, b) => new Date(a.scheduledStart).getTime() - new Date(b.scheduledStart).getTime(),
    )

  return upcoming[0] ?? null
})

const state = computed(() =>
  upNext.value === null ? null : sessionStartState(upNext.value, now.value),
)
</script>

<template>
  <div
    v-if="upNext !== null"
    class="mb-5 flex flex-wrap items-center justify-between gap-3.5 rounded-2xl border border-brand-500 bg-brand-500/12 px-5 py-4"
  >
    <div class="min-w-0">
      <p
        class="text-[10px] font-bold uppercase tracking-[0.8px] text-brand-500"
        :class="upNext.status === 'Live' ? 'text-rose-400' : ''"
      >
        {{ upNext.status === 'Live' ? 'Hozir jonli' : 'Keyingi dars' }}
      </p>
      <p
        class="mt-1 truncate text-[17px] font-bold text-slate-100"
        v-text="upNext.title ?? sessionTypeLabel(upNext.type)"
      />
      <p class="mt-0.5 text-[13px] text-slate-400">
        {{ sessionTypeLabel(upNext.type) }} ·
        {{ formatWeekdayDateTime(upNext.scheduledStart) }}
      </p>
      <p
        v-if="error !== null"
        class="mt-1.5 text-xs text-rose-400"
        role="alert"
        v-text="error"
      />
    </div>

    <BaseButton
      v-if="state?.kind === 'live'"
      variant="success"
      @click="openRoom(upNext.id)"
    >
      Darsga qaytish
    </BaseButton>
    <BaseButton
      v-else-if="state?.kind === 'ready'"
      :loading="pendingId === upNext.id"
      @click="start(upNext.id)"
    >
      Darsni boshlash
    </BaseButton>
    <span
      v-else-if="state?.kind === 'wait'"
      class="inline-flex h-11 items-center rounded-lg border border-line px-4 text-xs text-slate-400"
    >
      ⏳ {{ state.text }} qoldi
    </span>
  </div>
</template>
