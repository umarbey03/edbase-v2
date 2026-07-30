<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchGroupLeaderboard, fetchMyRank, rankBadge, scoreParts } from '@/entities/leaderboard'
import { toUserMessage } from '@/shared/api'
import type { LeaderboardRowDto } from '@/shared/types'
import { AppIcon, BaseAvatar, BaseModal, DataStatus } from '@/shared/ui'

/**
 * REYTING — eski `#progress` bo'limi ("Leaderboard").
 *
 * Tuzilishi eski ilovadagidek: sarlavha (guruh nomi + oy) -> podium (uchlik,
 * birinchida toj) -> to'liq ro'yxat -> qatorni bosganda ball tafsiloti.
 *
 * IKKI SO'ROV, ketma-ket: avval `/leaderboard/me` (o'quvchi qaysi guruhda va
 * o'rni qanday), keyin shu guruhning jadvali. Nima uchun bitta so'rov emas:
 * o'quvchi guruhini bilmaydi — guruh Id'sini SERVER aytadi, frontend uni
 * o'zi qidirmaydi (ruxsat qoidasi serverda qolsin).
 */
const myRankQuery = useQuery({
  queryKey: ['leaderboard', 'me'],
  queryFn: ({ signal }) => fetchMyRank(undefined, { signal }),
})

const groupId = computed(() => myRankQuery.data.value?.groupId ?? null)

const boardQuery = useQuery({
  queryKey: ['leaderboard', 'group', groupId],
  queryFn: ({ signal }) => fetchGroupLeaderboard(groupId.value as number, undefined, { signal }),
  // Guruh aniqlanmaguncha so'rov yuborilmaydi (aks holda `null` bilan 404 ketardi).
  enabled: computed(() => groupId.value !== null),
})

const board = computed(() => boardQuery.data.value ?? null)
const rows = computed<LeaderboardRowDto[]>(() => board.value?.rows ?? [])

/**
 * Podium — ATAYLAB `rows` tartibidan olinadi, `rank` bo'yicha emas: bir xil
 * ballda server takroriy o'rin beradi (1, 2, 2, 4) va `rank` bo'yicha
 * qidirilsa ikkita "2-o'rin" ustma-ust tushardi.
 */
const podium = computed(() => rows.value.slice(0, 3))

/** Podium ustunlari eski ilovadagi tartibda: 2 — 1 — 3. */
const podiumOrder = computed(() => {
  const [first, second, third] = podium.value
  return [second, first, third].filter((row): row is LeaderboardRowDto => row !== undefined)
})

const isEmpty = computed(
  () => groupId.value === null || (boardQuery.isSuccess.value && rows.value.length === 0),
)

const errorMessage = computed(() => {
  const error = myRankQuery.error.value ?? boardQuery.error.value
  return error !== null ? toUserMessage(error) : null
})

const isPending = computed(
  () => myRankQuery.isPending.value || (groupId.value !== null && boardQuery.isPending.value),
)

function refresh(): void {
  void myRankQuery.refetch()
  void boardQuery.refetch()
}

/* --------------------------------------------------------- ball tafsiloti */

/**
 * Qatorni bosganda tafsilot ochiladi. Bu ATAYLAB: yakuniy ball uch mezon
 * o'rtachasi va tafsilotsiz reyting "qora quti" bo'lib qolardi — eski
 * ilovada ham qator bosilganda ballar yoyilardi.
 */
const detailRow = ref<LeaderboardRowDto | null>(null)

/** `null` — "hisobga olinmagan", NOL EMAS. Shuning uchun chiziqcha. */
function formatPercent(value: number | null): string {
  return value === null ? '—' : `${Math.round(value)}%`
}
</script>

