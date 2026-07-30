<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'

/**
 * Sahifalash. Raqamli tugmalar ATAYLAB yo'q — 505 sahifali ro'yxatda
 * ular telefon ekraniga sig'maydi; oldinga/orqaga + "N / M" yetarli.
 */
const props = defineProps<{
  page: number
  totalPages: number
  total: number
}>()

const emit = defineEmits<{ 'update:page': [page: number] }>()

const canPrev = computed(() => props.page > 1)
const canNext = computed(() => props.page < props.totalPages)

function go(delta: number): void {
  const next = props.page + delta
  if (next < 1 || next > props.totalPages) return
  emit('update:page', next)
}
</script>

<template>
  <div
    v-if="props.totalPages > 1"
    class="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3.5 py-3 sm:px-5"
  >
    <p class="text-xs text-slate-400">
      Jami: <span
        class="font-semibold text-slate-200"
        v-text="props.total"
      />
    </p>
    <div class="flex items-center gap-2">
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 text-slate-300 transition-colors hover:bg-ink-750 disabled:opacity-40"
        :disabled="!canPrev"
        title="Oldingi sahifa"
        @click="go(-1)"
      >
        <AppIcon
          name="arrow-left"
          :size="16"
        />
      </button>
      <span class="min-w-16 text-center text-xs tabular-nums text-slate-300">
        {{ props.page }} / {{ props.totalPages }}
      </span>
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 text-slate-300 transition-colors hover:bg-ink-750 disabled:opacity-40"
        :disabled="!canNext"
        title="Keyingi sahifa"
        @click="go(1)"
      >
        <AppIcon
          name="chevron-right"
          :size="16"
        />
      </button>
    </div>
  </div>
</template>
