<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchTeacherAvailabilityToday } from '@/entities/teacher-availability'
import { toUserMessage } from '@/shared/api'
import { lookup } from '@/shared/lib/lookup'
import { formatTime } from '@/shared/lib/datetime'
import type { TeacherAvailabilityTodayDto } from '@/shared/types'
import { BaseBadge, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  USTOZLAR HOLATI (2026-08-17) — kunlik "darsga o'ta olasizmi?" tasdiqlash
 *  + o'rinbosar ustoz tizimining o'quv bo'limi paneli.
 * ════════════════════════════════════════════════════════════════════════
 *
 * Suhbatning O'ZI (savol/javob, dars tanlash, sabab, o'rinbosar qidirish
 * va taklif) BUTUNLAY Telegram bot orqali ketadi — bu sahifa faqat
 * BUGUNGI holatni ko'rsatadi. Real-vaqt push YO'Q (birinchi versiya —
 * loyihaning "Bu versiyada YO'Q" bo'limi), shuning uchun oddiy POLLING
 * (`refetchInterval`).
 */
const STATUS_LABELS: Record<string, string> = {
  Pending: 'Kutilmoqda',
  Confirmed: 'Tasdiqladi',
  SelectingSessions: 'Dars tanlamoqda',
  AwaitingReason: 'Sabab yozmoqda',
  AwaitingDays: 'Kun sonini kiritmoqda',
  Declined: 'Yo‘q dedi',
}

const STATUS_TONES: Record<string, 'neutral' | 'success' | 'warning' | 'danger'> = {
  Pending: 'neutral',
  Confirmed: 'success',
  SelectingSessions: 'warning',
  AwaitingReason: 'warning',
  AwaitingDays: 'warning',
  Declined: 'danger',
}

const COVERAGE_LABELS: Record<string, string> = {
  Open: 'O‘rinbosar qidirilmoqda',
  Resolved: 'O‘rinbosar topildi',
  Cancelled: 'Bekor qilindi',
}

const COVERAGE_TONES: Record<string, 'neutral' | 'success' | 'warning' | 'danger'> = {
  Open: 'warning',
  Resolved: 'success',
  Cancelled: 'neutral',
}

function statusLabel(status: string): string {
  return lookup(STATUS_LABELS, status, status)
}

function statusTone(status: string): 'neutral' | 'success' | 'warning' | 'danger' {
  return lookup(STATUS_TONES, status, 'neutral')
}

function coverageLabel(status: string | null): string {
  if (status === null) return '—'
  return lookup(COVERAGE_LABELS, status, status)
}

function coverageTone(status: string | null): 'neutral' | 'success' | 'warning' | 'danger' {
  if (status === null) return 'neutral'
  return lookup(COVERAGE_TONES, status, 'neutral')
}

/*
  ★ TUSHUNARSIZLIK (loyiha egasi, 2026-08-17): avval o'rinbosarning ISMI
  YALANG'OCH holda ko'rsatilardi — "Nodira Qosimova ... Bekzod Rahimov"
  qatorida kim ASL ustoz, kim O'RNIGA o'tayotgani umuman aniq emas edi.
  Endi shablonda TO'LIQ jumla chiziladi: "<asl ustoz> o'tolmaydi →
  <o'rinbosar> o'tib beradi" — funksiya emas, to'g'ridan-to'g'ri shablonda
  (mantiq juda oddiy, alohida yordamchiga chiqarish o'qishni qiyinlashtirardi).
*/

const query = useQuery({
  queryKey: ['teacher-availability', 'today'],
  queryFn: ({ signal }) => fetchTeacherAvailabilityToday({ signal }),
  refetchInterval: 20_000,
})

const rows = computed<TeacherAvailabilityTodayDto[]>(() => query.data.value ?? [])
const errorMessage = computed(() => (query.error.value !== null ? toUserMessage(query.error.value) : null))
</script>

<template>
  <div>
    <PageHeader
      title="Ustozlar holati"
      subtitle="Bugungi kun — ‘darsga o‘ta olasizmi?’ tasdiqlash va o‘rinbosar qidiruvi. Suhbat Telegram bot orqali ketadi."
    />

    <BaseCard flush>
      <DataStatus
        :pending="query.isPending.value"
        :error="errorMessage"
        :empty="rows.length === 0"
        :retrying="query.isFetching.value"
        :skeleton-rows="3"
        empty-icon="user-check"
        empty-title="Hozircha savol yuborilmagan"
        empty-text="Bugun darsi bor ustozlarga ertalab avtomatik yuboriladi (07:00–08:00)."
        @retry="query.refetch()"
      >
        <ul class="divide-y divide-line">
          <li
            v-for="row in rows"
            :key="row.checkinId"
            class="p-3.5"
          >
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
                v-text="row.teacherName"
              />
              <BaseBadge :tone="statusTone(row.status)">
                {{ statusLabel(row.status) }}
              </BaseBadge>
            </div>

            <template v-if="row.status === 'Declined'">
              <p
                v-if="row.declineReason !== null"
                class="mt-1.5 text-xs text-slate-400"
              >
                Sabab: {{ row.declineReason }}
                <span v-if="row.unavailableDays !== null && row.unavailableDays > 1">
                  ({{ row.unavailableDays }} kunga)
                </span>
              </p>

              <ul
                v-if="row.affectedSessions.length > 0"
                class="mt-2 space-y-1.5"
              >
                <li
                  v-for="session in row.affectedSessions"
                  :key="session.sessionId"
                  class="rounded-lg bg-ink-800 px-2.5 py-2 text-xs"
                >
                  <div class="flex flex-wrap items-center gap-2">
                    <span
                      class="tabular-nums text-slate-400"
                      v-text="formatTime(session.scheduledStart)"
                    />
                    <span
                      class="min-w-0 flex-1 truncate font-medium text-slate-200"
                      v-text="session.groupName"
                    />
                    <BaseBadge
                      size="xs"
                      :tone="session.substituteTeacherName !== null ? 'success' : coverageTone(session.status)"
                    >
                      {{ coverageLabel(session.status) }}
                    </BaseBadge>
                  </div>

                  <!--
                    ★ ANIQ JUMLA (loyiha egasi talabi): "kim asli o'tishi
                    kerak edi" va "kim o'tib berdi" bitta o'qib ketiladigan
                    qatorda, YO'NALISH belgisi (→) bilan.
                  -->
                  <p class="mt-1 text-slate-400">
                    <span v-text="row.teacherName" />
                    <span> o‘tolmaydi</span>
                    <span
                      v-if="session.substituteTeacherName !== null"
                      class="text-slate-500"
                    > → </span>
                    <span
                      v-if="session.substituteTeacherName !== null"
                      class="font-semibold text-emerald-400"
                      v-text="`${session.substituteTeacherName} o‘tib beradi`"
                    />
                    <span
                      v-else
                      class="text-amber-400"
                    > — {{ coverageLabel(session.status) }}</span>
                  </p>
                </li>
              </ul>
            </template>
          </li>
        </ul>
      </DataStatus>
    </BaseCard>
  </div>
</template>
