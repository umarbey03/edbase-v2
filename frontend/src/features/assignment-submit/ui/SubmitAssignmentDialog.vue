<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  allowsFormat,
  answerFormatsLabel,
  assignmentTitle,
  fileAcceptFor,
  MAX_ATTACHMENTS,
  submitAssignment,
  validateAttachments,
} from '@/entities/assignment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatFileSize } from '@/shared/lib/text'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { StudentAssignmentDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * O'quvchining javobi: matn va/yoki fayllar.
 *
 * So'rov `multipart/form-data` bilan ketadi (`submitAssignment`), chunki
 * javobga daftar surati yoki ovozli o'qish yozuvi ilova qilinadi.
 *
 * NIMA UCHUN FORMATLAR SHU YERDA HAM TEKSHIRILADI: server qoidasi
 * (`Assignment.EnsureFormatAllowed`) o'z joyida qoladi va u YAKUNIY hakam,
 * lekin 10 MB ovozni mobil internetda yuklab, so'ng "audio qabul qilinmaydi"
 * degan 409 olish — o'quvchi uchun bir necha daqiqa yo'qotish.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. */
  assignment: StudentAssignmentDto | null
}>()

const emit = defineEmits<{ close: []; submitted: [] }>()

const text = ref('')
const files = ref<File[]>([])
const errorMessage = ref<string | null>(null)

watch(
  () => props.assignment,
  (assignment) => {
    // Qayta topshirishda eski matn boshlang'ich qiymat bo'ladi — o'quvchi uni
    // noldan yozmasin (ustoz odatda kichik tuzatish so'raydi).
    text.value = assignment?.mySubmission?.text ?? ''
    files.value = []
    errorMessage.value = null
  },
  { immediate: true },
)

const allowedFormats = computed(() => props.assignment?.allowedFormats ?? '')
const textAllowed = computed(() => allowsFormat(allowedFormats.value, 'Text'))
const filesAllowed = computed(
  () => allowsFormat(allowedFormats.value, 'Image') || allowsFormat(allowedFormats.value, 'Audio'),
)
const accept = computed(() => fileAcceptFor(allowedFormats.value))

/** Qayta topshirishmi (ustoz ruxsat berganidan keyin). */
const isResubmit = computed(() => (props.assignment?.mySubmission ?? null) !== null)

const trimmedText = computed(() => text.value.trim())

const attachmentError = computed(() => validateAttachments(files.value, allowedFormats.value))

/** Server: javob bo'sh bo'lmasligi kerak (matn YOKI fayl). */
const isEmptyAnswer = computed(() => trimmedText.value.length === 0 && files.value.length === 0)

function onFilesPicked(event: Event): void {
  const input = event.target as HTMLInputElement
  const picked = Array.from(input.files ?? [])

  // Tanlangan fayllar QO'SHILADI (almashtirilmaydi): telefonda odatda avval
  // kamera, keyin galereya ochiladi va ikkinchi tanlov birinchisini o'chirib
  // yuborsa, o'quvchi buni sezmay qolardi.
  files.value = [...files.value, ...picked].slice(0, MAX_ATTACHMENTS)

  // Bir xil faylni qayta tanlash ham `change` hodisasini bersin.
  input.value = ''
  errorMessage.value = null
}

function removeFile(index: number): void {
  files.value = files.value.filter((_item, position) => position !== index)
}

const mutation = useMutation({
  mutationFn: (id: number) =>
    submitAssignment(id, {
      text: trimmedText.value.length > 0 ? trimmedText.value : null,
      files: files.value,
    }),
  onSuccess: () => {
    emit('submitted')
    emit('close')
  },
  onError: (error: Error) => {
    /*
      Xato manbalari:
        400 — fayl turi/hajmi yoki bo'sh javob (sabab `problem.errors` ichida);
        409 — "javob allaqachon yuborilgan" yoki format ruxsat etilmagan;
        403 — dars qulflangan yoki vazifa boshqa guruhniki;
        503 — fayl ombori sozlanmagan (matnli javob baribir ketadi).
      Hammasini `toUserMessage` o'qiydi — bu yerda qayta yozilmaydi.
    */
    errorMessage.value = toUserMessage(error)
  },
})

const canSubmit = computed(
  () => !isEmptyAnswer.value && attachmentError.value === null && !mutation.isPending.value,
)

const confirm = useConfirm()

/**
 * R4 — TASDIQ FAQAT QAYTA TOPSHIRISH SHOXIDA.
 *
 * ★ BIRINCHI TOPSHIRISHDA OYNA YO'Q — ATAYLAB. U hech narsani
 * almashtirmaydi (avval javob YO'Q edi) va bu o'quvchining eng tez-tez
 * takrorlanadigan amali; har topshirishga ikkinchi qadam qo'shish
 * vazifani mexanik ravishda og'irlashtirardi.
 *
 * 🔴 QAYTA TOPSHIRISH ESA — YO'QOTISH: yangi javob eskisini TO'LIQ
 * almashtiradi (matn ham, fayllar ham) va QO'YILGAN BAHO BEKOR QILINADI.
 * Bundan tashqari qayta yuborish ruxsati BIR MARTALIK: oyna yopilgach
 * o'quvchi uni o'zi qayta ocha olmaydi — ustoz yana ruxsat berishi kerak.
 * Ogohlantirish sahifada bor edi, lekin u FORMA USTIDAGI matn — fayl
 * tanlab, pastdagi "Yuborish" ga yetguncha ekrandan chiqib ketadi.
 */
