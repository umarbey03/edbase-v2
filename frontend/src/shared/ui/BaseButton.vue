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

// Eski dizayndagi `button.btn`: to'q yashil `--accent`, oq matn, 8px radius,
// soyasiz "tekis" ko'rinish.
//
// `primary` da `text-white` EMAS, `text-on-brand`: brend fonidagi matn rangi
// temaga bog'liq — xodimda oq (to'q yashil fon), o'quvchida to'q ko'k (oltin
// fon; oq matn u yerda o'qilmaydi, kontrast ~1.9:1). Qolgan variantlar ikkala
// temada ham yashil/qizil fonda, shuning uchun `text-white` bo'lib qoladi.
const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-brand-500 text-on-brand hover:bg-brand-400 active:bg-brand-600',
  secondary: 'bg-ink-800 text-slate-100 border border-line hover:bg-ink-750 active:bg-ink-850',
  ghost: 'bg-transparent text-slate-300 hover:bg-ink-800 active:bg-ink-750',
  // Eski `--red: #ef4444` = Tailwind `red-500` (rose'dan ko'ra kamroq pushti).
  danger: 'bg-red-500 text-white hover:bg-red-400 active:bg-red-600',
  success: 'bg-green-500 text-white hover:bg-green-400 active:bg-green-600',
}

/*
  Balandliklar teginish nishoniga moslangan: `md`/`lg` >= 44px. `sm` faqat
  zich jadval qatorlarida ishlatiladi — u yerda qatorning o'zi barmoqqa
  yetarli maydon beradi, aks holda jadval o'qib bo'lmas darajada cho'ziladi.
*/
const SIZES: Record<ButtonSize, string> = {
  sm: 'h-9 px-3 text-xs gap-1.5 rounded-lg',
  md: 'h-11 px-4 text-sm gap-2 rounded-lg',
  lg: 'h-12 px-5 text-base gap-2.5 rounded-lg',
}

const isDisabled = computed(() => props.disabled || props.loading)

const classes = computed(() => [
  'inline-flex select-none items-center justify-center font-semibold transition-colors duration-150',
  'disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none',
  VARIANTS[props.variant],
  SIZES[props.size],
  props.block ? 'w-full' : '',
])
</script>

<template>
  <button
    :type="props.type"
    :class="classes"
    :disabled="isDisabled"
  >
    <BaseSpinner
      v-if="props.loading"
      size="sm"
    />
    <slot
      v-else
      name="icon"
    />
    <slot />
  </button>
</template>
