<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import type { CourseLessonDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseModal } from '@/shared/ui'

/**
 * Dars varag'i — ochiq darsni bosganda pastdan chiqadi.
 *
 * ★ ESKI ILOVADAN FARQ (ataylab): u yerda dars to'liq ekran ochilib, ichida
 *   VIDEO, vazifa formasi, test va kuratorga savol paneli bor edi. v2 da bu
 *   ekranni chizishning imkoni yo'q:
 *     • video — `ModuleLesson` da video maydoni umuman yo'q, server har
 *       darsda `hasVideo = false` qaytaradi;
 *     • vazifa/test — kurs daraxti faqat `hasAssignment`/`hasTest`
 *       bayroqlarini beradi, ularning ID'sini bermaydi.
 *   Shu sababli varaq bor narsani ko'rsatadi (nom, modul, davomiylik, tavsif)
 *   va o'quvchini vazifa/test ro'yxatiga yo'naltiradi. Bo'sh pleyer yoki
 *   ishlamaydigan tugma chizilmaydi.
 */
const props = defineProps<{
  lesson: CourseLessonDto | null
  moduleName: string
}>()

const emit = defineEmits<{ close: [] }>()

const router = useRouter()

const description = computed(() => props.lesson?.description ?? '')

function go(routeName: string): void {
  emit('close')
  void router.push({ name: routeName })
}
</script>

<template>
  <BaseModal
    :open="props.lesson !== null"
    title=""
    sheet
    @close="emit('close')"
  >
    <div v-if="props.lesson !== null">
      <div class="flex items-start gap-3">
        <div class="min-w-0 flex-1">
          <p
            class="text-[10.5px] font-bold uppercase tracking-[0.5px] text-slate-400"
            v-text="props.moduleName"
          />
          <h2
            class="mt-1 text-[17px] font-extrabold leading-tight"
            v-text="props.lesson.name"
          />
        </div>
        <button
          type="button"
          class="tap-target -mr-2 flex shrink-0 items-center justify-center rounded-lg text-slate-400 transition-colors hover:text-slate-100"
          aria-label="Yopish"
          @click="emit('close')"
        >
          <AppIcon
            name="close"
            :size="18"
          />
        </button>
      </div>

      <p
        v-if="props.lesson.durationMin !== null"
        class="mt-2.5 inline-flex items-center gap-1.5 text-xs text-slate-400"
      >
        <AppIcon
          name="clock"
          :size="13"
        />
        {{ props.lesson.durationMin }} daq
      </p>

      <p
        v-if="description.length > 0"
        class="mt-3 whitespace-pre-wrap rounded-xl border border-line bg-ink-800 p-3.5 text-sm leading-relaxed text-slate-300"
        v-text="description"
      />

      <div class="mt-4 flex flex-col gap-2">
        <BaseButton
          v-if="props.lesson.hasAssignment"
          size="lg"
          block
          @click="go('student-assignments')"
        >
          <template #icon>
            <AppIcon
              name="clipboard"
              :size="16"
            />
          </template>
          Vazifani ochish
        </BaseButton>

        <BaseButton
          v-if="props.lesson.hasTest"
          :variant="props.lesson.hasAssignment ? 'secondary' : 'primary'"
          size="lg"
          block
          @click="go('student-tests')"
        >
          <template #icon>
            <AppIcon
              name="award"
              :size="16"
            />
          </template>
          Testni ochish
        </BaseButton>

        <p
          v-if="!props.lesson.hasAssignment && !props.lesson.hasTest"
          class="rounded-xl border border-line bg-ink-800 px-4 py-3 text-center text-[13px] text-slate-400"
        >
          Bu darsga kontent hali qo‘shilmagan
        </p>
      </div>
    </div>
  </BaseModal>
</template>
