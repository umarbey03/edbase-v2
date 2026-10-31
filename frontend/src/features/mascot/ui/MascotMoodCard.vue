<script setup lang="ts">
import { computed } from 'vue'
import type { RouteLocationRaw } from 'vue-router'

import { AppIcon } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

import type { MoodAction, MoodResult } from '../model/mood'
import type { MascotMood } from '../model/types'
import MascotAvatar from './MascotAvatar.vue'

/**
 * BOSH SAHIFADAGI «NURI» KARTASI (2026-10-29).
 *
 * Chapda maskot (kayfiyati o'quvchi holatidan), o'ngda bitta sarlavha,
 * bitta gap va BITTA tugma. Qaror `decideMood` da — bu komponent faqat
 * chizadi va niyatni (`MoodAction`) marshrutga aylantiradi.
 *
 * ★ BITTA TUGMA: karta "hisobot" emas, keyingi qadam. Bir nechta tugma
 *   bo'lsa o'quvchi tanlashga majbur bo'lardi — maskotning vazifasi esa
 *   aynan tanlovni osonlashtirish.
 *
 * ★ TELEFONDA: maskot 76px, matn ustuni ~220px (360px ekranda) — sarlavha
 *   ikki qatorga o'ralsa ham karta balandligi maskotdan oshmaydi.
 */
const props = defineProps<{
  /** `null` — ma'lumot kutilmoqda (skelet). */
  result: MoodResult | null
  liveSessionId: number | null
  /**
   * Tik, ixcham ko'rinish — bosh sahifada darslik kartasi bilan YONMA-YON
   * (yarim kenglik, 2026-10-31). Maskot tepada, matn pastda, tugma to'liq kenglikda.
   */
  compact?: boolean
}>()

/*
  Kayfiyat foni — juda och, faqat maskot "plashkasi" uchun. Xavotirda ham
  qizil EMAS: qizil rang ayblovdek o'qiladi (pastdagi `mood.ts` izohi).
*/
const TINT: Record<MascotMood, string> = {
  happy: 'bg-brand-500/8',
  celebrate: 'bg-amber-500/12',
  encouraging: 'bg-sky-500/10',
  concerned: 'bg-orange-500/10',
  sleepy: 'bg-slate-500/10',
  thinking: 'bg-cyan-500/10',
}

/** Yarim kenglikdagi kartada sig'adigan qisqa yozuvlar (ma'nosi o'sha). */
const SHORT_LABELS: Record<string, string> = {
  'Darsga qo‘shilish': 'Darsga kirish',
  'Vazifani topshirish': 'Vazifalar',
  'Darsni davom ettirish': 'Darsga o‘tish',
  'Reytingni ko‘rish': 'Reyting',
}

interface ActionView {
  label: string
  icon: IconName
  to: RouteLocationRaw
}

const action = computed<ActionView | null>(() => {
  const kind: MoodAction | null = props.result?.action ?? null
  switch (kind) {
    case 'join-live':
      return props.liveSessionId === null
        ? null
        : {
          label: 'Darsga qo‘shilish',
          icon: 'video',
          to: { name: 'live-room', params: { sessionId: String(props.liveSessionId) } },
        }
    case 'assignments':
      return { label: 'Vazifani topshirish', icon: 'clipboard', to: { name: 'student-assignments' } }
    case 'learn':
      return { label: 'Darsni davom ettirish', icon: 'play', to: { name: 'student-learn' } }
    case 'rating':
      return { label: 'Reytingni ko‘rish', icon: 'trophy', to: { name: 'student-rating' } }
    default:
      return null
  }
})
</script>

<template>
  <div
    v-if="result === null"
    class="h-[118px] animate-pulse rounded-xl border border-line bg-ink-900"
  />

  <section
    v-else-if="props.compact"
    class="flex h-full flex-col rounded-xl border border-line bg-ink-900 p-3"
  >
    <div class="flex items-center gap-2">
      <div
        class="flex size-12 shrink-0 items-center justify-center rounded-xl"
        :class="TINT[result.mood]"
      >
        <div class="h-[44px] w-[39px]">
          <MascotAvatar :mood="result.mood" />
        </div>
      </div>
      <p class="text-[10px] font-bold uppercase tracking-[1.4px] text-slate-400">
        Nuri
      </p>
    </div>
    <h2
      class="mt-2 text-sm font-bold leading-snug text-slate-100"
      v-text="result.title"
    />
    <p
      class="mb-3 mt-1 line-clamp-3 text-xs leading-relaxed text-slate-300"
      v-text="result.message"
    />
    <RouterLink
      v-if="action !== null"
      :to="action.to"
      class="mt-auto flex h-9 select-none items-center justify-center gap-1.5 rounded-xl bg-brand-500 px-2 text-xs font-semibold text-on-brand shadow-xs transition-colors hover:bg-brand-600"
    >
      <AppIcon
        :name="action.icon"
        :size="14"
      />
      <span class="truncate">{{ SHORT_LABELS[action.label] ?? action.label }}</span>
    </RouterLink>
  </section>

  <section
    v-else
    class="flex items-center gap-3.5 rounded-xl border border-line bg-ink-900 p-[14px] xs:gap-4 lg:p-4"
  >
    <div
      class="flex size-[88px] shrink-0 items-center justify-center rounded-2xl transition-colors xs:size-[96px]"
      :class="TINT[result.mood]"
    >
      <!-- viewBox nisbati 160×180 — o'ram ham shu nisbatda. -->
      <div class="h-[80px] w-[71px] xs:h-[88px] xs:w-[78px]">
        <MascotAvatar :mood="result.mood" />
      </div>
    </div>

    <div class="min-w-0 flex-1">
      <p class="text-[10px] font-bold uppercase tracking-[1.4px] text-slate-400">
        Nuri
      </p>
      <h2
        class="mt-0.5 text-[15px] font-bold leading-snug text-slate-100"
        v-text="result.title"
      />
      <p
        class="mt-1 text-[13px] leading-relaxed text-slate-300"
        v-text="result.message"
      />

      <RouterLink
        v-if="action !== null"
        :to="action.to"
        class="mt-2.5 inline-flex h-9 select-none items-center justify-center gap-1.5 rounded-xl bg-brand-500 px-3 text-xs font-semibold text-on-brand shadow-xs transition-colors hover:bg-brand-600 active:bg-brand-700"
      >
        <AppIcon
          :name="action.icon"
          :size="15"
        />
        {{ action.label }}
      </RouterLink>
    </div>
  </section>
</template>
