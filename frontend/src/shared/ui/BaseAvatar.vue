<script setup lang="ts">
import { computed } from 'vue'

import { colorIndex, initials } from '@/shared/lib/text'

type AvatarSize = 'xs' | 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    name: string
    size?: AvatarSize
    /** Gapirayotgan/qo'l ko'targan ishtirokchini ajratib ko'rsatish uchun halqa. */
    ring?: boolean
  }>(),
  { size: 'sm', ring: false },
)

const SIZES: Record<AvatarSize, string> = {
  xs: 'size-6 text-[10px]',
  sm: 'size-8 text-xs',
  md: 'size-10 text-sm',
  lg: 'size-14 text-lg',
}

/** Barqaror palitra — bir xil ism doim bir xil rangda ko'rinadi. */
const PALETTE = [
  'bg-indigo-500/20 text-indigo-200',
  'bg-emerald-500/20 text-emerald-200',
  'bg-amber-500/20 text-amber-200',
  'bg-rose-500/20 text-rose-200',
  'bg-sky-500/20 text-sky-200',
  'bg-violet-500/20 text-violet-200',
  'bg-teal-500/20 text-teal-200',
  'bg-orange-500/20 text-orange-200',
] as const

const label = computed(() => initials(props.name))
const tone = computed(() => PALETTE[colorIndex(props.name, PALETTE.length)] ?? PALETTE[0])
</script>

<template>
  <span
    class="inline-flex shrink-0 select-none items-center justify-center rounded-full font-semibold"
    :class="[SIZES[props.size], tone, props.ring ? 'ring-2 ring-brand-400/70' : '']"
    :title="props.name"
    aria-hidden="true"
    v-text="label"
  />
</template>
