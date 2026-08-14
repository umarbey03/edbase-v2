<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  LESSON_GRADE_COMMENT_MAX,
  deleteLessonGrade,
  lessonGradeChoices,
  upsertLessonGrade,
} from '@/entities/lesson-grade'
import type { LessonGradeRowDto } from '@/entities/lesson-grade'
import { sessionTypeLabel } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import type { SessionStatusName } from '@/shared/types'
import { BaseButton, BaseModal } from '@/shared/ui'

/**
 * Dars bahosi katagi — `AttendanceCellDialog` ning baho uchun juftligi.
 *
 * ★ TUZILISH DAVOMAT OYNASI BILAN AYNI (tafsilot ro'yxati -> tuzatish
 * qismi -> saqlash): ustoz ikki tabda ikki xil oyna o'rganmasin.
 *
 * ★ DAVOMATDAN YAGONA MA'NOVIY FARQI: bu yerda "platforma o'lchovi" bo'limi
 * YO'Q. Davomatda o'lchov va qaror yonma-yon turadi (va ZID bo'lishi
 * mumkin), bahoda esa o'lchov umuman mavjud emas — har bir qiymat odamning
 * qarori. Shuning uchun oyna qisqaroq.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. */
  cell: {
    sessionId: number
    sessionTitle: string
    sessionType: string
    sessionStatus: SessionStatusName
    sessionStart: string
    canEdit: boolean
    defaultMaxScore: number
    studentId: number
    studentName: string
    row: LessonGradeRowDto | null
  } | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

/** `''` — hali yozilmagan. `number` emas: bo'sh maydon `0` ga aylanmasin. */
const score = ref('')
const maxScore = ref('')
const comment = ref('')
const errorMessage = ref<string | null>(null)

/**
 * ★ PUT TUZOG'I: server tanani TO'LIQ ALMASHTIRADI — `comment` yuborilmasa
 * avvalgi izoh o'chadi. Shuning uchun oyna ochilganda MAVJUD qiymatlar
 * maydonlarga yuklanadi va saqlashda HAMMASI qaytariladi.
 */
