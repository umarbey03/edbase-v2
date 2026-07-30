<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { reopenSubmission } from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import type { SubmissionDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Qayta topshirishga ruxsat berish.
 *
 * O'quvchi bir vazifaga BIR MARTA javob yuboradi (`Submission` unikal
 * indeksi). Ikkinchi urinish faqat shu ruxsatdan keyin mumkin va ruxsat
 * BIR MARTALIK — yangi javob kelgach Domain uni o'zi yopadi.
 *
 * IZOH SO'RALADI, chunki u O'QUVCHIGA ko'rinadi: "qayta yuboring" degan
 * sababsiz talab eng ko'p savol tug'diradigan holat.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. */
  submission: SubmissionDto | null
}>()

const emit = defineEmits<{ close: []; reopened: [] }>()

const note = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.submission,
  (submission) => {
    // Avvalgi izoh bo'lsa ko'rsatamiz — ustoz uni to'ldirishi mumkin.
    note.value = submission?.resubmitNote ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

const mutation = useMutation({
  mutationFn: (id: number) =>
    reopenSubmission(id, {
      // Bo'sh izoh `null` bo'lib ketadi — server ham `IsNullOrWhiteSpace` ni
      // shunday normallashtiradi.
      note: note.value.trim().length > 0 ? note.value.trim() : null,
    }),
  onSuccess: () => {
    emit('reopened')
    emit('close')
  },
  onError: (error: Error) => {
    // 403 — o'quvchi bu ustozning guruhida emas.
    errorMessage.value = toUserMessage(error)
  },
})

const alreadyOpen = computed(() => props.submission?.allowResubmit === true)
</script>

<template>
  <BaseModal
    :open="props.submission !== null"
    title="Qayta topshirishga ruxsat"
    @close="emit('close')"
  >
    <template v-if="props.submission !== null">
      <p class="text-sm text-slate-300">
        <span
          class="font-semibold text-slate-100"
          v-text="props.submission.studentName ?? '—'"
        />
        yana bir marta javob yubora oladi.
      </p>

      <p class="mt-2 text-[11px] leading-relaxed text-dim">
        Ruxsat bir martalik: o‘quvchi yangi javob yuborgach avtomatik yopiladi. Qo‘yilgan baho
        hozir o‘chirilmaydi — u yangi javob kelganda bekor bo‘ladi.
      </p>

      <p
        v-if="alreadyOpen"
        class="mt-3 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-300"
      >
        Bu javobga ruxsat allaqachon berilgan. Qayta saqlash faqat izohni yangilaydi.
      </p>

      <div class="mt-4">
        <BaseField
          label="Sabab (o‘quvchiga ko‘rinadi)"
          hint="Masalan: “Rasm xira chiqibdi, qaytadan suratga oling”."
        >
          <textarea
            v-model="note"
            class="zn-input min-h-24 resize-y"
            rows="3"
          />
        </BaseField>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :loading="mutation.isPending.value"
        @click="props.submission !== null && mutation.mutate(props.submission.id)"
      >
        Ruxsat berish
      </BaseButton>
    </template>
  </BaseModal>
</template>
