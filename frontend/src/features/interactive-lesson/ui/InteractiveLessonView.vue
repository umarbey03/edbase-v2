<script setup lang="ts">
import { computed, nextTick, provide, ref, watch } from 'vue'

import type { FirstScore, StudentUnitDto, UnitBlock, UnitProgress } from '@/entities/interactive-unit'
import { toUserMessage } from '@/shared/api'
import { AppIcon } from '@/shared/ui'

import { LESSON_CONTEXT } from '../model/context'
import type { LessonAdapter, LessonContext } from '../model/context'
import { unitDoneFeedback } from '../model/feedbackMood'
import { useAudioPlayer } from '../model/useAudioPlayer'
import BlockRenderer from './blocks/BlockRenderer.vue'
import ArabicText from './parts/ArabicText.vue'
import NuriFeedback from './parts/NuriFeedback.vue'
import MiniTest from './test/MiniTest.vue'
import Workbook from './workbook/Workbook.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  INTERAKTIV DARSLIK KO'RINISHI (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Bitta vaqtda BITTA blok (prototip va o'quvchi "O'quv" ekrani uslubi):
 * tepada sarlavha va progress, telefonda gorizontal qadamlar, desktopda
 * chap rail; pastda "← Oldingi" / "Bajarildi, keyingisi →".
 *
 * ★ Oxirida ikki qo'shimcha qadam: mini-test va mashq daftari.
 * ★ Joriy qadam — `step` (sahifa uni `?b=` ga yozadi: havola berish va
 *   Telegram "orqaga" uchun).
 * ★ Server bilan aloqa — `adapter` orqali (sahifa API adapterini beradi).
 */
const props = defineProps<{
  unit: StudentUnitDto
  adapter: LessonAdapter
  step: string | null
}>()

const emit = defineEmits<{ 'update:step': [string] }>()

// ------------------------------------------------------------------ kontekst

const progress = ref<UnitProgress>(props.unit.progress)
const firstScores = ref<Record<string, FirstScore>>({ ...props.unit.progress.items })
const audio = useAudioPlayer((handle) => props.adapter.audioUrl(handle))

const context: LessonContext = {
  adapter: props.adapter,
  audio,
  firstScores,
  setFirst(itemKey, score) {
    firstScores.value = { ...firstScores.value, [itemKey]: score }
  },
  personas: new Map(props.unit.personajlar.map((p) => [p.id ?? '', p])),
  words: new Map(props.unit.lugat.map((w) => [w.id ?? '', w])),
}
provide(LESSON_CONTEXT, context)

// ------------------------------------------------------------------ qadamlar

interface Step {
  id: string
  kind: string
  title: string
  titleAr: string | null
  minutes: number | null
  block: UnitBlock | null
}

const KIND: Record<string, string> = {
  isinish: 'Isinish',
  grammatika: 'Qoida',
  harf: 'Harf',
  tovush: 'Tovushlar',
  yozish: 'Yozish',
  yakun: 'Yakun',
  vazifa: 'Muloqot vazifasi',
  tabir: 'Iboralar',
  dialog: 'Suhbat',
  oqish: 'O‘qish',
}

const steps = computed<Step[]>(() => {
  const list: Step[] = props.unit.darslar.flatMap((part) =>
    part.bloklar.map((b) => ({
      id: b.id,
      kind: KIND[b.tur] ?? b.tur,
      title: b.sarlavhaUz ?? b.id,
      titleAr: b.sarlavhaAr,
      minutes: b.vaqt,
      block: b,
    })),
  )
  if (props.unit.test !== null) {
    list.push({ id: 'TEST', kind: 'Mini-test', title: props.unit.test.nom ?? 'Mini-test', titleAr: null, minutes: 10, block: null })
  }
  if (props.unit.daftar.length > 0) {
    list.push({ id: 'DAFTAR', kind: 'Uy ishi', title: 'Mashq daftari', titleAr: null, minutes: null, block: null })
  }
  return list
})

const blockCount = computed(() => steps.value.filter((s) => s.block !== null).length)
const done = computed(() => new Set(progress.value.blocksDone))
const index = computed(() => Math.max(0, steps.value.findIndex((s) => s.id === props.step)))
const current = computed(() => steps.value[index.value] ?? null)
const percent = computed(() => (blockCount.value === 0 ? 0 : Math.round((done.value.size / blockCount.value) * 100)))
const totalMinutes = computed(() => steps.value.reduce((sum, s) => sum + (s.block !== null ? (s.minutes ?? 0) : 0), 0))

const heading = ref<HTMLElement | null>(null)

function go(i: number): void {
  const target = steps.value[i]
  if (target === undefined) return
  audio.stop()
  emit('update:step', target.id)
}

// Blok almashganda fokus sarlavhaga (skrinrider va klaviatura uchun) va sahifa tepaga.
watch(
  () => props.step,
  async () => {
    await nextTick()
    heading.value?.focus({ preventScroll: true })
    heading.value?.scrollIntoView({ block: 'start', behavior: 'smooth' })
    document.getElementById(`step-${current.value?.id ?? ''}`)?.scrollIntoView({ inline: 'center', block: 'nearest' })
  },
)

const saving = ref(false)
const navError = ref<string | null>(null)

async function next(): Promise<void> {
  const step = current.value
  if (step === null || saving.value) return
  navError.value = null

  if (step.block !== null && !done.value.has(step.id)) {
    saving.value = true
    try {
      progress.value = await props.adapter.blockDone(step.id)
    } catch (e) {
      navError.value = toUserMessage(e)
      return
    } finally {
      saving.value = false
    }
  }
  if (index.value < steps.value.length - 1) go(index.value + 1)
}

const isLast = computed(() => index.value >= steps.value.length - 1)
const finished = computed(() => progress.value.completedAt !== null)
</script>

<template>
  <div class="pb-6">
    <!-- -------------------------------------------------------- sarlavha -->
    <header class="rounded-xl border border-line bg-ink-900 px-4 py-3.5">
      <p class="text-xs font-semibold text-slate-400">
        {{ unit.meta.daraja }} · {{ unit.meta.birlik }}-{{ unit.meta.birlikNomi ?? 'dars' }}
      </p>
      <h1 class="mt-0.5 text-lg font-bold leading-snug text-slate-50">
        {{ unit.meta.nomUz }}
      </h1>
      <ArabicText
        v-if="unit.meta.nomAr"
        :text="unit.meta.nomAr"
        size="md"
        class="block text-right text-brand-500"
      />
      <p
        v-if="unit.meta.shior"
        class="mt-1 text-[13px] leading-relaxed text-slate-400"
        v-text="unit.meta.shior"
      />

      <div class="mt-3 flex items-center gap-3">
        <div
          class="h-2 flex-1 overflow-hidden rounded-full bg-ink-800"
          role="progressbar"
          :aria-valuenow="percent"
          aria-valuemin="0"
          aria-valuemax="100"
          aria-label="Bloklar bajarilishi"
        >
          <span
            class="block h-full rounded-full bg-green-500 transition-[width] duration-300"
            :style="{ width: `${percent}%` }"
          />
        </div>
        <span class="shrink-0 text-xs font-semibold tabular-nums text-slate-400">
          {{ done.size }}/{{ blockCount }} · {{ totalMinutes }} daq
        </span>
      </div>

      <div class="mt-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          class="inline-flex min-h-9 items-center gap-1.5 rounded-lg border px-3 text-[13px] font-semibold transition-colors"
          :class="audio.slow.value ? 'border-brand-500 bg-brand-500/10 text-brand-500' : 'border-line bg-ink-900 text-slate-300'"
          :aria-pressed="audio.slow.value"
          @click="audio.slow.value = !audio.slow.value"
        >
          <AppIcon
            name="clock"
            :size="13"
          />
          Sekin tinglash
        </button>
        <span
          v-if="unit.progress.exerciseTotal > 0 || Object.keys(firstScores).length > 0"
          class="text-xs text-slate-400"
        >
          Mashqlar: {{ Object.values(firstScores).reduce((s, f) => s + f.right, 0) }}/{{ Object.values(firstScores).reduce((s, f) => s + f.total, 0) }}
        </span>
      </div>
    </header>

    <div class="mt-3 lg:grid lg:grid-cols-[250px_minmax(0,1fr)] lg:gap-5">
      <!-- ------------------------------------------------ qadamlar -->
      <nav
        aria-label="Dars bloklari"
        class="lg:sticky lg:top-4 lg:self-start"
      >
        <ol class="-mx-4 flex snap-x gap-2 overflow-x-auto px-4 pb-1 lg:mx-0 lg:flex-col lg:gap-1 lg:overflow-visible lg:rounded-xl lg:border lg:border-line lg:bg-ink-900 lg:p-2">
          <li
            v-for="(s, i) in steps"
            :id="`step-${s.id}`"
            :key="s.id"
            class="snap-center"
          >
            <button
              type="button"
              class="flex min-h-11 w-full items-center gap-2 whitespace-nowrap rounded-xl border px-3 text-left text-[13px] transition-colors lg:whitespace-normal lg:border-transparent"
              :class="i === index
                ? 'border-brand-500 bg-brand-500/10 text-slate-50'
                : 'border-line bg-ink-900 text-slate-300 hover:bg-ink-800'"
              :aria-current="i === index ? 'step' : undefined"
              @click="go(i)"
            >
              <span
                class="flex size-6 shrink-0 items-center justify-center rounded-full text-[11px] font-bold"
                :class="done.has(s.id) ? 'bg-green-500 text-white' : 'border border-line-strong text-slate-400'"
              >
                <AppIcon
                  v-if="done.has(s.id)"
                  name="check"
                  :size="12"
                />
                <template v-else>{{ i + 1 }}</template>
              </span>
              <span class="min-w-0">
                <span class="block text-[11px] font-semibold text-slate-400">{{ s.kind }}</span>
                <span class="hidden font-semibold lg:block">{{ s.title.replace(/^[^:]+:\s*/, '') }}</span>
              </span>
            </button>
          </li>
        </ol>
      </nav>

      <!-- ------------------------------------------------ joriy blok -->
      <article
        v-if="current"
        class="mt-3 min-w-0 rounded-xl border border-line bg-ink-900 px-4 py-4 sm:px-5 lg:mt-0"
      >
        <div class="flex items-start justify-between gap-3">
          <div class="min-w-0">
            <p class="text-[11px] font-bold uppercase tracking-wider text-brand-500">
              {{ current.kind }}
            </p>
            <h2
              ref="heading"
              tabindex="-1"
              class="mt-0.5 text-[19px] font-bold leading-snug text-slate-50 outline-none"
              v-text="current.title"
            />
            <ArabicText
              v-if="current.titleAr"
              :text="current.titleAr"
              size="md"
              class="text-slate-400"
            />
          </div>
          <span
            v-if="current.minutes"
            class="shrink-0 rounded-full bg-ink-800 px-2.5 py-1 text-xs font-semibold text-slate-400"
          >{{ current.minutes }} daq</span>
        </div>

        <BlockRenderer
          v-if="current.block"
          :key="current.id"
          :block="current.block"
          :can-do="unit.meta.canDo"
        />
        <MiniTest
          v-else-if="current.id === 'TEST'"
          :title="unit.test?.nom ?? null"
          :previous="progress.test"
        />
        <Workbook
          v-else-if="current.id === 'DAFTAR'"
          :items="unit.daftar"
        />

        <NuriFeedback
          v-if="finished && current.block?.tur === 'yakun'"
          class="mt-4"
          :feedback="unitDoneFeedback(unit.meta.birlik)"
        />

        <!-- ---------------------------------------------- navigatsiya -->
        <p
          v-if="navError"
          class="mt-4 text-sm text-rose-400"
          role="alert"
          v-text="navError"
        />
        <div class="mt-6 flex items-center justify-between gap-2 border-t border-line pt-4">
          <button
            type="button"
            class="inline-flex min-h-11 items-center gap-1.5 rounded-xl border border-line bg-ink-900 px-3.5 text-sm font-semibold text-slate-300 hover:bg-ink-800 disabled:opacity-40"
            :disabled="index === 0"
            @click="go(index - 1)"
          >
            <AppIcon
              name="arrow-left"
              :size="15"
            />
            Oldingi
          </button>
          <button
            v-if="!isLast || (current.block && !done.has(current.id))"
            type="button"
            class="inline-flex min-h-11 items-center gap-1.5 rounded-xl bg-brand-500 px-4 text-sm font-semibold text-on-brand disabled:opacity-60"
            :disabled="saving"
            @click="next"
          >
            {{ current.block ? (isLast ? 'Bajarildi' : 'Bajarildi, keyingisi') : 'Keyingisi' }}
            <AppIcon
              name="chevron-right"
              :size="15"
            />
          </button>
        </div>
      </article>
    </div>
  </div>
</template>
