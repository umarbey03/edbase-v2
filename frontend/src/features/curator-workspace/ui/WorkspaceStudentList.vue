<script setup lang="ts">
import { computed, ref } from 'vue'

import {
  WORKSPACE_FILTERS,
  matchesFilter,
  shortAgo,
  type WorkspaceFilter,
  type WorkspaceStudentDto,
} from '@/entities/curator-workspace'
import { AppIcon } from '@/shared/ui'

/**
 * Ish stoli — chap ro'yxat: "kim nimani kutyapti".
 * Tartib SERVERDA (ish kutayotganlar tepada, eng uzoq kutgani birinchi);
 * bu yer faqat qidiradi va filtrlaydi.
 */
const props = defineProps<{
  rows: WorkspaceStudentDto[]
  selectedId: number | null
}>()

const emit = defineEmits<{ select: [studentId: number] }>()

const search = ref('')
const filter = ref<WorkspaceFilter>('all')

const counts = computed(() => {
  const result = {} as Record<WorkspaceFilter, number>
  for (const option of WORKSPACE_FILTERS)
    result[option.key] = props.rows.filter((row) => matchesFilter(row, option.key) && (option.key === 'all' ? row.needsAction : true)).length
  return result
})

const visible = computed(() => {
  const term = search.value.trim().toLocaleLowerCase('uz')
  return props.rows.filter((row) =>
    matchesFilter(row, filter.value)
    && (term.length === 0 || row.fullName.toLocaleLowerCase('uz').includes(term)))
})

function subtitle(row: WorkspaceStudentDto): string {
  if (row.isSelfStudy) return row.isBackup ? 'Mustaqil · zaxira' : 'Mustaqil'
  return row.groupName ?? 'Guruh'
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col rounded-xl border border-line bg-ink-900">
    <div class="shrink-0 space-y-2 border-b border-line p-3">
      <div class="relative">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="15"
          />
        </span>
        <input
          v-model="search"
          class="zn-input py-2 pl-9 text-sm"
          placeholder="O‘quvchini qidirish"
          aria-label="O‘quvchini qidirish"
        >
      </div>
      <div
        class="flex gap-1"
        role="tablist"
      >
        <button
          v-for="option in WORKSPACE_FILTERS"
          :key="option.key"
          type="button"
          role="tab"
          :aria-selected="filter === option.key"
          class="flex flex-1 items-center justify-center gap-1 rounded-lg px-2 py-1.5 text-xs font-semibold transition-colors"
          :class="filter === option.key ? 'bg-brand-500 text-on-brand' : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'"
          @click="filter = option.key"
        >
          {{ option.label }}
          <span
            v-if="counts[option.key] > 0"
            class="rounded-full px-1.5 text-[10px] tabular-nums"
            :class="filter === option.key ? 'bg-on-brand/20' : 'bg-ink-800'"
            v-text="counts[option.key]"
          />
        </button>
      </div>
    </div>

    <ul
      class="scrollbar-slim min-h-0 flex-1 overflow-y-auto p-1.5"
      role="listbox"
      aria-label="O‘quvchilar"
    >
      <li
        v-if="visible.length === 0"
        class="px-3 py-8 text-center text-sm text-slate-500"
      >
        {{ rows.length === 0 ? 'Sizga hali o‘quvchi biriktirilmagan.' : 'Bu filtr bo‘yicha hech kim yo‘q — hammasi bajarilgan.' }}
      </li>
      <li
        v-for="row in visible"
        :key="row.studentId"
      >
        <button
          type="button"
          role="option"
          :aria-selected="row.studentId === selectedId"
          class="flex w-full items-start gap-2.5 rounded-lg px-2.5 py-2 text-left transition-colors"
          :class="row.studentId === selectedId ? 'bg-brand-500/15' : 'hover:bg-ink-800'"
          @click="emit('select', row.studentId)"
        >
          <span
            class="mt-1.5 size-2 shrink-0 rounded-full"
            :class="row.needsAction ? 'bg-amber-400' : 'bg-transparent'"
            aria-hidden="true"
          />
          <span class="min-w-0 flex-1">
            <span class="flex items-baseline justify-between gap-2">
              <span
                class="truncate text-sm font-semibold text-slate-100"
                v-text="row.fullName"
              />
              <span
                class="shrink-0 text-[11px] tabular-nums"
                :class="row.needsAction ? 'text-amber-300' : 'text-dim'"
                v-text="shortAgo(row.waitingSince ?? row.lastActivityAt)"
              />
            </span>
            <span
              class="block truncate text-[11px] text-dim"
              v-text="subtitle(row)"
            />
            <span
              v-if="row.needsAction"
              class="mt-1 flex flex-wrap gap-1.5 text-[11px] font-semibold"
            >
              <span
                v-if="row.unreadCount > 0"
                class="rounded-md bg-sky-500/15 px-1.5 py-0.5 text-sky-300"
                :title="`${row.unreadCount} ta o‘qilmagan xabar`"
              >💬 {{ row.unreadCount }}</span>
              <span
                v-if="row.unansweredQuestions > 0"
                class="rounded-md bg-violet-500/15 px-1.5 py-0.5 text-violet-300"
                :title="`${row.unansweredQuestions} ta javobsiz dars savoli`"
              >❓ {{ row.unansweredQuestions }}</span>
              <span
                v-if="row.pendingSubmissions > 0"
                class="rounded-md bg-amber-500/15 px-1.5 py-0.5 text-amber-300"
                :title="`${row.pendingSubmissions} ta tekshirilmagan vazifa`"
              >📝 {{ row.pendingSubmissions }}</span>
              <span
                v-if="row.awaitingDecision !== null"
                class="rounded-md bg-emerald-500/15 px-1.5 py-0.5 text-emerald-300"
                :title="`${row.awaitingDecision.index + 1}-dars qarori kutilmoqda`"
              >🔓 {{ row.awaitingDecision.index + 1 }}-dars</span>
            </span>
          </span>
        </button>
      </li>
    </ul>
  </div>
</template>
