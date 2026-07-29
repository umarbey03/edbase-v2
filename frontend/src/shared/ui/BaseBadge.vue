<script setup lang="ts">
import { computed } from 'vue'

type BadgeTone = 'neutral' | 'teacher' | 'assistant' | 'student' | 'live' | 'warning' | 'danger'
type BadgeSize = 'xs' | 'sm'

const props = withDefaults(
  defineProps<{
    tone?: BadgeTone
    size?: BadgeSize
    dot?: boolean
  }>(),
  { tone: 'neutral', size: 'xs', dot: false },
)

const TONES: Record<BadgeTone, string> = {
  neutral: 'bg-ink-750 text-slate-300 ring-line-strong',
  teacher: 'bg-amber-500/15 text-amber-300 ring-amber-500/30',
  assistant: 'bg-sky-500/15 text-sky-300 ring-sky-500/30',
  student: 'bg-slate-500/15 text-slate-300 ring-slate-500/25',
  live: 'bg-rose-500/15 text-rose-300 ring-rose-500/30',
  warning: 'bg-amber-500/15 text-amber-300 ring-amber-500/30',
  danger: 'bg-rose-500/15 text-rose-300 ring-rose-500/30',
}

const DOT_TONES: Record<BadgeTone, string> = {
  neutral: 'bg-slate-400',
  teacher: 'bg-amber-400',
  assistant: 'bg-sky-400',
  student: 'bg-slate-400',
  live: 'bg-rose-400',
  warning: 'bg-amber-400',
  danger: 'bg-rose-400',
}

const SIZES: Record<BadgeSize, string> = {
  xs: 'h-5 px-1.5 text-[10px] gap-1 rounded-md',
  sm: 'h-6 px-2 text-xs gap-1.5 rounded-lg',
}

const classes = computed(() => [
  'inline-flex shrink-0 items-center font-semibold uppercase tracking-wide ring-1 ring-inset',
  TONES[props.tone],
  SIZES[props.size],
])
</script>

<template>
  <span :class="classes">
    <span
      v-if="props.dot"
      class="size-1.5 shrink-0 rounded-full"
      :class="DOT_TONES[props.tone]"
      aria-hidden="true"
    />
    <slot />
  </span>
</template>
