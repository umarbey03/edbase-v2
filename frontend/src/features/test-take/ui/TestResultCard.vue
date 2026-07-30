<script setup lang="ts">
import { computed } from 'vue'

import { attemptStatusLabel, percentLabel, resultTone, scoreLabel, testTitle } from '@/entities/test'
import { formatDateTime } from '@/shared/lib/datetime'
import type { MyResultDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard } from '@/shared/ui'

/**
 * O'quvchining natija kartochkasi.
 *
 * TO'G'RI JAVOBLAR KO'RSATILMAYDI — backend ularni o'quvchiga UMUMAN
 * yubormaydi (`MyResultDto` da faqat ball va foiz bor, savol-javob tafsiloti
 * yo'q). Bu ataylab: bir test bir marta topshiriladi va javoblar oshkor
 * bo'lsa boshqa o'quvchiga uzatilardi.
 */
const props = defineProps<{ result: MyResultDto }>()

const tone = computed(() => resultTone(props.result))
</script>

<template>
  <BaseCard>
    <div class="flex flex-wrap items-start justify-between gap-2">
      <div class="min-w-0">
        <h2
          class="text-sm font-semibold text-slate-100"
          v-text="testTitle({ id: props.result.testId, title: props.result.title })"
        />
        <p class="mt-0.5 text-[11px] tabular-nums text-dim">
          Boshlangan: {{ formatDateTime(props.result.startedAt) }}
        </p>
      </div>
      <BaseBadge :tone="tone">
        {{ attemptStatusLabel(props.result.status) }}
      </BaseBadge>
    </div>

    <div class="mt-4 flex flex-wrap items-end gap-x-6 gap-y-2">
      <div>
        <p class="text-[11px] uppercase tracking-wide text-dim">
          Ball
        </p>
        <p
          class="text-2xl font-bold tabular-nums text-slate-100"
          v-text="scoreLabel(props.result.score, props.result.maxScore)"
        />
      </div>
      <div>
        <p class="text-[11px] uppercase tracking-wide text-dim">
          Foiz
        </p>
        <p
          class="text-2xl font-bold tabular-nums text-slate-100"
          v-text="percentLabel(props.result.percent)"
        />
      </div>
      <div v-if="props.result.submittedAt !== null">
        <p class="text-[11px] uppercase tracking-wide text-dim">
          Topshirilgan
        </p>
        <p
          class="text-sm tabular-nums text-slate-300"
          v-text="formatDateTime(props.result.submittedAt)"
        />
      </div>
    </div>

    <!--
      Vaqt tugagani uchun yopilgan urinish — ALOHIDA ko'rsatiladi. Aks holda
      o'quvchi "0 ball" ni "hammasini xato yechdim" deb tushunardi.
    -->
    <p
      v-if="props.result.closedByTimeout"
      class="mt-4 flex items-start gap-2 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
    >
      <AppIcon
        name="clock"
        :size="14"
        class="mt-px"
      />
      <span>
        Urinish vaqt tugagani uchun yopilgan: javoblar qabul qilinmagan va
        natija 0 ball bilan yozilgan.
      </span>
    </p>

    <p class="mt-4 text-[11px] leading-relaxed text-dim">
      To‘g‘ri javoblar ko‘rsatilmaydi: bitta testga bitta urinish beriladi va
      javoblar oshkor qilinmaydi.
    </p>
  </BaseCard>
</template>
