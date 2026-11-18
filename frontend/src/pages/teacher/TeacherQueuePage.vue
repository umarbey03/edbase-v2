<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchAssignments } from '@/entities/assignment'
import { fetchConversations, waitLabel, waitTone, waitingHours } from '@/entities/direct-message'
import { fetchMySelfStudyStudents, SELF_STUDY_CURATOR_KEYS } from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { useNow } from '@/shared/lib/use-now'
import { AppIcon, BaseBadge, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  NAVBAT — KURATORNING BOSH SAHIFASI (loyiha egasi, 2026-11-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * *"Kurator endi dars o'tmaydi — faqat o'quvchilarning savollariga javob
 * beradi."* Eski panelda "Chatlar", "Savollar", "Kuratorlik", "Darslarim"
 * aralashib yotardi va kurator "nima qilishim kerak?" degan savolga bitta
 * joydan javob topa olmasdi.
 *
 * ★ BITTA SAVOL — "KIM MENI KUTYAPTI?": uch xil ish bitta ekranda, har
 *   biri eng uzoq kutgani tepada:
 *     1) javob kutayotgan savollar (chat — Milliy ham, Mustaqil ham);
 *     2) tekshiriladigan uy vazifalari;
 *     3) Mustaqil o'quvchilar — dars o'zlashtirilganini belgilash.
 * ★ Ish shu yerda bajarilmaydi — qator bosilganda tegishli ekran AYNAN
 *   o'sha o'quvchi/vazifa bilan ochiladi.
 */
const now = useNow()

const conversationsQuery = useQuery({
  queryKey: ['dm', 'conversations'],
  queryFn: ({ signal }) => fetchConversations({ signal }),
  refetchInterval: 30_000,
})

const assignmentsQuery = useQuery({
  queryKey: ['assignments', 'list'],
  queryFn: ({ signal }) => fetchAssignments({ page: 1, pageSize: 50 }, { signal }),
  refetchInterval: 60_000,
})

const selfStudyQuery = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.mine,
  queryFn: ({ signal }) => fetchMySelfStudyStudents({ signal }),
  refetchInterval: 60_000,
})

const waiting = computed(() =>
  (conversationsQuery.data.value ?? [])
    .map((c) => ({ conversation: c, hours: waitingHours(c, now.value) }))
    .filter((row): row is { conversation: typeof row.conversation, hours: number } => row.hours !== null)
    .sort((a, b) => b.hours - a.hours),
)

const toGrade = computed(() =>
  (assignmentsQuery.data.value?.items ?? [])
    .map((a) => ({ assignment: a, pending: a.submissionCount - a.gradedCount }))
    .filter((row) => row.pending > 0)
    .sort((a, b) => b.pending - a.pending),
)

const selfStudy = computed(() =>
  (selfStudyQuery.data.value ?? [])
    .filter((s) => s.pendingSubmissions > 0)
    .sort((a, b) => b.pendingSubmissions - a.pendingSubmissions),
)

const allClear = computed(
  () =>
    !conversationsQuery.isPending.value
    && !assignmentsQuery.isPending.value
    && waiting.value.length === 0
    && toGrade.value.length === 0
    && selfStudy.value.length === 0,
)

