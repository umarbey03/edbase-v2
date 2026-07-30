<script setup lang="ts">
import { rankBadge } from '@/entities/leaderboard'
import { BaseCard, DataStatus } from '@/shared/ui'

import { useGroupBoard } from '../model/use-group-board'

/**
 * "Reyting" tabi — eski `#tab-board`.
 *
 * USTUNLAR eski jadvaldan aynan: `# · O'quvchi · Davomat · Vazifa · Test ·
 * Jami`.
 *
 * ★ IZOH MATNI O'ZGARTIRILDI. Eskisi "Ball: ustoz darsiga qatnashish
 * (ko'proq) + yordamchi darsi + vazifa va test natijalari" derdi — ya'ni
 * ballar QO'SHILARDI. v2 da yakuniy ball uch mezonning O'RTACHASI va har
 * mezon foizda (`LeaderboardScore`). Eski matnni saqlash raqamlarni
 * noto'g'ri tushuntirardi.
 *
 * ★ Bu tab KURATORGA ko'rsatilmaydi (`visibleGroupTabs`) — eski qoida.
 */
const props = defineProps<{ groupId: number }>()

const board = useGroupBoard(props.groupId)
</script>

<template>
  <BaseCard
    flush
    title="Guruh reytingi"
    subtitle="Ball — uch mezon (davomat, vazifa, test) foizlarining o‘rtachasi. Davr: joriy oy."
  >
    <div class="p-3.5 sm:p-5">
      <DataStatus
        :pending="board.pending.value"
        :error="board.errorMessage.value"
        :empty="board.rows.value.length === 0"
        :retrying="board.fetching.value"
        :skeleton-rows="3"
        empty-icon="trophy"
        empty-title="Ma’lumot yo‘q."
        @retry="board.refetch()"
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th>#</th>
                <th>O‘quvchi</th>
                <th>Davomat</th>
                <th>Vazifa</th>
                <th>Test</th>
                <th>Jami</th>
              </tr>
            </thead>
            <tbody>
              <!--
                ★ QATORLAR `rows` TARTIBIDA chiziladi, `rank` bo'yicha EMAS:
                bir xil ballda ikki o'quvchi bir xil o'rin oladi (1, 2, 2, 4)
                va `rank` faqat YORLIQ (server izohi).
              -->
              <tr
                v-for="row in board.rows.value"
                :key="row.studentId"
              >
                <td class="font-bold tabular-nums text-brand-500">
                  {{ rankBadge(row.rank) }}
                </td>
                <td
                  class="font-medium text-slate-100"
                  v-text="row.studentName ?? '—'"
                />
                <td class="tabular-nums text-slate-400">
                  {{ row.attendancePercent === null ? '—' : `${row.attendancePercent}%` }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ row.assignmentPercent === null ? '—' : `${row.assignmentPercent}%` }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ row.testPercent === null ? '—' : `${row.testPercent}%` }}
                </td>
                <td class="font-bold tabular-nums text-slate-100">
                  {{ row.total }}
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <p class="mt-3 text-[11px] text-dim">
          “—” — shu oyda mezon bo‘yicha ma’lumot yo‘q (nol ball emas).
        </p>
      </DataStatus>
    </div>
  </BaseCard>
</template>
