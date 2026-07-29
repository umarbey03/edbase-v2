<script setup lang="ts">
import { computed } from 'vue'

import BaseSpinner from './BaseSpinner.vue'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'success'
type ButtonSize = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
    block?: boolean
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    loading: false,
    disabled: false,
    block: false,
  },
)

const VARIANTS: Record<ButtonVariant, string> = {
  primary:
    'bg-brand-600 text-white shadow-lg shadow-brand-600/25 hover:bg-brand-500 active:bg-brand-700',
  secondary:
    'bg-ink-750 text-slate-100 border border-line-strong hover:bg-ink-700 active:bg-ink-800',
  ghost: 'bg-transparent text-slate-300 hover:bg-white/5 active:bg-white/10',
  danger: 'bg-rose-600 text-white shadow-lg shadow-rose-600/25 hover:bg-rose-500 active:bg-rose-700',
  success:
    'bg-emerald-600 text-white shadow-lg shadow-emerald-600/25 hover:bg-emerald-500 active:bg-emerald-700',
}

const SIZES: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-xs gap-1.5 rounded-lg',
  md: 'h-10 px-4 text-sm gap-2 rounded-xl',
  lg: 'h-12 px-5 text-base gap-2.5 rounded-xl',
}

const isDisabled = computed(() => props.disabled || props.loading)

const classes = computed(() => [
  'inline-flex select-none items-center justify-center font-medium transition-colors duration-150',
  'disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none',
  VARIANTS[props.variant],
  SIZES[props.size],
  props.block ? 'w-full' : '',
])
</script>

<template>
  <button :type="props.type" :class="classes" :disabled="isDisabled">
    <BaseSpinner v-if="props.loading" size="sm" />
    <slot v-else name="icon" />
    <slot />
  </button>
</template>
