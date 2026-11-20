<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { CURATOR_WORKSPACE_KEYS, type AwaitingLessonDto } from '@/entities/curator-workspace'
import {
  SELF_STUDY_CURATOR_KEYS,
  decideLessonMastery,
  fetchStudentLessonBoard,
} from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseButton } from '@/shared/ui'

/**
 * ⏳ DARS QARORI — ish stolining tepasida QOTIB turadi (2026-11-20).
 *
 * Kurator o'quvchining yozishmasini o'qib turib, shu yerning o'zida keyingi
 * darsni ochadi: "O'zlashtirdi" yoki izoh bilan "Qayta ishlasin". Qoida va
 * ruxsat — mavjud `PUT .../lessons/{id}/mastery` (o'zgarmagan); dars tafsiloti
 * (video/vazifa/test) — mavjud darslar xaritasidan.
 */
const props = defineProps<{
  studentId: number
  /** Qaror kutilayotgan dars; `null` — o'quvchi joriy darsni hali tugatmagan. */
  awaiting: AwaitingLessonDto | null
}>()

const emit = defineEmits<{ openBoard: [] }>()

const queryClient = useQueryClient()

const boardQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_CURATOR_KEYS.board(props.studentId)),
  queryFn: ({ signal }) => fetchStudentLessonBoard(props.studentId, { signal }),
})

/*
  ★ JORIY DARS (2026-11-20, jonli holat): o'quvchi "2-darsim yopilmadi" deb
  yozdi, lekin 2-dars testini topshirmagan edi — qaror kartasi chiqmagani
  TO'G'RI, ammo kurator NIMA yetishmayotganini ko'rmasdi. Endi qaror hali
  erta bo'lsa ham karta joriy darsni ✓/✗ ro'yxati bilan ko'rsatadi.
*/
const row = computed(() => {
  const lessons = boardQuery.data.value?.lessons ?? []
  if (props.awaiting !== null) return lessons.find((l) => l.lessonId === props.awaiting!.lessonId) ?? null
  return lessons.find((l) => l.unlocked && l.mastery !== 'Mastered') ?? null
})
const ready = computed(() => props.awaiting !== null)
const lessonId = computed(() => props.awaiting?.lessonId ?? row.value?.lessonId ?? 0)
const lessonNumber = computed(() => (props.awaiting?.index ?? row.value?.index ?? 0) + 1)
const lessonName = computed(() => props.awaiting?.lessonName ?? row.value?.lessonName ?? '')
const canDecide = computed(() => ready.value && (boardQuery.data.value?.canDecide ?? false))
const nextLesson = computed(() => {
  const lessons = boardQuery.data.value?.lessons ?? []
  const index = lessons.findIndex((l) => l.lessonId === lessonId.value)
  return index >= 0 ? lessons[index + 1] ?? null : null
})

const needsWorkOpen = ref(false)
const note = ref('')
const error = ref<string | null>(null)

watch(lessonId, () => {
  needsWorkOpen.value = false
  note.value = ''
  error.value = null
})

const decide = useMutation({
  mutationFn: (status: 'Mastered' | 'NeedsWork') =>
    decideLessonMastery(props.studentId, lessonId.value, {
      status,
      note: note.value.trim().length > 0 ? note.value.trim() : null,
    }),
  onSuccess: async (_board, status) => {
    error.value = null
    needsWorkOpen.value = false
    note.value = ''
    showToast(status === 'Mastered'
      ? `${lessonNumber.value}-dars o‘zlashtirildi — keyingi dars ochildi`
      : 'O‘quvchiga «qayta ishlasin» yuborildi')
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: CURATOR_WORKSPACE_KEYS.all }),
      queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.board(props.studentId) }),
      queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.events(props.studentId) }),
    ])
  },
  onError: (err) => { error.value = toUserMessage(err) },
})

function mark(done: boolean): string {
  return done ? 'text-emerald-300' : 'text-rose-300'
}
</script>

