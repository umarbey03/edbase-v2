<script setup lang="ts">
import { MascotAvatar } from '@/features/mascot'

import type { Feedback } from '../../model/feedbackMood'

/** Natija ostidagi Nuri — kayfiyat va bitta qisqa gap (`role="status"`). */
defineProps<{ feedback: Feedback }>()
</script>

<template>
  <div
    class="mt-3 flex items-center gap-3 rounded-xl border border-line bg-ink-850 px-3 py-2"
    role="status"
    aria-live="polite"
  >
    <MascotAvatar
      :mood="feedback.mood"
      :size="44"
    />
    <p
      class="text-sm font-semibold text-slate-200"
      v-text="feedback.text"
    />
  </div>
</template>
