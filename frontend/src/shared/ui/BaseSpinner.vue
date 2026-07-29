<script setup lang="ts">
import { computed } from 'vue'

type SpinnerSize = 'xs' | 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    size?: SpinnerSize
    label?: string
  }>(),
  { size: 'md', label: 'Yuklanmoqda' },
)

const SIZES: Record<SpinnerSize, string> = {
  xs: 'size-3 border-[1.5px]',
  sm: 'size-4 border-2',
  md: 'size-6 border-2',
  lg: 'size-9 border-[3px]',
}

const sizeClass = computed(() => SIZES[props.size])
</script>

<template>
  <span
    class="inline-block shrink-0 animate-spin rounded-full border-current border-r-transparent align-[-0.125em] motion-reduce:animate-[spin_1.5s_linear_infinite]"
    :class="sizeClass"
    role="status"
    :aria-label="props.label"
  />
</template>
