<script setup lang="ts">
import { computed } from 'vue'

import { formatWeekdayDate } from '@/shared/lib/datetime'

/**
 * Eski ustoz panelidagi `.todaypill`:
 * `● Bugun: **Payshanba, 30-iyul 2026**`.
 *
 * `shared/ui` DA, chunki uni TO'RT ekran bir xil ko'rsatadi (Bosh sahifa,
 * Guruhlarim, Kuratorlik va guruh ichi) — eski `setToday()` ham aynan shu
 * to'rt joyni bir vaqtda to'ldirardi. Ichida biznes mantiq yo'q.
 */
const label = computed(() => formatWeekdayDate(new Date()))
</script>

<template>
  <p
    class="mb-4 mt-1.5 inline-flex items-center gap-[7px] rounded-[20px] border border-line bg-ink-800 px-3.5 py-[5px] text-xs font-medium text-slate-400"
  >
    <span
      class="size-[7px] shrink-0 rounded-full bg-brand-500"
      aria-hidden="true"
    />
    Bugun:
    <b
      class="font-semibold text-slate-100"
      v-text="label"
    />
  </p>
</template>
