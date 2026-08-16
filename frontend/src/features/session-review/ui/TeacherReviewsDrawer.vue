<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { reviewVerdictLabel, reviewVerdictTone } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseDrawer, DataStatus } from '@/shared/ui'

import { fetchSessionReviewsByTeacher } from '../api/session-review-api'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  BITTA XODIMNING BARCHA TAHLILLARI — "Tahlillar" panelining ikkinchi
 *  bosqichi (loyiha egasi, 2026-08-16)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Talab: *"o'qituvchilar jadval ko'rinishida bo'lishi kerak va har bir
 * ustoz ustiga bosilganda standart modal(ekranni o'ng tarafidan 85%ini
 * egallab ochiladigan modal) ko'rinishida ochilishi kerak va bu yerda
 * har bir qilingan tahlillari ko'rinib turishi kerak"*.
 *
 * ★ `BaseDrawer` — talabdagi "standart modal" AYNI shu komponent
 * (`SessionReviewModal`/`StudentProfileDrawer` bilan bir xil naqsh:
 * ekranni o'ng tarafidan 85% egallab ochiladi).
 *
 * ★ O'QISH UCHUN, TAHRIRLASH UCHUN EMAS: bu ro'yxat FAQAT ko'rish. Bitta
 * tahlilni o'zgartirish kerak bo'lsa xodim uni "Dars yozuvlari" ro'yxatidan
 * (`RecordingCard` -> `SessionReviewModal`) ochadi — bu yerda ikkinchi
 * tahrirlash yo'li YARATILMAYDI: bitta amal, bitta joy, ikkita ekranda
 * saqlash mantig'ini takrorlash xatoga ochiq bo'lardi.
 */
const props = defineProps<{
  /** `null` — panel yopiq. */
  teacherId: number | null
  teacherName: string
}>()

const emit = defineEmits<{ close: [] }>()

const reviewsQuery = useQuery({
  queryKey: ['session-reviews', 'by-teacher', computed(() => props.teacherId)],
  queryFn: ({ signal }) => fetchSessionReviewsByTeacher(props.teacherId!, { signal }),
  enabled: computed(() => props.teacherId !== null),
})

const reviews = computed(() => reviewsQuery.data.value ?? [])

const errorMessage = computed(() =>
  reviewsQuery.error.value !== null ? toUserMessage(reviewsQuery.error.value) : null,
)
</script>

<template>
  <BaseDrawer
    :open="props.teacherId !== null"
    :title="`Tahlillar — ${props.teacherName}`"
    :subtitle="`Jami: ${reviews.length} ta tahlil`"
    @close="emit('close')"
  >
    <DataStatus
      :pending="reviewsQuery.isPending.value"
      :error="errorMessage"
      :empty="reviews.length === 0"
      :retrying="reviewsQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="check-square"
      empty-title="Tahlil yo‘q"
      empty-text="Bu xodimning darsi hali tahlil qilinmagan."
      @retry="reviewsQuery.refetch()"
    >
      <ul class="space-y-3">
        <li
          v-for="review in reviews"
          :key="review.id"
          class="rounded-xl border border-line bg-ink-950 p-3.5"
        >
          <div class="flex flex-wrap items-start justify-between gap-2">
            <div class="min-w-0">
              <p
                class="truncate text-sm font-semibold text-slate-100"
                v-text="review.sessionTitle ?? review.groupName"
              />
              <p class="mt-0.5 flex flex-wrap items-center gap-x-1.5 text-xs text-slate-400">
                <span v-text="review.groupName" />
                <span aria-hidden="true">·</span>
                <span
                  class="tabular-nums"
                  v-text="formatDateTimeNumeric(review.sessionScheduledStart)"
                />
              </p>
            </div>
            <BaseBadge :tone="reviewVerdictTone(review.verdict)">
              {{ reviewVerdictLabel(review.verdict) }}
            </BaseBadge>
          </div>

          <!-- Mezon ballari (bo'lsa). -->
          <div
            v-if="review.scores.length > 0"
            class="mt-2.5 flex flex-wrap items-center gap-1.5"
          >
            <BaseBadge
              v-for="score in review.scores"
              :key="`${review.id}-${score.criterionName}`"
              tone="neutral"
            >
              {{ score.criterionName }}: {{ score.score }}/{{ score.maxScore }}
            </BaseBadge>
            <span
              v-if="review.scorePercent !== null"
              class="text-xs font-semibold tabular-nums text-slate-300"
            >
              — {{ review.totalScore }}/{{ review.totalMaxScore }} ({{ review.scorePercent }}%)
            </span>
          </div>

          <div class="mt-2.5 grid gap-2 sm:grid-cols-2">
            <div
              v-if="review.plus !== null && review.plus.length > 0"
              class="rounded-lg border border-emerald-500/20 bg-emerald-500/5 p-2.5"
            >
              <p class="mb-1 text-[11px] font-bold uppercase tracking-[1px] text-emerald-400">
                Ijobiy tomonlar
              </p>
              <p
                class="whitespace-pre-line text-xs leading-relaxed text-slate-300"
                v-text="review.plus"
              />
            </div>
            <div
              v-if="review.minus !== null && review.minus.length > 0"
              class="rounded-lg border border-rose-500/20 bg-rose-500/5 p-2.5"
            >
              <p class="mb-1 text-[11px] font-bold uppercase tracking-[1px] text-rose-400">
                Kamchiliklar
              </p>
              <p
                class="whitespace-pre-line text-xs leading-relaxed text-slate-300"
                v-text="review.minus"
              />
            </div>
          </div>

          <div class="mt-2.5 rounded-lg border border-line bg-ink-900 p-2.5">
            <p class="mb-1 text-[11px] font-bold uppercase tracking-[1px] text-slate-400">
              Xulosa
            </p>
            <p
              class="whitespace-pre-line text-xs leading-relaxed text-slate-300"
              v-text="review.conclusion"
            />
          </div>

          <p class="mt-2 flex items-center gap-1.5 text-[11px] text-dim">
            <AppIcon
              name="user"
              :size="12"
            />
            {{ review.authorName }} ·
            <span
              class="tabular-nums"
              v-text="formatDateTimeNumeric(review.createdAt)"
            />
          </p>
        </li>
      </ul>
    </DataStatus>
  </BaseDrawer>
</template>
