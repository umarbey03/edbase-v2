<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchAttendanceSummary } from '@/entities/progress'
import { useStudentSchedule } from '@/features/student-schedule/model/useStudentSchedule'
import NextLessonCard from '@/features/student-schedule/ui/NextLessonCard.vue'
import { useNow } from '@/shared/lib/use-now'
import { AppIcon, BaseButton } from '@/shared/ui'

/**
 * BOSH SAHIFA — eski `#home` bo'limi.
 *
 * Ikki qism: "keyingi dars" kartochkalari (ustoz + kurator) va davomat doirasi.
 *
 * DAVOMAT: `GET /api/v1/progress/attendance` dan `overall` chelagi olinadi —
 * eski ilovada ham doira BARCHA darslarni (ustoz + kurator) ko'rsatardi.
 * Server `teacher`/`assistant` ni alohida beradi, lekin ular reyting va
 * hisobotlar uchun; bosh sahifada bittasini ko'rsatish o'quvchini chalg'itardi.
 */
const now = useNow()
const schedule = useStudentSchedule(now)

const attendanceQuery = useQuery({
  queryKey: ['progress', 'attendance'],
  queryFn: ({ signal }) => fetchAttendanceSummary({}, { signal }),
})

const attendance = computed(() => attendanceQuery.data.value?.overall ?? null)

/** Doira uzunligi: 2 × π × 40 (eski `CIRC` o'zgaruvchisi). */
const RING_CIRCUMFERENCE = 251.3

/**
 * Doiraning to'ldirilmagan qismi. Ma'lumot kelmaguncha doira BO'SH turadi —
 * nol foizli to'la doira "hammasini qoldirgan" degan yolg'on taassurot berardi.
 */
const ringOffset = computed(() => {
  const percent = attendance.value?.percent ?? 0
  return RING_CIRCUMFERENCE * (1 - Math.min(100, Math.max(0, percent)) / 100)
})

/** Dars hali o'tilmagan bo'lsa foiz emas, chiziqcha ko'rsatiladi. */
const hasLessons = computed(() => (attendance.value?.total ?? 0) > 0)

function statValue(value: number | undefined): string {
  if (attendanceQuery.isPending.value) return '…'
  return value === undefined ? '—' : String(value)
}
</script>

