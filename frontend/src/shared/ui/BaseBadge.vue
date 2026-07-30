<script setup lang="ts">
import { computed } from 'vue'

/*
  Eski dizayndagi `.badge` (radius 20px, 11px, 600) va ko'rinishlari:
  `.b-present` yashil, `.b-absent` qizil, `.b-partial` sariq, `.b-live` qizil,
  `.b-scheduled` accent-soft, `.b-ended` kulrang.
*/
type BadgeTone =
  | 'neutral'
  | 'accent'
  | 'success'
  | 'teacher'
  | 'assistant'
  | 'student'
  | 'live'
  | 'warning'
  | 'danger'
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
  neutral: 'bg-ink-800 text-slate-400',
  accent: 'bg-brand-500/16 text-brand-400',
  success: 'bg-green-500/15 text-green-400',
  teacher: 'bg-brand-500/16 text-brand-300',
  assistant: 'bg-sky-500/15 text-sky-300',
  student: 'bg-ink-800 text-slate-300',
  live: 'bg-rose-500/15 text-rose-400',
  warning: 'bg-amber-500/15 text-amber-400',
  danger: 'bg-rose-500/15 text-rose-400',
}

const DOT_TONES: Record<BadgeTone, string> = {
  neutral: 'bg-slate-400',
  accent: 'bg-brand-400',
  success: 'bg-green-400',
  teacher: 'bg-brand-400',
  assistant: 'bg-sky-400',
  student: 'bg-slate-400',
  live: 'bg-rose-400',
  warning: 'bg-amber-400',
  danger: 'bg-rose-400',
}

const SIZES: Record<BadgeSize, string> = {
  xs: 'px-2 py-0.5 text-[11px] gap-1',
  sm: 'px-2.5 py-1 text-xs gap-1.5',
}

const classes = computed(() => [
  'inline-flex shrink-0 items-center rounded-full font-semibold leading-tight',
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
