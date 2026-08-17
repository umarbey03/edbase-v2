<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import {
  eventKindLabel,
  eventKindTone,
  fetchAttrition,
  fetchAttritionGroupDetail,
  trialLabel,
  trialTone,
} from '@/entities/attrition'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric, formatDateTimeNumeric } from '@/shared/lib/datetime'
import type { AttritionListParams } from '@/shared/types'
import { BaseBadge, BaseButton, BaseModal, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  GURUH TO'KILISHLARI — TO'LIQ MA'LUMOT MODALI (2026-08-17)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"ustiga bosganda to'liq to'kilishlar haqida ma'lumotlar
 * modali chiqishi kerak bunda guruh haqida to'liq ma'lumotlar (ustoz,
 * boshlangan sanasi, hozir qaysi modul qaysi darsga kelgani). qaysi
 * o'quvchilar nima sababdan to'kilgani bo'yicha ma'lumotlar bo'lishi kerak"*.
 *
 * ★ IKKI SO'ROV, BITTA MODAL:
 *   • sarlavha ma'lumoti — `GET /attrition/group/{id}` (guruh + sur'at);
 *   • o'quvchilar ro'yxati — MAVJUD `GET /attrition?groupId=…` (yangi
 *     endpoint yozilmadi, sahifalash va saralash tekinga keldi).
 *
 * ★ PANEL FILTRI SAQLANADI (`params`): modal tepadagi jadval bilan AYNI
 * kesimni ko'rsatadi — aks holda "jadvalda 4, modalda 7" degan chalkashlik
 * chiqardi.
 *
 * ★ "QAYSI DARSGA KELGANI" — ORDINAL HISOB: `LiveSession` da dars mavzusiga
 * havola YO'Q, shuning uchun pozitsiya yakunlangan darslar SONI bo'yicha
 * aniqlanadi (gating ham AYNAN shunday ishlaydi — batafsil backend
 * `GroupPaceDto` izohida).
 */
const props = defineProps<{
  /** `null` — modal yopiq. */
  groupId: number | null
  groupName: string
  /** Paneldagi joriy filtr. */
  params: AttritionListParams
}>()

const emit = defineEmits<{ close: [] }>()

const isOpen = computed(() => props.groupId !== null)

const detailQuery = useQuery({
  queryKey: ['attrition', 'group-detail', computed(() => props.groupId), computed(() => props.params)],
  queryFn: ({ signal }) => fetchAttritionGroupDetail(props.groupId!, props.params, { signal }),
  enabled: isOpen,
})

const eventsQuery = useQuery({
  queryKey: ['attrition', 'group-events', computed(() => props.groupId), computed(() => props.params)],
  queryFn: ({ signal }) =>
    fetchAttrition(
      { ...props.params, groupId: props.groupId ?? undefined, pageSize: 100, sort: 'Date', desc: true },
      { signal },
    ),
  enabled: isOpen,
})

const detail = computed(() => detailQuery.data.value ?? null)
const events = computed(() => eventsQuery.data.value?.items ?? [])

const detailError = computed(() =>
  detailQuery.error.value !== null ? toUserMessage(detailQuery.error.value) : null,
)
const eventsError = computed(() =>
  eventsQuery.error.value !== null ? toUserMessage(eventsQuery.error.value) : null,
)

/** Kurs tugagan bo'lsa navbatdagi dars bo'lmaydi. */
const courseFinished = computed(
  () => detail.value !== null && detail.value.nextPosition === null && detail.value.totalLessons > 0,
)
</script>

