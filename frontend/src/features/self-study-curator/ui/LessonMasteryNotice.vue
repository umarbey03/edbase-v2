<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchMyLessonMastery, SELF_STUDY_CURATOR_KEYS } from '@/entities/self-study-curator'
import { formatDate } from '@/shared/lib/datetime'
import type { MasteryStatusName } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

/**
 * DARS SAHIFASIDA KURATOR QARORI (Mustaqil, 2026-11-18).
 *
 * ★ Faqat qaror BOR darsda chiqadi — `mastery` kurs daraxtidan keladi, ya'ni
 *   guruh o'quvchisida (doim `null`) so'rov umuman ketmaydi. Izoh alohida
 *   endpointdan: kurs daraxtida u yo'q (har darsga matn tortish ortiqcha).
 */
const props = defineProps<{
  lessonId: number
  mastery: MasteryStatusName | null | undefined
  /** 2026-11-25: dars kurator qarorisiz vaqtincha ochilgan («bitta dars oldinga»). */
  provisional?: boolean
}>()

const query = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.myMastery,
  queryFn: ({ signal }) => fetchMyLessonMastery({ signal }),
  enabled: computed(() => props.mastery != null),
  staleTime: 60_000,
})

const decision = computed(() => (query.data.value ?? []).find((m) => m.lessonId === props.lessonId) ?? null)
</script>

<template>
  <div
    v-if="props.mastery === 'NeedsWork'"
    class="mb-4 flex gap-3 rounded-xl border border-rose-500/30 bg-rose-500/10 px-4 py-3"
    role="status"
  >
    <AppIcon
      name="alert"
      :size="18"
      class="mt-0.5 shrink-0 text-rose-300"
    />
    <div class="min-w-0 text-sm">
      <p class="font-semibold text-rose-100">
        Kurator bu darsni qayta ishlashni so‘radi
      </p>
      <p
        v-if="decision?.note"
        class="mt-1 whitespace-pre-line text-rose-200"
        v-text="decision.note"
      />
      <p class="mt-1 text-xs text-rose-200/80">
        Tuzatib, vazifani qayta topshiring — kurator «o‘zlashtirdi» desa keyingi dars ochiladi.
      </p>
    </div>
  </div>
  <div
    v-else-if="props.mastery === 'Mastered'"
    class="mb-4 flex items-center gap-3 rounded-xl border border-emerald-500/30 bg-emerald-500/10 px-4 py-3 text-sm text-emerald-100"
    role="status"
  >
    <AppIcon
      name="award"
      :size="18"
      class="shrink-0 text-emerald-300"
    />
    <span class="min-w-0">
      Kurator: «yaxshi o‘zlashtirdingiz»<template v-if="decision !== null">
        · {{ formatDate(decision.decidedAt) }}
      </template>
      <template v-if="decision?.note">
        — {{ decision.note }}
      </template>
    </span>
  </div>
  <div
    v-else-if="props.provisional === true"
    class="mb-4 flex gap-3 rounded-xl border border-sky-500/30 bg-sky-500/10 px-4 py-3"
    role="status"
  >
    <AppIcon
      name="clock"
      :size="18"
      class="mt-0.5 shrink-0 text-sky-300"
    />
    <div class="min-w-0 text-sm">
      <p class="font-semibold text-sky-100">
        Zo‘r! Testni 100% yechganingiz uchun bu dars kuratorni kutmasdan ochildi
      </p>
      <p class="mt-1 text-xs text-sky-200/80">
        Kurator oldingi darsingizni ish vaqtida tekshiradi. Agar u «qayta ishlang» desa,
        avval o‘sha darsni tuzatasiz, keyin davom etasiz.
      </p>
    </div>
  </div>
</template>
