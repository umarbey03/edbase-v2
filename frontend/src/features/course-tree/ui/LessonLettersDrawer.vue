<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, reactive, ref, watch } from 'vue'

import { fetchCourseLetters, saveLessonLetters, type LessonLettersDto } from '@/entities/points'
import { toUserMessage } from '@/shared/api'
import { BaseDrawer, DataStatus } from '@/shared/ui'

/**
 * Kurs quruvchi: qaysi darsda qaysi arab harflari o'rgatiladi (2026-11-21).
 * Shundan o'quvchi profilidagi 28 harfli jadval yonadi. Qoida, imtihon kabi
 * darslarda bo'sh qoldiriladi. Saqlash — maydondan chiqqanda (har dars alohida).
 */
const props = defineProps<{ open: boolean, courseId: number }>()
const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()

const query = useQuery({
  queryKey: computed(() => ['course', 'letters', props.courseId]),
  queryFn: ({ signal }) => fetchCourseLetters(props.courseId, { signal }),
  enabled: computed(() => props.open),
})

const drafts = reactive<Record<number, string>>({})
const status = reactive<Record<number, 'saving' | 'saved' | string>>({})
const loadedFor = ref<number | null>(null)

watch(() => query.data.value, (rows) => {
  if (rows === undefined) return
  for (const row of rows) drafts[row.lessonId] = row.letters ?? ''
  loadedFor.value = props.courseId
}, { immediate: true })

const used = computed(() => new Set(Object.values(drafts).join('').replace(/\s/g, '').split('')))

async function save(row: LessonLettersDto): Promise<void> {
  const value = (drafts[row.lessonId] ?? '').trim()
  if (value === (row.letters ?? '')) return
  status[row.lessonId] = 'saving'
  try {
    const saved = await saveLessonLetters(props.courseId, row.lessonId, value.length > 0 ? value : null)
    drafts[row.lessonId] = saved.letters ?? ''
    status[row.lessonId] = 'saved'
    await queryClient.invalidateQueries({ queryKey: ['course', 'letters', props.courseId] })
  } catch (err) {
    status[row.lessonId] = toUserMessage(err)
  }
}

const error = computed(() => (query.error.value ? toUserMessage(query.error.value) : null))
</script>

<template>
  <BaseDrawer
    :open="open"
    title="Harflar jadvali"
    subtitle="Qaysi darsda qaysi harf o‘rgatiladi"
    @close="emit('close')"
  >
    <p class="mb-3 text-xs text-slate-400">
      Arab harflarini yozing (masalan <span class="font-arabic text-base">رز</span>). O‘quvchi darsni o‘zlashtirganda
      profilidagi jadvalda shu harflar yonadi. Belgilangan: <b>{{ used.size }}</b> / 28.
    </p>
    <DataStatus
      :pending="query.isPending.value"
      :error="error"
      :empty="(query.data.value ?? []).length === 0"
      :skeleton-rows="6"
      empty-title="Kursda dars yo‘q."
      @retry="query.refetch()"
    >
      <ul class="space-y-1.5">
        <li
          v-for="row in query.data.value ?? []"
          :key="row.lessonId"
          class="flex items-center gap-2 rounded-lg border border-line bg-ink-900 px-2.5 py-1.5"
        >
          <span class="w-6 shrink-0 text-right text-xs tabular-nums text-dim">{{ row.number }}</span>
          <span
            class="min-w-0 flex-1 truncate text-sm text-slate-200"
            v-text="row.lessonName"
          />
          <input
            v-model="drafts[row.lessonId]"
            class="zn-input font-arabic w-24 py-1 text-center text-lg"
            dir="rtl"
            maxlength="16"
            :aria-label="`${row.lessonName} — harflar`"
            @blur="save(row)"
            @keydown.enter.prevent="($event.target as HTMLInputElement).blur()"
          >
          <span
            class="w-4 shrink-0 text-xs"
            :class="status[row.lessonId] === 'saved' ? 'text-emerald-400' : 'text-rose-400'"
            :title="status[row.lessonId] !== 'saved' && status[row.lessonId] !== 'saving' ? status[row.lessonId] : undefined"
          >{{ status[row.lessonId] === 'saved' ? '✓' : status[row.lessonId] === 'saving' ? '…' : status[row.lessonId] ? '!' : '' }}</span>
        </li>
      </ul>
    </DataStatus>
  </BaseDrawer>
</template>
