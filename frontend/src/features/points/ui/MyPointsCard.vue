<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchMyPoints } from '@/entities/points'
import { useMySubscription } from '@/entities/subscription'
import { AppIcon } from '@/shared/ui'

/**
 * BOSH SAHIFADAGI "BALLARIM" KARTOCHKASI (2026-10-23).
 *
 * Oydagi ball, guruhdagi o'rin va keyingi ketma-ketlik bonusigacha qancha
 * qolgani. Maqsad — keyingi darsga kelish uchun ANIQ sabab ko'rsatish
 * ("yana 1 dars — +10 bonus"), shuning uchun bonus yo'li 4 katakli chiziq
 * bilan chiziladi.
 *
 * ★ XATO SAHIFANI YIQITMAYDI: kartochka qo'shimcha ma'lumot — yuklanmasa
 *   bosh sahifaning qolgani ishlayveradi.
 */
const query = useQuery({
  queryKey: ['progress', 'points', 'current'],
  queryFn: ({ signal }) => fetchMyPoints(undefined, { signal }),
})

const data = computed(() => query.data.value ?? null)

// Mustaqil o'quvchining o'rni — mustaqil o'quvchilar orasida (guruhi yo'q).
const { isSelfStudy } = useMySubscription()
const STEPS = 4
const filled = computed(() => (data.value === null ? 0 : STEPS - data.value.lessonsToNextBonus))
</script>

<template>
  <section
    v-if="data !== null"
    class="mt-4 rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
  >
    <div class="flex items-start justify-between gap-3">
      <div>
        <p class="flex items-center gap-1.5 text-xs font-bold uppercase tracking-[1.4px] text-slate-400">
          <AppIcon
            name="trophy"
            :size="15"
          />
          Ballarim
        </p>
        <p class="mt-2 text-[28px] font-extrabold leading-none tabular-nums text-brand-300">
          {{ data.total }} <span class="text-sm font-bold text-slate-400">ball</span>
        </p>
        <p class="mt-1.5 text-xs text-slate-400">
          <template v-if="data.rank !== null">
            {{ isSelfStudy ? 'Mustaqil o‘quvchilar orasida' : 'Guruhda' }}
            <b class="text-slate-200">{{ data.rank }}-o‘rin</b>
          </template>
          <template v-else-if="data.total === 0">
            Bu oyda hali ball yo‘q — birinchi darsdan boshlanadi
          </template>
          <template v-else>
            Bu oy uchun
          </template>
        </p>
      </div>
      <RouterLink
        :to="{ name: 'student-rating' }"
        class="tap-expand inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-brand-400 hover:bg-ink-800"
      >
        Reyting
        <AppIcon
          name="chevron-right"
          :size="14"
        />
      </RouterLink>
    </div>

    <!-- Ketma-ket darsga kelish bonusi — faqat guruh (jonli dars) o'quvchisiga. -->
    <div
      v-if="data.groupName !== null"
      class="mt-4 border-t border-line pt-3"
    >
      <div class="flex gap-1.5">
        <span
          v-for="step in STEPS"
          :key="step"
          class="h-1.5 flex-1 rounded-full"
          :class="step <= filled ? 'bg-brand-400' : 'bg-ink-750'"
        />
      </div>
      <p class="mt-2 text-xs text-slate-300">
        Yana <b>{{ data.lessonsToNextBonus }}</b> dars ketma-ket kelsangiz —
        <b class="text-brand-300">+{{ data.nextBonus }}</b> bonus
      </p>
    </div>
  </section>
</template>
