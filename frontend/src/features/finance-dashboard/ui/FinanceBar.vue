<script setup lang="ts">
import { computed } from 'vue'

import { barPercent } from '../model/finance-view'

/**
 * Eski ilovadagi `bar(val,max,color)` yordamchisi (academic.html, 2620–2625).
 *
 * Balandlik 7px, radius 4px. "Qarz yoshi" va "Guruhlar bo'yicha" kesimlarida
 * ishlatiladi.
 *
 * Bu YANGI umumiy komponent EMAS: `shared/ui` ga chiqarilmagan, chunki uni
 * faqat shu dashboard ishlatadi va u dizayn tizimining bir qismi emas —
 * diagrammaning ichki detali.
 *
 * 🔴 YO'L (track) RANGI TUZATILDI (2026-08-11): eskisida
 * `rgba(255,255,255,.07)` — ya'ni `bg-white/[0.07]` edi. Oq kartochkada oq
 * ustiga 7% oq = 1.02:1, yo'l UMUMAN ko'rinmasdi va har bir bar DOIM to'liq
 * to'lgandek tuyulardi. Bu estetika emas, MA'NO buzilishi: kassir "0-30 kun"
 * guruhini ham, "90+" ni ham "to'la" deb o'qirdi va nisbatni ko'rmasdi.
 * Endi `ink-750` — dizayn tizimining "kuchli hover" sirti (`StudentLearnPage`
 * dagi jarayon halqasining yo'li ham aynan shunga o'tkazilgan).
 *
 * ⚠️ CHEKINISH: `dataviz` ko'nikmasi "meter" uchun yo'lni TO'LDIRISH
 * rangining yorug'roq qadami bo'lishini tavsiya qiladi (yashil ustun ostida
 * och yashil yo'l). Bu yerda NEYTRAL yo'l qoldirildi: to'ldirish rangi
 * prop'dan keladi (`var(--color-chart-*)`), ya'ni uning "yorug'roq qadami"
 * ni CSS'da hisoblash `color-mix` bilan mumkin bo'lsa ham, natijada bitta
 * qoida o'rniga to'rt xil yo'l rangi paydo bo'lardi va oq kartochkada
 * ularning hammasi 1.1:1 atrofida — ya'ni ko'rinish YAXSHILANMASDI.
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
    class="h-[7px] overflow-hidden rounded bg-ink-750"
    aria-hidden="true"
  >
    <div
      class="h-full rounded"
      :style="{ width, backgroundColor: props.color }"
    />
  </div>
</template>
