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
    class="h-[176px] animate-pulse rounded-xl border border-line bg-ink-900"
  />

  <!--
    ★ TENG IKKI YARIM (loyiha egasi, 2026-10-31): chap yarmi — katta Nuri,
      o'ng yarmi — gap va tugma. Nuri sahifaning BIRINCHI kadrida, katta
      bo'lib turishi kerak: u bosh sahifaning "yuzi".
  -->
  <section
    v-else
    class="grid grid-cols-2 items-center gap-3 overflow-hidden rounded-xl border border-line bg-ink-900 p-3 lg:gap-4 lg:p-4"
  >
    <div
      class="flex h-full min-h-[150px] items-center justify-center rounded-2xl transition-colors"
      :class="TINT[result.mood]"
    >
      <!-- viewBox nisbati 160×180 — o'ram ham shu nisbatda. -->
      <div class="h-[135px] w-[120px] lg:h-[160px] lg:w-[142px]">
        <MascotAvatar :mood="result.mood" />
      </div>
    </div>

    <div class="min-w-0">
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
        class="mt-3 flex h-9 w-full select-none items-center justify-center gap-1.5 rounded-xl bg-brand-500 px-2 text-xs font-semibold text-on-brand shadow-xs transition-colors hover:bg-brand-600 active:bg-brand-700"
      >
        <AppIcon
          :name="action.icon"
          :size="15"
        />
        <span class="truncate">{{ SHORT_LABELS[action.label] ?? action.label }}</span>
      </RouterLink>
    </div>
  </section>
</template>