const TONE_CLASS = {
  danger: 'text-rose-300',
  accent: 'text-amber-300',
  neutral: 'text-slate-400',
} as const
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Navbat"
      subtitle="Sizni kutayotganlar — eng uzoq kutgani tepada. Qatorni bosing, kerakli ekran ochiladi."
    />

    <div class="grid grid-cols-3 gap-3">
      <BaseCard class="p-4">
        <p class="text-2xl font-bold tabular-nums text-slate-50">
          {{ waiting.length }}
        </p>
        <p class="text-xs text-slate-400">
          Javob kutayotgan savol
        </p>
      </BaseCard>
      <BaseCard class="p-4">
        <p class="text-2xl font-bold tabular-nums text-slate-50">
          {{ toGrade.reduce((sum, row) => sum + row.pending, 0) }}
        </p>
        <p class="text-xs text-slate-400">
          Tekshiriladigan vazifa
        </p>
      </BaseCard>
      <BaseCard class="p-4">
        <p class="text-2xl font-bold tabular-nums text-slate-50">
          {{ selfStudy.length }}
        </p>
        <p class="text-xs text-slate-400">
          Mustaqil: baho kutmoqda
        </p>
      </BaseCard>
    </div>

    <BaseCard
      v-if="allClear"
      class="flex items-center gap-3 p-5"
    >
      <AppIcon
        name="check"
        :size="20"
        class="text-emerald-400"
      />
      <p class="text-sm text-slate-300">
        Hamma savolga javob berilgan, tekshiriladigan ish yo‘q. Yangi savol kelsa shu yerda
        va Telegram’da ko‘rinadi.
      </p>
    </BaseCard>

    <!-- 1) Savollar -->
    <section
      v-if="waiting.length > 0 || conversationsQuery.isPending.value"
      class="space-y-2"
    >
      <h2 class="text-sm font-bold text-slate-200">
        Javob kutayotgan savollar
      </h2>
      <DataStatus
        :pending="conversationsQuery.isPending.value"
        :error="conversationsQuery.error.value !== null ? toUserMessage(conversationsQuery.error.value) : null"
        :empty="false"
      >
        <ul class="divide-y divide-line overflow-hidden rounded-xl bg-ink-900 ring-1 ring-inset ring-line">
          <li
            v-for="row in waiting"
            :key="row.conversation.peerId"
          >
            <RouterLink
              :to="{ name: 'teacher-chat', query: { peer: String(row.conversation.peerId) } }"
              class="flex items-center gap-3 px-4 py-3 hover:bg-ink-800"
            >
              <span class="min-w-0 flex-1">
                <span class="flex items-center gap-2">
                  <span class="truncate text-sm font-semibold text-slate-100">{{ row.conversation.peerName ?? '—' }}</span>
                  <BaseBadge
                    v-if="row.conversation.groupName"
                    tone="neutral"
                  >{{ row.conversation.groupName }}</BaseBadge>
                </span>
                <span class="block truncate text-xs text-slate-400">{{ row.conversation.lastMessagePreview ?? '—' }}</span>
              </span>
              <span
                class="shrink-0 text-xs font-semibold tabular-nums"
                :class="TONE_CLASS[waitTone(row.hours)]"
              >{{ waitLabel(row.hours) }}</span>
              <AppIcon
                name="chevron-right"
                :size="16"
                class="shrink-0 text-slate-500"
              />
            </RouterLink>
          </li>
        </ul>
      </DataStatus>
    </section>

    <!-- 2) Uy vazifalari -->
    <section
      v-if="toGrade.length > 0"
      class="space-y-2"
    >
      <h2 class="text-sm font-bold text-slate-200">
        Tekshiriladigan uy vazifalari
      </h2>
      <ul class="divide-y divide-line overflow-hidden rounded-xl bg-ink-900 ring-1 ring-inset ring-line">
        <li
          v-for="row in toGrade"
          :key="row.assignment.id"
        >
          <RouterLink
            :to="{ name: 'teacher-grading', query: { assignment: String(row.assignment.id) } }"
            class="flex items-center gap-3 px-4 py-3 hover:bg-ink-800"
          >
            <span class="min-w-0 flex-1">
              <span class="block truncate text-sm font-semibold text-slate-100">{{ row.assignment.title ?? 'Vazifa' }}</span>
              <span class="block truncate text-xs text-slate-400">{{ row.assignment.groupName ?? 'Mustaqil' }}</span>
            </span>
            <BaseBadge tone="accent">
              {{ row.pending }} ta ish
            </BaseBadge>
            <AppIcon
              name="chevron-right"
              :size="16"
              class="shrink-0 text-slate-500"
            />
          </RouterLink>
        </li>
      </ul>
    </section>

    <!-- 3) Mustaqil: o'zlashtirish -->
    <section
      v-if="selfStudy.length > 0"
      class="space-y-2"
    >
      <h2 class="text-sm font-bold text-slate-200">
        Mustaqil o‘quvchilar — darsni baholang
      </h2>
      <ul class="divide-y divide-line overflow-hidden rounded-xl bg-ink-900 ring-1 ring-inset ring-line">
        <li
          v-for="row in selfStudy"
          :key="row.studentId"
        >
          <RouterLink
            :to="{ name: 'teacher-self-study-student', params: { studentId: String(row.studentId) } }"
            class="flex items-center gap-3 px-4 py-3 hover:bg-ink-800"
          >
            <span class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100">
              {{ row.fullName }}
              <span
                v-if="row.isBackup"
                class="ml-1 text-xs font-normal text-slate-400"
              >(zaxira)</span>
            </span>
            <BaseBadge tone="accent">
              {{ row.pendingSubmissions }} ta ish
            </BaseBadge>
            <AppIcon
              name="chevron-right"
              :size="16"
              class="shrink-0 text-slate-500"
            />
          </RouterLink>
        </li>
      </ul>
    </section>
  </div>
</template>
