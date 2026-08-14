<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { askAboutLesson } from '@/entities/direct-message'
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

/**
 * ============================================================================
 * R40 · «BU DARS BO'YICHA SAVOL BERISH»
 * ============================================================================
 *
 * Loyiha egasi: *"savollar qismida darslarda video darslardan kelgan savollar
 * bo'ladi"*. Server buni ALLAQACHON qo'llab-quvvatlaydi
 * (`DirectMessage.moduleLessonId`) va nishon ikkala chat ekranida chizilgan —
 * faqat uni to'ldiradigan tugma yo'q edi, shuning uchun prod'da har bir
 * nishon `null` bo'lib turardi. Mana o'sha tugma.
 *
 * ★ SAVOL SHU YERDA YOZILMAYDI, chatga OLIB BORILADI: yozishmaning o'zi
 *   (tarix, o'qildi belgisi, emoji, kun ajratgichlari) allaqachon bitta
 *   ekranda. Bu yerga ikkinchi yozish maydoni qo'yilsa u shu narsalarning
 *   hammasini qaytadan talab qilardi — ya'ni ikkinchi chat.
 *
 * ★ QULFLANGAN DARSDA TUGMA YO'Q (`unlocked` sharti shablonda): server ham
 *   buni rad etadi (`EnsureLessonUnlockedAsync` — "ketma-ketlik bo'yicha"
 *   talabi). Tugmani ko'rsatib, keyin 403 berish eng yomon variant bo'lardi.
 *   Bu yerdagi shart — QULAYLIK, himoya SERVERDA.
 */
function askQuestion(): void {
  const lesson = props.lesson
  if (lesson === null) return

  askAboutLesson(lesson.id, lesson.name)
  go('student-chat')
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

        <!--
          R40 — dars bo'yicha savol. Tugma DOIM oxirgi va DOIM ikkilamchi
          ko'rinishda: asosiy amal — vazifa/test topshirish, savol esa
          o'quvchi TIQILIB QOLGANDA bosadigan yordam yo'li.
        -->
        <BaseButton
          v-if="props.lesson.unlocked"
          variant="ghost"
          size="lg"
          block
          @click="askQuestion"
        >
          <template #icon>
            <AppIcon
              name="chat"
              :size="16"
            />
          </template>
          Bu dars bo‘yicha savol berish
        </BaseButton>
      </div>
    </div>
  </BaseModal>
</template>
