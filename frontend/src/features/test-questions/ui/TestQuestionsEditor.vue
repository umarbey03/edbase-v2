<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { deleteTestQuestion } from '@/entities/test'
import { toUserMessage } from '@/shared/api'
import type { AuthoringQuestionDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, ConfirmDeleteDialog, EmptyState } from '@/shared/ui'

import QuestionFormDialog from './QuestionFormDialog.vue'

/**
 * Test savollari (o'quv bo'limi/admin ko'rinishi).
 *
 * ★ BU KO'RINISHDA TO'G'RI JAVOBLAR KO'RINADI — u `GET /tests/{id}`
 * (`TestAuthoringDto`) ma'lumotidan quriladi va endpoint faqat xodimga
 * ochiq. O'quvchi varaqasi (`TakeTestDto`) esa bunday maydonni UMUMAN
 * olmaydi, shuning uchun bu yerdagi ko'rinish tasodifan o'quvchiga tushib
 * qolishi mumkin emas.
 *
 * ★ TARTIB O'ZGARTIRISH TUGMALARI YO'Q: serverda savollar uchun `reorder`
 * endpointi mavjud emas (`kurslar`dan farqli). Tartib savol formasidagi
 * `position` orqali beriladi va yangi savol oxiriga qo'shiladi.
 */
const props = defineProps<{
  testId: number
  questions: AuthoringQuestionDto[]
  /**
   * Tuzilma QULFLANGANMI (o'quvchi urinishlari bor).
   *
   * Server: `TestService.EnsureNoAttemptsAsync` — savol qo'shish/o'zgartirish/
   * o'chirish 409 bilan rad etiladi, chunki qo'yilgan ballar ma'nosini
   * yo'qotardi. Bu yerdagi to'siq faqat OLDINDAN OGOHLANTIRISH: server
   * boshlangan (lekin topshirilmagan) urinishlarni ham hisobga oladi, ya'ni
   * 409 baribir kelishi mumkin va ko'rsatiladi.
   */
  locked: boolean
}>()

const emit = defineEmits<{ changed: [] }>()

const totalPoints = computed(() =>
  props.questions.reduce((sum, question) => sum + question.points, 0),
)

/* ------------------------------------------------------------------ forma */

const formOpen = ref(false)
const editing = ref<AuthoringQuestionDto | null>(null)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(question: AuthoringQuestionDto): void {
  editing.value = question
  formOpen.value = true
}

/* -------------------------------------------------------------- o'chirish */

const deleteTarget = ref<AuthoringQuestionDto | null>(null)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: (questionId: number) => deleteTestQuestion(props.testId, questionId),
  onSuccess: () => {
    deleteTarget.value = null
    emit('changed')
  },
  onError: (error: Error) => {
    // Oyna OCHIQ qoladi: 409 sababi ("o'quvchilar yechishni boshlagan")
    // aynan shu yerda o'qilishi kerak.
    deleteError.value = toUserMessage(error)
  },
})

function askDelete(question: AuthoringQuestionDto): void {
  deleteError.value = null
  deleteTarget.value = question
}

function confirmDelete(): void {
  const target = deleteTarget.value
  if (target === null) return
  deleteError.value = null
  deleteMutation.mutate(target.id)
}

/** Boshqa testga o'tilsa ochiq oynalar yopilsin. */
watch(
  () => props.testId,
  () => {
    formOpen.value = false
    deleteTarget.value = null
  },
)
</script>

<template>
  <section>
    <header class="mb-3 flex flex-wrap items-center justify-between gap-2">
      <div class="min-w-0">
        <h2 class="text-sm font-semibold text-slate-200">
          Savollar
        </h2>
        <p class="mt-0.5 text-[11px] tabular-nums text-dim">
          {{ props.questions.length }} ta savol · jami {{ totalPoints }} ball
        </p>
      </div>
      <BaseButton
        v-if="!props.locked"
        size="sm"
        @click="openCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Savol qo‘shish
      </BaseButton>
    </header>

    <p
      v-if="props.locked"
      class="mb-3 flex items-start gap-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs leading-relaxed text-amber-200"
    >
      <AppIcon
        name="lock"
        :size="14"
        class="mt-px"
      />
      <span>
        O‘quvchilar bu testni yechishni boshlagan — savollar QULFLANGAN.
        O‘zgartirish qo‘yilgan ballar ma’nosini yo‘qotardi. Boshqa savollar
        kerak bo‘lsa yangi test yarating.
      </span>
    </p>

    <EmptyState
      v-if="props.questions.length === 0"
      icon="file-text"
      title="Savol yo‘q"
      text="Bo‘sh test e’lon qilinmaydi — birinchi savolni qo‘shing."
    >
      <template
        v-if="!props.locked"
        #default
      >
        <BaseButton @click="openCreate">
          <template #icon>
            <AppIcon
              name="plus"
              :size="16"
            />
          </template>
          Savol qo‘shish
        </BaseButton>
      </template>
    </EmptyState>

    <ol
      v-else
      class="space-y-2.5"
    >
      <li
        v-for="(question, index) in props.questions"
        :key="question.id"
        class="rounded-xl border border-line bg-ink-900 p-3 sm:p-3.5"
      >
        <div class="flex items-start gap-2">
          <p class="min-w-0 flex-1 text-sm text-slate-100">
            <span class="mr-1.5 font-semibold tabular-nums text-dim">{{ index + 1 }}.</span>
            <span v-text="question.body" />
          </p>

          <div class="flex shrink-0 items-center gap-1">
            <button
              v-if="!props.locked"
              type="button"
              class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
              title="Savolni tahrirlash"
              @click="openEdit(question)"
            >
              <AppIcon
                name="edit"
                :size="15"
              />
            </button>
            <button
              v-if="!props.locked"
              type="button"
              class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-rose-500/10 hover:text-rose-300"
              title="Savolni o‘chirish"
              @click="askDelete(question)"
            >
              <AppIcon
                name="trash"
                :size="15"
              />
            </button>
          </div>
        </div>

        <div class="mt-1.5 flex flex-wrap items-center gap-1.5">
          <BaseBadge tone="neutral">
            {{ question.points }} ball
          </BaseBadge>
          <BaseBadge
            v-if="question.isMultipleChoice"
            tone="assistant"
          >
            Ko‘p javobli
          </BaseBadge>
        </div>

        <!-- To'g'ri variant yashil belgi bilan — xodim bir qarashda tekshiradi. -->
        <ul class="mt-2 space-y-1">
          <li
            v-for="option in question.options ?? []"
            :key="option.id"
            class="flex items-start gap-2 text-xs"
            :class="option.isCorrect ? 'text-green-300' : 'text-slate-400'"
          >
            <AppIcon
              :name="option.isCorrect ? 'check' : 'close'"
              :size="13"
              class="mt-0.5 shrink-0"
              :class="option.isCorrect ? 'text-green-400' : 'text-slate-600'"
            />
            <span v-text="option.body" />
          </li>
        </ul>
      </li>
    </ol>

    <QuestionFormDialog
      :open="formOpen"
      :test-id="props.testId"
      :question="editing"
      @close="formOpen = false"
      @saved="emit('changed')"
    />

    <ConfirmDeleteDialog
      :open="deleteTarget !== null"
      title="Savolni o‘chirish"
      :message="`“${deleteTarget?.body ?? 'Savol'}” savoli variantlari bilan o‘chiriladi. Bu amalni qaytarib bo‘lmaydi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleteTarget = null"
      @confirm="confirmDelete"
    />
  </section>
</template>