<template>
  <div>
    <h2
      class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-brand-300"
    >
      <AppIcon
        name="chart"
        :size="15"
      />
      Leaderboard
    </h2>

    <DataStatus
      :pending="isPending"
      :error="errorMessage"
      :empty="isEmpty"
      :retrying="myRankQuery.isFetching.value || boardQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="chart"
      empty-title="Guruh yo‘q"
      empty-text="Guruhga qo‘shilganingizdan keyin reyting shu yerda ko‘rinadi."
      @retry="refresh"
    >
      <template v-if="board !== null">
        <p
          class="text-center text-[17px] font-extrabold text-slate-100"
          v-text="board.groupName ?? 'Guruh'"
        />
        <p class="mb-3.5 text-center text-xs text-slate-400">
          {{ board.period }} · {{ board.studentCount }} o‘quvchi
        </p>

        <!-- Podium: 2 — 1 — 3, birinchida toj (eski ilovadagidek) -->
        <div
          v-if="podium.length > 0"
          class="mb-3 flex items-end justify-center gap-2.5"
        >
          <button
            v-for="row in podiumOrder"
            :key="row.studentId"
            type="button"
            class="flex max-w-[120px] flex-1 flex-col items-center rounded-2xl p-2 transition-transform active:scale-95"
            :class="row.isMe ? 'bg-brand-500/12' : ''"
            @click="detailRow = row"
          >
            <span
              v-if="row.rank === 1"
              class="-mb-1 text-[22px]"
              aria-hidden="true"
            >👑</span>

            <BaseAvatar
              :name="row.studentName ?? '?'"
              :size="row.rank === 1 ? 'lg' : 'md'"
              :ring="row.isMe"
            />

            <span
              class="mt-1.5 w-full truncate text-center text-[11px] font-bold text-slate-200"
              v-text="row.studentName ?? '—'"
            />
            <span class="text-[15px] font-extrabold tabular-nums text-brand-400">
              {{ Math.round(row.total) }}
            </span>
            <span
              class="text-[10px] font-bold text-dim"
              v-text="rankBadge(row.rank)"
            />
          </button>
        </div>

        <!-- To'liq ro'yxat -->
        <ul class="space-y-2">
          <li
            v-for="row in rows"
            :key="row.studentId"
          >
            <button
              type="button"
              class="flex w-full items-center gap-[11px] rounded-[14px] border px-3 py-2.5 text-left transition-colors"
              :class="
                row.isMe
                  ? 'border-brand-500 bg-brand-500/13'
                  : 'border-line bg-ink-900 hover:bg-ink-800'
              "
              @click="detailRow = row"
            >
              <span
                class="w-7 shrink-0 text-center text-[13px] font-extrabold tabular-nums text-dim"
                v-text="rankBadge(row.rank)"
              />
              <BaseAvatar
                :name="row.studentName ?? '?'"
                size="sm"
                :ring="row.isMe"
              />
              <span class="min-w-0 flex-1">
                <span
                  class="block truncate text-sm font-semibold text-slate-100"
                  v-text="row.studentName ?? '—'"
                />
                <span
                  v-if="row.isMe"
                  class="text-[11px] font-bold text-brand-400"
                >Siz</span>
              </span>
              <span class="shrink-0 text-base font-extrabold tabular-nums text-brand-400">
                {{ Math.round(row.total) }}
              </span>
            </button>
          </li>
        </ul>
      </template>
    </DataStatus>

    <!-- Ball tafsiloti: pastdan chiquvchi varaq -->
    <BaseModal
      :open="detailRow !== null"
      :title="detailRow?.studentName ?? 'Ball tafsiloti'"
      @close="detailRow = null"
    >
      <p class="text-xs text-slate-400">
        Yakuniy ball uchta mezonning o‘rtachasi. “—” — shu oyda bu mezon
        bo‘yicha ma’lumot yo‘q (nol emas).
      </p>

      <dl
        v-if="detailRow !== null"
        class="mt-3 space-y-2"
      >
        <div
          v-for="part in scoreParts(detailRow)"
          :key="part.label"
          class="flex items-center justify-between rounded-xl border border-line bg-ink-950 px-3.5 py-2.5"
        >
          <dt
            class="text-[13px] text-slate-300"
            v-text="part.label"
          />
          <dd
            class="text-sm font-bold tabular-nums text-slate-100"
            v-text="formatPercent(part.percent)"
          />
        </div>

        <div
          class="flex items-center justify-between rounded-xl border border-brand-500/40 bg-brand-500/10 px-3.5 py-2.5"
        >
          <dt class="text-[13px] font-bold text-brand-200">
            Yakuniy ball
          </dt>
          <dd class="text-base font-extrabold tabular-nums text-brand-300">
            {{ Math.round(detailRow.total) }}
          </dd>
        </div>
      </dl>
    </BaseModal>
  </div>
</template>
