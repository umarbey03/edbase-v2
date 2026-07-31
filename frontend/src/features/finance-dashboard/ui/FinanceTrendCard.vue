<script setup lang="ts">
import { computed } from 'vue'

import { periodLabel } from '@/entities/payment'
import { formatMoney } from '@/shared/lib/money'
import type { PaymentMonthSummaryDto } from '@/shared/types'
import { AppIcon, BaseCard } from '@/shared/ui'

import {
  barPercent,
  monthTick,
  TREND_COLLECTED_COLOR,
  TREND_PLANNED_COLOR,
  trendMax,
} from '../model/finance-view'

/**
 * "OXIRGI 12 OY" dinamikasi (academic.html, 885–889 va 2688–2704).
 *
 * ★★ GRAFIK KUTUBXONASI QO'SHILMADI — QAROR VA SABABI:
 *
 *  Loyihada bundle o'lchami ONGLI boshqariladi (Sentry SDK aynan shu sababli
 *  dinamik yuklanadi, `vendor` 72 KB da ushlab turiladi). Eng yengil chart
 *  kutubxonasi ham gzip'dan keyin 15–40 KB qo'shadi va o'z rang tizimi,
 *  o'z tooltip'i, o'z DOM ustuni bilan keladi — ya'ni temani (`data-theme`)
 *  ikkinchi marta sozlash kerak bo'lardi.
 *
 *  Bu yerdagi vazifa esa 12 × 2 ta to'rtburchak: CSS `flex` + foizli
 *  balandlik buni AYNAN eski ilovadagidek chizadi (eski kod ham shunday
 *  qilgan — `academic.html` 2691-qator). Natijada qo'shilgan bog'liqlik = 0,
 *  bundle o'sishi = 0 bayt, ko'rinish esa eskisining nusxasi.
 *
 *  Kutubxona faqat o'q, zum, brush yoki bir necha o'nlab nuqta kerak
 *  bo'lganda oqlanardi — bu yerda ularning birortasi yo'q.
 *
 * ★ Server DOIM 12 ta oy yuboradi (bo'sh bazada ham, nol qiymatlar bilan),
 * shuning uchun diagramma bo'sh massivdan yiqilmaydi.
 */
const props = defineProps<{ months: readonly PaymentMonthSummaryDto[] }>()

const max = computed(() => trendMax(props.months))

/** Sichqoncha ustiga kelganda chiqadigan matn — eski `title` atributi. */
function tooltip(month: PaymentMonthSummaryDto): string {
  return `${periodLabel(month.period)} — reja ${formatMoney(month.billed)}, yig‘ilgan ${formatMoney(month.collected)}`
}
</script>

<template>
  <BaseCard>
    <!-- Sarlavhadan oldingi diagramma ikonkasi eski dizayndan; `BaseCard`
         ning `title` prop'i faqat matn qabul qiladi, shuning uchun qo'lda. -->
    <header class="mb-4">
      <h2 class="flex items-center gap-2 text-[15px] font-semibold sm:text-base">
        <AppIcon
          name="chart"
          :size="17"
        />
        Oxirgi 12 oy
      </h2>
    </header>

    <!--
      Hamma oy nol bo'lsa (bo'sh baza) ustunlar balandligi 0 bo'lib, 130px
      lik bo'sh quti qolardi — u sahifa buzilgandek ko'rinardi. Shuning
      uchun bunday holatda matn chiziladi (raqamlar baribir 0, `NaN` emas).
    -->
    <p
      v-if="max === 0"
      class="text-xs text-muted"
    >
      Oxirgi 12 oyda yozuv yo‘q.
    </p>

    <!-- Telefonda 12 ta ustun siqilib o'qilmay qoladi — shuning uchun eng
         kichik kenglik berilib, gorizontal skroll qoldirilgan. -->
    <div
      v-else
      class="scroll-x-safe scrollbar-slim"
    >
      <div class="min-w-[420px]">
        <div class="flex h-[130px] items-end gap-1.5">
          <div
            v-for="month in props.months"
            :key="month.period"
            class="flex flex-1 flex-col items-center gap-1"
            :title="tooltip(month)"
          >
            <div class="flex h-[100px] w-full items-end gap-0.5">
              <div
                class="flex-1 rounded-t-[3px]"
                :style="{
                  height: `${barPercent(month.billed, max)}%`,
                  backgroundColor: TREND_PLANNED_COLOR,
                }"
              />
              <div
                class="flex-1 rounded-t-[3px]"
                :style="{
                  height: `${barPercent(month.collected, max)}%`,
                  backgroundColor: TREND_COLLECTED_COLOR,
                }"
              />
            </div>
            <span
              class="text-[9.5px] text-muted"
              v-text="monthTick(month.period)"
            />
          </div>
        </div>

        <div class="mt-3 flex gap-4 text-[11.5px] text-muted">
          <span class="inline-flex items-center gap-1.5">
            <span
              class="size-2.5 rounded-sm"
              :style="{ backgroundColor: TREND_PLANNED_COLOR }"
            />
            Reja
          </span>
          <span class="inline-flex items-center gap-1.5">
            <span
              class="size-2.5 rounded-sm"
              :style="{ backgroundColor: TREND_COLLECTED_COLOR }"
            />
            Yig‘ilgan
          </span>
        </div>
      </div>
    </div>
  </BaseCard>
</template>
