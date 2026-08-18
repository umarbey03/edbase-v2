<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { fetchAbsentees } from '@/entities/absentee'
import { fetchAttritionStudents } from '@/entities/attrition'
import { fetchPenaltySummary } from '@/entities/penalty'
import { daysAgoIso, fetchTeacherAvailabilitySummary, todayIso } from '@/entities/teacher-availability'
import { fetchStudentStats } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon, BaseCard, PageHeader } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  BOSHQARUV PANELI (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"default holatida biror bir dashboard qil, dashboard pro
 * darajada bo'lishi kerak"*.
 *
 * ★ NEGA "GURUHLAR" BOSH SAHIFA BO'LMASLIGI KERAK edi: u ISH RO'YXATI,
 * manzara emas. Xodim kirgan zahoti "bugun nima diqqat talab qiladi?"
 * degan savolga javob olishi kerak — ilgari u javobni oltita panelni
 * birma-bir ochib o'zi yig'ardi.
 *
 * ★ HAR KARTA — HAVOLA: raqamni ko'rish yetarli emas, undan KEYINGI
 * QADAM bo'lishi kerak. "12 ta javob yo'q" bosilganda aynan o'sha
 * ro'yxat ochiladi, uni qaytadan qidirish shart emas.
 *
 * ★ YANGI AGREGAT ENDPOINT QO'SHILMADI: barcha raqamlar MAVJUD
 * yig'malardan olinadi va PARALLEL so'raladi. Yangi endpoint yozilsa,
 * u panellardagi hisoblar bilan vaqt o'tib ajralib ketardi — aynan
 * shu turdagi nomuvofiqlik audit topgan asosiy muammo edi.
 *
 * ★ HAR BLOK MUSTAQIL YUKLANADI: bittasi sekin yoki yiqilgan bo'lsa,
 * qolganlari baribir ko'rinadi (`DataStatus` bilan bitta umumiy
 * "yuklanmoqda" ekran o'rniga).
 */
const auth = useAuthStore()
const router = useRouter()

const isAdmin = computed(() => auth.role === 'Admin')

const yesterday = daysAgoIso(1)
const monthStart = todayIso().slice(0, 8) + '01'

/* ------------------------------------------------------------ so'rovlar */

const studentsQuery = useQuery({
  queryKey: ['users', 'student-stats'],
  queryFn: ({ signal }) => fetchStudentStats({ signal }),
})

const availabilityQuery = useQuery({
  queryKey: ['teacher-availability', 'summary', 'dashboard'],
  queryFn: ({ signal }) =>
    fetchTeacherAvailabilitySummary({ from: todayIso(), to: todayIso() }, { signal }),
})

const absenteesQuery = useQuery({
  queryKey: ['absentees', 'dashboard', yesterday],
  queryFn: ({ signal }) =>
    fetchAbsentees({ from: yesterday, to: yesterday, pageSize: 1 }, { signal }),
})

const attritionQuery = useQuery({
  queryKey: ['attrition', 'students', 'dashboard', monthStart],
  queryFn: ({ signal }) =>
    fetchAttritionStudents({ from: monthStart, to: todayIso() }, { signal }),
})

const penaltiesQuery = useQuery({
  queryKey: ['penalties', 'summary', 'dashboard'],
  queryFn: ({ signal }) => fetchPenaltySummary({ status: 'Pending' }, { signal }),
})

const students = computed(() => studentsQuery.data.value ?? null)
const availability = computed(() => availabilityQuery.data.value ?? null)
const absentees = computed(() => absenteesQuery.data.value ?? null)
const attrition = computed(() => attritionQuery.data.value ?? null)
const penalties = computed(() => penaltiesQuery.data.value ?? null)

const anyError = computed(() => {
  const failed = [studentsQuery, availabilityQuery, absenteesQuery, attritionQuery, penaltiesQuery]
    .map((q) => q.error.value)
    .find((error) => error !== null)

  return failed === undefined || failed === null ? null : toUserMessage(failed)
})

/* ------------------------------------------------------------ kartalar */

interface Kpi {
  key: string
  label: string
  value: string
  hint: string
  icon: IconName
  tone: 'neutral' | 'good' | 'warn' | 'bad'
  route: string
}

/**
 * ★ TARTIB — SHOSHILINCHLIK BO'YICHA, chiroyli guruhlash bo'yicha emas:
 * chapdan o'ngga o'qiyotgan xodim eng avval bugun hal qilinishi kerak
 * bo'lgan narsani ko'radi.
 */
const kpis = computed<Kpi[]>(() => {
  const list: Kpi[] = []

  if (availability.value !== null) {
    const open = availability.value.coverageOpen

    list.push({
      key: 'coverage',
      label: 'O‘rinbosarsiz dars',
      value: String(open),
      hint: open > 0 ? 'Bugun — darhol hal qilish kerak' : 'Bugun hammasi qoplangan',
      icon: 'user-check',
      tone: open > 0 ? 'bad' : 'good',
      route: 'manage-teacher-availability',
    })
  }

  if (absentees.value !== null) {
    list.push({
      key: 'absentees',
      label: 'Kecha kelmaganlar',
      value: String(absentees.value.totalAbsent),
      hint: absentees.value.riskCount > 0
        ? `${absentees.value.riskCount} tasi ketma-ket 3+ dars`
        : 'Ketma-ket qoldirgan yo‘q',
      icon: 'user-x',
      tone: absentees.value.riskCount > 0 ? 'bad' : (absentees.value.totalAbsent > 0 ? 'warn' : 'good'),
      route: 'manage-absentees',
    })
  }

  if (students.value !== null) {
    list.push({
      key: 'active',
      label: 'Faol o‘quvchi',
      value: String(students.value.active + students.value.trial),
      hint: `${students.value.trial} tasi probniy · ${students.value.paused} pauzada`,
      icon: 'users',
      tone: 'neutral',
      route: 'manage-users',
    })
  }

  if (attrition.value !== null) {
    list.push({
      key: 'attrition',
      label: 'Bu oyda to‘kilgan',
      value: String(attrition.value.studentsLost),
      hint: attrition.value.studentsLost > 0
        ? `${attrition.value.returnRate}% qayta jalb qilindi`
        : 'Yo‘qotish yo‘q',
      icon: 'chart',
      tone: attrition.value.studentsLost > 0 ? 'warn' : 'good',
      route: 'manage-attrition',
    })
  }

  if (penalties.value !== null) {
    list.push({
      key: 'penalties',
      label: 'Tasdiq kutayotgan jarima',
      value: String(penalties.value.pendingCount),
      hint: penalties.value.pendingCount > 0
        ? `${formatMoney(penalties.value.pendingAmount)} so‘m`
        : 'Kutayotgani yo‘q',
      icon: 'wallet',
      tone: penalties.value.pendingCount > 0 ? 'warn' : 'good',
      route: 'manage-penalties',
    })
  }

  return list
})

const TONES: Record<Kpi['tone'], string> = {
  neutral: 'border-l-slate-500',
  good: 'border-l-emerald-500',
  warn: 'border-l-amber-500',
  bad: 'border-l-rose-500',
}

const VALUE_TONES: Record<Kpi['tone'], string> = {
  neutral: 'text-slate-100',
  good: 'text-emerald-400',
  warn: 'text-amber-400',
  bad: 'text-rose-400',
}

/* -------------------------------------------------- diqqat talab qiladi */

interface Task {
  label: string
  count: number
  route: string
  icon: IconName
}

/**
 * "Bugun nima qilishim kerak" ro'yxati — FAQAT nol bo'lmagan qatorlar.
 *
 * ★ NEGA BO'SH QATORLAR OLIB TASHLANADI: "0 ta" yozuvlar ro'yxatni
 * uzaytiradi va haqiqiy vazifalarni ko'zdan yashiradi. Hech narsa
 * qolmasa — bu o'zi ham xabar ("hammasi joyida").
 */
const tasks = computed<Task[]>(() => {
  const list: Task[] = []

  if ((availability.value?.coverageOpen ?? 0) > 0) {
    list.push({
      label: 'O‘rinbosar topilmagan darslar',
      count: availability.value!.coverageOpen,
      route: 'manage-teacher-availability',
      icon: 'user-check',
    })
  }

  if ((availability.value?.pending ?? 0) > 0) {
    list.push({
      label: 'Ustozdan javob kutilmoqda',
      count: availability.value!.pending,
      route: 'manage-teacher-availability',
      icon: 'clock',
    })
  }

  if ((absentees.value?.riskCount ?? 0) > 0) {
    list.push({
      label: 'Ketma-ket 3+ dars qoldirganlar',
      count: absentees.value!.riskCount,
      route: 'manage-absentees',
      icon: 'user-x',
    })
  }

  if ((penalties.value?.pendingCount ?? 0) > 0) {
    list.push({
      label: 'Tasdiqlanmagan jarimalar',
      count: penalties.value!.pendingCount,
      route: 'manage-penalties',
      icon: 'wallet',
    })
  }

  if ((students.value?.withoutGroup ?? 0) > 0) {
    list.push({
      label: 'Guruhga biriktirilmagan o‘quvchilar',
      count: students.value!.withoutGroup,
      route: 'manage-users',
      icon: 'users',
    })
  }

  return list
})

function go(routeName: string): void {
  void router.push({ name: routeName })
}
</script>

<template>
  <div>
    <PageHeader
      title="Boshqaruv paneli"
      :subtitle="`Bugun ${todayIso()} — diqqat talab qiladigan ishlar va umumiy manzara`"
    />

    <p
      v-if="anyError !== null"
      class="mb-4 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      role="alert"
    >
      Ba’zi ko‘rsatkichlarni yuklab bo‘lmadi: {{ anyError }}
    </p>

    <!-- ═════════════════════ KPI ═════════════════════ -->
    <div class="mb-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-5">
      <button
        v-for="kpi in kpis"
        :key="kpi.key"
        type="button"
        class="rounded-2xl border border-line border-l-[3px] bg-ink-900 p-4 text-left transition-colors hover:border-line-strong hover:bg-ink-800"
        :class="TONES[kpi.tone]"
        @click="go(kpi.route)"
      >
        <span class="mb-2 flex items-center gap-2 text-[11px] font-semibold text-slate-400">
          <AppIcon
            :name="kpi.icon"
            :size="14"
          />
          {{ kpi.label }}
        </span>
        <span
          class="block text-3xl font-bold tabular-nums"
          :class="VALUE_TONES[kpi.tone]"
          v-text="kpi.value"
        />
        <span
          class="mt-1 block text-[11px] text-dim"
          v-text="kpi.hint"
        />
      </button>

      <!-- Hali hech narsa yuklanmagan — skeletlar o'rniga jim joy. -->
      <div
        v-if="kpis.length === 0"
        class="col-span-full rounded-2xl border border-line bg-ink-900 p-8 text-center text-sm text-dim"
      >
        Ko‘rsatkichlar yuklanmoqda...
      </div>
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      <!-- ═════════════════════ DIQQAT TALAB QILADI ═════════════════════ -->
      <BaseCard
        title="Diqqat talab qiladi"
        subtitle="Bugun hal qilinishi kerak bo‘lgan ishlar"
        flush
      >
        <ul
          v-if="tasks.length > 0"
          class="divide-y divide-line"
        >
          <li
            v-for="task in tasks"
            :key="task.label"
          >
            <button
              type="button"
              class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-ink-800"
              @click="go(task.route)"
            >
              <AppIcon
                :name="task.icon"
                :size="16"
                class="shrink-0 text-amber-400"
              />
              <span
                class="min-w-0 flex-1 truncate text-sm text-slate-200"
                v-text="task.label"
              />
              <span
                class="shrink-0 rounded-lg bg-amber-500/15 px-2 py-0.5 text-sm font-bold tabular-nums text-amber-300"
                v-text="task.count"
              />
              <AppIcon
                name="chevron-right"
                :size="15"
                class="shrink-0 text-slate-600"
              />
            </button>
          </li>
        </ul>

        <!--
          ★ BO'SH HOLAT — IJOBIY XABAR: "ma'lumot yo'q" emas, "hammasi
          joyida". Bu ekranda bo'shlik — yaxshi natija.
        -->
        <p
          v-else
          class="px-4 py-10 text-center text-sm text-dim"
        >
          ✅ Hozircha diqqat talab qiladigan ish yo‘q.
        </p>
      </BaseCard>

      <!-- ═════════════════════ O'QUVCHILAR MANZARASI ═════════════════════ -->
      <BaseCard
        title="O‘quvchilar"
        subtitle="Hozirgi holat bo‘yicha taqsimot"
      >
        <div
          v-if="students !== null"
          class="space-y-3"
        >
          <div
            v-for="row in [
              { label: 'Faol (8+ dars o‘tagan)', value: students.active, tone: 'bg-emerald-500' },
              { label: 'Probniy (8 darsgacha)', value: students.trial, tone: 'bg-sky-500' },
              { label: 'Pauzada', value: students.paused, tone: 'bg-amber-500' },
              { label: 'Chiqib ketgan', value: students.stopped, tone: 'bg-rose-500' },
            ]"
            :key="row.label"
          >
            <div class="mb-1 flex items-baseline justify-between gap-3">
              <span
                class="text-xs text-slate-300"
                v-text="row.label"
              />
              <span
                class="text-sm font-bold tabular-nums text-slate-100"
                v-text="row.value"
              />
            </div>
            <div class="h-2 overflow-hidden rounded-full bg-ink-800">
              <div
                class="h-full rounded-full transition-[width]"
                :class="row.tone"
                :style="{
                  width: `${students.active + students.trial + students.paused + students.stopped > 0
                    ? (row.value * 100) / (students.active + students.trial + students.paused + students.stopped)
                    : 0}%`,
                }"
              />
            </div>
          </div>

          <p
            v-if="students.activeLosses > 0"
            class="border-t border-line pt-3 text-xs text-dim"
          >
            Shundan <span class="font-semibold text-rose-300">{{ students.activeLosses }}</span> tasi
            8+ dars o‘tab, keyin ketgan — bu sifat/ushlab qolish muammosi,
            sotuv emas.
          </p>
        </div>

        <p
          v-else
          class="py-8 text-center text-sm text-dim"
        >
          Yuklanmoqda...
        </p>
      </BaseCard>
    </div>

    <!--
      ★ MOLIYA BLOKI FAQAT ADMINDA: `/api/v1/payments/*` serverda
      `[Authorize(Roles = "Admin")]`. O'quv bo'limiga ko'rsatilsa, bosgan
      zahoti 403 olardi.
    -->
    <BaseCard
      v-if="isAdmin"
      class="mt-4"
      title="Moliya"
      subtitle="Batafsil ko‘rsatkichlar moliya panelida"
    >
      <div class="flex flex-wrap gap-2.5">
        <button
          v-for="link in [
            { label: 'To‘lovlar', route: 'manage-payments', icon: 'star' },
            { label: 'Moliya hisoboti', route: 'manage-finance', icon: 'chart' },
            { label: 'Oylik hisoblash', route: 'manage-payroll', icon: 'wallet' },
          ]"
          :key="link.route"
          type="button"
          class="flex items-center gap-2 rounded-xl border border-line bg-ink-800 px-3.5 py-2.5 text-sm text-slate-300 transition-colors hover:border-line-strong hover:text-slate-100"
          @click="go(link.route)"
        >
          <AppIcon
            :name="(link.icon as IconName)"
            :size="15"
          />
          {{ link.label }}
        </button>
      </div>
    </BaseCard>
  </div>
</template>
