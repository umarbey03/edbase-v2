<script setup lang="ts">
import { computed } from 'vue'

import { formatDateTime, formatTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

import type { LiveSession } from '../model/types'
import {
  isJoinable,
  sessionStatusLabel,
  sessionStatusTone,
  sessionTitle,
  sessionTypeLabel,
} from '../model/types'

const props = defineProps<{
  session: LiveSession
}>()

const emit = defineEmits<{ join: [sessionId: number] }>()

const title = computed(() => sessionTitle(props.session))
const joinable = computed(() => isJoinable(props.session))
const statusTone = computed(() => sessionStatusTone(props.session.status))
const timeRange = computed(
  () => `${formatDateTime(props.session.scheduledStart)} – ${formatTime(props.session.scheduledEnd)}`,
)
</script>

<template>
  <article
    class="group flex flex-col gap-3 rounded-2xl bg-ink-900 p-4 ring-1 ring-inset ring-line transition-colors hover:ring-line-strong sm:flex-row sm:items-center"
  >
    <div class="min-w-0 flex-1">
      <div class="flex flex-wrap items-center gap-2">
        <h3 class="truncate text-sm font-semibold text-slate-100" v-text="title" />
        <BaseBadge :tone="statusTone" :dot="props.session.status === 'Live'">
          {{ sessionStatusLabel(props.session.status) }}
        </BaseBadge>
        <BaseBadge v-if="props.session.isHost" tone="teacher">Siz olib borasiz</BaseBadge>
      </div>

      <div class="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-500">
        <span class="inline-flex items-center gap-1.5">
          <AppIcon name="users" :size="13" />
          <span v-text="props.session.groupName" />
        </span>
        <span class="inline-flex items-center gap-1.5">
          <AppIcon name="calendar" :size="13" />
          <span class="tabular-nums" v-text="timeRange" />
        </span>
        <span v-text="sessionTypeLabel(props.session.type)" />
      </div>
    </div>

    <BaseButton
      class="shrink-0"
      :variant="props.session.status === 'Live' ? 'primary' : 'secondary'"
      :disabled="!joinable"
      @click="emit('join', props.session.id)"
    >
      <template #icon><AppIcon name="play" :size="14" /></template>
      Darsga kirish
    </BaseButton>
  </article>
</template>
