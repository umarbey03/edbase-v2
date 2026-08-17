<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchAttritionByGroup } from '@/entities/attrition'
import { toUserMessage } from '@/shared/api'
import type { AttritionListParams } from '@/shared/types'
import { AppIcon, DataStatus } from '@/shared/ui'

/**
 * USTOZ → GURUHLAR bo'linishi (2026-08-17).
 *
 * Loyiha egasi: *"ustozni ustiga bosganda drop down holatida qaysi guruhdan
 * nechtadan to'kilgani bo'yicha ma'lumotlar chiqishi kerak, u ham bosilganda
 * qaysi o'quvchilar to'kilgani ... chiqishi kerak"*.
 *
 * ★ YANGI ENDPOINT KERAK BO'LMADI: mavjud `GET /attrition/by-group` allaqachon
 * `teacherId` filtrini qabul qiladi — ya'ni "shu ustozning guruhlari bo'yicha
 * bo'linish" AYNI so'rov, faqat filtri torroq. Panelning boshqa filtrlari
 * (sana oralig'i, tur, probniy) ham `params` orqali BIRGA uzatiladi, aks
 * holda ochilgan bo'lim tepadagi filtrga qaramay boshqa raqam ko'rsatardi.
 *
 * ★ FAQAT OCHILGANDA YUKLANADI (`enabled`): ustozlar ro'yxati 10-20 qator
 * bo'lishi mumkin, hammasi uchun oldindan yuklash keraksiz so'rov edi.
 */
const props = defineProps<{
  teacherId: number | null
  /** Paneldagi joriy filtr — ochilgan bo'lim AYNI kesimni ko'rsatishi uchun. */
  params: AttritionListParams
}>()

const emit = defineEmits<{ openGroup: [groupId: number, groupName: string] }>()

const query = useQuery({
  queryKey: ['attrition', 'by-group', 'for-teacher', computed(() => props.teacherId), computed(() => props.params)],
  queryFn: ({ signal }) =>
    fetchAttritionByGroup({ ...props.params, teacherId: props.teacherId ?? undefined }, { signal }),
  // ⚠️ `teacherId === null` — "ustoz tayinlanmagan" guruhlar to'plami; bu
  //    HAM haqiqiy holat, shuning uchun `null` bo'lsa ham so'rov yuboriladi.
  //    Farqi: `undefined` yuborilsa filtr UMUMAN qo'llanmaydi.
  enabled: true,
})

const rows = computed(() => query.data.value ?? [])

const errorMessage = computed(() =>
  query.error.value !== null ? toUserMessage(query.error.value) : null,
)
</script>

<template>
  <div class="bg-ink-800/60 px-3.5 py-3">
    <DataStatus
      :pending="query.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="query.isFetching.value"
      :skeleton-rows="2"
      empty-icon="grid"
      empty-title="Guruh topilmadi"
      empty-text="Bu ustozda tanlangan davrda a’zolik o‘zgarishi bo‘lmagan."
      @retry="query.refetch()"
    >
      <p class="mb-2 text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
        Guruhlar bo‘yicha ({{ rows.length }})
      </p>

      <ul class="space-y-1.5">
        <li
          v-for="row in rows"
          :key="row.groupId"
        >
          <!--
            Guruh qatori — BOSILADIGAN: o'quvchilar ro'yxati modalda
            ochiladi (uch qatlamli ochiladigan ro'yxat o'rniga modal —
            aks holda jadval ichida uchinchi qatlam sig'masdi).
          -->
          <button
            type="button"
            class="flex w-full flex-wrap items-center gap-2 rounded-lg border border-line bg-ink-900 px-2.5 py-2 text-left text-xs transition-colors hover:border-line-strong hover:bg-ink-800"
            @click="emit('openGroup', row.groupId, row.groupName)"
          >
            <span
              class="min-w-0 flex-1 truncate font-medium text-slate-100"
              v-text="row.groupName"
            />

            <span class="flex shrink-0 items-center gap-2.5 tabular-nums">
              <span
                :class="row.stopped > 0 ? 'font-semibold text-rose-400' : 'text-dim'"
                :title="`Chiqarilgan: ${row.stopped}`"
              >{{ row.stopped }} chiqdi</span>
              <span
                v-if="row.paused > 0"
                class="text-amber-400"
                :title="`Muzlatilgan: ${row.paused}`"
              >{{ row.paused }} muzlatildi</span>
              <span
                v-if="row.moved > 0"
                class="text-slate-400"
                :title="`Ko‘chirilgan: ${row.moved}`"
              >{{ row.moved }} ko‘chdi</span>
              <span
                class="text-dim"
                title="Hozir guruhda faol o‘quvchilar"
              >{{ row.activeMembers }} faol</span>
            </span>

            <AppIcon
              name="chevron-right"
              :size="14"
              class="shrink-0 text-dim"
            />
          </button>
        </li>
      </ul>
    </DataStatus>
  </div>
</template>
