<script setup lang="ts">
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue'

import { CHAT_RATE_MAX_MESSAGES, MAX_MESSAGE_LENGTH } from '@/entities/message'
import { AppIcon, BaseSpinner } from '@/shared/ui'

const props = defineProps<{
  /**
   * Xabar yuborish funksiyasi. `false` qaytsa matn maydonga QAYTARILADI.
   * (Optimistik nusxa bilan ishlash `useOptimisticChat` ichida.)
   */
  send: (body: string) => Promise<boolean>
  canSend: boolean
  isSending: boolean
  /** SPEC 6.2 — tezlik chegarasi qoldig'i (0 bo'lsa yuborsa bo'ladi). */
  cooldownRemainingMs: number
  disabled: boolean
  disabledHint: string
}>()

const draft = ref('')
const textarea = useTemplateRef<HTMLTextAreaElement>('textarea')

/**
 * Foydalanuvchi chegara ishlab turgan paytda yuborishga URINDIMI.
 *
 * ★ NIMA UCHUN KERAK: ilgari chegara paytida `canSubmit` `false` bo'lib,
 * Enter UMUMAN hech narsa qilmasdi — xabar ham ketmasdi, xato ham
 * chiqmasdi. Foydalanuvchi buni "chat javob bermayapti" deb his qilardi.
 * Endi urinish yozib olinadi va sabab yozuvi kuchayadi.
 */
const blockedAttempt = ref(false)

const cooldownSeconds = computed(() => Math.ceil(props.cooldownRemainingMs / 1000))
const isCoolingDown = computed(() => props.cooldownRemainingMs > 0)
const remaining = computed(() => MAX_MESSAGE_LENGTH - draft.value.length)

/** Matn bor va yuborish jarayoni ketmayapti — Enter qabul qilinadi. */
const canSubmit = computed(
  () => !props.disabled && !props.isSending && draft.value.trim().length > 0,
)

/** Tugma chegara paytida ham o'chib turadi (raqamli hisob ko'rsatiladi). */
const canPressSend = computed(() => canSubmit.value && props.canSend && !isCoolingDown.value)

const hint = computed(() => {
  if (blockedAttempt.value)
    return `Xabar hali yuborilmadi — ${cooldownSeconds.value} soniyadan so‘ng urinib ko‘ring`
  if (isCoolingDown.value)
    return `Sekinroq — 10 soniyada ${CHAT_RATE_MAX_MESSAGES} tagacha xabar`
  if (props.disabled) return props.disabledHint
  return 'Enter — yuborish, Shift+Enter — yangi qator'
})

// Chegara tugadi — ogohlantirish ham o'chadi.
watch(
  () => props.cooldownRemainingMs,
  (value) => {
    if (value <= 0) blockedAttempt.value = false
  },
)

function autoResize(): void {
  const element = textarea.value
  if (element === null) return
  element.style.height = 'auto'
  // 4 qatordan oshmasin (~104px), keyin ichida skroll bo'ladi.
  element.style.height = `${Math.min(element.scrollHeight, 104)}px`
}

async function clearDraft(): Promise<void> {
  draft.value = ''
  await nextTick()
  autoResize()
}

async function submit(): Promise<void> {
  if (!canSubmit.value) return

  if (isCoolingDown.value || !props.canSend) {
    blockedAttempt.value = true
    return
  }

  const body = draft.value

  // ★ OPTIMISTIK: matn DARHOL tozalanadi va xabar shu zahoti ro'yxatda
  // ko'rinadi. Ilgari matn server javobi qaytguncha maydonda turardi —
  // sekin internetda (RTT 150-400 ms) bu "yopishib qolgan" bo'lib
  // sezilardi va aynan shu "kechikish" deb shikoyat qilingan edi.
  await clearDraft()

  const ok = await props.send(body)
  if (ok) return

  // Yuborilmadi — matnni qaytaramiz, lekin foydalanuvchi yangisini yozib
  // ulgurgan bo'lsa uni BOSIB KETMAYMIZ.
  if (draft.value.length === 0) {
    draft.value = body
    await nextTick()
    autoResize()
  }
}

function onKeydown(event: KeyboardEvent): void {
  // Enter — yuborish, Shift+Enter — yangi qator (IME kompozitsiyasiga tegmaymiz).
  if (event.key !== 'Enter' || event.shiftKey || event.isComposing) return
  event.preventDefault()
  void submit()
}
</script>

<template>
  <form
    class="border-t border-line bg-ink-900 p-3"
    @submit.prevent="submit"
  >
    <div
      class="flex items-end gap-2 rounded-xl bg-ink-850 p-1.5 ring-1 ring-inset transition-colors"
      :class="props.disabled ? 'ring-line opacity-60' : 'ring-line focus-within:ring-brand-500/70'"
    >
      <textarea
        ref="textarea"
        v-model="draft"
        rows="1"
        :maxlength="MAX_MESSAGE_LENGTH"
        :disabled="props.disabled"
        :placeholder="props.disabled ? props.disabledHint : 'Xabar yozing…'"
        class="scrollbar-slim max-h-26 min-h-9 w-full resize-none bg-transparent px-2 py-1.5 text-sm text-slate-100 placeholder:text-slate-500 focus:outline-none disabled:cursor-not-allowed"
        @input="autoResize"
        @keydown="onKeydown"
      />

      <button
        type="submit"
        class="mb-0.5 inline-flex size-9 shrink-0 items-center justify-center rounded-lg bg-brand-600 text-white transition-colors hover:bg-brand-500 disabled:cursor-not-allowed disabled:bg-ink-750 disabled:text-slate-500"
        :disabled="!canPressSend"
        :title="isCoolingDown ? `${cooldownSeconds} soniyadan so‘ng` : 'Yuborish'"
      >
        <BaseSpinner
          v-if="props.isSending"
          size="sm"
        />
        <span
          v-else-if="isCoolingDown"
          class="text-xs font-semibold tabular-nums"
        >
          {{ cooldownSeconds }}
        </span>
        <AppIcon
          v-else
          name="send"
          :size="17"
        />
        <span class="sr-only">Yuborish</span>
      </button>
    </div>

    <div class="mt-1.5 flex h-4 items-center justify-between px-1 text-[11px]">
      <!--
        `aria-live` — chegara paytidagi rad javobi ekran o'quvchi uchun ham
        e'lon qilinsin: ilgari bu holat FAQAT o'chgan tugmadan bilinardi.
      -->
      <span
        aria-live="polite"
        :class="
          blockedAttempt
            ? 'font-medium text-amber-300'
            : isCoolingDown
              ? 'text-amber-400/90'
              : props.disabled
                ? 'text-slate-500'
                : 'text-slate-600'
        "
        v-text="hint"
      />

      <span
        v-if="remaining <= 100"
        class="tabular-nums"
        :class="remaining <= 0 ? 'text-rose-400' : 'text-slate-500'"
      >
        {{ remaining }}
      </span>
    </div>
  </form>
</template>
