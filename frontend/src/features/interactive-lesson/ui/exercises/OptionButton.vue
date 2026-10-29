<script setup lang="ts">
import { computed } from 'vue'

import { hasArabic } from '../../model/context'

/**
 * Javob varianti. Holat rang bilan BIRGA belgi (✓ / ✗) bilan ko'rsatiladi —
 * faqat rangga tayanilmaydi. Kamida 44 px balandlik.
 */
const props = defineProps<{ text: string | null; state?: 'idle' | 'right' | 'wrong' | 'selected'; disabled?: boolean }>()
defineEmits<{ pick: [] }>()

const arabic = computed(() => hasArabic(props.text))
</script>

<template>
  <button
    type="button"
    class="inline-flex min-h-11 items-center gap-1.5 rounded-xl border px-3.5 transition-colors disabled:cursor-default"
    :class="[
      arabic ? 'font-arabic text-xl leading-[1.7]' : 'text-sm font-semibold',
      state === 'right'
        ? 'border-green-500 bg-green-500/10 text-green-400'
        : state === 'wrong'
          ? 'border-rose-500 bg-rose-500/10 text-rose-400'
          : state === 'selected'
            ? 'border-brand-500 bg-brand-500/10 text-brand-400'
            : 'border-line bg-ink-900 text-slate-200 enabled:hover:border-brand-500',
    ]"
    :disabled="disabled"
    :lang="arabic ? 'ar' : undefined"
    :dir="arabic ? 'rtl' : undefined"
    @click="$emit('pick')"
  >
    <span
      v-if="state === 'right'"
      aria-hidden="true"
      class="font-sans text-sm"
    >✓</span>
    <span
      v-else-if="state === 'wrong'"
      aria-hidden="true"
      class="font-sans text-sm"
    >✗</span>
    <span>{{ text }}</span>
    <span
      v-if="state === 'right'"
      class="sr-only"
    >(to‘g‘ri)</span>
    <span
      v-else-if="state === 'wrong'"
      class="sr-only"
    >(noto‘g‘ri)</span>
  </button>
</template>
