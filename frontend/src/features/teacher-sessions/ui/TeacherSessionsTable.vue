<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { reviewVerdictLabel, reviewVerdictTone } from '@/entities/recording'
import { fetchSessionStats, sessionStatusLabel, sessionStatusTone } from '@/entities/session'
import { SessionReviewModal } from '@/features/session-review'
import { toUserMessage } from '@/shared/api'
import { formatWeekdayDateTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import type { SessionStatsDto, SessionStatusName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard, DataStatus, PaginationBar } from '@/shared/ui'

/**
 * ═══════════════════════════════════════════════════════════════════════
 * R31 — "DARSLARIM" JADVALI: o'quvchi soni · qatnashgan · davomiylik
 * ═══════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"darslarim bo'limida jadval ma'lumoti sifatida nechta
 * student borligi, nechta qatnashganligi, davomiyligi"*.
 *
 * ★ NEGA `SessionBoard` PARAMETRLASHTIRILMADI, BALKI YANGI KOMPONENT:
 * `SessionBoard` o'quv bo'limi sahifasi (`ManageSessionsPage`) bilan
 * BO'LISHILADI va u BOSHQA savolga javob beradi — "hozir nima qilay?"
 * (jonli / yaqinda / o'tgan kartochkalari, darsga kirish tugmasi bilan).
 * Bu jadval esa "darsim QANDAY o'tdi?" degan savolga javob beradi va
 * BOSHQA endpointdan oziqlanadi (`/live-sessions/stats`, sahifalash
 * bilan). Bittasiga ikkinchisining ma'lumot manbai va tartibini
 * qo'shish — bitta komponent ichida ikki ekran degani; DTO darajasida
 * aynan shu ajratish qilingan (`SessionStatsDto` izohi).
 *
 * ⚠️ SHU SABABLI `SessionBoard` SAHIFADA QOLDI (yuqorida): "darsni
 * boshlash" oqimi tegilmaydi, jadval esa uning ostida turadi.
 *
 * ★ SUKUTDAGI FILTR — "Yakunlangan". Uchala ustun ham FAQAT o'tgan darsda
 * ma'noli: rejalashtirilgan darsda qatnashgan 0 va davomiylik "—" bo'ladi,
 * ya'ni sukut bo'yicha "hammasi" ko'rsatilsa, jadvalning birinchi ekrani
 * kelajakdagi bo'sh qatorlardan iborat bo'lardi (server tartibi —
 * yangidan eskiga).
 */
const router = useRouter()

/*
  Kartochka ↔ jadval chegarasi `lg` (1024px) — ilovadagi barcha jadvallar
  bilan bir xil (`TeacherGroupsPage`, `ManageGroupsPage`). `v-if`/`v-else`,
  `hidden lg:block` EMAS: u ikkala daraxtni ham quradi.
*/
const { isDesktop } = useBreakpoint()

const PAGE_SIZE = 20

const statusFilter = ref<SessionStatusName | ''>('Ended')
const page = ref(1)

// Filtr o'zgarsa 5-sahifada qolib ketmaslik uchun boshiga qaytariladi.
watch(statusFilter, () => {
  page.value = 1
})

const statsQuery = useQuery({
  queryKey: ['live-sessions', 'stats', statusFilter, page],
  queryFn: ({ signal }) =>
    fetchSessionStats(
      {
        status: statusFilter.value === '' ? undefined : statusFilter.value,
        page: page.value,
        pageSize: PAGE_SIZE,
      },
      { signal },
    ),
})

const rows = computed(() => statsQuery.data.value?.items ?? [])
const total = computed(() => statsQuery.data.value?.total ?? 0)
const totalPages = computed(() => statsQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  statsQuery.error.value !== null ? toUserMessage(statsQuery.error.value) : null,
)

/**
 * Davomiylik ustuni.
 *
 * ★ HAQIQIY vaqt ko'rsatiladi, reja esa YONIDA — "45 daq · reja 80".
 * Faqat haqiqiysi ko'rsatilsa, ustoz "80 daqiqalik dars edimi?" degan
 * savolga javob topolmasdi; faqat reja ko'rsatilsa ustun butun guruh
 * bo'ylab bir xil son bo'lib, hech nima aytmasdi.
 *
 * `null` — dars boshlanmagan yoki yakunlanmagan: "—" bilan REJA yozuvi.
 * "0 daqiqa" YOZILMAYDI, u yolg'on bo'lardi.
 */
function durationLabel(row: SessionStatsDto): string {
  if (row.actualMinutes === null) return `— · reja ${row.plannedMinutes} daq.`
  return `${row.actualMinutes} daq. · reja ${row.plannedMinutes}`
}

/** "3 / 12" — qatnashgan / guruhdagi o'quvchi. */
function attendanceLabel(row: SessionStatsDto): string {
  return `${row.attendedCount} / ${row.studentCount}`
}

function sessionTitleOf(row: SessionStatsDto): string {
  const title = row.title?.trim()
  return title !== undefined && title.length > 0 ? title : row.groupName
}

function openSession(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}

/* ==========================================================================
   R30 — "O'ZIMNING DARS TAHLILIM"
   ========================================================================== */

/**
 * Loyiha egasi: *"darslarim bo'limida qo'shimcha button orqali teacher
 * o'zining dars tahlilini ko'ra olsin modal window orqali"*.
 *
 * ★ NEGA AYNAN SHU JADVAL: u "darsim QANDAY o'tdi?" degan savolga javob
 * beradi (yuqoridagi sarlavha izohi) — sifat tahlili ham AYNI savolning
 * davomi. Yuqoridagi `SessionBoard` esa "hozir nima qilay?" ga javob
 * beradi va u o'quv bo'limi sahifasi bilan BO'LISHILADI, ya'ni tugma u
 * yerga qo'yilsa akademik ko'rinishga ham chiqib ketardi.
 *
 * ★ TUGMA FAQAT `hasReview` BO'LGANDA CHIZILADI: aks holda ustoz har
 * qatorda tugma ko'rib, aksariyatida bo'sh oyna ochardi. Bayroq AYNI
 * so'rovda keladi (server tomonda korrelyatsion so'rov), ya'ni qo'shimcha
 * chaqiruv YO'Q.
 *
 * ⚠️ USTOZ TAHRIRLAY OLMAYDI — u sifat nazoratining OBYEKTI. Oyna buni
 * `canEdit: false` orqali biladi va tahrirlash tugmasini chizmaydi;
 * haqiqiy chegara esa SERVERDA (`403`).
 */
const reviewSessionId = ref<number | null>(null)
const reviewTitle = ref('')

function openReview(row: SessionStatsDto): void {
  reviewTitle.value = sessionTitleOf(row)
  reviewSessionId.value = row.id
}
</script>

<template>
  <section class="mt-7">
    <div class="mb-2.5 flex flex-wrap items-center justify-between gap-2">
      <h2 class="text-xs font-semibold uppercase tracking-wide text-slate-400">
        Darslar jadvali
      </h2>
      <div class="flex items-center gap-2">
        <span
          class="rounded-[20px] border border-brand-500/20 bg-brand-500/14 px-3 py-1 text-[13px] font-semibold text-brand-500"
        >
          Jami: {{ total }} ta dars
        </span>
        <select
          v-model="statusFilter"
          class="zn-input w-auto min-w-[132px] flex-none text-[13px]"
          aria-label="Dars holati bo‘yicha filtr"
        >
          <option value="Ended">
            Yakunlangan
          </option>
          <option value="Scheduled">
            Rejalashtirilgan
          </option>
          <option value="Cancelled">
            Bekor qilingan
          </option>
          <option value="">
            Barcha darslar
          </option>
        </select>
      </div>
    </div>

    <DataStatus
      :pending="statsQuery.isPending.value"
      :error="errorMessage"
      :empty="rows.length === 0"
      :retrying="statsQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="calendar"
      empty-title="Dars topilmadi"
      empty-text="Tanlangan holat bo‘yicha dars yo‘q. Filtrni o‘zgartirib ko‘ring."
      @retry="statsQuery.refetch()"
    >
      <!-- Telefon/planshet: kartochka (jadval 7 ustun bilan siqilib ketardi). -->
      <ul
        v-if="!isDesktop"
        class="space-y-2"
      >
        <li
          v-for="row in rows"
          :key="row.id"
          class="rounded-lg border border-line bg-ink-950 p-3"
        >
          <div class="flex items-start justify-between gap-2">
            <button
              type="button"
              class="min-w-0 flex-1 truncate text-left text-sm font-medium text-slate-100"
              @click="openSession(row.id)"
              v-text="sessionTitleOf(row)"
            />
            <BaseBadge :tone="sessionStatusTone(row.status)">
              {{ sessionStatusLabel(row.status) }}
            </BaseBadge>
          </div>
          <p class="mt-1 text-xs tabular-nums text-slate-400">
            {{ formatWeekdayDateTime(row.scheduledStart) }}
          </p>
          <dl class="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-400">
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="users"
                :size="13"
              />
              <span class="tabular-nums">{{ attendanceLabel(row) }}</span>
            </div>
            <div class="inline-flex items-center gap-1.5">
              <AppIcon
                name="clock"
                :size="13"
              />
              <span class="tabular-nums">{{ durationLabel(row) }}</span>
            </div>
          </dl>

          <!-- R30: tahlil FAQAT yozilgan darsda ko'rinadi. -->
          <button
            v-if="row.hasReview"
            type="button"
            class="mt-2 inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1.5 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
            @click="openReview(row)"
          >
            <AppIcon
              name="clipboard"
              :size="13"
            />
            Dars tahlili
            <BaseBadge :tone="reviewVerdictTone(row.reviewStatus)">
              {{ reviewVerdictLabel(row.reviewStatus) }}
            </BaseBadge>
          </button>
        </li>
      </ul>

      <!-- Desktop (≥1024px): jadval -->
      <BaseCard
        v-else
        flush
      >
        <div class="scroll-x-safe scrollbar-slim">
          <table class="zn-table">
            <thead>
              <tr>
                <th>Dars</th>
                <th>Guruh</th>
                <th>Sana va vaqt</th>
                <th>Holat</th>
                <th>O‘quvchi</th>
                <th>Qatnashgan</th>
                <th>Davomiyligi</th>
                <th>Tahlil</th>
                <th />
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in rows"
                :key="row.id"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="sessionTitleOf(row)"
                />
                <td
                  class="text-slate-400"
                  v-text="row.groupName"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatWeekdayDateTime(row.scheduledStart)"
                />
                <td>
                  <BaseBadge :tone="sessionStatusTone(row.status)">
                    {{ sessionStatusLabel(row.status) }}
                  </BaseBadge>
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="row.studentCount"
                />
                <!--
                  "3 / 12" — sanoq YOLG'IZ ma'nosiz: 3 ta qatnashgani
                  ko'p ham, kam ham bo'lishi mumkin. Maxraj yonida
                  turgani uchun ustoz darhol ko'radi.
                -->
                <td
                  class="tabular-nums text-slate-200"
                  v-text="attendanceLabel(row)"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="durationLabel(row)"
                />
                <!--
                  R30. Tahlil YO'Q bo'lsa katak BO'SH qoladi ("—" ham
                  emas): "hali ko'rilmagan" — normal holat va uni har
                  qatorda takrorlash jadvalni shovqinga to'ldirardi.
                -->
                <td>
                  <button
                    v-if="row.hasReview"
                    type="button"
                    class="inline-flex min-h-11 items-center gap-1.5 rounded-lg px-1.5 transition-colors hover:bg-brand-500/10"
                    title="O‘quv bo‘limining sifat tahlilini ochish"
                    @click="openReview(row)"
                  >
                    <BaseBadge :tone="reviewVerdictTone(row.reviewStatus)">
                      {{ reviewVerdictLabel(row.reviewStatus) }}
                    </BaseBadge>
                    <AppIcon
                      name="chevron-right"
                      :size="12"
                      class="text-dim"
                    />
                  </button>
                </td>
                <td>
                  <button
                    type="button"
                    class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
                    @click="openSession(row.id)"
                  >
                    Ochish
                    <AppIcon
                      name="chevron-right"
                      :size="14"
                    />
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </BaseCard>

      <PaginationBar
        v-if="!isDesktop"
        :page="page"
        :total-pages="totalPages"
        :total="total"
        @update:page="page = $event"
      />
    </DataStatus>

    <!--
      R30. `@saved` bu yerda TINGLANMAYDI: ustoz tahlilni o'zgartira
      olmaydi (server `403`), ya'ni ro'yxatni qayta o'qishning sababi yo'q.
    -->
    <SessionReviewModal
      :session-id="reviewSessionId"
      :title="reviewTitle"
      @close="reviewSessionId = null"
    />
  </section>
</template>
