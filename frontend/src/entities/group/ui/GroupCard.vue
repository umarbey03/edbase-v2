<script setup lang="ts">
import { LiveIndicator } from '@/entities/session'
import { formatDateWithYear } from '@/shared/lib/datetime'
import type { GroupDto } from '@/shared/types'
import { AppIcon, BaseBadge } from '@/shared/ui'

import { groupDisplayName, groupScheduleSummary, groupTypeLabel, groupTypeTone } from '../model/types'

/**
 * Guruh kartochkasi. Telefonda jadval o'rniga AYNAN shu ishlatiladi —
 * gorizontal skroll bilan "hal qilingan" jadval o'qilmaydi.
 *
 * ★ JONLI HOLAT (2026-08-18, loyiha egasi: *"dars boshlangan guruhlarda
 * card rangi o'zgarsin"*): `live` bo'lsa chegara va fon `rose` ohangiga
 * o'tadi va sarlavha yonida pulsatsiyalanuvchi nishon chiqadi.
 *
 * 🔴 RANG YOLG'IZ O'ZI MA'NO TASHIMAYDI: nishon MATN bilan birga keladi
 * ("Jonli") — rangni ajrata olmaydigan foydalanuvchi ham holatni
 * biladi (WCAG 1.4.1, `BaseBadge` dagi AYNI qoida).
 */
const props = withDefaults(
  defineProps<{
    group: GroupDto
    /** Guruhda HOZIR jonli dars ketyaptimi (`useLiveGroups`). */
    live?: boolean
  }>(),
  { live: false },
)

const emit = defineEmits<{ open: [groupId: number] }>()
</script>

<template>
  <article
    class="flex flex-col rounded-xl border p-3.5 text-left transition-colors sm:p-4"
    :class="
      props.live
        ? 'border-rose-500/45 bg-rose-500/[0.06] hover:border-rose-500/70'
        : 'border-line bg-ink-900 hover:border-line-strong'
    "
  >
    <div class="flex items-start justify-between gap-2">
      <h3
        class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
        v-text="groupDisplayName(props.group)"
      />
      <LiveIndicator v-if="props.live" />
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
