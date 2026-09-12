<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'

import {
  PAYROLL_GUIDE_SECTIONS,
  PAYROLL_KIND_GUIDE,
  payrollRuleKindLabel,
} from '@/entities/payroll'
import type { PayrollRuleKindName } from '@/shared/types'
import { BaseBadge, BaseDrawer } from '@/shared/ui'

/**
 * «QANDAY HISOBLANADI?» — oylik turlari qo'llanmasi.
 *
 * ★ NEGA DRAWER: yetti tur + oltita umumiy bo'lim — bu o'qiladigan matn,
 * markazdagi modalga sig'maydi. Drawer'da admin qoidalar ro'yxatini
 * fonda ko'rib turadi va qo'llanma bilan solishtiradi.
 *
 * ★ `focusKind` — ochilganda kerakli turga o'tadi (masalan ro'yxatdagi
 * tur sarlavhasidan bosilganda). Berilmasa boshidan ochiladi.
 *
 * ★ MATN BU YERDA EMAS: hammasi `entities/payroll/model/kind-guide.ts`
 * da — forma bilan bitta manba.
 */
const props = withDefaults(
  defineProps<{ open: boolean; focusKind?: PayrollRuleKindName | null }>(),
  { focusKind: null },
)
const emit = defineEmits<{ close: [] }>()

const sessionKinds = computed(() => PAYROLL_KIND_GUIDE.filter((e) => e.scope === 'session'))
const periodKinds = computed(() => PAYROLL_KIND_GUIDE.filter((e) => e.scope === 'period'))

const root = ref<HTMLElement | null>(null)

function anchorId(kind: PayrollRuleKindName): string {
  return `payroll-guide-${kind}`
}

watch(
  () => [props.open, props.focusKind] as const,
  async ([open, kind]) => {
    if (!open || kind === null) return
    await nextTick()
    root.value
      ?.querySelector(`#${anchorId(kind)}`)
      ?.scrollIntoView({ block: 'start', behavior: 'smooth' })
  },
)
</script>

<template>
  <BaseDrawer
    :open="props.open"
    title="Oylik qanday hisoblanadi"
    subtitle="Har tur uchun formula, sonli misol va chekka holatlar. Qoidalar bir-biri bilan qanday birlashishi — pastda."
    @close="emit('close')"
  >
    <div
      ref="root"
      class="space-y-6"
    >
      <!-- ============================================ dars qamrovi -->
      <section>
        <h3 class="mb-1 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
          Har dars uchun
        </h3>
        <p class="mb-3 text-xs text-slate-400">
          Dars yakunlanganda hisoblanadi va muzlatiladi — keyingi tahrir o‘tgan darsga ta’sir
          qilmaydi.
        </p>
        <div class="space-y-3">
          <article
            v-for="entry in sessionKinds"
            :id="anchorId(entry.kind)"
            :key="entry.kind"
            class="scroll-mt-4 rounded-xl border border-line bg-ink-950 p-3.5"
            :class="props.focusKind === entry.kind ? 'ring-1 ring-brand-500/60' : ''"
          >
            <div class="flex flex-wrap items-center gap-2">
              <h4
                class="text-sm font-semibold text-slate-100"
                v-text="payrollRuleKindLabel(entry.kind)"
              />
              <BaseBadge tone="accent">
                Har dars
              </BaseBadge>
            </div>
            <p
              class="mt-1 text-xs text-slate-300"
              v-text="entry.summary"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Formula
            </p>
            <p
              class="mt-0.5 rounded-lg border border-line bg-ink-900 px-2.5 py-1.5 font-mono text-xs text-brand-300"
              v-text="entry.formula"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Misol
            </p>
            <ol class="mt-0.5 space-y-0.5 text-xs text-slate-300">
              <li
                v-for="(line, index) in entry.example"
                :key="index"
                class="tabular-nums"
                :class="index === entry.example.length - 1 ? 'font-semibold text-slate-100' : ''"
                v-text="line"
              />
            </ol>

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Formadagi maydonlar
            </p>
            <p
              class="mt-0.5 text-xs text-slate-400"
              v-text="entry.fields.join(' · ')"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Bilib qo‘ying
            </p>
            <ul class="mt-0.5 list-disc space-y-0.5 pl-4 text-xs text-slate-400">
              <li
                v-for="(note, index) in entry.notes"
                :key="index"
                v-text="note"
              />
            </ul>
          </article>
        </div>
      </section>

      <!-- ============================================ davr qamrovi -->
      <section>
        <h3 class="mb-1 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
          Oyga bir marta
        </h3>
        <p class="mb-3 text-xs text-slate-400">
          Davr oxiridagi holat bo‘yicha jonli hisoblanadi — darslar soniga bog‘liq emas.
        </p>
        <div class="space-y-3">
          <article
            v-for="entry in periodKinds"
            :id="anchorId(entry.kind)"
            :key="entry.kind"
            class="scroll-mt-4 rounded-xl border border-line bg-ink-950 p-3.5"
            :class="props.focusKind === entry.kind ? 'ring-1 ring-brand-500/60' : ''"
          >
            <div class="flex flex-wrap items-center gap-2">
              <h4
                class="text-sm font-semibold text-slate-100"
                v-text="payrollRuleKindLabel(entry.kind)"
              />
              <BaseBadge tone="warning">
                Oyga bir marta
              </BaseBadge>
            </div>
            <p
              class="mt-1 text-xs text-slate-300"
              v-text="entry.summary"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Formula
            </p>
            <p
              class="mt-0.5 rounded-lg border border-line bg-ink-900 px-2.5 py-1.5 font-mono text-xs text-brand-300"
              v-text="entry.formula"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Misol
            </p>
            <ol class="mt-0.5 space-y-0.5 text-xs text-slate-300">
              <li
                v-for="(line, index) in entry.example"
                :key="index"
                class="tabular-nums"
                :class="index === entry.example.length - 1 ? 'font-semibold text-slate-100' : ''"
                v-text="line"
              />
            </ol>

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Formadagi maydonlar
            </p>
            <p
              class="mt-0.5 text-xs text-slate-400"
              v-text="entry.fields.join(' · ')"
            />

            <p class="mt-3 text-[11px] font-semibold uppercase tracking-wide text-slate-500">
              Bilib qo‘ying
            </p>
            <ul class="mt-0.5 list-disc space-y-0.5 pl-4 text-xs text-slate-400">
              <li
                v-for="(note, index) in entry.notes"
                :key="index"
                v-text="note"
              />
            </ul>
          </article>
        </div>
      </section>

      <!-- ============================================ umumiy qoidalar -->
      <section
        v-for="section in PAYROLL_GUIDE_SECTIONS"
        :key="section.title"
      >
        <h3
          class="mb-1.5 text-[11px] font-semibold uppercase tracking-wide text-slate-500"
          v-text="section.title"
        />
        <ul class="list-disc space-y-1 pl-4 text-xs text-slate-300">
          <li
            v-for="(item, index) in section.items"
            :key="index"
            v-text="item"
          />
        </ul>
      </section>
    </div>
  </BaseDrawer>
</template>
