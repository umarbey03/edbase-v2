<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import {
  fetchLiveSessions,
  sessionStartState,
  sessionStateBadge,
  sessionTypeShortLabel,
  START_LEAD_MINUTES,
  startLiveSession,
} from '@/entities/session'
import type { SessionStartState } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatDate, formatTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useNow } from '@/shared/lib/use-now'
import type { LiveSessionDto } from '@/shared/types'
import { BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * Eski ustoz panelining "Kelgusi darslar" jadvali (`#dash-list`).
 *
 * TARTIB eski `renderDashboard()` dagidek: avval hali tugamagan darslar
 * (eng yaqini birinchi), keyin vaqti o'tib ketganlari. Eng yaqin dars
 * qatori ajratib ko'rsatiladi (eski `tr.dash-next`).
 *
 * `GET /live-sessions` o'zi kelajakdagi va oxirgi 6 soatdagi darslarni
 * qaytaradi (server: `ScheduledEnd >= now - 6h`, `Take(100)`) va boshlanish
 * vaqti bo'yicha saralangan holda beradi — shuning uchun bu yerda qo'shimcha
 * sana filtri YO'Q: bo'lsa, "kelgusi" ta'rifi ikki joyda ikki xil bo'lardi.
 */
const router = useRouter()
const queryClient = useQueryClient()

/**
 * Vaqt sekundiga bir yangilanadi: "⏳ 14 daq qoldi" yozuvi va "Darsni
 * boshlash" tugmasining ochilishi vaqtga bog'liq. Manba butun ilova uchun
 * BITTA taymer (`useNow`) — sahifa yopilganda o'zi to'xtaydi.
 */
const now = useNow()

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi va bu yerda narx ikki barobar: har sekundda yangilanadigan
  `now` HAR IKKI ro'yxatni qayta chizardi (ustoz bosh sahifasi, 12 qator).

  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi
  (`style.css` dagi "md va lg haqidagi asosiy qaror" izohi).
  ★ "Yana ko'rsatish" hisoblagichi (`limit`) SHU komponentda — daraxt
  almashsa ham ochilgan qatorlar soni saqlanadi.
*/
const { isDesktop } = useBreakpoint()

const sessionsQuery = useQuery({
  queryKey: ['live-sessions'],
  queryFn: ({ signal }) => fetchLiveSessions({ signal }),
})

const sessions = computed(() => sessionsQuery.data.value ?? [])

/** Ekranda birdaniga 12 ta qator; qolgani tugma bilan ochiladi. */
const CHUNK = 12
const limit = ref(CHUNK)

interface LessonRow {
  session: LiveSessionDto
  state: SessionStartState
  badgeLabel: string
  badgeTone: 'neutral' | 'live' | 'warning' | 'danger' | 'success' | 'accent'
  isNext: boolean
}

const rows = computed<LessonRow[]>(() => {
  const current = now.value.getTime()
  const isAhead = (item: LiveSessionDto): boolean =>
    item.status === 'Live' || new Date(item.scheduledEnd).getTime() >= current

  const ahead = sessions.value.filter(isAhead)
  const behind = sessions.value.filter((item) => !isAhead(item))
  const nextId = ahead[0]?.id ?? null

  return [...ahead, ...behind].map((session) => {
    const badge = sessionStateBadge(session, now.value)
    return {
      session,
      state: sessionStartState(session, now.value),
      badgeLabel: badge.label,
      badgeTone: badge.tone,
      isNext: session.id === nextId,
    }
  })
})

const visible = computed(() => rows.value.slice(0, limit.value))
const hasMore = computed(() => rows.value.length > limit.value)

const errorMessage = computed(() =>
  sessionsQuery.error.value !== null ? toUserMessage(sessionsQuery.error.value) : null,
)

const waitHint = `Dars boshlanishiga ${START_LEAD_MINUTES} daqiqa qolganda ochiladi`

/* ------------------------------------------------------------- amallar */

const actionError = ref<string | null>(null)
/** Faqat SHU qatordagi tugma kutish holatiga o'tsin. */
const startingId = ref<number | null>(null)

const startMutation = useMutation({
  mutationFn: (sessionId: number) => startLiveSession(sessionId),
  onSuccess: (session) => {
    actionError.value = null
    void queryClient.invalidateQueries({ queryKey: ['live-sessions'] })
    void router.push({ name: 'live-room', params: { sessionId: String(session.id) } })
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

function start(sessionId: number): void {
  startingId.value = sessionId
  startMutation.mutate(sessionId)
}

function openRoom(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}

function openGroup(groupId: number): void {
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}

function isStarting(sessionId: number): boolean {
  return startMutation.isPending.value && startingId.value === sessionId
}
</script>

<template>
  <BaseCard
    flush
    title="Kelgusi darslar"
  >
    <div class="p-3.5 sm:p-5">
      <p
        v-if="actionError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
        role="alert"
        v-text="actionError"
      />

      <DataStatus
        :pending="sessionsQuery.isPending.value"
        :error="errorMessage"
        :empty="rows.length === 0"
        :retrying="sessionsQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="calendar"
        empty-title="Bugun darslar yo‘q"
        empty-text="Yangi dars rejalashtirilganda shu yerda ko‘rinadi."
        @retry="sessionsQuery.refetch()"
      >
        <!-- ================= Telefon/planshet: kartochka ================= -->
        <ul
          v-if="!isDesktop"
          class="space-y-2"
        >
          <li
            v-for="row in visible"
            :key="row.session.id"
            class="rounded-lg border p-3"
            :class="row.isNext ? 'border-brand-500/45 bg-brand-500/10' : 'border-line bg-ink-950'"
          >
            <div class="flex items-start justify-between gap-2">
              <button
                type="button"
                class="min-w-0 flex-1 truncate text-left text-sm font-semibold text-brand-500"
                @click="openGroup(row.session.groupId)"
                v-text="row.session.groupName"
              />
              <BaseBadge
                :tone="row.badgeTone"
                :dot="row.session.status === 'Live'"
              >
                {{ row.badgeLabel }}
              </BaseBadge>
            </div>
            <p class="mt-1 text-xs tabular-nums text-slate-400">
              {{ formatDate(row.session.scheduledStart) }} ·
              {{ formatTime(row.session.scheduledStart) }}
              <span class="text-dim">· {{ sessionTypeShortLabel(row.session.type) }} darsi</span>
            </p>

            <div class="mt-2.5">
              <BaseButton
                v-if="row.state.kind === 'live'"
                size="sm"
                variant="success"
                block
                @click="openRoom(row.session.id)"
              >
                Darsga qaytish
              </BaseButton>
              <BaseButton
                v-else-if="row.state.kind === 'ready'"
                size="sm"
                block
                :loading="isStarting(row.session.id)"
                @click="start(row.session.id)"
              >
                Darsni boshlash
              </BaseButton>
              <p
                v-else-if="row.state.kind === 'wait'"
                class="text-xs text-slate-400"
                :title="waitHint"
              >
                ⏳ {{ row.state.text }} qoldi
              </p>
              <BaseBadge
                v-else-if="row.state.kind === 'ended'"
                tone="success"
              >
                ✓ O‘tilgan
              </BaseBadge>
              <p
                v-else
                class="text-xs text-slate-400"
              >
                Bekor
              </p>
            </div>
          </li>
        </ul>

        <!-- ================= Desktop (≥1024px): jadval ================= -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>Sana</th>
                <th>Vaqt</th>
                <th>Guruh</th>
                <th>Tur</th>
                <th>Holat</th>
                <th>Harakat</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in visible"
                :key="row.session.id"
                :class="row.isNext ? 'zn-row-next' : ''"
              >
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDate(row.session.scheduledStart)"
                />
                <td
                  class="font-semibold tabular-nums text-slate-100"
                  v-text="formatTime(row.session.scheduledStart)"
                />
                <td>
                  <button
                    type="button"
                    class="max-w-56 truncate text-left font-medium text-brand-500 hover:underline"
                    @click="openGroup(row.session.groupId)"
                    v-text="row.session.groupName"
                  />
                </td>
                <td
                  class="text-slate-400"
                  v-text="sessionTypeShortLabel(row.session.type)"
                />
                <td>
                  <BaseBadge
                    :tone="row.badgeTone"
                    :dot="row.session.status === 'Live'"
                  >
                    {{ row.badgeLabel }}
                  </BaseBadge>
                </td>
                <td>
                  <BaseButton
                    v-if="row.state.kind === 'live'"
                    size="sm"
                    variant="success"
                    @click="openRoom(row.session.id)"
                  >
                    Darsga qaytish
                  </BaseButton>
                  <BaseButton
                    v-else-if="row.state.kind === 'ready'"
                    size="sm"
                    :loading="isStarting(row.session.id)"
                    @click="start(row.session.id)"
                  >
                    Darsni boshlash
                  </BaseButton>
                  <!--
                    Eski ilovada bu holat o'chirilgan TUGMA edi va sababi
                    `title` atributida yashiringan — telefonda o'qib
                    bo'lmasdi. Bu yerda sabab matn sifatida ham turadi.
                  -->
                  <span
                    v-else-if="row.state.kind === 'wait'"
                    class="inline-flex h-9 items-center rounded-lg border border-line px-3 text-xs text-slate-400"
                    :title="waitHint"
                  >
                    ⏳ {{ row.state.text }} qoldi
                  </span>
                  <BaseBadge
                    v-else-if="row.state.kind === 'ended'"
                    tone="success"
                  >
                    ✓ O‘tilgan
                  </BaseBadge>
                  <span
                    v-else
                    class="text-xs text-slate-400"
                  >Bekor</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <BaseButton
          v-if="hasMore"
          class="mt-3"
          size="sm"
          variant="secondary"
          block
          @click="limit += CHUNK"
        >
          Yana {{ Math.min(CHUNK, rows.length - limit) }} ta dars
        </BaseButton>
      </DataStatus>
    </div>
  </BaseCard>
</template>

<style scoped>
/* Eski `tr.dash-next` — eng yaqin dars qatori: accent fon va chap chiziq. */
.zn-row-next :deep(td) {
  background: color-mix(in oklab, var(--color-brand-500) 12%, transparent);
}
.zn-row-next :deep(td:first-child) {
  box-shadow: inset 3px 0 0 var(--color-brand-500);
}
</style>