<template>
  <BaseModal
    :open="isOpen"
    wide
    :title="`To‘kilishlar — ${detail?.groupName ?? props.groupName}`"
    @close="emit('close')"
  >
    <!-- ══════════════ GURUH HAQIDA ══════════════ -->
    <DataStatus
      :pending="detailQuery.isPending.value"
      :error="detailError"
      :empty="false"
      :retrying="detailQuery.isFetching.value"
      :skeleton-rows="2"
      @retry="detailQuery.refetch()"
    >
      <div
        v-if="detail !== null"
        class="mb-4 rounded-xl border border-line bg-ink-900 p-3.5"
      >
        <dl class="grid grid-cols-1 gap-x-4 gap-y-2 text-xs sm:grid-cols-2">
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              Kurs:
            </dt>
            <dd
              class="min-w-0 truncate text-slate-200"
              v-text="detail.courseName ?? '—'"
            />
          </div>
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              Ustoz:
            </dt>
            <dd
              class="min-w-0 truncate font-medium text-slate-100"
              v-text="detail.teacherName ?? 'Tayinlanmagan'"
            />
          </div>
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              Kurator:
            </dt>
            <dd
              class="min-w-0 truncate text-slate-200"
              v-text="detail.assistantName ?? '—'"
            />
          </div>
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              Boshlangan:
            </dt>
            <dd class="tabular-nums text-slate-200">
              {{ formatDateNumeric(detail.startDate) }}
              <span class="text-dim">→ {{ formatDateNumeric(detail.endDate) }}</span>
            </dd>
          </div>
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              Hozir faol:
            </dt>
            <dd
              class="tabular-nums text-slate-200"
              v-text="`${detail.activeMembers} o‘quvchi`"
            />
          </div>
          <div class="flex gap-1.5">
            <dt class="shrink-0 text-slate-400">
              O‘tilgan dars:
            </dt>
            <dd class="tabular-nums text-slate-200">
              {{ detail.coveredLessons }} / {{ detail.totalLessons }}
            </dd>
          </div>
        </dl>

        <!--
          ★ "HOZIR QAYSI MODUL, QAYSI DARS" — ALOHIDA, KO'ZGA TASHLANADIGAN
          BLOK: loyiha egasi aynan shu ma'lumotni so'radi va uni yuqoridagi
          ro'yxat ichida yo'qotib qo'ymaslik kerak.
        -->
        <div class="mt-3 border-t border-line pt-3">
          <p class="mb-1 text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
            Kursda qayerda
          </p>
          <p
            v-if="detail.currentPosition !== null"
            class="text-sm font-semibold text-slate-100"
            v-text="detail.currentPosition"
          />
          <p
            v-else
            class="text-sm text-dim"
          >
            Hali birorta dars o‘tilmagan
          </p>

          <p
            v-if="detail.nextPosition !== null"
            class="mt-0.5 text-xs text-slate-400"
          >
            Navbatdagi: <span class="text-slate-300">{{ detail.nextPosition }}</span>
          </p>
          <p
            v-else-if="courseFinished"
            class="mt-0.5 text-xs text-emerald-400"
          >
            Kurs dasturi tugagan
          </p>
        </div>

        <!-- To'kilish yig'masi -->
        <div class="mt-3 flex flex-wrap gap-2 border-t border-line pt-3">
          <span class="rounded-lg bg-ink-800 px-2.5 py-1 text-xs">
            <span
              class="font-semibold tabular-nums"
              :class="detail.stopped > 0 ? 'text-rose-400' : 'text-slate-300'"
              v-text="detail.stopped"
            />
            <span class="text-slate-400"> chiqarilgan</span>
          </span>
          <span class="rounded-lg bg-ink-800 px-2.5 py-1 text-xs">
            <span
              class="font-semibold tabular-nums text-amber-400"
              v-text="detail.paused"
            />
            <span class="text-slate-400"> muzlatilgan</span>
          </span>
          <span class="rounded-lg bg-ink-800 px-2.5 py-1 text-xs">
            <span
              class="font-semibold tabular-nums text-slate-300"
              v-text="detail.moved"
            />
            <span class="text-slate-400"> ko‘chirilgan</span>
          </span>
          <span class="rounded-lg bg-ink-800 px-2.5 py-1 text-xs">
            <span
              class="font-semibold tabular-nums text-amber-400"
              v-text="detail.trialLosses"
            />
            <span class="text-slate-400"> probniy yo‘qotish</span>
          </span>
        </div>
      </div>
    </DataStatus>

    <!-- ══════════════ KIM, NIMA SABABDAN ══════════════ -->
    <p class="mb-2 text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
      Kim va nima sababdan
    </p>

    <DataStatus
      :pending="eventsQuery.isPending.value"
      :error="eventsError"
      :empty="events.length === 0"
      :retrying="eventsQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="users"
      empty-title="Hodisa yo‘q"
      empty-text="Tanlangan davrda bu guruhda a’zolik o‘zgarishi bo‘lmagan."
      @retry="eventsQuery.refetch()"
    >
      <ul class="space-y-1.5">
        <li
          v-for="row in events"
          :key="row.eventId"
          class="rounded-lg border border-line bg-ink-900 px-2.5 py-2 text-xs"
        >
          <div class="flex flex-wrap items-center gap-2">
            <span
              class="min-w-0 flex-1 truncate font-medium text-slate-100"
              v-text="row.studentName"
            />
            <BaseBadge
              size="xs"
              :tone="eventKindTone(row.kind)"
            >
              {{ eventKindLabel(row.kind) }}
            </BaseBadge>
            <BaseBadge
              size="xs"
              :tone="trialTone(row.isTrial)"
            >
              {{ trialLabel(row.isTrial) }}
            </BaseBadge>
          </div>

          <p class="mt-1 text-slate-400">
            <span
              class="tabular-nums"
              v-text="formatDateTimeNumeric(row.occurredAt)"
            />
            <span> · {{ row.lessonsCompleted }} dars o‘tagan</span>
            <template v-if="row.movedToGroupName !== null">
              <span class="text-slate-500"> → </span>
              <span
                class="text-slate-300"
                v-text="row.movedToGroupName"
              />
            </template>
          </p>

          <p
            v-if="row.reason !== null"
            class="mt-1 text-slate-300"
          >
            <span class="text-slate-400">Sabab: </span>{{ row.reason }}
          </p>
        </li>
      </ul>
    </DataStatus>

    <!--
      ★ STANDART PASTKI PANEL (loyiha egasi, 2026-08-17): faqat o'qish
      uchun tafsilot modallarida ilovadagi qonun — pastda bitta
      "Yopish" tugmasi (`PayrollDetailDialog` bilan AYNI). Bunsiz oyna
      boshqa dialoglardan farq qilib turardi: yopish uchun faqat
      yuqoridagi kichik ✕ qolardi.
    -->
    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
