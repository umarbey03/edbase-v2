<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { reviewVerdictLabel, reviewVerdictTone } from '@/entities/recording'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import type { SessionReviewDto } from '@/shared/types'
import { BaseBadge, BaseButton, BaseDrawer, DataStatus } from '@/shared/ui'

import { fetchSessionReviewsByTeacher } from '../api/session-review-api'
import SessionReviewModal from './SessionReviewModal.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  BITTA XODIMNING BARCHA TAHLILLARI — "Tahlillar" panelining ikkinchi
 *  bosqichi (loyiha egasi, 2026-08-16, 2026-08-17 da jadvalga o'tkazildi)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Talab (2026-08-16): *"o'qituvchilar jadval ko'rinishida bo'lishi kerak
 * va har bir ustoz ustiga bosilganda standart modal ... ochilishi kerak
 * va bu yerda har bir qilingan tahlillari ko'rinib turishi kerak"*.
 *
 * Talab (2026-08-17): *"...jadval ko'rinishida bo'lishi kerak jadvaldagi
 * barchasi raqamlangan bo'lishi kerak. see button bosilganda modal
 * ochilishi kerak modalda tahlil to'liq ko'rinishi kerak. edit qilish
 * va delete qilish imkoni bo'lishi kerak."*
 *
 * ★ Bu ekranning O'ZI (ustoz ro'yxati -> shu panel) `BaseDrawer`
 * (o'ngdan 85%). Ichkarida "Ko'rish" bosilganda esa AYNI
 * `SessionReviewModal` QAYTA ISHLATILADI — u allaqachon to'liq ko'rish +
 * tahrirlash + o'chirishni qo'llab-quvvatlaydi (R29/R30). Ikkinchi,
 * duragay komponent yozish shu funksiyani TAKRORLAR va ikkalasi vaqt
 * o'tib boshqacha ishlay boshlardi (masalan: bittasida o'chirish bor,
 * ikkinchisida yo'q). Faqat QOBIG'I boshqacha: `as-modal` prop'i uni
 * `BaseModal` sifatida ochadi (`BaseDrawer` ICHIDA yana `BaseDrawer`
 * TAQIQLANGANI uchun — pastdagi izoh).
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

/** Jadvaldan "Ko'rish" bosilgan qator — `SessionReviewModal` shu asosda ochiladi. */
const selectedReview = ref<SessionReviewDto | null>(null)

function openReview(review: SessionReviewDto): void {
  selectedReview.value = review
}

/**
 * Tahrirlash yoki o'chirishdan KEYIN — ro'yxat QAYTA so'raladi (mahalliy
 * taxmin emas, server ma'lumoti). `SessionReviewModal.vue` o'zi yopilmaydi
 * (o'chirilgandan keyin "tahlil hali yozilmagan" holatini ko'rsatadi),
 * shuning uchun bu yerda ham `selectedReview` ni yopish shart emas —
 * xodim "Yopish" ni o'zi bosadi.
 */
function onReviewSaved(): void {
  void reviewsQuery.refetch()
}
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
      <!--
        ★ JADVAL, KARTOCHKA RO'YXATI EMAS (2026-08-17 qarori) — barcha
        qatorlar RAQAMLANGAN. Ustunlar minimal: dars/guruh, sana, xulosa,
        ball — to'liq matn (Ijobiy/Kamchilik/Xulosa) endi jadvalda emas,
        "Ko'rish" bosilganda ochiladigan `SessionReviewModal` da.
      -->
      <div class="scroll-x-safe scrollbar-slim">
        <table class="zn-table">
          <thead>
            <tr>
              <th class="w-10">
                #
              </th>
              <th>Dars / Guruh</th>
              <th>Sana</th>
              <th>Xulosa</th>
              <th>Ball</th>
              <th class="w-24" />
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(review, index) in reviews"
              :key="review.id"
            >
              <td
                class="tabular-nums text-dim"
                v-text="index + 1"
              />
              <td class="min-w-0 max-w-56">
                <p
                  class="truncate font-medium text-slate-100"
                  v-text="review.sessionTitle ?? review.groupName"
                />
                <p
                  class="truncate text-xs text-slate-400"
                  v-text="review.groupName"
                />
              </td>
              <td
                class="tabular-nums text-slate-400"
                v-text="formatDateTimeNumeric(review.sessionScheduledStart)"
              />
              <td>
                <BaseBadge :tone="reviewVerdictTone(review.verdict)">
                  {{ reviewVerdictLabel(review.verdict) }}
                </BaseBadge>
              </td>
              <td class="tabular-nums text-slate-300">
                {{ review.scorePercent !== null ? `${review.totalScore}/${review.totalMaxScore}` : '—' }}
              </td>
              <td>
                <BaseButton
                  size="sm"
                  variant="secondary"
                  @click="openReview(review)"
                >
                  Ko‘rish
                </BaseButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </DataStatus>

    <!--
      🔴 `as-modal` MAJBURIY: bu drawer O'ZI `BaseDrawer`, ichma-ich
      drawer esa TAQIQLANGAN (`BaseDrawer.vue` izohi). `as-modal` shu
      komponentni `BaseModal` sifatida ochadi — `useModalHost` qatlam
      stekiga to'g'ri qo'shiladi (ESC faqat ustki qatlamni yopadi, skroll
      qulfi sanoqli), orqadagi jadval panel ochiq qoladi.
    -->
    <SessionReviewModal
      as-modal
      :session-id="selectedReview?.sessionId ?? null"
      :title="selectedReview?.sessionTitle ?? selectedReview?.groupName ?? ''"
      :group-name="selectedReview?.groupName ?? ''"
      :scheduled-start="selectedReview?.sessionScheduledStart ?? ''"
      @close="selectedReview = null"
      @saved="onReviewSaved"
    />
  </BaseDrawer>
</template>
