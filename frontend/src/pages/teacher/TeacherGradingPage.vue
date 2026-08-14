<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  answerFormatsLabel,
  assignmentTitle,
  fetchAssignments,
  fetchSubmissions,
  submissionStatusLabel,
  submissionStatusTone,
} from '@/entities/assignment'
import GradingQueueOverlay from '@/features/grading-queue/ui/GradingQueueOverlay.vue'
import GradeDialog from '@/features/grading/ui/GradeDialog.vue'
import ReopenDialog from '@/features/grading/ui/ReopenDialog.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { SubmissionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * Baholash navbati (ustoz/kurator).
 *
 * Backendda "barcha topshiriqlar bo'yicha navbat" endpoint'i YO'Q —
 * `GET /assignments/{id}/submissions` faqat bitta vazifa kesimida ishlaydi.
 * Shuning uchun oqim ikki bosqichli: vazifa tanlanadi -> uning ishlari
 * yuklanadi. Tanlov chip'lar bilan (telefonda ham bir qatorda skroll qiladi).
 *
 * ═══════════════════════════════════════════════════════════════════════
 * R32 (2026-08-13) — SAHIFA VAZIFA YARATMAYDI
 * ═══════════════════════════════════════════════════════════════════════
 * Loyiha egasi: *"teacher vazifa yaratishi kerakmas, o'quv bo'limi yaratadi
 * vazifalarni"*. Shuning uchun "Yangi vazifa" va "Tahrirlash" tugmalari,
 * `AssignmentFormDialog` bilan birga, bu sahifadan OLIB TASHLANDI.
 *
 * ★ SERVER BIRINCHI, UI IKKINCHI: qoida `AssignmentsController` da
 * (`Academic,Admin`) va `AssignmentService.EnsureCanCreate` da. Bu yerdagi
 * o'zgarish faqat "bosilsa 403 qaytaradigan tugma" ni yo'q qilish uchun —
 * UI hech qachon ruxsatning YAGONA joyi emas.
 *
 * ⚠️ BAHOLASH TO'LIQ QOLDI: ro'yxat, tekshirish navbati, "Baholash" va
 * "Qayta yuborish" — hammasi avvalgidek ishlaydi. Talab faqat YARATISHGA
 * tegishli edi.
 *
 * ★ RO'YXAT hamon `GET /assignments` dan keladi va u ustozga o'z
 * guruhlarining vazifalari + BARCHA kurs vazifalarini beradi (server
 * qoidasi o'zgarmadi) — ya'ni o'quv bo'limi yaratgan vazifa shu yerda
 * darhol ko'rinadi.
 */
const queryClient = useQueryClient()

/*
  Navbat ro'yxati: kartochka ↔ jadval CSS emas, `v-if` — `hidden lg:block`
  IKKALA daraxtni ham quradi (telefonda ko'rinmas jadval ham mount bo'lardi).

  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi
  (`style.css` dagi "md va lg haqidagi asosiy qaror" izohi).
  ★ Baholash/qayta yuborish dialoglari holati (`grading`, `reopening`) SHU
  komponentda saqlanadi, almashinadigan daraxtdan TASHQARIDA — ya'ni ekran
  1024px dan o'tganda ochiq dialog yo'qolmaydi.
*/
const { isDesktop } = useBreakpoint()

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'list'],
  queryFn: ({ signal }) => fetchAssignments({ page: 1, pageSize: 50 }, { signal }),
})

const assignments = computed(() => assignmentsQuery.data.value?.items ?? [])
const selectedId = ref<number | null>(null)

// Ro'yxat kelgach birinchi vazifa avtomatik tanlanadi — bo'sh ekran ko'rsatmaymiz.
watch(assignments, (list) => {
  if (selectedId.value === null && list.length > 0) selectedId.value = list[0]?.id ?? null
})

const selected = computed(
  () => assignments.value.find((item) => item.id === selectedId.value) ?? null,
)

const submissionsQuery = useQuery({
  queryKey: ['assignment-submissions', selectedId],
  queryFn: ({ signal }) => {
    const id = selectedId.value
    if (id === null) return Promise.resolve<SubmissionDto[]>([])
    return fetchSubmissions(id, { signal })
  },
  enabled: computed(() => selectedId.value !== null),
})

const submissions = computed(() => submissionsQuery.data.value ?? [])

const assignmentsError = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)
const submissionsError = computed(() =>
  submissionsQuery.error.value !== null ? toUserMessage(submissionsQuery.error.value) : null,
)

const grading = ref<SubmissionDto | null>(null)
const reopening = ref<SubmissionDto | null>(null)

