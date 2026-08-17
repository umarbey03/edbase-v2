<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import {
  checkinStatusLabel,
  checkinStatusTone,
  coverageLabel,
  coverageTone,
  fetchTeacherAvailabilityDetail,
  offerStatusLabel,
  offerStatusTone,
} from '@/entities/teacher-availability'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric, formatDateTimeNumeric } from '@/shared/lib/datetime'
import { BaseBadge, BaseButton, BaseDrawer, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  BITTA KUNLIK JAVOBNING TO'LIQ TAFSILOTI (2026-08-17)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Ro'yxatda faqat YAKUNIY holat ko'rinadi ("o'rinbosar topildi"). Bu
 * modalda esa BUTUN ZANJIR: kimga taklif yuborilgan, kim rad etgan, kim
 * rozi bo'lgan va qachon. O'quv bo'limining "nega bu darsga hech kim
 * topilmadi?" degan savoliga javob AYNAN shu yerda.
 *
 * ★ MA'LUMOT FAQAT SHU YERDA YUKLANADI (`enabled` orqali): taklif tarixi
 * bitta darsga 5-10 qator bo'lishi mumkin — 20 qatorli ro'yxat sahifasida
 * har qator uchun yuklansa, yuzlab keraksiz yozuv tortilardi.
 */
const props = defineProps<{
  /** `null` — modal yopiq. */
  checkinId: number | null
}>()

const emit = defineEmits<{ close: [] }>()

const detailQuery = useQuery({
  queryKey: ['teacher-availability', 'detail', computed(() => props.checkinId)],
  queryFn: ({ signal }) => fetchTeacherAvailabilityDetail(props.checkinId!, { signal }),
  enabled: computed(() => props.checkinId !== null),
})

const detail = computed(() => detailQuery.data.value ?? null)

const errorMessage = computed(() =>
  detailQuery.error.value !== null ? toUserMessage(detailQuery.error.value) : null,
)
</script>

<template>
  <BaseDrawer
    :open="props.checkinId !== null"
    :title="detail !== null ? detail.teacherName : 'Tafsilot'"
    :subtitle="detail !== null ? formatDateNumeric(detail.checkinDate) : ''"
    @close="emit('close')"
  >
    <DataStatus
      :pending="detailQuery.isPending.value"
      :error="errorMessage"
      :empty="false"
      :retrying="detailQuery.isFetching.value"
      :skeleton-rows="3"
      @retry="detailQuery.refetch()"
    >
      <div v-if="detail !== null">
        <!-- ─────────────────── UMUMIY HOLAT ─────────────────── -->
        <div class="mb-4 rounded-xl border border-line bg-ink-900 p-3.5">
          <div class="mb-2 flex flex-wrap items-center gap-2">
            <BaseBadge :tone="checkinStatusTone(detail.status)">
              {{ checkinStatusLabel(detail.status) }}
            </BaseBadge>
            <span
              v-if="detail.unavailableDays !== null"
              class="text-xs text-slate-400"
            >
              {{ detail.unavailableDays === 1 ? 'Faqat bugun' : `${detail.unavailableDays} kunga` }}
            </span>
          </div>

          <dl class="grid grid-cols-1 gap-x-4 gap-y-1.5 text-xs sm:grid-cols-2">
            <div class="flex gap-1.5">
              <dt class="text-slate-400">
                Savol yuborilgan:
              </dt>
              <dd
                class="tabular-nums text-slate-200"
                v-text="formatDateTimeNumeric(detail.sentAt)"
              />
            </div>
            <div class="flex gap-1.5">
              <dt class="text-slate-400">
                Javob berilgan:
              </dt>
              <dd
                class="tabular-nums text-slate-200"
                v-text="detail.respondedAt !== null ? formatDateTimeNumeric(detail.respondedAt) : 'Javob bermagan'"
              />
            </div>
          </dl>

          <p
            v-if="detail.declineReason !== null"
            class="mt-2.5 border-t border-line pt-2.5 text-xs text-slate-300"
          >
            <span class="text-slate-400">Sabab: </span>{{ detail.declineReason }}
          </p>
        </div>

        <!-- ─────────────── TA'SIRLANGAN DARSLAR + TAKLIF TARIXI ─────────────── -->
        <p
          v-if="detail.coverages.length === 0"
          class="text-xs text-dim"
        >
          Bu javob hech qanday darsga ta'sir qilmagan.
        </p>

        <div
          v-for="coverage in detail.coverages"
          :key="coverage.sessionId"
          class="mb-3 rounded-xl border border-line bg-ink-900 p-3.5 last:mb-0"
        >
          <div class="mb-2 flex flex-wrap items-center gap-2">
            <span
              class="tabular-nums text-xs text-slate-400"
              v-text="formatDateTimeNumeric(coverage.scheduledStart)"
            />
            <span
              class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
              v-text="coverage.groupName"
            />
            <BaseBadge :tone="coverageTone(coverage.status)">
              {{ coverageLabel(coverage.status) }}
            </BaseBadge>
          </div>

          <!--
            ★ ANIQ JUMLA — panel ro'yxatidagi bilan AYNI shakl: kim asl
            ustoz, kim o'rniga o'tayotgani so'zma-so'z yozilgan.
          -->
          <p class="mb-2.5 text-xs text-slate-400">
            <span
              class="font-medium text-slate-300"
              v-text="detail.teacherName"
            />
            <span> o‘tolmaydi</span>
            <template v-if="coverage.substituteTeacherName !== null">
              <span class="text-slate-500"> → </span>
              <span
                class="font-semibold text-emerald-400"
                v-text="`${coverage.substituteTeacherName} o‘tib beradi`"
              />
            </template>
            <span
              v-else
              class="text-amber-400"
            > — hali o‘rinbosar topilmadi</span>
          </p>

          <!-- Taklif tarixi -->
          <div v-if="coverage.offers.length > 0">
            <p class="mb-1.5 text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
              Taklif yuborilganlar ({{ coverage.offers.length }})
            </p>
            <ul class="space-y-1">
              <li
                v-for="offer in coverage.offers"
                :key="offer.offerId"
                class="flex flex-wrap items-center gap-2 rounded-lg bg-ink-800 px-2.5 py-1.5 text-xs"
              >
                <span
                  class="min-w-0 flex-1 truncate text-slate-200"
                  v-text="offer.candidateTeacherName"
                />
                <span
                  class="tabular-nums text-dim"
                  v-text="formatDateTimeNumeric(offer.sentAt)"
                />
                <BaseBadge
                  size="xs"
                  :tone="offerStatusTone(offer.status)"
                >
                  {{ offerStatusLabel(offer.status) }}
                </BaseBadge>
              </li>
            </ul>
          </div>
          <p
            v-else
            class="text-xs text-dim"
          >
            Hech kimga taklif yuborilmagan — shu vaqtda bo‘sh ustoz topilmadi.
          </p>
        </div>
      </div>
    </DataStatus>

    <!--
      ★ STANDART PASTKI PANEL: faqat o'qish uchun tafsilot modallarida
      ilovadagi qonun (`PayrollDetailDialog` bilan AYNI) — pastda bitta
      "Yopish" tugmasi.
    -->
    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseDrawer>
</template>
