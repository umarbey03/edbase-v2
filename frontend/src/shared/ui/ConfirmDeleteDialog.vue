<script setup lang="ts">
import BaseButton from './BaseButton.vue'
import BaseModal from './BaseModal.vue'

/**
 * O'chirishni tasdiqlash.
 *
 * NEGA `window.confirm` EMAS: server 409 bilan SABABNI matn qilib qaytaradi
 * ("bu kursga 3 ta guruh biriktirilgan...", "unga 12 ta topshirilgan vazifa
 * bog'langan..."). Brauzer oynasi yopilgach o'sha matnni ko'rsatadigan joy
 * qolmaydi. Bu oyna xato kelganda OCHIQ turadi va sababni aynan server
 * so'zlari bilan ko'rsatadi — foydalanuvchi nima qilishini biladi.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    title: string
    message: string
    pending: boolean
    /** Server xatosi (asosan 409). `null` bo'lsa xato yo'q. */
    error: string | null
    /**
     * Tasdiqlash tugmasi matni. Standart — "O'chirish", lekin hamma amal ham
     * o'chirish emas: guruhdan chiqarish YUMSHOQ (yozuv qoladi, holati
     * `Stopped` bo'ladi) va uni "o'chirish" deb atash foydalanuvchini
     * chalg'itadi — u ma'lumot yo'qoladi deb o'ylaydi.
     */
    confirmLabel?: string
  }>(),
  { confirmLabel: 'O‘chirish' },
)

const emit = defineEmits<{ close: []; confirm: [] }>()
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="props.title"
    @close="emit('close')"
  >
    <p
      class="text-sm text-slate-300"
      v-text="props.message"
    />

    <div
      v-if="props.error !== null"
      class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3.5 text-xs leading-relaxed text-rose-200"
      role="alert"
      v-text="props.error"
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
        :loading="props.pending"
        @click="emit('confirm')"
      >
        {{ props.confirmLabel }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
