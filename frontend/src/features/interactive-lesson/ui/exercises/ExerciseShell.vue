<script setup lang="ts">
import { computed } from 'vue'

import type { FirstScore } from '@/entities/interactive-unit'

import { feedbackMood } from '../../model/feedbackMood'
import { splitInstruction } from '../../model/context'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import NuriFeedback from '../parts/NuriFeedback.vue'

/**
 * Mashq qobig'i: ko'rsatma, natija nishoni, Nuri va "Qayta mashq".
 *
 * ★ Nishon — BIRINCHI urinish natijasi (serverdan); qayta mashq uni
 *   o'zgartirmaydi, shuning uchun "Qayta mashq" faqat mahalliy holatni
 *   tozalaydi (`retry` hodisasi, ota komponent `key` ni almashtiradi).
 */
const props = defineProps<{
  korsatma: string | null
  first: FirstScore | undefined
  /** Shu sessiyada mashq tugadimi (Nuri shunda chiqadi). */
  finished: boolean
  pending?: boolean
  slow?: boolean
  error?: string | null
}>()

const emit = defineEmits<{ retry: [] }>()

const instruction = computed(() => splitInstruction(props.korsatma))
const feedback = computed(() => (props.first !== undefined ? feedbackMood(props.first.right, props.first.total) : null))
</script>

<template>
  <section class="mt-5 border-t border-line pt-4">
    <div class="flex flex-wrap items-start justify-between gap-2">
      <p class="min-w-0 flex-1 text-[15px] font-semibold text-slate-100">
        <template v-if="instruction.ar">
          <ArabicText
            :text="instruction.ar"
            size="sm"
          />
          —
        </template>
        <MixedText :text="instruction.uz" />
      </p>
      <span
        class="shrink-0 rounded-full px-2.5 py-1 text-xs font-bold"
        :class="first ? 'bg-green-500/10 text-green-400' : 'bg-ink-800 text-slate-400'"
      >
        {{ first ? `Natija: ${first.right}/${first.total}` : 'Mashq' }}
      </span>
    </div>

    <slot />

    <p
      v-if="slow"
      class="mt-2 text-xs text-amber-400"
      role="status"
    >
      Internet sekin — javob yuborilmoqda…
    </p>
    <p
      v-if="error"
      class="mt-2 text-xs text-rose-400"
      role="alert"
      v-text="error"
    />

    <template v-if="finished && feedback">
      <NuriFeedback :feedback="feedback" />
      <button
        type="button"
        class="mt-2 min-h-10 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
        @click="emit('retry')"
      >
        Qayta mashq qilish
      </button>
    </template>
  </section>
</template>
