<script setup lang="ts">
import { BaseCard } from '@/shared/ui'

/**
 * Eski ilovadagi `kpiCard(label,val,color,sub)` (academic.html, 2616–2619):
 * markazlangan matn, tepada 3px rangli chiziq, 26px qalin son, ostida
 * BOSH HARFLI kichkina yorliq va ixtiyoriy izoh.
 *
 * ★ YANGI KARTOCHKA KOMPONENTI EMAS: sirt sifatida `shared/ui` dagi
 * `BaseCard` ishlatiladi (chegara, radius, fon — hammasi o'shanikidan),
 * bu yerda faqat KPI joylashuvi qo'shiladi. Aks holda dizayn tizimida
 * ikkita "kartochka" paydo bo'lib, tema o'zgarganda ular ajralib ketardi.
 *
 * `value` — TAYYOR MATN. Formatlash chaqiruvchida bajariladi
 * (`formatMoney` / `collectionRateLabel`), shunda bu komponent pul va foizni
 * farqlashi shart emas va ikkinchi formatlovchi tug'ilmaydi.
 */
withDefaults(
  defineProps<{
    label: string
    value: string
    /** Kartochka urg'u rangi — `model/finance-view.ts` dagi `KPI_COLORS`. */
    color: string
    /** Yorliq ostidagi kichik izoh (eski `sub`). Bo'sh bo'lsa chizilmaydi. */
    sub?: string
  }>(),
  { sub: '' },
)
</script>

<template>
  <BaseCard
    class="text-center"
    :style="{ borderTopWidth: '3px', borderTopColor: color }"
  >
    <p
      class="text-[26px] font-extrabold leading-[1.15] tabular-nums"
      :style="{ color }"
      v-text="value"
    />
    <p
      class="mt-1.5 text-[11.5px] uppercase tracking-[0.4px] text-muted"
      v-text="label"
    />
    <p
      v-if="sub.length > 0"
      class="mt-1 text-[11px] text-muted"
      v-text="sub"
    />
  </BaseCard>
</template>
