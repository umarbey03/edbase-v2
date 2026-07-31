<script setup lang="ts">
import { computed } from 'vue'

import { formatMoney, sumMoney } from '@/shared/lib/money'
import type { PaymentAgingBucketDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard } from '@/shared/ui'

import { agingColor, agingLabel } from '../model/finance-view'
import FinanceBar from './FinanceBar.vue'

/**
 * "QARZ YOSHI" — kassir uchun eng muhim jadval (academic.html, 875–880 va
 * 2674–2686). Qaysi qarz eskirib ketyapti degan savolga shu javob beradi.
 *
 * ★ BU RAQAMLAR BUGUNGI HOLAT va davr filtriga BOG'LIQ EMAS. Kassir davrni
 * o'zgartirib, qarz o'zgarmaganda buni bug deb o'ylamasligi uchun sarlavha
 * yonida "bugungi holat" nishoni turadi.
 *
 * Server DOIM 4 ta guruh yuboradi, shuning uchun setka bo'sh massivdan
 * yiqilmaydi; bu yerdagi yagona shart — qarz umuman yo'qmi.
 */
const props = defineProps<{ buckets: readonly PaymentAgingBucketDto[] }>()

/**
 * Eng baland ustun. Eski ilovada `Math.max(..., 1)` yozilgan edi — nolga
 * bo'lishdan saqlash uchun; bu yerda uni `barPercent` o'zi hal qiladi,
 * shuning uchun sun'iy `1` kerak emas.
 */
const maxAmount = computed(() => {
  let max = 0
  for (const bucket of props.buckets) if (bucket.amount > max) max = bucket.amount
  return max
})

/**
 * Umumiy qarz — TIYINDA qo'shiladi (`sumMoney`), suzuvchi nuqta xatosi
 * "qarz yo'q" ni "0,0000001" ga aylantirmasin.
 */
const total = computed(() => sumMoney(props.buckets.map((bucket) => bucket.amount)))
</script>

<template>
  <BaseCard>
    <!--
      Sarlavha `BaseCard` ning `title` prop'i orqali emas, qo'lda chizilgan:
      eski dizaynda h2 dan oldin QIZIL ogohlantirish ikonkasi turadi va
      `title` faqat oddiy matnni qabul qiladi. Kartochka SIRTI baribir
      `BaseCard` niki — yangi kartochka komponenti yaratilmagan.
    -->
    <header class="mb-3.5 flex flex-wrap items-center justify-between gap-2">
      <h2 class="flex items-center gap-2 text-[15px] font-semibold sm:text-base">
        <AppIcon
          name="alert"
          :size="17"
          class="text-rose-500"
        />
        Qarz yoshi
        <BaseBadge tone="neutral">
          bugungi holat
        </BaseBadge>
      </h2>
      <span class="text-xs text-muted">Eng eski qarz — eng xavflisi</span>
    </header>

    <p
      v-if="total === 0"
      class="text-xs text-muted"
    >
      Qarz yo‘q.
    </p>

    <div
      v-else
      class="grid grid-cols-[repeat(auto-fit,minmax(150px,1fr))] gap-3"
    >
      <div
        v-for="bucket in props.buckets"
        :key="bucket.bucket"
        class="rounded-[10px] border border-line px-3.5 py-3"
      >
        <div class="mb-2 flex items-baseline justify-between gap-2">
          <b
            class="text-[13px]"
            :style="{ color: agingColor(bucket.bucket) }"
            v-text="agingLabel(bucket.bucket)"
          />
          <!-- Eski ilovada faqat "N ta" bor edi; oylar soni QO'SHIMCHA — u
               qarzning nechta oyga tarqalganini ko'rsatadi. -->
          <span class="shrink-0 text-[11px] text-muted">
            {{ bucket.students }} ta · {{ bucket.months }} oy
          </span>
        </div>
        <p
          class="mb-2 text-[17px] font-bold tabular-nums"
          v-text="formatMoney(bucket.amount)"
        />
        <FinanceBar
          :value="bucket.amount"
          :max="maxAmount"
          :color="agingColor(bucket.bucket)"
        />
      </div>
    </div>
  </BaseCard>
</template>
