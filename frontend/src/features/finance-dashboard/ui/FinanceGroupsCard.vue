<script setup lang="ts">
import { collectionRateLabel } from '@/entities/payment'
import { formatMoney } from '@/shared/lib/money'
import type { PaymentGroupSummaryDto } from '@/shared/types'
import { BaseCard } from '@/shared/ui'

import { KPI_COLORS } from '../model/finance-view'
import FinanceBar from './FinanceBar.vue'

/**
 * "GURUHLAR BO'YICHA" kesimi (academic.html, 896–898 va 2707–2714).
 *
 * ★ TARTIB SERVERNIKI — qarzi kattasidan kichigiga. Mijozda QAYTA
 * SARALANMAYDI: kassir birinchi qatorni "eng muammoli guruh" deb o'qiydi va
 * ikkinchi saralash qoidasi paydo bo'lsa, ikki ekranda ikki xil tartib
 * chiqardi.
 *
 * ★ Bu raqamlar HISOB (accrual) — `fromPeriod..toPeriod` oylari bo'yicha.
 * Sarlavha ostidagi izoh aynan shuni aytadi.
 */
const props = defineProps<{
  groups: readonly PaymentGroupSummaryDto[]
  /** Qaysi oylar hisobga olingani (`periodRangeLabel` natijasi). */
  periods: string
}>()
</script>

<template>
  <BaseCard
    title="Guruhlar bo‘yicha"
    :subtitle="`${props.periods} oylari hisobi`"
  >
    <p
      v-if="props.groups.length === 0"
      class="text-xs text-muted"
    >
      Ma’lumot yo‘q.
    </p>

    <ul v-else>
      <li
        v-for="group in props.groups"
        :key="group.groupId"
        class="border-b border-line py-2.5 last:border-b-0"
      >
        <div class="mb-1.5 flex items-baseline justify-between gap-2">
          <b
            class="min-w-0 truncate text-[13px]"
            v-text="group.groupName"
          />
          <span
            class="shrink-0 text-[12.5px] font-medium tabular-nums"
            :class="group.outstanding > 0 ? 'text-rose-500' : 'text-green-500'"
          >
            {{ group.outstanding > 0 ? `${formatMoney(group.outstanding)} qarz` : 'qarzsiz' }}
          </span>
        </div>

        <!-- Eski ilovada ham "yig'ilgan / reja" nisbati YASHIL bar bilan. -->
        <FinanceBar
          :value="group.collected"
          :max="group.billed"
          :color="KPI_COLORS.collected"
        />

        <p class="mt-1.5 text-[11px] tabular-nums text-muted">
          {{ formatMoney(group.collected) }} / {{ formatMoney(group.billed) }} ·
          {{ group.students }} o‘quvchi · {{ collectionRateLabel(group.collectionRate) }}
        </p>
      </li>
    </ul>
  </BaseCard>
</template>
