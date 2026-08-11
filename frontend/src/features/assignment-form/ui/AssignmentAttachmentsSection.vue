<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { ref } from 'vue'

import {
  ASSIGNMENT_ATTACHMENT_ACCEPT,
  assignmentAttachmentUploadPath,
  attachmentKindLabel,
  buildAssignmentAttachmentForm,
  deleteAssignmentAttachment,
  MAX_ASSIGNMENT_ATTACHMENTS,
} from '@/entities/assignment'
import {
  probeKindForAttachment,
  probeMedia,
  UploadQueueList,
  uploadWithProgress,
  useUploadLimits,
  useUploadQueue,
} from '@/features/lesson-media'
import type { UploadProgress } from '@/features/lesson-media'
import { toUserMessage } from '@/shared/api'
import { formatFileSize } from '@/shared/lib/text'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { AssignmentAttachmentDto } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'

import AssignmentAttachmentRow from './AssignmentAttachmentRow.vue'

/**
 * VAZIFA SHARTINING BIRIKTIRMALARI: rasm / audio / PDF, BIR NECHTA.
 *
 * Talab: *"shart biriktirmalari — rasm/audio/fayl, bir nechta"*. Ilgari
 * shart uchun faqat `imageKey` (BITTA rasm, ombor kaliti) bor edi va u UI'da
 * umuman tahrirlanmasdi.
 *
 * ★ MEXANIZM `features/lesson-media` DAN: progress, bekor qilish, chegarani
 * OLDINDAN tekshirish va ketma-ket yuborish — dars videosi bilan AYNI kod.
 * Ikkinchi nusxa bo'lsa, tuzatish bir joyda qolib ketardi.
 *
 * ⚠️ VIDEO QABUL QILINMAYDI (server ham rad etadi): shart uchun video kerak
 * bo'lsa u DARS mediasi bo'ladi — u yerda `Range` bilan oqim va katta hajm
 * chegarasi bor.
 *
 * ⚠️ HAJM CHEGARASI shart biriktirmasi uchun ham `lesson.image_max_mb`
 * (standart 10 MB) — server AYNI sozlamani uch turga (rasm/audio/hujjat)
 * qo'llaydi.
 */
const props = defineProps<{
  assignmentId: number
  attachments: readonly AssignmentAttachmentDto[]
}>()

const emit = defineEmits<{ 'update:attachments': [value: AssignmentAttachmentDto[]] }>()

const confirm = useConfirm()
const limits = useUploadLimits()

const actionError = ref<string | null>(null)
const fileInput = ref<HTMLInputElement | null>(null)

/* ================================================================ yuklash */

async function uploadOne(
  file: File,
  onProgress: (progress: UploadProgress) => void,
  signal: AbortSignal,
): Promise<void> {
  // Audio davomiyligi FAQAT ko'rsatish uchun (serverda dekoder yo'q).
  const kind = probeKindForAttachment(file)
  const meta = kind === 'Image' ? { durationSec: null } : await probeMedia(file, kind)
  if (signal.aborted) return

  const created = await uploadWithProgress<AssignmentAttachmentDto>({
    path: assignmentAttachmentUploadPath(props.assignmentId),
    form: buildAssignmentAttachmentForm(file, meta.durationSec),
    onProgress,
    signal,
  })

  emit('update:attachments', [...props.attachments, created])
}

/*
  ★ CHEGARA TEKSHIRUVI NAVBAT ICHIDA: chegaradan katta fayl navbatga
  `error` holatida tushadi va SERVERGA UMUMAN yuborilmaydi — lekin
  ro'yxatdan yo'qolmaydi, ya'ni foydalanuvchi sababni ko'radi.
*/
const queue = useUploadQueue({
  upload: uploadOne,
  validate: (file) => limits.attachmentSizeError(file),
})

function openPicker(): void {
  fileInput.value?.click()
}

function onFilesPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const picked = Array.from(input.files ?? [])
  input.value = ''
  if (picked.length === 0) return

  actionError.value = null
  const room = MAX_ASSIGNMENT_ATTACHMENTS - props.attachments.length - queue.activeCount.value
  if (room <= 0) {
    actionError.value =
      `Vazifa shartiga ko‘pi bilan ${MAX_ASSIGNMENT_ATTACHMENTS} ta fayl biriktiriladi.`
    return
  }

  queue.enqueue(picked.slice(0, room))
  if (picked.length > room) {
    actionError.value =
      `Faqat ${room} ta fayl qabul qilindi: ko‘pi bilan `
      + `${MAX_ASSIGNMENT_ATTACHMENTS} ta fayl biriktiriladi.`
  }
}

/* ============================================================== o'chirish */

const deletingId = ref<number | null>(null)

const deleteMutation = useMutation({
  mutationFn: (attachmentId: number) => deleteAssignmentAttachment(attachmentId),
  onSuccess: (_result, attachmentId) => {
    emit(
      'update:attachments',
      props.attachments.filter((item) => item.id !== attachmentId),
    )
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    deletingId.value = null
  },
})

async function askDelete(attachment: AssignmentAttachmentDto): Promise<void> {
  const ok = await confirm({
    title: 'Biriktirmani o‘chirish',
    message:
      `${attachmentKindLabel(attachment.kind)} shart biriktirmasi o‘chiriladi. `
      + 'Fayl ombordan ham olib tashlanadi — bu amalni QAYTARIB BO‘LMAYDI.',
    confirmLabel: 'O‘chirish',
    tone: 'danger',
    details: [`Hajmi: ${formatFileSize(attachment.sizeBytes)}`],
  })
  if (!ok) return

  actionError.value = null
  deletingId.value = attachment.id
  deleteMutation.mutate(attachment.id)
}
</script>

<template>
  <section>
    <h4 class="text-xs font-semibold text-slate-200">
      Shart biriktirmalari
      <span
        v-if="props.attachments.length > 0"
        class="ml-1 tabular-nums font-normal text-dim"
      >{{ props.attachments.length }}</span>
    </h4>
    <p class="mt-0.5 text-[11px] leading-relaxed text-dim">
      Rasm, ovozli izoh yoki PDF (bittadan ko‘p bo‘lishi mumkin). Bitta fayl uchun
      chegara: <span class="tabular-nums">{{ limits.imageMaxMb.value }} MB</span>.
      Video biriktirilmaydi — u dars mediasi.
    </p>

    <div
      v-if="actionError !== null"
      class="mt-2 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-[11px] text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <ul
      v-if="props.attachments.length > 0"
      class="mt-2 space-y-2"
    >
      <AssignmentAttachmentRow
        v-for="attachment in props.attachments"
        :key="attachment.id"
        :attachment="attachment"
        :deleting="deletingId === attachment.id"
        @delete="askDelete(attachment)"
      />
    </ul>

    <UploadQueueList
      :items="queue.items.value"
      @cancel="queue.cancel"
      @retry="queue.retry"
    />

    <div class="mt-2.5 flex flex-wrap items-center gap-2">
      <!-- `sr-only`: `display:none` maydonni `click()` bilan ochib bo'lmaydi. -->
      <input
        ref="fileInput"
        type="file"
        class="sr-only"
        :accept="ASSIGNMENT_ATTACHMENT_ACCEPT"
        multiple
        tabindex="-1"
        aria-hidden="true"
        @change="onFilesPicked"
      >
      <BaseButton
        size="sm"
        variant="secondary"
        @click="openPicker"
      >
        <template #icon>
          <AppIcon
            name="paperclip"
            :size="14"
          />
        </template>
        Fayl biriktirish
      </BaseButton>
      <BaseButton
        v-if="queue.items.value.length > 0"
        size="sm"
        variant="ghost"
        :disabled="queue.isBusy.value"
        @click="queue.clearFinished"
      >
        Ro‘yxatni tozalash
      </BaseButton>
    </div>
  </section>
</template>
