<script setup lang="ts">
import { collectionRateLabel } from '@/entities/payment'
import { formatMoney } from '@/shared/lib/money'
import type { PaymentMethodSummaryDto } from '@/shared/types'
import { BaseCard } from '@/shared/ui'

/**
 * "TO'LOV USULI BO'YICHA" kesimi (academic.html, 899–900 va 2717–2723).
 *
 * ★ YORLIQNI SERVER BERADI (`methodName`: `Naqd` / `Karta` /
 * `Ko'rsatilmagan`). Mijozdagi `paymentMethodLabel` ATAYLAB ishlatilmadi: u
 * `null` ni "—" ga aylantiradi va eski yozuvlar "—" nomli usul bo'lib
 * ko'rinardi. Server esa ularni "Ko'rsatilmagan" deb ataydi — hisobotda
 * aynan shu so'z kerak.
 *
 * ★ Bu raqamlar DAVR (jurnal) — `from..to` kunlarida kassaga tushgan pul.
 */
const props = defineProps<{
  methods: readonly PaymentMethodSummaryDto[]
  /** Qaysi kunlar oralig'i (`isoDateLabel` bilan tayyorlangan matn). */
  range: string
}>()
</script>

<template>
  <BaseCard
    title="To‘lov usuli bo‘yicha"
    :subtitle="props.range"
  >
    <p
      v-if="props.methods.length === 0"
      class="text-xs text-muted"
    >
      <!-- Eskisida "Bu oyda to'lov yo'q." edi. Filtr endi OY emas, sana
           oralig'i (backend shartnomasi) — shuning uchun "oyda" so'zi
           "davrda" ga almashtirildi, qolgan matn o'sha. -->
      Bu davrda to‘lov yo‘q.
    </p>

    <ul v-else>
      <li
        v-for="item in props.methods"
        :key="item.methodName"
        class="flex items-baseline justify-between gap-2 border-b border-line py-2.5 last:border-b-0"
      >
        <span
          class="min-w-0 truncate text-[13px]"
          v-text="item.methodName"
        />
        <span class="shrink-0 tabular-nums">
          <b
            class="text-[13px]"
            v-text="formatMoney(item.amount)"
          />
          <span class="ml-1.5 text-[11px] text-muted">
            ({{ item.count }}) · {{ collectionRateLabel(item.share) }}
          </span>
        </span>
      </li>
    </ul>
  </BaseCard>
</template>
