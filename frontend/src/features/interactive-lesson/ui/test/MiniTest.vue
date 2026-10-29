<script setup lang="ts">
import { computed, reactive, ref } from 'vue'

import type { MiniTestDto, MiniTestResult, UnitProgress } from '@/entities/interactive-unit'
import { toUserMessage } from '@/shared/api'

import { useLessonContext } from '../../model/context'
import { feedbackMood } from '../../model/feedbackMood'
import OptionButton from '../exercises/OptionButton.vue'
import ArabicText from '../parts/ArabicText.vue'
import MixedText from '../parts/MixedText.vue'
import NuriFeedback from '../parts/NuriFeedback.vue'
import PlayButton from '../parts/PlayButton.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  MINI-TEST — dars oxiridagi 8–10 savol (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ Savollar `test/start` dan KALITSIZ keladi; javoblar klientda yig'iladi
 *   va oxirida BITTA `submit` (sekin tarmoqda har savolga so'rov ketmasin).
 * ★ Birinchi topshiriq ball beradi (0–10), keyingilari — mashq.
 * ★ Gapirish (ovozli) savoli avtomatik baholanmaydi — natijada alohida aytiladi.
 * ★ Vaqt chegarasi yo'q: kitobdagi "10 daqiqa" — tavsiya.
 */
const props = defineProps<{ title: string | null; previous: UnitProgress['test'] }>()

const { adapter } = useLessonContext()

const test = ref<MiniTestDto | null>(null)
const result = ref<MiniTestResult | null>(null)
const loading = ref(false)
const error = ref<string | null>(null)
const choices = reactive<Record<number, number>>({})
const texts = reactive<Record<number, string>>({})

const answerable = computed(() => (test.value?.savollar ?? []).filter((q) => q.mode !== 'oral'))
const answered = computed(
  () =>
    answerable.value.filter((q) => (q.mode === 'choice' ? choices[q.index] !== undefined : (texts[q.index] ?? '').trim().length > 0))
      .length,
)

async function start(): Promise<void> {
  loading.value = true
  error.value = null
  result.value = null
  for (const k of Object.keys(choices)) delete choices[Number(k)]
  for (const k of Object.keys(texts)) delete texts[Number(k)]
  try {
    test.value = await adapter.startTest()
  } catch (e) {
    error.value = toUserMessage(e)
  } finally {
    loading.value = false
  }
}

async function submit(): Promise<void> {
  if (test.value === null) return
  loading.value = true
  error.value = null
  try {
    result.value = await adapter.submitTest(
      answerable.value.map((q) => ({
        index: q.index,
        choice: q.mode === 'choice' ? (choices[q.index] ?? null) : null,
        text: q.mode === 'text' ? (texts[q.index] ?? null) : null,
      })),
    )
  } catch (e) {
    error.value = toUserMessage(e)
  } finally {
    loading.value = false
  }
}

function questionResult(index: number): MiniTestResult['questions'][number] | undefined {
  return result.value?.questions.find((q) => q.index === index)
}

function optionState(index: number, option: number): 'idle' | 'right' | 'wrong' | 'selected' {
  const r = questionResult(index)
  if (r === undefined) return choices[index] === option ? 'selected' : 'idle'
  if (r.correctChoice === option) return 'right'
  return choices[index] === option ? 'wrong' : 'idle'
}

const KON: Record<string, string> = {
  tinglash: 'Tinglash',
  oqish: 'O‘qish',
  gapirish: 'Gapirish',
  yozish: 'Yozish',
  lugat: 'Lug‘at',
}
</script>

<template>
  <div class="mt-3">
    <p class="text-[15px] leading-relaxed text-slate-300">
      Tinglash savollarida avval audioni eshiting. Javoblaringiz oxirida birga tekshiriladi;
      birinchi topshirish natijasi hisobga olinadi.
    </p>

    <p
      v-if="props.previous && !result"
      class="mt-3 rounded-xl bg-green-500/10 px-3.5 py-2.5 text-sm font-semibold text-green-400"
    >
      Birinchi natijangiz: {{ props.previous.score }}/{{ props.previous.max }}
    </p>

    <button
      v-if="test === null"
      type="button"
      class="mt-4 min-h-11 rounded-xl bg-brand-500 px-5 text-sm font-semibold text-on-brand disabled:opacity-60"
      :disabled="loading"
      @click="start"
    >
      {{ props.previous ? 'Testni qayta ishlash' : `«${title ?? 'Mini-test'}» ni boshlash` }}
    </button>

    <ol
      v-else
      class="mt-4 space-y-5"
    >
      <li
        v-for="(q, n) in test.savollar"
        :key="q.index"
      >
        <p class="text-xs font-bold uppercase tracking-wider text-slate-400">
          {{ n + 1 }} · {{ KON[q.kon ?? ''] ?? q.kon }}
        </p>
        <div class="mt-1 flex items-center gap-2 text-[15px] text-slate-200">
          <PlayButton
            :handle="q.audio"
            :label="`${n + 1}-savol`"
          />
          <MixedText :text="q.uz" />
        </div>

        <div
          v-if="q.mode === 'choice'"
          class="mt-2 flex flex-wrap gap-2"
        >
          <OptionButton
            v-for="(v, vi) in q.variantlar ?? []"
            :key="vi"
            :text="v"
            :state="optionState(q.index, vi)"
            :disabled="result !== null"
            @pick="choices[q.index] = vi"
          />
        </div>

        <div
          v-else-if="q.mode === 'text'"
          class="mt-2"
        >
          <input
            v-model="texts[q.index]"
            lang="ar"
            dir="rtl"
            class="min-h-11 w-full rounded-xl border border-line-strong bg-ink-900 px-3 font-arabic text-xl text-slate-100"
            :aria-label="`${n + 1}-savolga javob (arab yozuvida)`"
            :disabled="result !== null"
          >
          <p
            v-if="questionResult(q.index)"
            class="mt-1 text-[13px]"
            :class="questionResult(q.index)?.correct ? 'text-green-400' : 'text-rose-400'"
          >
            {{ questionResult(q.index)?.correct ? '✓ To‘g‘ri' : '✗ To‘g‘risi:' }}
            <ArabicText
              v-if="!questionResult(q.index)?.correct"
              :text="questionResult(q.index)?.javob"
              size="sm"
            />
          </p>
        </div>

        <p
          v-else
          class="mt-2 rounded-xl bg-amber-500/10 px-3.5 py-2.5 text-[13px] text-slate-300"
        >
          Ovozli savol — buni ustoz tekshiradi (ovozni yuborish mashq daftari bilan birga keyingi bosqichda ochiladi).
        </p>
      </li>
    </ol>

    <p
      v-if="error"
      class="mt-3 text-sm text-rose-400"
      role="alert"
      v-text="error"
    />

    <div
      v-if="test !== null && result === null"
      class="mt-5 flex flex-wrap items-center gap-3"
    >
      <button
        type="button"
        class="min-h-11 rounded-xl bg-brand-500 px-5 text-sm font-semibold text-on-brand disabled:opacity-60"
        :disabled="loading || answered === 0"
        @click="submit"
      >
        Topshirish
      </button>
      <span class="text-xs text-slate-400">Javob berildi: {{ answered }} / {{ answerable.length }}</span>
    </div>

    <template v-if="result">
      <p class="mt-5 text-lg font-bold text-slate-100">
        Natija: {{ result.score }} / {{ result.max }}
        <span
          v-if="result.oralCount > 0"
          class="text-sm font-semibold text-slate-400"
        > + ovozli qism ustozda</span>
      </p>
      <p
        v-if="!result.counted"
        class="text-xs text-slate-400"
      >
        Bu mashq urinishi — ballga birinchi natija hisoblangan.
      </p>
      <NuriFeedback :feedback="feedbackMood(result.score, result.max)" />
      <button
        type="button"
        class="mt-2 min-h-10 rounded-lg border border-line bg-ink-900 px-3 text-[13px] font-semibold text-slate-300 hover:bg-ink-800"
        @click="start"
      >
        Qayta ishlash
      </button>
    </template>
  </div>
</template>
