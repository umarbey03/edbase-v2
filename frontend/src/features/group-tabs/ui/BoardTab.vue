<script setup lang="ts">
import { rankBadge } from '@/entities/leaderboard'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
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

/*
  Telefonda jadval o'rniga kartochka ro'yxati chiziladi (ilovadagi umumiy
  naqsh). `hidden lg:block` EMAS, `v-if`: CSS bilan yashirilgan jadval ham
  mount bo'lib, 30 ta qatorni bekorga quradi (`useBreakpoint` izohi).
*/
const { isDesktop } = useBreakpoint()

/**
 * `null` — "shu oyda mezon bo'yicha ma'lumot yo'q", NOL EMAS (jadval
 * ostidagi izoh shuni aytadi). Ikki ko'rinishda bir xil bo'lishi uchun
 * qoida bitta joyda.
 */
function percentText(value: number | null): string {
  return value === null ? '—' : `${value}%`
}
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
        <!--
          Telefon: kartochka. Jadvalning HAMMA ustuni saqlanadi — o'rin va
          ism birinchi qatorda, mezonlar esa pastda ikki ustunli setkada
          (R24 gacha uchta mezon va uch ustun edi).
          "Jami" ATAYLAB tepada, mezonlar bilan bir qatorda emas: reyting
          ro'yxatida ko'z avval o'rinni, keyin yakuniy ballni qidiradi.

          ★ QATORLAR TARTIBI jadvaldagi bilan bir xil (`board.rows`) —
          pastdagi izohga qarang.
        -->
        <ul
          v-if="!isDesktop"
          class="space-y-2"
        >
          <li
            v-for="row in board.rows.value"
            :key="row.studentId"
            class="rounded-lg border border-line bg-ink-950 p-3"
          >
            <div class="flex items-center gap-2.5">
              <span class="w-7 shrink-0 text-center text-[15px] font-bold tabular-nums text-brand-500">
                {{ rankBadge(row.rank) }}
              </span>
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="row.studentName ?? '—'"
              />
              <span class="shrink-0 whitespace-nowrap">
                <b class="text-[15px] tabular-nums text-slate-100">{{ row.total }}</b>
                <span class="ml-1 text-[10px] text-dim">Jami</span>
              </span>
            </div>

            <!--
              ★ R24 dan keyin mezon TO'RTTA — setka `grid-cols-2` ga
              o'tdi, `grid-cols-4` ga EMAS: 375px ekranda to'rtta ustunda
              "Dars bahosi" yorlig'i ikki qatorga sinib, kartochkalar
              balandligi bir-biridan farq qilib qolardi.
            -->
            <dl class="mt-2.5 grid grid-cols-2 gap-2 border-t border-line pt-2.5 text-center">
              <div>
                <dt class="text-[10px] uppercase tracking-[0.06em] text-dim">
                  Davomat
                </dt>
                <dd class="mt-0.5 text-[13px] tabular-nums text-slate-400">
                  {{ percentText(row.attendancePercent) }}
                </dd>
              </div>
              <div>
                <dt class="text-[10px] uppercase tracking-[0.06em] text-dim">
                  Vazifa
                </dt>
                <dd class="mt-0.5 text-[13px] tabular-nums text-slate-400">
                  {{ percentText(row.assignmentPercent) }}
                </dd>
              </div>
              <div>
                <dt class="text-[10px] uppercase tracking-[0.06em] text-dim">
                  Test
                </dt>
                <dd class="mt-0.5 text-[13px] tabular-nums text-slate-400">
                  {{ percentText(row.testPercent) }}
                </dd>
              </div>
              <div>
                <dt class="text-[10px] uppercase tracking-[0.06em] text-dim">
                  Dars bahosi
                </dt>
                <dd class="mt-0.5 text-[13px] tabular-nums text-slate-400">
                  {{ percentText(row.lessonPercent) }}
                </dd>
              </div>
            </dl>
          </li>
        </ul>

        <!-- Desktop: jadval. Gorizontal skroll SHU konteynerda. -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>#</th>
                <th>O‘quvchi</th>
                <th>Davomat</th>
                <th>Vazifa</th>
                <th>Test</th>
                <!-- R24 · dars bahosi — reytingning to'rtinchi mezoni. -->
                <th>Dars bahosi</th>
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
                  {{ percentText(row.attendancePercent) }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ percentText(row.assignmentPercent) }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ percentText(row.testPercent) }}
                </td>
                <td class="tabular-nums text-slate-400">
                  {{ percentText(row.lessonPercent) }}
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
