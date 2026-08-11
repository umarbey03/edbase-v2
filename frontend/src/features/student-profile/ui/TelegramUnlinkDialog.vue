<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { UNLINK_REASON_MAX, unlinkTelegram } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { MODAL_AUTOFOCUS_CLASS } from '@/shared/lib/useModalHost'
import { AppIcon, BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * TELEGRAM BOG'LANISHINI UZISH — sababli tasdiq oynasi.
 *
 * ★ NEGA `useConfirm` EMAS: `useConfirm` matn maydonini qo'llamaydi
 * (`ConfirmOptions` da faqat `title`/`message`/`details`), backend esa
 * ixtiyoriy `reason` ni AUDIT IZIGA yozadi va u keyin tiklanmaydi. Sababni
 * so'ramasak, "kim, qachon, nima uchun uzdi" ustunining uchdan biri abadiy
 * bo'sh qolardi. Shu sababli — alohida kichik oyna, lekin AYNI ogohlantirish
 * matni va `danger` tugma bilan.
 *
 * ★ NEGA XATO OYNADA QOLADI (mavjud `ConfirmDeleteDialog` naqshi): server
 * **409** ("allaqachon bog'lanmagan") yoki **403** ("nishon Admin — faqat
 * Admin uzadi) qaytarishi mumkin. Oyna yopilib ketsa xodim sababni ko'rmasdi
 * va amalni qayta-qayta bosardi.
 *
 * 🔴 YON TA'SIRI: server `TokenVersion` ni oshiradi — o'quvchining MAVJUD
 * kirish tokeni darhol 401 bo'ladi, ya'ni u platformaga KIRA OLMAYDI.
 * Matnda aynan shu aytiladi (talab: *"O'quvchi platformaga kira olmaydi"*).
 */
const props = defineProps<{
  open: boolean
  /** `null` bo'lsa oyna hech qachon ochilmaydi (himoya, oqim uchun emas). */
  userId: number | null
  userName: string
  /** Hozirgi Telegram nomi — matnda ko'rsatiladi (`@` belgisiz keladi). */
  username: string | null
}>()

const emit = defineEmits<{ close: []; unlinked: [] }>()

const reason = ref('')
const errorMessage = ref<string | null>(null)

// Har ochilishda toza holat: oldingi urinishning sababi va xatosi qolmasin.
watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    reason.value = ''
    errorMessage.value = null
  },
  { immediate: true },
)

const reasonError = computed(() =>
  reason.value.length > UNLINK_REASON_MAX
    ? `Sabab ${UNLINK_REASON_MAX} belgidan oshmasin.`
    : null,
)

const mutation = useMutation({
  mutationFn: () => {
    const id = props.userId
    if (id === null) throw new Error('Foydalanuvchi tanlanmagan.')
    const trimmed = reason.value.trim()
    return unlinkTelegram(id, { reason: trimmed.length > 0 ? trimmed : null })
  },
  onSuccess: () => {
    emit('unlinked')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function submit(): void {
  if (props.userId === null || reasonError.value !== null || mutation.isPending.value) return
  errorMessage.value = null
  mutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Telegram bog‘lanishini uzish"
    @close="emit('close')"
  >
    <div
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 p-3.5"
      role="alert"
    >
      <p class="text-sm font-semibold text-rose-200">
        O‘quvchi platformaga kira olmaydi.
      </p>
      <p class="mt-1 text-xs leading-relaxed text-rose-200">
        <span
          class="font-semibold"
          v-text="props.userName"
        />
        hisobidan Telegram uziladi va uning barcha sessiyalari DARHOL bekor
        qilinadi. Qayta kirish uchun o‘quvchi botga raqamini yana ulashishi kerak.
      </p>
    </div>

    <p
      v-if="props.username !== null"
      class="mt-3 text-xs text-slate-400"
    >
      Hozirgi nom:
      <span
        class="font-medium text-slate-100"
        v-text="`@${props.username}`"
      />
    </p>

    <div class="mt-3">
      <BaseField
        label="Sababi (ixtiyoriy)"
        hint="Audit iziga yoziladi va keyin o‘zgartirilmaydi."
        :error="reasonError"
      >
        <!--
          `MODAL_AUTOFOCUS_CLASS` — fokus SABAB maydoniga tushadi, tasdiq
          tugmasiga EMAS: `danger` amalda Enter bosilishi bilan uzilib
          ketmasin (23-tuzoq bilan bir chiziqda).
        -->
        <textarea
          v-model="reason"
          class="zn-input"
          :class="MODAL_AUTOFOCUS_CLASS"
          rows="2"
          :maxlength="UNLINK_REASON_MAX"
          placeholder="Masalan: raqam boshqa odamga o‘tgan"
        />
      </BaseField>
    </div>

    <p
      v-if="errorMessage !== null"
      class="mt-3 text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        variant="danger"
        :disabled="props.userId === null || reasonError !== null"
        :loading="mutation.isPending.value"
        @click="submit"
      >
        <template #icon>
          <AppIcon
            name="link-off"
            :size="15"
          />
        </template>
        Uzish
      </BaseButton>
    </template>
  </BaseModal>
</template>
