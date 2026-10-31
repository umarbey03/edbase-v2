<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import type { LiveSessionDto } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

/**
 * ══════════════════════════════════════════════════════════════════════════
 * «SHU HAFTA» QATORI (loyiha egasi, 2026-10-31)
 * ══════════════════════════════════════════════════════════════════════════
 *
 * Pastki menyudan «Kalendar» olib tashlandi (o'rniga «Vazifalar»): Milliy
 * jadvali qat'iy, o'quvchi oy setkasiga deyarli qaramaydi. Kerakli narsa —
 * "shu hafta qaysi kunlari dars bor" — bosh sahifada shu ixcham qatorda.
 * To'liq kalendar o'chmadi: qator bosilsa (va «Guruhim» → «Dars jadvali»)
 * ochiladi.
 *
 * ★ HAFTA DUSHANBADAN: o'zbek kundalik hisobi shunday (eski oy setkasi
 *   yakshanbadan — u o'z joyida tegilmadi).
 * ★ Nuqta rangi: asosiy ustoz darsi — brend, support teacher darsi — yashil.
 */
const props = defineProps<{ sessions: LiveSessionDto[], now: Date }>()
const router = useRouter()

const LABELS = ['Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh', 'Ya']

function dayKey(date: Date): string {
  return `${date.getFullYear()}-${date.getMonth()}-${date.getDate()}`
}

const days = computed(() => {
  const today = new Date(props.now)
  today.setHours(0, 0, 0, 0)
  const monday = new Date(today)
  monday.setDate(today.getDate() - ((today.getDay() + 6) % 7))

  const byDay = new Map<string, { teacher: boolean, assistant: boolean }>()
  for (const session of props.sessions) {
    const key = dayKey(new Date(session.scheduledStart))
    const entry = byDay.get(key) ?? { teacher: false, assistant: false }
    if (session.type === 'Teacher') entry.teacher = true
    else entry.assistant = true
    byDay.set(key, entry)
  }

  return LABELS.map((label, index) => {
    const date = new Date(monday)
    date.setDate(monday.getDate() + index)
    const marks = byDay.get(dayKey(date)) ?? { teacher: false, assistant: false }
    return {
      label,
      day: date.getDate(),
      isToday: date.getTime() === today.getTime(),
      isPast: date < today,
      ...marks,
    }
  })
})

const lessonCount = computed(() => days.value.filter((d) => d.teacher || d.assistant).length)
</script>

<template>
  <button
    type="button"
    class="w-full rounded-[15px] border border-line bg-ink-900 p-3.5 text-left transition-colors hover:border-line-strong"
    :aria-label="`Shu hafta ${lessonCount} kun dars. To‘liq jadvalni ochish`"
    @click="router.push({ name: 'student-calendar' })"
  >
    <span class="flex items-center justify-between gap-2">
      <span class="text-xs font-bold uppercase tracking-[1.2px] text-slate-400">Shu hafta</span>
      <span class="flex items-center gap-1 text-xs font-semibold text-brand-400">
        Jadval
        <AppIcon
          name="chevron-right"
          :size="14"
        />
      </span>
    </span>
    <span class="mt-2.5 grid grid-cols-7 gap-1">
      <span
        v-for="d in days"
        :key="d.label"
        class="flex flex-col items-center gap-1 rounded-xl py-1.5"
        :class="d.isToday ? 'bg-brand-500 text-on-brand' : d.isPast ? 'text-slate-500' : 'text-slate-200'"
      >
        <span class="text-[10.5px] font-semibold opacity-80">{{ d.label }}</span>
        <span class="text-sm font-bold tabular-nums">{{ d.day }}</span>
        <span class="flex h-1.5 gap-0.5">
          <span
            v-if="d.teacher"
            class="size-1.5 rounded-full"
            :class="d.isToday ? 'bg-on-brand' : 'bg-brand-500'"
          />
          <span
            v-if="d.assistant"
            class="size-1.5 rounded-full"
            :class="d.isToday ? 'bg-on-brand/70' : 'bg-emerald-400'"
          />
        </span>
      </span>
    </span>
  </button>
</template>