<template>
  <div>
    <!-- ============================ Keyingi dars ============================ -->
    <div
      v-if="schedule.isPending.value"
      class="mb-4 h-[190px] animate-pulse rounded-[18px] border border-line bg-ink-900"
    />

    <div
      v-else-if="schedule.error.value !== null"
      class="mb-4 rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
      role="alert"
    >
      <p
        class="text-sm text-rose-200"
        v-text="schedule.error.value"
      />
      <BaseButton
        class="mt-4"
        size="sm"
        variant="secondary"
        :loading="schedule.isFetching.value"
        @click="schedule.refetch()"
      >
        Qayta urinish
      </BaseButton>
    </div>

    <!-- Ikkala tur ham bo'sh bo'lsa — eski ilovadagi bitta umumiy matn. -->
    <div
      v-else-if="schedule.nextTeacher.value === null && schedule.nextAssistant.value === null"
      class="mb-4 rounded-xl border border-line bg-ink-900 px-2.5 py-8 text-center text-sm text-slate-400"
    >
      Rejalashtirilgan darslar yo‘q
    </div>

    <!-- Eski `.herostack`: telefonda ustun, 560px dan keng ekranda yonma-yon. -->
    <div
      v-else
      class="mb-4 flex flex-col gap-3 min-[560px]:flex-row"
    >
      <NextLessonCard
        class="min-w-0 min-[560px]:flex-1"
        type="Teacher"
        :session="schedule.nextTeacher.value"
        :now="now"
      />
      <NextLessonCard
        class="min-w-0 min-[560px]:flex-1"
        type="Assistant"
        :session="schedule.nextAssistant.value"
        :now="now"
      />
    </div>

    <!-- ============================== Davomat ============================== -->
    <h2
      class="mb-3 ml-1 mt-6 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-slate-400"
    >
      <AppIcon
        name="chart"
        :size="15"
      />
      Davomat
    </h2>

    <section class="rounded-xl border border-line bg-ink-900 p-[18px]">
      <div class="flex items-center gap-5">
        <div class="relative size-[92px] shrink-0">
          <svg
            width="92"
            height="92"
            class="-rotate-90"
            aria-hidden="true"
          >
            <circle
              cx="46"
              cy="46"
              r="40"
              fill="none"
              stroke-width="9"
              class="stroke-ink-800"
            />
            <circle
              cx="46"
              cy="46"
              r="40"
              fill="none"
              stroke-width="9"
              stroke-linecap="round"
              class="stroke-green-500"
              :stroke-dasharray="RING_CIRCUMFERENCE"
              :stroke-dashoffset="ringOffset"
              style="transition: stroke-dashoffset .5s cubic-bezier(.4,0,.2,1)"
            />
          </svg>
          <div class="absolute inset-0 flex flex-col items-center justify-center">
            <!--
              Dars o'tilmagan bo'lsa ham FOIZ ko'rsatiladi (`0%`) — eski
              ilovada aynan shunday edi va o'quvchi shunga o'rgangan.
              Sababi esa pastdagi izohda aytiladi, ya'ni "0%" ayblov bo'lib
              tuyulmaydi.
            -->
            <b
              class="text-[21px] font-extrabold"
              v-text="`${Math.round(attendance?.percent ?? 0)}%`"
            />
            <span class="text-[9px] text-slate-400">qatnashish</span>
          </div>
        </div>

        <dl class="flex-1">
          <div class="flex items-center justify-between border-b border-line py-1.5 text-[13px]">
            <dt class="flex items-center gap-2 text-slate-400">
              <i
                class="size-[9px] rounded-full bg-green-500"
                aria-hidden="true"
              />
              Qatnashgan
            </dt>
            <dd
              class="text-[15px] font-bold tabular-nums"
              v-text="statValue(attendance?.attended)"
            />
          </div>
          <div class="flex items-center justify-between border-b border-line py-1.5 text-[13px]">
            <dt class="flex items-center gap-2 text-slate-400">
              <i
                class="size-[9px] rounded-full bg-red-500"
                aria-hidden="true"
              />
              Qatnashmagan
            </dt>
            <dd
              class="text-[15px] font-bold tabular-nums"
              v-text="statValue(attendance?.missed)"
            />
          </div>
          <div class="flex items-center justify-between py-1.5 text-[13px]">
            <dt class="flex items-center gap-2 text-slate-400">
              <i
                class="size-[9px] rounded-full bg-dim"
                aria-hidden="true"
              />
              Jami o‘tgan
            </dt>
            <dd
              class="text-[15px] font-bold tabular-nums"
              v-text="statValue(attendance?.total)"
            />
          </div>
        </dl>
      </div>

      <!--
        Hali dars o'tilmagan bo'lsa sabab AYTILADI: nol foiz "hammasini
        qoldirgan" degan taassurot berardi.
      -->
      <p
        v-if="!hasLessons && !attendanceQuery.isPending.value"
        class="mt-3.5 border-t border-line pt-3 text-xs leading-relaxed text-slate-400"
      >
        Hali o‘tilgan dars yo‘q — birinchi darsdan keyin bu yerda foiz va
        sonlar paydo bo‘ladi.
      </p>
      <p
        v-else-if="(attendanceQuery.data.value?.streak ?? 0) > 1"
        class="mt-3.5 border-t border-line pt-3 text-xs text-brand-300"
      >
        Ketma-ket {{ attendanceQuery.data.value?.streak }} darsda qatnashdingiz.
      </p>
    </section>
  </div>
</template>