<template>
  <section
    v-if="ready || row !== null"
    class="shrink-0 rounded-xl border px-3 py-2.5"
    :class="ready ? 'border-emerald-500/30 bg-emerald-500/[0.07]' : 'border-line bg-ink-900'"
    aria-label="Dars qarori"
  >
    <div class="flex items-start justify-between gap-2">
      <div class="min-w-0">
        <p
          class="text-[11px] font-bold uppercase tracking-wider"
          :class="ready ? 'text-emerald-300' : 'text-slate-400'"
        >
          {{ ready ? '⏳ Dars qarori kutilmoqda' : '📖 Joriy dars — o‘quvchi bajaryapti' }}
        </p>
        <p class="mt-0.5 truncate text-sm font-semibold text-slate-100">
          {{ lessonNumber }}-dars · {{ lessonName }}
        </p>
        <p
          v-if="row !== null"
          class="mt-1 flex flex-wrap gap-x-3 gap-y-0.5 text-xs"
        >
          <span
            v-if="row.hasVideo"
            :class="mark(row.videoWatched)"
          >{{ row.videoWatched ? '✓' : '✗' }} video</span>
          <span
            v-if="row.hasAssignment"
            :class="mark(row.assignment?.submissionId != null)"
          >
            {{ row.assignment?.submissionId != null ? '✓' : '✗' }} vazifa
            <template v-if="row.assignment?.score != null">{{ row.assignment.score }}/{{ row.assignment.maxScore }}</template>
            <template v-else-if="row.assignment?.submissionStatus === 'Submitted'">(tekshirilmagan)</template>
          </span>
          <span
            v-if="row.hasTest"
            :class="mark(row.testTaken)"
          >{{ row.testTaken ? '✓' : '✗' }} test <template v-if="row.testPercent !== null">{{ row.testPercent }}%</template></span>
        </p>
      </div>
      <button
        type="button"
        class="hidden shrink-0 items-center gap-1 rounded-lg px-2 py-1 text-xs font-semibold text-slate-400 hover:bg-ink-800 hover:text-slate-100 sm:flex"
        @click="emit('openBoard')"
      >
        <AppIcon
          name="book"
          :size="13"
        />
        Darslar xaritasi
      </button>
    </div>
    <p
      v-if="!ready && row !== null"
      class="mt-1.5 text-xs text-slate-400"
    >
      Qaror tugmasi o‘quvchi <b>✗</b> belgili qismlarni bajargach chiqadi — kerak bo‘lsa unga yozib eslating.
    </p>

    <div
      v-if="canDecide"
      class="mt-2.5"
    >
      <div
        v-if="!needsWorkOpen"
        class="flex flex-wrap gap-2"
      >
        <BaseButton
          size="sm"
          variant="success"
          :loading="decide.isPending.value && !needsWorkOpen"
          @click="decide.mutate('Mastered')"
        >
          ✅ O‘zlashtirdi{{ nextLesson !== null ? ` → ${nextLesson.index + 1}-dars ochiladi` : '' }}
        </BaseButton>
        <BaseButton
          size="sm"
          variant="ghost"
          @click="needsWorkOpen = true"
        >
          ↩ Qayta ishlasin
        </BaseButton>
      </div>
      <div
        v-else
        class="space-y-2"
      >
        <textarea
          v-model="note"
          class="zn-input min-h-16 w-full text-sm"
          maxlength="500"
          placeholder="O‘quvchiga nimani qayta ishlashini yozing (masalan: «Alif harfini 20 marta qayta yozing»)"
        />
        <div class="flex gap-2">
          <BaseButton
            size="sm"
            variant="warning"
            :disabled="note.trim().length < 3"
            :loading="decide.isPending.value"
            @click="decide.mutate('NeedsWork')"
          >
            Yuborish
          </BaseButton>
          <BaseButton
            size="sm"
            variant="ghost"
            @click="needsWorkOpen = false"
          >
            Bekor qilish
          </BaseButton>
        </div>
      </div>
    </div>
    <p
      v-else-if="ready && !boardQuery.isPending.value"
      class="mt-2 text-xs text-slate-400"
    >
      Qarorni o‘quvchining kuratori beradi.
    </p>

    <p
      v-if="error !== null"
      class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      role="alert"
      v-text="error"
    />
  </section>
</template>
