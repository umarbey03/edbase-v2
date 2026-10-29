<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { fetchLessonBook, fetchUnit, interactiveKeys } from '@/entities/interactive-unit'
import { createApiAdapter, InteractiveLessonView } from '@/features/interactive-lesson'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseButton } from '@/shared/ui'
import StudentSubHeader from '@/widgets/student-shell/ui/StudentSubHeader.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARSLIK SAHIFASI — `/oquv/dars/:lessonId/darslik` (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ ALOHIDA MARSHRUT, TAB EMAS: 80 daqiqalik, 7 blokli dars dars sahifasidagi
 *   Vazifa/Test/Savol-javob tablari qatoriga sig'maydi va telefonda "skroll
 *   ichida skroll" bo'lardi. Telegram "orqaga" tugmasi marshrut tarixi bilan
 *   darsga qaytaradi; `?b=A3` — joriy blok (havola berish mumkin).
 * ★ `oquv/` ostida — "O'quv" bandi yonib turadi; mehmon `GUEST_ROUTES` bo'yicha
 *   Tariflarga ketadi; `GROUP_ONLY_ROUTES` ga QO'SHILMAGAN (Mustaqil+ ham ochadi).
 * 🔴 Himoya SERVERDA: huquqsiz o'quvchiga birlik 404 (borligi ham aytilmaydi),
 *   bu sahifa faqat holatni ko'rsatadi.
 */
const route = useRoute()
const router = useRouter()

const lessonId = computed(() => Number(route.params['lessonId']))

const lessonQuery = useQuery({
  queryKey: computed(() => interactiveKeys.lesson(lessonId.value)),
  queryFn: ({ signal }) => fetchLessonBook(lessonId.value, { signal }),
})

const unitId = computed(() => lessonQuery.data.value?.unit?.id ?? null)

const unitQuery = useQuery({
  queryKey: computed(() => interactiveKeys.unit(unitId.value ?? 0)),
  queryFn: ({ signal }) => fetchUnit(unitId.value!, { signal }),
  enabled: computed(() => unitId.value !== null),
  // Progress — server holati; sahifaga qaytilganda yangidan o'qiladi.
  staleTime: 0,
})

const adapter = computed(() => (unitId.value === null ? null : createApiAdapter(unitId.value)))

const step = computed<string | null>({
  get: () => (typeof route.query['b'] === 'string' ? route.query['b'] : null),
  set: (b) => {
    void router.replace({ query: { ...route.query, b: b ?? undefined } })
  },
})

const loading = computed(() => lessonQuery.isPending.value || (unitId.value !== null && unitQuery.isPending.value))
const error = computed(() => {
  const e = lessonQuery.error.value ?? unitQuery.error.value
  return e === null ? null : toUserMessage(e)
})

const access = computed(() => lessonQuery.data.value?.access ?? null)

function backToLesson(): void {
  void router.push({ name: 'student-lesson', params: { lessonId: String(lessonId.value) } })
}
</script>

<template>
  <div class="lg:max-w-5xl">
    <StudentSubHeader
      title="Interaktiv darslik"
      :subtitle="unitQuery.data.value ? `${unitQuery.data.value.meta.daraja} · ${unitQuery.data.value.meta.birlik}-dars` : undefined"
    />

    <div
      v-if="loading"
      class="space-y-3"
      aria-busy="true"
    >
      <div class="h-28 animate-pulse rounded-xl border border-line bg-ink-900" />
      <div class="h-11 animate-pulse rounded-xl bg-ink-800" />
      <div class="h-72 animate-pulse rounded-xl border border-line bg-ink-900" />
    </div>

    <div
      v-else-if="error"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
      role="alert"
    >
      <p
        class="text-sm text-rose-400"
        v-text="error"
      />
      <BaseButton
        class="mt-4"
        size="sm"
        variant="secondary"
        @click="lessonQuery.refetch(); unitQuery.refetch()"
      >
        Qayta urinish
      </BaseButton>
    </div>

    <InteractiveLessonView
      v-else-if="unitQuery.data.value && adapter"
      v-model:step="step"
      :unit="unitQuery.data.value"
      :adapter="adapter"
    />

    <div
      v-else
      class="rounded-xl border border-line bg-ink-900 px-[18px] py-[26px] text-center"
    >
      <AppIcon
        name="book"
        :size="28"
        class="mx-auto text-slate-400"
      />
      <p class="mt-3 text-[15px] font-bold">
        {{ access === 'upsell' ? 'Interaktiv darslik Mustaqil+ tarifida' : 'Bu darsda interaktiv darslik yo‘q' }}
      </p>
      <p class="mt-[7px] text-[13px] leading-relaxed text-slate-400">
        {{ access === 'upsell'
          ? 'Audio, mashqlar, harf yozish va mini-test — Mustaqil+ tarifiga o‘tsangiz ochiladi.'
          : 'Darslik hali bu darsga ulanmagan yoki dars yopiq.' }}
      </p>
      <div class="mt-4 flex flex-wrap justify-center gap-2">
        <BaseButton
          v-if="access === 'upsell'"
          size="sm"
          @click="router.push({ name: 'student-plans' })"
        >
          Tariflarni ko‘rish
        </BaseButton>
        <BaseButton
          size="sm"
          variant="secondary"
          @click="backToLesson"
        >
          Darsga qaytish
        </BaseButton>
      </div>
    </div>
  </div>
</template>
