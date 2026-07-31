<script setup lang="ts">
import { computed } from 'vue'

import { barPercent } from '../model/finance-view'

/**
 * Eski ilovadagi `bar(val,max,color)` yordamchisi (academic.html, 2620–2625).
 *
 * Balandlik 7px, radius 4px, orqa fon `rgba(255,255,255,.07)` — o'sha
 * qiymatlar. "Qarz yoshi" va "Guruhlar bo'yicha" kesimlarida ishlatiladi.
 *
 * Bu YANGI umumiy komponent EMAS: `shared/ui` ga chiqarilmagan, chunki uni
 * faqat shu dashboard ishlatadi va u dizayn tizimining bir qismi emas —
 * diagrammaning ichki detali.
 */
const props = defineProps<{
  value: number
  max: number
  color: string
}>()

const width = computed(() => `${barPercent(props.value, props.max)}%`)
</script>

<template>
  <!--
    `aria-hidden`: bar yonidagi matn (summa) ayni ma'noni allaqachon aytadi,
    ekran o'qigich uni ikkinchi marta o'qishi shart emas.
  -->
  <div
    class="h-[7px] overflow-hidden rounded bg-white/[0.07]"
    aria-hidden="true"
  >
    <div
      class="h-full rounded"
      :style="{ width, backgroundColor: props.color }"
    />
  </div>
</template>
