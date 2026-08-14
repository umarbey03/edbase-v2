<script setup lang="ts">
import { AppIcon } from '@/shared/ui'

/**
 * YUBORISH XATOSI / OGOHLANTIRISHI — chatning YAGONA xabar berish joyi.
 *
 * ★ NEGA ALOHIDA KOMPONENT (2026-08-13, R28): guruh suhbatida bu yopiladigan
 * sariq ogohlantirish edi (429, ruxsat xatosi, ulanish), ikkala shaxsiy
 * chatda esa yopilmaydigan qizil bir qator matn. Ya'ni bir xil vaziyat
 * ("xabaringiz ketmadi") uch ekranda uch xil ko'rinardi — o'quvchi chati
 * qoidasi esa hammasi uchun bitta bo'lishi kerak.
 *
 * ★ YOPISH TUGMASI SHART: xabar ekranda abadiy osilib qolmasin. Shaxsiy
 * chatda yozilgan matn maydonda QOLADI (u faqat muvaffaqiyatda tozalanadi),
 * ya'ni ogohlantirish — ma'lumot, harakat emas.
 *
 * ★ `role="alert"` ILDIZDA: skrinrider matnni paydo bo'lishi bilan o'qiydi.
 */
defineProps<{ text: string }>()

const emit = defineEmits<{ dismiss: [] }>()
</script>

<template>
  <div
    class="flex shrink-0 items-start gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-2"
    role="alert"
  >
    <p
      class="min-w-0 flex-1 text-xs leading-relaxed text-amber-200"
      v-text="text"
    />
    <button
      type="button"
      class="shrink-0 text-amber-300/70 transition-colors hover:text-amber-200"
      aria-label="Yopish"
      @click="emit('dismiss')"
    >
      <AppIcon
        name="close"
        :size="14"
      />
    </button>
  </div>
</template>