/**
 * Tekshirish navbati — to'liq ekranli tez baholash oqimi.
 *
 * Bu sahifadagi jadval "ko'rib chiqish" uchun (kim topshirdi, qaysi holatda),
 * navbat esa ISHLASH uchun: bitta ekran, klaviatura yorliqlari, ketma-ket
 * o'tish. Ustoz kuniga o'nlab ish tekshiradi va jadvaldan har safar
 * "Baholash" tugmasini qidirish sezilarli sekin.
 */
const queueOpen = ref(false)

/** Navbatga tushadigan ishlar (baholanmaganlar) — tugmadagi sanoq shuning uchun. */
const pendingCount = computed(
  () => submissions.value.filter((item) => item.status !== 'Graded').length,
)

function refreshSubmissions(): void {
  void queryClient.invalidateQueries({ queryKey: ['assignment-submissions'] })
  // Vazifa chip'idagi "baholangan/topshirilgan" sanog'i ham eskiradi.
  void queryClient.invalidateQueries({ queryKey: ['assignments', 'list'] })
}

function handleGraded(): void {
  grading.value = null
  refreshSubmissions()
}
</script>

<template>
  <div>
    <!--
      Sarlavhada AMAL TUGMASI YO'Q (R32): vazifa yaratish o'quv bo'limida.
      Sarlavha matni ham shunga moslandi — "berish" so'zi qolsa, tugmasi
      yo'q va'da bo'lardi.
    -->
    <PageHeader
      title="Tekshirish va baholash"
      subtitle="O‘quv bo‘limi bergan vazifalar bo‘yicha topshirilgan ishlar"
    />

    <DataStatus
      :pending="assignmentsQuery.isPending.value"
      :error="assignmentsError"
      :empty="assignments.length === 0"
      :retrying="assignmentsQuery.isFetching.value"
      :skeleton-rows="2"
      empty-icon="clipboard"
      empty-title="Vazifa yo‘q"
      empty-text="Vazifalarni o‘quv bo‘limi tuzadi. Guruhingizga vazifa berilgach shu yerda ko‘rinadi."
      @retry="assignmentsQuery.refetch()"
    >
      <!-- Vazifa tanlash. Telefonda bir qatorda gorizontal skroll — sahifa emas, SHU blok. -->
      <div class="scroll-x-safe scrollbar-slim -mx-4 mb-4 px-4 sm:mx-0 sm:px-0">
        <div class="flex gap-2 pb-1">
          <button
            v-for="item in assignments"
            :key="item.id"
            type="button"
            class="flex min-h-11 shrink-0 items-center gap-2 rounded-lg border px-3 text-xs font-medium transition-colors"
            :class="
              item.id === selectedId
                ? 'border-brand-500 bg-brand-500/16 text-brand-400'
                : 'border-line bg-ink-800 text-slate-300 hover:bg-ink-750'
            "
            @click="selectedId = item.id"
          >
            <span
              class="max-w-40 truncate"
              v-text="assignmentTitle(item.title, item.id)"
            />
            <span class="rounded-full bg-ink-950/60 px-1.5 py-0.5 tabular-nums text-[11px]">
              {{ item.gradedCount }}/{{ item.submissionCount }}
            </span>
          </button>
        </div>
      </div>

      <BaseCard
        v-if="selected !== null"
        flush
        :title="assignmentTitle(selected.title, selected.id)"
        :subtitle="`Maksimal ball: ${selected.maxScore} · Topshirilgan: ${selected.submissionCount} · Baholangan: ${selected.gradedCount}`"
      >
        <template #actions>
          <!--
            Navbat — ASOSIY harakat: ustoz odatda ro'yxatni ko'rish uchun
            emas, ishlarni tekshirish uchun keladi. Baholanmagan ish
            bo'lmasa tugma o'chiriladi (bo'sh navbat ochilmasin).
          -->
          <BaseButton
            size="sm"
            :disabled="pendingCount === 0"
            @click="queueOpen = true"
          >
            <template #icon>
              <AppIcon
                name="check-square"
                :size="13"
              />
            </template>
            Tekshirish navbati
            <span
              v-if="pendingCount > 0"
              class="tabular-nums"
            >· {{ pendingCount }}</span>
          </BaseButton>
          <!--
            "Tahrirlash" OLIB TASHLANDI (R32). O'rniga MATN qoldirildi:
            ustoz shart noto'g'ri deb hisoblasa, kimga murojaat qilishini
            bilishi kerak — tugmaning jimgina yo'qolishi "sindi" degan
            taassurot qoldirardi.
          -->
          <span class="text-[11px] text-dim">
            Vazifa shartini o‘quv bo‘limi tahrirlaydi
          </span>
        </template>

        <div class="p-3.5 sm:p-5">
          <dl class="mb-4 flex flex-wrap gap-x-4 gap-y-1.5 text-xs text-slate-400">
            <div
              v-if="selected.dueAt !== null"
              class="inline-flex items-center gap-1.5"
            >
              <AppIcon
                name="clock"
                :size="13"
              />
              <span
                class="tabular-nums"
                v-text="formatDateTime(selected.dueAt)"
              />
            </div>
            <div class="text-dim">
              Javob turi: {{ answerFormatsLabel(selected.allowedFormats) }}
            </div>
          </dl>

          <DataStatus
            :pending="submissionsQuery.isPending.value"
            :error="submissionsError"
            :empty="submissions.length === 0"
            :retrying="submissionsQuery.isFetching.value"
            :skeleton-rows="2"
            empty-icon="check"
            empty-title="Navbat bo‘sh"
            empty-text="Bu vazifa bo‘yicha hali hech kim ish topshirmagan."
            @retry="submissionsQuery.refetch()"
          >
            <!-- Telefon/planshet: kartochka -->
            <ul
              v-if="!isDesktop"
              class="space-y-2"
            >
              <li
                v-for="item in submissions"
                :key="item.id"
                class="rounded-lg border border-line bg-ink-950 p-3"
              >
                <div class="flex items-start justify-between gap-2">
                  <p
                    class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                    v-text="item.studentName ?? '—'"
                  />
                  <BaseBadge :tone="submissionStatusTone(item.status)">
                    {{ submissionStatusLabel(item.status) }}
                  </BaseBadge>
                </div>
                <p class="mt-1 text-xs tabular-nums text-slate-400">
                  {{ formatDateTime(item.submittedAt) }} · {{ item.attemptNumber }}-urinish
                  <span
                    v-if="item.isLate"
                    class="text-amber-400"
                  > · kechikkan</span>
                  <span
                    v-if="item.allowResubmit"
                    class="text-brand-400"
                  > · qayta yuborishga ruxsat bor</span>
                </p>
                <div class="mt-2 flex flex-wrap items-center justify-between gap-2">
                  <span class="text-xs tabular-nums text-slate-300">
                    Ball: {{ item.score ?? '—' }} / {{ selected.maxScore }}
                  </span>
                  <div class="flex items-center gap-2">
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="reopening = item"
                    >
                      Qayta yuborish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      @click="grading = item"
                    >
                      Baholash
                    </BaseButton>
                  </div>
                </div>
              </li>
            </ul>

            <!-- Desktop (≥1024px): jadval -->
            <div
              v-else
              class="scroll-x-safe scrollbar-slim"
            >
              <table class="zn-table">
                <thead>
                  <tr>
                    <th>O‘quvchi</th>
                    <th>Topshirilgan</th>
                    <th>Urinish</th>
                    <th>Holat</th>
                    <th>Ball</th>
                    <th />
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="item in submissions"
                    :key="item.id"
                  >
                    <td
                      class="font-medium text-slate-100"
                      v-text="item.studentName ?? '—'"
                    />
                    <td class="tabular-nums text-slate-400">
                      {{ formatDateTime(item.submittedAt) }}
                      <span
                        v-if="item.isLate"
                        class="text-amber-400"
                      >(kech)</span>
                    </td>
                    <td
                      class="tabular-nums text-slate-400"
                      v-text="item.attemptNumber"
                    />
                    <td>
                      <BaseBadge :tone="submissionStatusTone(item.status)">
                        {{ submissionStatusLabel(item.status) }}
                      </BaseBadge>
                      <span
                        v-if="item.allowResubmit"
                        class="ml-1.5 text-[11px] text-brand-400"
                      >ruxsat</span>
                    </td>
                    <td class="tabular-nums text-slate-200">
                      {{ item.score ?? '—' }} / {{ selected.maxScore }}
                    </td>
                    <td>
                      <div class="flex items-center justify-end gap-1.5">
                        <BaseButton
                          size="sm"
                          variant="ghost"
                          @click="reopening = item"
                        >
                          Qayta yuborish
                        </BaseButton>
                        <BaseButton
                          size="sm"
                          variant="secondary"
                          @click="grading = item"
                        >
                          Baholash
                        </BaseButton>
                      </div>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </DataStatus>
        </div>
      </BaseCard>
    </DataStatus>

    <!--
      Navbat `v-if` bilan: yopilganda komponent butunlay yo'q qilinadi va
      uning klaviatura tinglovchisi ham `document` dan olinadi (aks holda
      ro'yxatda raqam bosilganda ko'rinmas navbatga baho qo'yilardi).
    -->
    <GradingQueueOverlay
      v-if="queueOpen && selected !== null"
      :assignment="selected"
      @close="queueOpen = false"
      @changed="refreshSubmissions"
    />

    <GradeDialog
      :submission="grading"
      :max-score="selected?.maxScore ?? 0"
      @close="grading = null"
      @graded="handleGraded"
    />

    <ReopenDialog
      :submission="reopening"
      @close="reopening = null"
      @reopened="refreshSubmissions"
    />
  </div>
</template>