watch(
  () => props.cell,
  (cell) => {
    score.value = cell?.row?.score != null ? String(cell.row.score) : ''
    maxScore.value = cell?.row?.maxScore != null ? String(cell.row.maxScore) : ''
    comment.value = cell?.row?.comment ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

/** Bekor qilingan darsga baho qo'yib bo'lmaydi — server 409 qaytaradi. */
const isCancelled = computed(() => props.cell?.sessionStatus === 'Cancelled')
const canEdit = computed(() => props.cell?.canEdit === true && !isCancelled.value)

/**
 * ★ O'CHIRISH BEKOR QILINGAN DARSDA HAM MUMKIN (qo'yishdan FARQI): dars
 * baholangandan KEYIN bekor qilinishi mumkin va endi ma'nosiz bo'lib
 * qolgan bahoni olib tashlash kerak bo'ladi. Server ham shunday ishlaydi.
 */
const canDelete = computed(() => props.cell?.canEdit === true && props.cell.row?.score != null)

/** Amaldagi maxraj: yozilgani, bo'lmasa qatordagi, bo'lmasa serverning standarti. */
const effectiveMax = computed(() => {
  const typed = Number(maxScore.value)
  if (maxScore.value.trim().length > 0 && Number.isFinite(typed) && typed > 0) return typed
  return props.cell?.defaultMaxScore ?? 5
})

/** Standart shkalada tez tanlash tugmalari; 100 ballikda bo'sh (sabab modelda). */
const choices = computed(() => lessonGradeChoices(effectiveMax.value))

const saveMutation = useMutation({
  mutationFn: (payload: {
    sessionId: number
    studentId: number
    score: number
    maxScore: number | null
    comment: string | null
  }) =>
    upsertLessonGrade(payload.sessionId, payload.studentId, {
      score: payload.score,
      maxScore: payload.maxScore,
      comment: payload.comment,
    }),
  onSuccess: () => {
    errorMessage.value = null
    emit('saved')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const deleteMutation = useMutation({
  mutationFn: (payload: { sessionId: number; studentId: number }) =>
    deleteLessonGrade(payload.sessionId, payload.studentId),
  onSuccess: () => {
    errorMessage.value = null
    emit('saved')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const pending = computed(
  () => saveMutation.isPending.value || deleteMutation.isPending.value,
)

function save(): void {
  const cell = props.cell
  if (cell === null) return

  const parsed = Number(score.value)

  // ★ TEKSHIRUV SHU YERDA HAM: server baribir 400 qaytaradi, lekin
  //   xabar tarmoqqa borib kelmasdan darhol ko'rinsa ustoz tuzatishni
  //   tezroq qiladi. Server — YAGONA haqiqat manbai, bu esa qulaylik.
  if (score.value.trim().length === 0 || !Number.isFinite(parsed)) {
    errorMessage.value = 'Baho kiriting'
    return
  }
  if (parsed < 0) {
    errorMessage.value = 'Baho manfiy bo‘lmaydi'
    return
  }
  if (parsed > effectiveMax.value) {
    errorMessage.value = `Baho ${effectiveMax.value} balldan oshmasin`
    return
  }

  const typedMax = maxScore.value.trim()
  const trimmedComment = comment.value.trim()

  saveMutation.mutate({
    sessionId: cell.sessionId,
    studentId: cell.studentId,
    score: parsed,

    // Bo'sh maydon — "standart shkala" (`null`), 5 EMAS: shkala keyin
    // o'zgarsa qatorda saqlangan son eskirib qolardi.
    maxScore: typedMax.length > 0 ? Number(typedMax) : null,
    // Bo'sh maydon — izohni ATAYLAB o'chirish.
    comment: trimmedComment.length > 0 ? trimmedComment : null,
  })
}

function remove(): void {
  const cell = props.cell
  if (cell === null) return
  deleteMutation.mutate({ sessionId: cell.sessionId, studentId: cell.studentId })
}
</script>

<template>
  <BaseModal
    :open="props.cell !== null"
    :title="props.cell?.studentName ?? 'Baho'"
    @close="emit('close')"
  >
    <template v-if="props.cell !== null">
      <p class="text-xs text-slate-400">
        {{ props.cell.sessionTitle || sessionTypeLabel(props.cell.sessionType) }} ·
        {{ formatDateTime(props.cell.sessionStart) }}
      </p>

      <dl class="mt-3.5 divide-y divide-line text-[13px]">
        <div class="flex items-center justify-between gap-3 py-2">
          <dt class="text-slate-400">
            Joriy baho
          </dt>
          <dd class="tabular-nums text-slate-200">
            <!-- `0` va "baho yo'q" ATAYLAB ajratiladi: birinchisi ustozning
                 qarori, ikkinchisi — hech kim qaramagan. -->
            <template v-if="props.cell.row?.score != null">
              <b class="text-slate-100">{{ props.cell.row.score }}</b>
              <span class="text-dim">/{{ props.cell.row.maxScore ?? props.cell.defaultMaxScore }}</span>
              <span
                v-if="props.cell.row.percent !== null"
                class="ml-1.5 text-dim"
              >({{ props.cell.row.percent }}%)</span>
            </template>
            <template v-else>
              —
            </template>
          </dd>
        </div>
        <div
          v-if="props.cell.row?.gradedByName != null"
          class="flex items-center justify-between gap-3 py-2"
        >
          <dt class="text-slate-400">
            Qo‘ydi
          </dt>
          <dd class="text-slate-200">
            {{ props.cell.row.gradedByName }}
            <span
              v-if="props.cell.row.gradedAt !== null"
              class="text-dim"
            >· {{ formatDateTime(props.cell.row.gradedAt) }}</span>
          </dd>
        </div>
        <div class="flex items-start justify-between gap-3 py-2">
          <dt class="shrink-0 text-slate-400">
            Izoh
          </dt>
          <dd
            class="min-w-0 break-words text-right"
            :class="props.cell.row?.comment ? 'text-amber-400' : 'text-slate-500'"
            v-text="props.cell.row?.comment ?? 'Izoh kiritilmagan'"
          />
        </div>
      </dl>

      <template v-if="canEdit">
        <p class="mt-4 text-[13px] font-semibold text-slate-200">
          Baho qo‘yish
        </p>

        <!--
          Tez tanlash tugmalari FAQAT kichik shkalada (5 ballik). 100 ballik
          imtihonda ular chalg'itardi — sabab `lessonGradeChoices` izohida.
        -->
        <div
          v-if="choices.length > 0"
          class="mt-2 flex flex-wrap gap-1.5"
        >
          <button
            v-for="choice in choices"
            :key="choice"
            type="button"
            class="min-h-11 min-w-11 flex-1 rounded-lg border px-3 text-sm font-semibold tabular-nums transition-colors"
            :class="
              score === String(choice)
                ? 'border-brand-500 bg-brand-500 text-on-brand'
                : 'border-line bg-ink-950 text-slate-300 hover:border-line-strong'
            "
            :aria-pressed="score === String(choice)"
            @click="score = String(choice)"
            v-text="choice"
          />
        </div>

        <div class="mt-3 flex gap-2">
          <div class="flex-1">
            <label
              class="block text-xs text-slate-400"
              for="lg-score"
            >Ball</label>
            <input
              id="lg-score"
              v-model="score"
              class="zn-input mt-1.5"
              type="number"
              min="0"
              :max="effectiveMax"
              step="0.01"
              inputmode="decimal"
            >
          </div>
          <div class="flex-1">
            <label
              class="block text-xs text-slate-400"
              for="lg-max"
            >Maksimal ball</label>
            <input
              id="lg-max"
              v-model="maxScore"
              class="zn-input mt-1.5"
              type="number"
              min="1"
              step="0.01"
              inputmode="decimal"
              :placeholder="String(props.cell.defaultMaxScore)"
            >
          </div>
        </div>
        <p class="mt-1 text-[11px] text-dim">
          Maksimal ball bo‘sh qoldirilsa {{ props.cell.defaultMaxScore }} ballik
          shkala ishlatiladi.
        </p>

        <label
          class="mt-3.5 block text-xs text-slate-400"
          for="lg-comment"
        >
          Izoh (ixtiyoriy, {{ LESSON_GRADE_COMMENT_MAX }} belgigacha)
        </label>
        <input
          id="lg-comment"
          v-model="comment"
          class="zn-input mt-1.5"
          type="text"
          :maxlength="LESSON_GRADE_COMMENT_MAX"
          placeholder="Masalan: darsda faol qatnashdi"
        >
        <p class="mt-1 text-[11px] text-dim">
          Maydon bo‘sh qoldirilsa avvalgi izoh o‘chadi.
        </p>
      </template>

      <p
        v-else
        class="mt-4 rounded-lg border border-line bg-ink-950 p-3 text-xs text-slate-400"
      >
        {{
          isCancelled
            ? 'Dars bekor qilingan — yangi baho qo‘yib bo‘lmaydi.'
            : 'Bu darsga baho qo‘yishga ruxsatingiz yo‘q.'
        }}
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-xs text-rose-200"
        role="alert"
        v-text="errorMessage"
      />
    </template>

    <template #footer>
      <!--
        O'CHIRISH — "0 qo'yish" EMAS. 0 reytingga to'liq kiradi, o'chirilgan
        baho esa umuman hisobga olinmaydi; bu tugmasiz adashib qo'yilgan
        bahoni tuzatishning yagona yo'li o'quvchiga 0 yozib qo'yish bo'lardi.
      -->
      <BaseButton
        v-if="canDelete"
        variant="danger"
        :loading="deleteMutation.isPending.value"
        @click="remove"
      >
        Bahoni o‘chirish
      </BaseButton>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
      <BaseButton
        v-if="canEdit"
        :loading="saveMutation.isPending.value"
        :disabled="pending"
        @click="save"
      >
        Saqlash
      </BaseButton>
    </template>
  </BaseModal>
</template>
