<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchTeacherReviewsOverview, TeacherReviewsDrawer } from '@/features/session-review'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import type { TeacherReviewOverviewDto } from '@/shared/types'
import { AppIcon, BaseCard, DataStatus, PageHeader } from '@/shared/ui'
import { RecordingBoard } from '@/widgets/recording-board'

/**
 * DARS YOZUVLARI (barcha guruhlar).
 *
 * ★ SARLAVHA VA IZOH ESKI ILOVADAN AYNAN (`academic.html`, 679–681-qatorlar):
 *   <h1>Dars yozuvlari</h1>
 *   <div class="sub">Barcha guruhlarda o'tilgan jonli darslarning to'liq
 *                    video yozuvlari</div>
 * Menyudagi band ham o'sha nom bilan va o'sha o'rinda ("Testlar" dan keyin).
 *
 * ════════════════════════════════════════════════════════════════════════
 *  IKKI BO'LIM (2026-08-16 talabi)
 * ════════════════════════════════════════════════════════════════════════
 * "Yozuvlar" — video ro'yxati (o'zgarmagan xatti-harakat). "Tahlillar" —
 * YANGI: *"o'qituvchilar jadval ko'rinishida bo'lishi kerak va har bir
 * ustoz ustiga bosilganda ... har bir qilingan tahlillari ko'rinib
 * turishi kerak"*. Ikkalasi ALOHIDA: birinchisi VIDEO haqida, ikkinchisi
 * o'sha videolarga yozilgan SIFAT XULOSALARI (R29) haqida — bitta ro'yxatga
 * qo'shilsa, "bu yerda nima ko'rsatilyapti — yozuvmi, tahlilmi" degan
 * chalkashlik keltirardi.
 */
const SECTIONS = [
  { key: 'recordings', label: 'Yozuvlar', icon: 'camera' },
  { key: 'reviews', label: 'Tahlillar', icon: 'check-square' },
] as const

const activeTab = ref<(typeof SECTIONS)[number]['key']>('recordings')

/* ------------------------------------------------------- Tahlillar jadvali */

const teachersQuery = useQuery({
  queryKey: ['session-reviews', 'teachers-overview'],
  queryFn: ({ signal }) => fetchTeacherReviewsOverview({ signal }),
  enabled: computed(() => activeTab.value === 'reviews'),
})

const teacherRows = computed(() => teachersQuery.data.value ?? [])
const teachersError = computed(() =>
  teachersQuery.error.value !== null ? toUserMessage(teachersQuery.error.value) : null,
)

/** Ochiq drawer — `null` bo'lsa yopiq. */
const selectedTeacher = ref<TeacherReviewOverviewDto | null>(null)

function openTeacher(row: TeacherReviewOverviewDto): void {
  selectedTeacher.value = row
}
</script>

<template>
  <div>
    <PageHeader
      title="Dars yozuvlari"
      subtitle="Barcha guruhlarda o‘tilgan jonli darslarning to‘liq video yozuvlari"
    />

    <div
      class="mb-5 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="section in SECTIONS"
        :key="section.key"
        type="button"
        role="tab"
        :aria-selected="activeTab === section.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          activeTab === section.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="activeTab = section.key"
      >
        <AppIcon
          :name="section.icon"
          :size="15"
        />
        {{ section.label }}
      </button>
    </div>

    <RecordingBoard v-if="activeTab === 'recordings'" />

    <template v-else>
      <BaseCard
        title="Tahlillar"
        subtitle="Xodim (ustoz/kurator) ustiga bosib, uning barcha dars tahlillarini ko‘ring."
        flush
      >
        <DataStatus
          :pending="teachersQuery.isPending.value"
          :error="teachersError"
          :empty="teacherRows.length === 0"
          :retrying="teachersQuery.isFetching.value"
          :skeleton-rows="3"
          empty-icon="check-square"
          empty-title="Hali tahlil yo‘q"
          empty-text="Dars yozuviga birinchi sifat tahlilini yozing."
          @retry="teachersQuery.refetch()"
        >
          <div class="scroll-x-safe scrollbar-slim">
            <table class="zn-table">
              <thead>
                <tr>
                  <th>Xodim</th>
                  <th>Jami tahlil</th>
                  <th>Tasdiqlangan</th>
                  <th>Muammoli</th>
                  <th>Ko‘rilmagan</th>
                  <th>Oxirgi tahlil</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="row in teacherRows"
                  :key="row.teacherId"
                  class="cursor-pointer hover:bg-ink-800"
                  role="button"
                  tabindex="0"
                  :aria-label="`${row.teacherName} tahlillarini ochish`"
                  @click="openTeacher(row)"
                  @keydown.enter.prevent="openTeacher(row)"
                >
                  <td
                    class="font-medium text-slate-100"
                    v-text="row.teacherName"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.totalReviews"
                  />
                  <td
                    class="tabular-nums text-emerald-400"
                    v-text="row.approvedCount"
                  />
                  <td
                    class="tabular-nums"
                    :class="row.hasIssueCount > 0 ? 'font-semibold text-rose-400' : 'text-dim'"
                    v-text="row.hasIssueCount"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="row.notReviewedCount"
                  />
                  <td
                    class="tabular-nums text-slate-400"
                    v-text="formatDateTimeNumeric(row.lastReviewAt)"
                  />
                </tr>
              </tbody>
            </table>
          </div>
        </DataStatus>
      </BaseCard>

      <TeacherReviewsDrawer
        :teacher-id="selectedTeacher?.teacherId ?? null"
        :teacher-name="selectedTeacher?.teacherName ?? ''"
        @close="selectedTeacher = null"
      />
    </template>
  </div>
</template>
