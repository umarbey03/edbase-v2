<script setup lang="ts">
import { computed } from 'vue'

import { ANSWER_FORMAT_OPTIONS } from '@/entities/assignment'
import type { AnswerFormatName } from '@/entities/assignment'
import { BaseField } from '@/shared/ui'

import { validateAssignmentForm } from '../model/assignment-form'
import type { AssignmentFormState } from '../model/assignment-form'

/**
 * UY VAZIFASI MAYDONLARI — IKKI joyda ishlatiladigan YAGONA forma tanasi:
 *  • `AssignmentFormDialog` (vazifalar sahifasi / baholash sahifasi);
 *  • `LessonAssignmentSection` (dars drawer'ining 4-bo'limi).
 *
 * NISHON (guruh/dars) bu yerda YO'Q: dialogda u tanlanadi, drawer'da esa
 * allaqachon ma'lum (shu dars). Umumiy qismga faqat HAR IKKISIDA bir xil
 * bo'lgan maydonlar kiradi.
 *
 * ★ TEKSHIRUV `validateAssignmentForm` DAN: qoida bu yerda TAKRORLANMAYDI,
 * chaqiruvchi ham AYNI funksiyani ishlatib "Saqlash" tugmasini boshqaradi.
 *
 * ★ "MAJBURIY MAYDON" XATOSI DARHOL KO'RSATILMAYDI: yangi forma ochilganda
 * sarlavha bo'sh bo'ladi va "Sarlavha kiritilishi kerak" degan qizil yozuv
 * hech narsa qilmagan foydalanuvchini ayblab turardi. Xato saqlashga
 * urinilgandan keyin (`submitted`) yoki maydon to'ldirilib, keyin
 * bo'shatilganda chiqadi.
 */
const props = withDefaults(
  defineProps<{
    modelValue: AssignmentFormState
    /** Saqlashga urinilganmi — "majburiy maydon" xatolari shundan keyin ko'rinadi. */
    submitted?: boolean
    /** Maydonlar o'chirilganmi (saqlash davom etayotganda). */
    disabled?: boolean
  }>(),
  { submitted: false, disabled: false },
)

const emit = defineEmits<{ 'update:modelValue': [value: AssignmentFormState] }>()

function patch(changes: Partial<AssignmentFormState>): void {
  emit('update:modelValue', { ...props.modelValue, ...changes })
}

function toggleFormat(value: AnswerFormatName): void {
  const current = props.modelValue.formats
  patch({
    formats: current.includes(value)
      ? current.filter((item) => item !== value)
      : [...current, value],
  })
}

const errors = computed(() => validateAssignmentForm(props.modelValue))

/** Ko'rinadigan xatolar (yuqoridagi "majburiy maydon" qoidasi). */
const shown = computed(() => ({
  title:
    props.submitted || props.modelValue.title.trim().length > 0 ? errors.value.title : null,
  description: errors.value.description,
  maxScore:
    props.submitted || props.modelValue.maxScoreText.trim().length > 0
      ? errors.value.maxScore
      : null,
  // Standart holatda ikki format tanlangan, ya'ni bu xato faqat foydalanuvchi
  // HAMMASINI olib tashlaganda chiqadi — darhol ko'rsatish to'g'ri.
  formats: errors.value.formats,
}))
</script>

<template>
  <div>
    <BaseField
      label="Sarlavha"
      :error="shown.title"
    >
      <input
        class="zn-input js-modal-autofocus"
        :value="props.modelValue.title"
        :disabled="props.disabled"
        placeholder="Masalan: 3-dars uy vazifasi"
        @input="patch({ title: ($event.target as HTMLInputElement).value })"
      >
    </BaseField>

    <div class="mt-3">
      <BaseField
        label="Shart (tavsif)"
        hint="Ixtiyoriy. O‘quvchi vazifa kartochkasida ko‘radi."
        :error="shown.description"
      >
        <textarea
          class="zn-input min-h-24 resize-y"
          rows="3"
          :value="props.modelValue.description"
          :disabled="props.disabled"
          @input="patch({ description: ($event.target as HTMLTextAreaElement).value })"
        />
      </BaseField>
    </div>

    <div class="mt-3 grid gap-3 sm:grid-cols-2">
      <BaseField
        label="Maksimal ball"
        :error="shown.maxScore"
      >
        <input
          class="zn-input"
          inputmode="decimal"
          placeholder="5"
          :value="props.modelValue.maxScoreText"
          :disabled="props.disabled"
          @input="patch({ maxScoreText: ($event.target as HTMLInputElement).value })"
        >
      </BaseField>

      <BaseField
        label="Topshirish muddati"
        hint="Bo‘sh bo‘lsa — muddatsiz."
      >
        <input
          class="zn-input"
          type="datetime-local"
          :value="props.modelValue.dueLocal"
          :disabled="props.disabled"
          @input="patch({ dueLocal: ($event.target as HTMLInputElement).value })"
        >
      </BaseField>
    </div>

    <p class="mt-1 text-[11px] leading-relaxed text-dim">
      Muddat o‘tgach javob RAD ETILMAYDI — u “kechikkan” deb belgilanadi va baholashda
      ko‘rinadi.
    </p>

    <!--
      🔴 JAVOB FORMATLARI — BIR VAQTDA BIR NECHTASI (`[Flags]` enum).
      Kamida bittasi tanlanishi SHART: bo'sh to'plam `None` bo'lib ketadi va
      o'quvchi javob bera olmaydi (server 400 +
      `problem.errors.allowedFormats` beradi).
    -->
    <div class="mt-3">
      <BaseField
        label="Qanday javob qabul qilinadi"
        :error="shown.formats"
      >
        <div class="mt-0.5 space-y-1.5">
          <label
            v-for="option in ANSWER_FORMAT_OPTIONS"
            :key="option.value"
            class="flex min-h-11 items-center gap-2.5 rounded-lg border border-line bg-ink-950 px-3 text-sm text-slate-200"
          >
            <input
              type="checkbox"
              class="size-4 accent-brand-500"
              :checked="props.modelValue.formats.includes(option.value)"
              :disabled="props.disabled"
              @change="toggleFormat(option.value)"
            >
            <span v-text="option.label" />
            <span
              class="text-[11px] text-dim"
              v-text="option.hint"
            />
          </label>
        </div>
      </BaseField>
    </div>
  </div>
</template>
