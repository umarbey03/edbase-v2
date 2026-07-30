<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { AppIcon, BaseSpinner } from '@/shared/ui'

/**
 * O'quvchi tanlash — qidiruv orqali.
 *
 * NEGA `<select>` EMAS: markazda mingdan ortiq o'quvchi bor va `fetchUsers`
 * bir so'rovda hammasini bermaydi (server sahifalaydi). To'liq ro'yxatni
 * yuklab olish uchun o'nlab so'rov kerak bo'lardi, tanlash esa baribir
 * uzun ro'yxatdan qidirishga aylanardi.
 *
 * ★ Server qidiruvda kamida `USER_SEARCH_MIN` (3) belgi talab qiladi va
 * qisqasida 400 beradi — shuning uchun qisqa satr UMUMAN yuborilmaydi.
 */
const props = defineProps<{
  /** Tanlangan o'quvchi (`null` — hali tanlanmagan). */
  modelValue: { id: number; name: string } | null
  label?: string
}>()

const emit = defineEmits<{ 'update:modelValue': [value: { id: number; name: string } | null] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)

const term = computed(() => debouncedSearch.value.trim())
const tooShort = computed(() => term.value.length > 0 && term.value.length < USER_SEARCH_MIN)
const effectiveSearch = computed(() =>
  term.value.length >= USER_SEARCH_MIN ? term.value : undefined,
)

const studentsQuery = useQuery({
  queryKey: ['users', 'student-picker', effectiveSearch],
  queryFn: ({ signal }) =>
    fetchUsers(
      { role: 'Student', isActive: true, search: effectiveSearch.value, pageSize: 20 },
      { signal },
    ),
  // Qidiruvsiz ham birinchi 20 ta faol o'quvchi ko'rsatiladi: ko'p holatda
  // kassir yaqinda qo'shilgan o'quvchini qidiradi va u ro'yxat boshida bo'ladi.
  enabled: computed(() => !tooShort.value),
})

const students = computed(() => studentsQuery.data.value?.items ?? [])

const errorMessage = computed(() =>
  studentsQuery.error.value !== null ? toUserMessage(studentsQuery.error.value) : null,
)

function choose(student: { id: number; fullName: string | null; email: string | null }): void {
  emit('update:modelValue', { id: student.id, name: student.fullName ?? student.email ?? '—' })
}

function clear(): void {
  emit('update:modelValue', null)
  search.value = ''
}
</script>

<template>
  <div>
    <span
      class="mb-1.5 block text-xs font-medium text-slate-400"
      v-text="props.label ?? 'O‘quvchi'"
    />

    <!-- Tanlangan holat: qayta qidirmasdan ko'rinib turadi. -->
    <div
      v-if="props.modelValue !== null"
      class="flex items-center gap-2 rounded-lg border border-line bg-ink-800 px-3 py-2"
    >
      <AppIcon
        name="user-check"
        :size="16"
        class="shrink-0 text-brand-400"
      />
      <span
        class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
        v-text="props.modelValue.name"
      />
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg text-slate-400 hover:text-slate-100"
        title="Boshqa o‘quvchi tanlash"
        @click="clear"
      >
        <AppIcon
          name="close"
          :size="16"
        />
      </button>
    </div>

    <template v-else>
      <div class="relative">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="Ism, telefon yoki email bo‘yicha qidirish"
        >
      </div>

      <p
        v-if="tooShort"
        class="mt-1 text-[11px] text-dim"
      >
        Qidirish uchun kamida {{ USER_SEARCH_MIN }} belgi kiriting.
      </p>

      <p
        v-else-if="errorMessage !== null"
        class="mt-1 text-[11px] text-rose-400"
        role="alert"
        v-text="errorMessage"
      />

      <div
        v-else-if="studentsQuery.isPending.value"
        class="mt-2 flex justify-center py-3"
      >
        <BaseSpinner />
      </div>

      <p
        v-else-if="students.length === 0"
        class="mt-2 text-[11px] text-dim"
      >
        O‘quvchi topilmadi.
      </p>

      <ul
        v-else
        class="scrollbar-slim mt-2 max-h-56 overflow-y-auto rounded-lg border border-line"
      >
        <li
          v-for="student in students"
          :key="student.id"
          class="border-b border-line last:border-b-0"
        >
          <button
            type="button"
            class="flex min-h-11 w-full flex-col items-start justify-center px-3 py-2 text-left transition-colors hover:bg-ink-800"
            @click="choose(student)"
          >
            <span
              class="text-sm text-slate-100"
              v-text="student.fullName ?? student.email ?? '—'"
            />
            <span
              class="text-[11px] text-dim"
              v-text="student.phone ?? student.email ?? ''"
            />
          </button>
        </li>
      </ul>
    </template>
  </div>
</template>
