<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchMyCurator, SELF_STUDY_CURATOR_KEYS } from '@/entities/self-study-curator'
import { AppIcon } from '@/shared/ui'

/**
 * MUSTAQIL O'QUVCHINING "KURATORINGIZ" KARTASI (2026-11-18).
 *
 * Kurator hali biriktirilmagan bo'lsa ham karta chiqadi va buni ochiq
 * aytadi: o'quvchi vazifasi nega tekshirilmayotganini bilishi kerak.
 */
const query = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.myCurator,
  queryFn: ({ signal }) => fetchMyCurator({ signal }),
  staleTime: 5 * 60_000,
})
const curator = computed(() => query.data.value ?? null)
</script>

<template>
  <section
    v-if="query.isSuccess.value"
    class="flex items-center gap-3 rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
  >
    <span class="grid size-10 shrink-0 place-items-center rounded-full bg-brand-500/15 text-brand-500">
      <AppIcon
        name="user-check"
        :size="18"
      />
    </span>
    <span class="min-w-0 flex-1">
      <span class="block text-xs font-bold uppercase tracking-[1.4px] text-slate-400">Kuratoringiz</span>
      <span
        v-if="curator !== null"
        class="block truncate text-base font-bold text-slate-100"
      >{{ curator.fullName }}</span>
      <span
        v-else
        class="block text-sm text-slate-300"
      >Tez orada biriktiriladi — vazifalaringizni u tekshiradi.</span>
    </span>
  </section>
</template>
