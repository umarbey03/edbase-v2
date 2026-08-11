<script setup lang="ts">
import { computed } from 'vue'

import { submissionStatusLabel, submissionStatusTone } from '@/entities/assignment'
import { scoreLabel, testKindLabel } from '@/entities/test'
import { attendanceTone, percentLabel } from '@/entities/user'
import { formatDateTime } from '@/shared/lib/datetime'
import type { ProfileStudyDto } from '@/shared/types'
import { BaseBadge, BaseCard } from '@/shared/ui'

/**
 * 4-BO'LIM: O'QUV NATIJALARI — uy vazifalari · testlar · davomat.
 *
 * ⚠️ TESTDA "NECHTA TO'G'RI JAVOB" DEGAN SON YO'Q (13-bo'lim, 34-tuzoq):
 * har savolning o'z `points` i bor, ya'ni model umumiy holatda to'g'ri
 * javoblar sonini bilmaydi. Shuning uchun faqat BALL va FOIZ ko'rsatiladi;
 * "N/M to'g'ri" deb yozish jimgina yolg'on bo'lardi (masalan 10 ballik
 * savol 1 ta to'g'ri javob sifatida ko'rinardi).
 *
 * ★ FAYLLAR: vazifa javobida faqat fayl SONI bor — havola ham, `objectKey`
 * ham ataylab yo'q (16-tuzoq, ichki ombor kaliti UI'ga chiqmaydi). Faylni
 * ochish "Tekshirish navbati" oqimida, himoyalangan endpoint orqali bo'ladi.
 */
const props = defineProps<{ study: ProfileStudyDto }>()

const assignments = computed(() => props.study.assignments)
const tests = computed(() => props.study.tests)
const attendance = computed(() => props.study.attendance)
</script>

<template>
  <BaseCard title="O‘quv natijalari">
    <!-- ------------------------------------------------------- davomat -->
    <dl class="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd class="mt-0.5">
          <BaseBadge
            :tone="attendanceTone(attendance.percent)"
            size="sm"
          >
            {{ percentLabel(attendance.percent) }}
          </BaseBadge>
        </dd>
        <dt class="mt-1.5 text-[11px] text-slate-400">
          Davomat
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums text-green-400"
          v-text="attendance.present"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          Qatnashgan
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums"
          :class="attendance.missed > 0 ? 'text-rose-400' : 'text-slate-100'"
          v-text="attendance.missed"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          Kelmagan
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums text-slate-100"
          v-text="attendance.total"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          O‘tkazilgan dars
        </dt>
      </div>
    </dl>

    <!-- --------------------------------------------------- uy vazifalari -->
    <h3 class="mb-2 mt-4 text-xs font-semibold uppercase tracking-wide text-slate-400">
      Uy vazifalari
    </h3>

    <p
      v-if="assignments.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hali birorta vazifa topshirilmagan.
    </p>

    <ul
      v-else
      class="divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="item in assignments"
        :key="item.submissionId"
        class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3"
      >
        <span
          class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
          v-text="item.title"
        />
        <span class="shrink-0 text-sm tabular-nums text-slate-300">
          {{ scoreLabel(item.score, item.maxScore) }}
        </span>
        <BaseBadge :tone="submissionStatusTone(item.status)">
          {{ submissionStatusLabel(item.status) }}
        </BaseBadge>
        <BaseBadge
          v-if="item.isLate"
          tone="warning"
        >
          Kechikkan
        </BaseBadge>
        <p class="w-full text-[11px] text-slate-400">
          <span v-if="item.groupName !== null">{{ item.groupName }} · </span>
          <span v-if="item.lessonName !== null">{{ item.lessonName }} · </span>
          {{ formatDateTime(item.submittedAt) }}
          <span v-if="item.fileCount > 0"> · {{ item.fileCount }} fayl</span>
        </p>
      </li>
    </ul>

    <p
      v-if="props.study.hasMoreAssignments"
      class="mt-1.5 text-[11px] text-slate-400"
    >
      Oxirgi 50 ta javob ko‘rsatilgan.
    </p>

    <!-- ---------------------------------------------------------- testlar -->
    <h3 class="mb-2 mt-4 text-xs font-semibold uppercase tracking-wide text-slate-400">
      Testlar
    </h3>

    <p
      v-if="tests.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hali birorta test topshirilmagan.
    </p>

    <ul
      v-else
      class="divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="item in tests"
        :key="item.attemptId"
        class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3"
      >
        <span
          class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
          v-text="item.title"
        />
        <!-- Ball VA foiz birga: "12 / 20" xodimga, foiz esa taqqoslash uchun. -->
        <span class="shrink-0 text-sm tabular-nums text-slate-300">
          {{ scoreLabel(item.score, item.maxScore) }}
        </span>
        <span
          v-if="item.scorePercent !== null"
          class="shrink-0 text-sm font-semibold tabular-nums text-slate-100"
          v-text="percentLabel(item.scorePercent)"
        />
        <BaseBadge tone="neutral">
          {{ testKindLabel(item.kind) }}
        </BaseBadge>
        <!--
          "Vaqt tugab yopilgan" — natijani TUSHUNTIRADI: past foiz bilimdan
          emas, vaqtdan bo'lishi mumkin.
        -->
        <BaseBadge
          v-if="item.closedByTimeout"
          tone="danger"
        >
          Vaqt tugadi
        </BaseBadge>
        <p
          v-if="item.finishedAt !== null"
          class="w-full text-[11px] text-slate-400"
        >
          {{ formatDateTime(item.finishedAt) }}
        </p>
        <p
          v-else
          class="w-full text-[11px] text-slate-400"
        >
          Tugatilmagan urinish
        </p>
      </li>
    </ul>

    <p
      v-if="props.study.hasMoreTests"
      class="mt-1.5 text-[11px] text-slate-400"
    >
      Oxirgi 50 ta urinish ko‘rsatilgan.
    </p>
  </BaseCard>
</template>
