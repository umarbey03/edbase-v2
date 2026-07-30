<script setup lang="ts">
import { computed } from 'vue'

import { showToast } from '@/features/student-toast/model/useToast'
import type { CourseLessonDto } from '@/shared/types'
import { AppIcon } from '@/shared/ui'

import { lockMessage } from '../model/useStudentCourse'

/**
 * Modul darslari — "ilon izi" yo'lakcha (eski `coursePath()` / `.c-path`).
 *
 * Nega aynan shu ko'rinish: bugungi o'quvchi kursni AYNAN shunday ko'radi —
 * dumaloq tugmalar zigzag bo'lib pastga tushadi va orasida punktir ulagich
 * bo'ladi. Ro'yxat ko'rinishiga o'tkazish "boshqa ilova" taassurotini berardi.
 */
const props = defineProps<{
  lessons: CourseLessonDto[]
  /** Hozirgi qadam — oltin, pulsatsiyalanadigan tugma. */
  currentLessonId: number | null
}>()

const emit = defineEmits<{ open: [lesson: CourseLessonDto] }>()

/** Eski `PATH_OFF` — tugmalarning gorizontal siljishi (piksel). */
const PATH_OFFSETS = [0, 26, 38, 26, 0, -26, -38, -26]

type NodeState = 'now' | 'open' | 'lock'

const nodes = computed(() =>
  props.lessons.map((lesson, index) => {
    const state: NodeState = !lesson.unlocked
      ? 'lock'
      : lesson.id === props.currentLessonId
        ? 'now'
        : 'open'
    return {
      lesson,
      state,
      offset: PATH_OFFSETS[index % PATH_OFFSETS.length] ?? 0,
      isLast: index === props.lessons.length - 1,
    }
  }),
)

/*
  Tugma uslublari eski CSS'dan aynan: 66px doira, ostida 5px "qalinlik"
  soyasi (bosilganda 2px ga tushadi — o'yin tugmasi effekti).
*/
const NODE_STYLE: Record<NodeState, Record<string, string>> = {
  now: {
    background: 'linear-gradient(180deg, #f7c948, #e8a412)',
    color: '#3a2600',
    boxShadow: '0 5px 0 #a9760a',
  },
  open: {
    background: 'var(--color-ink-800)',
    color: 'var(--color-brand-500)',
    border: '2px solid var(--color-brand-500)',
    boxShadow: '0 5px 0 rgb(245 183 49 / 0.28)',
  },
  lock: {
    background: 'var(--color-ink-800)',
    color: 'var(--color-dim)',
    boxShadow: '0 5px 0 rgb(0 0 0 / 0.28)',
  },
}

function handleClick(lesson: CourseLessonDto): void {
  // Qulflangan darsda SABAB aytiladi — "bosdim, hech nima bo'lmadi" holati
  // eng ko'p savol tug'diradigan joy edi.
  if (!lesson.unlocked) {
    showToast(lockMessage(lesson.lockReason))
    return
  }
  emit('open', lesson)
}
</script>

<template>
  <div class="relative px-0 pb-[18px] pt-3.5">
    <template
      v-for="node in nodes"
      :key="node.lesson.id"
    >
      <div
        class="relative z-[1] mx-auto flex w-24 flex-col items-center py-[7px]"
        :style="{ transform: `translateX(${node.offset}px)` }"
      >
        <button
          type="button"
          class="flex size-[66px] items-center justify-center rounded-full transition-transform active:translate-y-[3px]"
          :class="[
            node.state === 'now' ? 'animate-node-pulse' : '',
            node.state === 'lock' ? 'cursor-not-allowed' : '',
          ]"
          :style="NODE_STYLE[node.state]"
          :aria-label="node.lesson.name ?? 'Dars'"
          @click="handleClick(node.lesson)"
        >
          <AppIcon
            :name="node.state === 'lock' ? 'lock' : 'play'"
            :size="28"
          />
        </button>

        <span
          class="mt-[7px] line-clamp-2 max-w-[104px] text-center text-[11.5px] font-bold leading-tight"
          :class="node.state === 'now' ? 'text-brand-500' : 'text-slate-400'"
          v-text="node.lesson.name"
        />
        <span
          v-if="node.state === 'now'"
          class="mt-[5px] text-[10px] font-extrabold uppercase tracking-[0.4px] text-brand-500"
        >
          Boshlash
        </span>
      </div>

      <!--
        Ulagich (eski `.c-seg`) — DOIM punktir.

        Eski ilovada u dars TUGATILGANDA yashil uzluksiz chiziqqa aylanardi
        (`.c-seg.done`). v2 da server darsning tugatilganini bermaydi, shuning
        uchun "uzluksiz" holat hech qachon chiqmaydi: uni "ochilgan" bilan
        almashtirish o'quvchiga tugatmagan darsini tugatilgandek ko'rsatardi.
      -->
      <div
        v-if="!node.isLast"
        class="flex h-[26px] items-center justify-center"
        aria-hidden="true"
      >
        <span
          class="h-full w-1 rounded-sm"
          style="
            background: repeating-linear-gradient(
              180deg,
              var(--color-line) 0 6px,
              transparent 6px 12px
            );
          "
        />
      </div>
    </template>
  </div>
</template>
