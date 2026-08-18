<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchAbsentees } from '@/entities/absentee'
import { daysAgoIso, todayIso } from '@/entities/teacher-availability'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
import type { AbsenteeStudentDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseCard,
  DataStatus,
  PageHeader,
} from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARSGA KIRMAGANLAR — KUNLIK XARITA (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"bir kun avval darsga kirmagan o'quvchilarni bittada
 * ko'ra olishimiz uchun"*.
 *
 * ★ NEGA MAVJUD DAVOMAT EKRANI YETARLI EMAS: u BITTA DARS kesimida
 * ishlaydi. Kurator esa ertalab "kecha kim kelmadi?" deb so'raydi va
 * buni bilish uchun o'nlab guruhni birma-bir ochishi kerak edi.
 *
 * ★ STANDART SANA — KECHA (bugun emas): bugungi darslarning ko'pi hali
 * o'tmagan bo'ladi va ro'yxat chala chiqardi.
 *
 * ★ KETMA-KET QOLDIRISH — ENG MUHIM USTUN va SARALASH kaliti: bitta
 * qoldirilgan dars odatiy hol, ketma-ket uchtasi esa "bu o'quvchi
 * ketyapti" degan signal. Kurator ro'yxatni yuqoridan pastga qo'ng'iroq
 * qiladi, ya'ni vaqti tugasa qolganlari eng kam xavflilari bo'ladi.
 */

/** Ketma-ket shu sondan ko'p — "xavf" (server bilan AYNI chegara). */
const RISK_STREAK = 3

const date = ref(daysAgoIso(1))
const search = ref('')
const debouncedSearch = useDebounced(search)
const includePartial = ref(false)
const onlyRisk = ref(false)

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const params = computed(() => ({
  date: date.value,
  includePartial: includePartial.value,
  minStreak: onlyRisk.value ? RISK_STREAK : 0,
  search: effectiveSearch.value,
}))

const absenteesQuery = useQuery({
  queryKey: ['absentees', params],
  queryFn: ({ signal }) => fetchAbsentees(params.value, { signal }),
  enabled: computed(() => date.value.length > 0),
})

const report = computed(() => absenteesQuery.data.value ?? null)
const groups = computed(() => report.value?.groups ?? [])

const loadError = computed(() =>
  absenteesQuery.error.value !== null ? toUserMessage(absenteesQuery.error.value) : null,
)

/** Ketma-ket qoldirish nishonining rangi — xavf darajasi bo'yicha. */
function streakTone(count: number): 'neutral' | 'warning' | 'danger' {
  if (count >= RISK_STREAK) return 'danger'
  if (count > 1) return 'warning'

  return 'neutral'
}

function streakLabel(count: number): string {
  return count <= 1 ? '1-marta' : `${count}-marta ketma-ket`
}

/** Telefon raqamdan `tel:` havolasi — bosilganda qo'ng'iroq boshlanadi. */
function telHref(student: AbsenteeStudentDto): string | null {
  return student.phone === null ? null : `tel:${student.phone.replace(/\s/g, '')}`
}
</script>

<template>
  <div>
    <PageHeader
      title="Darsga kirmaganlar"
      subtitle="Bir kunning barcha guruhlari bo‘yicha — qo‘ng‘iroq qilish uchun tayyor ro‘yxat."
    />

    <!-- ═════════════════════ FILTRLAR ═════════════════════ -->
    <div class="mb-4 rounded-2xl border border-line bg-ink-900 p-4">
      <div class="grid gap-2.5 sm:grid-cols-2 lg:grid-cols-3">
        <label class="block">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">Kun</span>
          <input
            v-model="date"
            class="zn-input"
            type="date"
            :max="todayIso()"
          >
        </label>

        <label class="block sm:col-span-2 lg:col-span-1">
          <span class="mb-1 block text-[11px] font-semibold text-slate-400">O‘quvchi ismi</span>
          <input
            v-model="search"
            class="zn-input"
            placeholder="Qidirish"
          >
        </label>

        <div class="flex flex-wrap items-end gap-4 pb-1">
          <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
            <input
              v-model="onlyRisk"
              type="checkbox"
            >
            Faqat ketma-ket {{ RISK_STREAK }}+ qoldirganlar
          </label>
          <label class="flex cursor-pointer items-center gap-2 text-xs text-slate-300">
            <input
              v-model="includePartial"
              type="checkbox"
            >
            Erta chiqib ketganlar ham
          </label>
        </div>
      </div>

      <div class="mt-3 flex flex-wrap items-center gap-1.5 border-t border-line pt-3">
        <span class="mr-1 text-[11px] text-dim">Tez tanlash:</span>
        <button
          type="button"
          class="rounded-lg border border-line bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
          @click="date = daysAgoIso(1)"
        >
          Kecha
        </button>
        <button
          type="button"
          class="rounded-lg border border-line bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400 transition-colors hover:text-slate-100"
          @click="date = todayIso()"
        >
          Bugun
        </button>
      </div>
    </div>

    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="report !== null"
      class="mb-4 grid grid-cols-2 gap-2.5 lg:grid-cols-4"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-2xl font-bold tabular-nums text-slate-100"
          v-text="report.totalAbsent"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Kelmagan o‘quvchi
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
        <p
          class="text-2xl font-bold tabular-nums text-rose-400"
          v-text="report.riskCount"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Ketma-ket {{ RISK_STREAK }}+ qoldirgan
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-2xl font-bold tabular-nums text-slate-100"
          v-text="groups.length"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Guruh
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-2xl font-bold tabular-nums text-slate-100"
          v-text="report.sessionCount"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          O‘tilgan dars
        </p>
      </div>
    </div>

    <!-- ═════════════════════ GURUHLAR ═════════════════════ -->
    <DataStatus
      :pending="absenteesQuery.isPending.value"
      :error="loadError"
      :empty="groups.length === 0"
      :retrying="absenteesQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="check-square"
      empty-title="Hamma kelgan"
      empty-text="Bu kuni darsni qoldirgan o‘quvchi yo‘q (yoki o‘sha kuni dars bo‘lmagan)."
      @retry="absenteesQuery.refetch()"
    >
      <div class="space-y-4">
        <BaseCard
          v-for="group in groups"
          :key="group.groupId"
          flush
        >
          <!-- Guruh sarlavhasi — kurator qaysi ro'yxatni oldida turganini bilsin. -->
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-line px-4 py-3">
            <span
              class="font-semibold text-slate-100"
              v-text="group.groupName"
            />
            <span
              v-if="group.teacherName !== null"
              class="text-xs text-dim"
              v-text="`Ustoz: ${group.teacherName}`"
            />
            <span
              v-if="group.assistantName !== null"
              class="text-xs text-dim"
              v-text="`Kurator: ${group.assistantName}`"
            />
            <span class="ml-auto text-sm font-bold tabular-nums text-rose-400">
              {{ group.absentCount }}<span
                v-if="group.activeMembers > 0"
                class="text-xs font-normal text-dim"
              >/{{ group.activeMembers }}</span>
            </span>
          </div>

          <ul class="divide-y divide-line">
            <li
              v-for="student in group.students"
              :key="`${student.studentId}-${student.sessionId}`"
              class="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3"
            >
              <span class="min-w-0 flex-1">
                <span
                  class="block truncate font-medium text-slate-100"
                  v-text="student.studentName"
                />
                <span class="mt-0.5 flex flex-wrap items-center gap-x-2 text-xs text-dim">
                  <span v-text="formatTime(student.sessionStart)" />
                  <span
                    v-if="student.status === 'Partial'"
                    class="text-amber-400"
                  >erta chiqib ketgan</span>
                  <span v-text="`30 kunda ${student.missedInLast30Days} ta`" />
                </span>
              </span>

              <BaseBadge :tone="streakTone(student.consecutiveMisses)">
                {{ streakLabel(student.consecutiveMisses) }}
              </BaseBadge>

              <!--
                ★ QO'NG'IROQ RO'YXATDAN CHIQMASDAN: telefonda `tel:`
                havolasi to'g'ridan-to'g'ri terishni ochadi. Kurator
                raqamni ko'chirib olish uchun profilga o'tmasin.
              -->
              <a
                v-if="telHref(student) !== null"
                :href="telHref(student)!"
                class="tap-target flex items-center gap-1.5 rounded-lg border border-line bg-ink-800 px-2.5 text-xs font-semibold text-slate-300 transition-colors hover:border-line-strong hover:text-slate-100"
              >
                <AppIcon
                  name="phone"
                  :size="13"
                />
                {{ formatPhone(student.phone!) }}
              </a>
              <span
                v-else
                class="text-xs text-dim"
              >Telefon yo‘q</span>

              <!-- `AppIcon` `aria-label` propini olmaydi — sarlavha o'ram ustida. -->
              <span
                v-if="student.telegramLinked"
                class="flex items-center text-sky-400"
                title="Telegram ulangan — xabar yuborish mumkin"
              >
                <AppIcon
                  name="send"
                  :size="14"
                />
              </span>
            </li>
          </ul>
        </BaseCard>
      </div>
    </DataStatus>
  </div>
</template>