async function handleSubmit(): Promise<void> {
  const assignment = props.assignment
  if (assignment === null || !canSubmit.value) return

  if (isResubmit.value) {
    const previousScore = assignment.mySubmission?.score ?? null
    const ok = await confirm({
      title: 'Javobni qayta yuborish',
      message:
        `“${assignmentTitle(assignment.title, assignment.id)}” uchun avvalgi javob `
        + 'TO‘LIQ almashtiriladi.',
      confirmLabel: 'Yuborish',
      tone: 'warning',
      details: [
        'Eski matn va fayllar saqlanmaydi.',
        previousScore === null
          ? 'Ustoz tekshiruvi qaytadan boshlanadi.'
          : `Qo‘yilgan baho (${previousScore} / ${assignment.maxScore}) bekor qilinadi.`,
        'Qayta yuborish ruxsati bir martalik — keyingi urinish uchun ustoz yana ruxsat berishi kerak.',
      ],
    })
    if (!ok) return
  }

  errorMessage.value = null
  mutation.mutate(assignment.id)
}
</script>

<template>
  <BaseModal
    :open="props.assignment !== null"
    :title="isResubmit ? 'Javobni qayta yuborish' : 'Javob topshirish'"
    @close="emit('close')"
  >
    <template v-if="props.assignment !== null">
      <div class="mb-4 rounded-lg border border-line bg-ink-950 p-3">
        <p
          class="text-sm font-semibold text-slate-100"
          v-text="assignmentTitle(props.assignment.title, props.assignment.id)"
        />
        <p class="mt-0.5 text-xs text-slate-400">
          Maksimal ball: {{ props.assignment.maxScore }} · Javob turi:
          {{ answerFormatsLabel(props.assignment.allowedFormats) }}
        </p>
        <p
          v-if="props.assignment.dueAt !== null"
          class="mt-0.5 text-xs tabular-nums text-slate-400"
        >
          Muddat: {{ formatDateTime(props.assignment.dueAt) }}
        </p>
      </div>

      <!-- Muddat o'tgan bo'lsa ham topshirish MUMKIN — server "kech" deb belgilaydi. -->
      <p
        v-if="props.assignment.isOverdue"
        class="mb-4 flex items-start gap-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      >
        <AppIcon
          name="clock"
          :size="14"
          class="mt-px"
        />
        <span>Muddat o‘tgan. Javobingiz qabul qilinadi, lekin “kechikkan” deb belgilanadi.</span>
      </p>

      <p
        v-if="(props.assignment.mySubmission?.resubmitNote ?? '').length > 0"
        class="mb-4 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-300"
      >
        <span class="font-semibold text-slate-200">Ustoz izohi: </span>
        <span v-text="props.assignment.mySubmission?.resubmitNote" />
      </p>

      <p
        v-if="isResubmit"
        class="mb-4 text-[11px] leading-relaxed text-dim"
      >
        Yangi javob eskisini TO‘LIQ almashtiradi: avvalgi matn va fayllar saqlanmaydi, qo‘yilgan
        baho bekor qilinadi. Qayta yuborish ruxsati bir martalik.
      </p>

      <form
        novalidate
        @submit.prevent="handleSubmit"
      >
        <BaseField
          v-if="textAllowed"
          label="Javob matni"
          hint="Ixtiyoriy, agar fayl yuklasangiz."
        >
          <textarea
            v-model="text"
            class="zn-input min-h-32 resize-y"
            rows="5"
            placeholder="Javobingizni yozing"
          />
        </BaseField>

        <p
          v-else
          class="text-xs text-dim"
        >
          Bu vazifa matnli javob qabul qilmaydi — fayl yuklang.
        </p>

        <div
          v-if="filesAllowed"
          class="mt-3"
        >
          <BaseField
            label="Fayllar"
            :hint="`Ko‘pi bilan ${MAX_ATTACHMENTS} ta. Rasm 5 MB, ovoz 10 MB gacha.`"
            :error="attachmentError"
          >
            <input
              type="file"
              multiple
              class="zn-input pt-2.5 text-xs file:mr-3 file:rounded-md file:border-0 file:bg-ink-800 file:px-3 file:py-1.5 file:text-xs file:text-slate-200"
              :accept="accept"
              @change="onFilesPicked"
            >
          </BaseField>

          <ul
            v-if="files.length > 0"
            class="mt-2 space-y-1.5"
          >
            <li
              v-for="(file, index) in files"
              :key="`${file.name}-${index}`"
              class="flex items-center gap-2 rounded-lg border border-line bg-ink-950 px-3 py-2"
            >
              <AppIcon
                name="paperclip"
                :size="14"
                class="text-slate-400"
              />
              <span
                class="min-w-0 flex-1 truncate text-xs text-slate-200"
                v-text="file.name"
              />
              <span
                class="shrink-0 text-[11px] tabular-nums text-dim"
                v-text="formatFileSize(file.size)"
              />
              <button
                type="button"
                class="tap-target -my-2 flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-300"
                title="Faylni olib tashlash"
                @click="removeFile(index)"
              >
                <AppIcon
                  name="close"
                  :size="15"
                />
              </button>
            </li>
          </ul>
        </div>

        <p
          v-if="errorMessage !== null"
          class="mt-3 text-xs text-rose-400"
          role="alert"
          v-text="errorMessage"
        />
      </form>
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="mutation.isPending.value"
        @click="handleSubmit"
      >
        <template #icon>
          <AppIcon
            name="send"
            :size="15"
          />
        </template>
        Yuborish
      </BaseButton>
    </template>
  </BaseModal>
</template>
