<script setup lang="ts">
import { computed } from 'vue'

import { periodState, periodStateLabel } from '@/entities/subscription'
import type { MySubscriptionDto } from '@/entities/subscription'
import { formatDate } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon, BaseBadge } from '@/shared/ui'

/**
 * MUSTAQIL O'QISH OBUNASI KARTOCHKASI (2026-10-23) — bosh sahifa va profil.
 *
 * ★ TUGASHIGA 3 KUN QOLGANDA SARIQ: o'quvchi to'lovni oldindan qilsin,
 *   darslari bir kun ham yopilib qolmasin.
 */
const props = defineProps<{
  data: MySubscriptionDto
  showHistory?: boolean
}>()

const SOON_DAYS = 3

const tone = computed(() => {
  if (!props.data.isActive) return 'expired'
  return (props.data.daysLeft ?? 0) <= SOON_DAYS ? 'soon' : 'ok'
})
</script>

<template>
  <section
    class="rounded-xl border p-[18px] lg:p-5"
    :class="{
      'border-line bg-ink-900': tone === 'ok',
      'border-amber-500/40 bg-amber-500/10': tone === 'soon',
      'border-rose-500/40 bg-rose-500/10': tone === 'expired',
    }"
  >
    <p class="flex items-center gap-1.5 text-xs font-bold uppercase tracking-[1.4px] text-slate-400">
      <AppIcon
        name="book"
        :size="15"
      />
      Mustaqil o‘qish obunasi
    </p>

    <p
      class="mt-2 text-base font-bold text-slate-100"
      v-text="props.data.courseName ?? 'Kurs'"
    />

    <p
      v-if="props.data.isActive"
      class="mt-1 text-sm"
      :class="tone === 'soon' ? 'text-amber-200' : 'text-slate-300'"
    >
      {{ props.data.endsAt !== null ? formatDate(props.data.endsAt) : '' }} gacha ·
      <b>{{ props.data.daysLeft }}</b> kun qoldi
    </p>
    <p
      v-else
      class="mt-1 text-sm text-rose-200"
    >
      Obuna muddati tugagan. Darslarni davom ettirish uchun obunani uzaytiring.
    </p>

    <!-- Uzaytirish — «Tariflar» sahifasida (2026-10-29, onlayn sotuv). -->
    <RouterLink
      v-if="tone !== 'ok'"
      :to="{ name: 'student-plans' }"
      class="mt-3 inline-flex items-center gap-1 rounded-lg bg-brand-500 px-3 py-1.5 text-sm font-semibold text-on-brand hover:bg-brand-600"
    >
      Obunani uzaytirish
      <AppIcon
        name="chevron-right"
        :size="14"
      />
    </RouterLink>

    <template v-if="props.showHistory && props.data.history.length > 0">
      <h3 class="mb-2 mt-4 text-[11px] font-bold uppercase tracking-[1.2px] text-dim">
        To‘lovlar tarixi
      </h3>
      <ul class="space-y-1.5">
        <li
          v-for="period in props.data.history"
          :key="period.id"
          class="flex items-center justify-between gap-3 rounded-lg border border-line bg-ink-950 px-3 py-2"
        >
          <span class="min-w-0">
            <span class="block truncate text-[13px] text-slate-200">
              {{ formatDate(period.startsAt) }} — {{ formatDate(period.endsAt) }}
            </span>
            <span class="block text-[11px] text-dim">{{ formatMoney(period.amount) }}</span>
          </span>
          <BaseBadge
            :tone="periodState(period) === 'active' ? 'success' : 'neutral'"
            size="xs"
          >
            {{ periodStateLabel(periodState(period)) }}
          </BaseBadge>
        </li>
      </ul>
    </template>
  </section>
</template>
