<script setup lang="ts">
import { formatDateWithYear } from '@/shared/lib/datetime'
import type { GroupDto } from '@/shared/types'
import { AppIcon, BaseBadge } from '@/shared/ui'

import { groupDisplayName, groupScheduleSummary, groupTypeLabel, groupTypeTone } from '../model/types'

/**
 * Guruh kartochkasi. Telefonda jadval o'rniga AYNAN shu ishlatiladi —
 * gorizontal skroll bilan "hal qilingan" jadval o'qilmaydi.
 */
const props = defineProps<{ group: GroupDto }>()

const emit = defineEmits<{ open: [groupId: number] }>()
</script>

<template>
  <article
    class="flex flex-col rounded-xl border border-line bg-ink-900 p-3.5 text-left transition-colors hover:border-line-strong sm:p-4"
  >
    <div class="flex items-start justify-between gap-2">
      <h3
        class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
        v-text="groupDisplayName(props.group)"
      />
      <BaseBadge :tone="groupTypeTone(props.group.type)">
        {{ groupTypeLabel(props.group.type) }}
      </BaseBadge>
    </div>

    <dl class="mt-2.5 space-y-1.5 text-xs text-slate-400">
      <div class="flex items-center gap-1.5">
        <AppIcon
          name="clock"
          :size="13"
        />
        <span v-text="groupScheduleSummary(props.group)" />
        <span class="text-dim">· {{ props.group.durationMinutes }} daq.</span>
      </div>
      <div class="flex items-center gap-1.5">
        <AppIcon
          name="users"
          :size="13"
        />
        <span class="tabular-nums">{{ props.group.memberCount }} o‘quvchi</span>
        <span class="text-dim">· {{ props.group.sessionCount }} dars</span>
      </div>
      <div
        v-if="props.group.teacherName !== null"
        class="flex min-w-0 items-center gap-1.5"
      >
        <AppIcon
          name="star"
          :size="13"
        />
        <span
          class="truncate"
          v-text="props.group.teacherName"
        />
      </div>
      <div class="flex items-center gap-1.5">
        <AppIcon
          name="calendar"
          :size="13"
        />
        <span class="tabular-nums">
          {{ formatDateWithYear(props.group.startDate) }} — {{ formatDateWithYear(props.group.endDate) }}
        </span>
      </div>
    </dl>

    <div class="mt-3 flex items-center justify-between gap-2 border-t border-line pt-3">
      <BaseBadge :tone="props.group.isActive ? 'success' : 'neutral'">
        {{ props.group.isActive ? 'Faol' : 'Arxiv' }}
      </BaseBadge>
      <button
        type="button"
        class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
        @click="emit('open', props.group.id)"
      >
        Batafsil
        <AppIcon
          name="chevron-right"
          :size="14"
        />
      </button>
    </div>
  </article>
</template>
