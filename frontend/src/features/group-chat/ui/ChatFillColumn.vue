<script setup lang="ts">
import { ref } from 'vue'

import { useFillHeight } from '../model/useFillHeight'

/**
 * SUHBAT USTUNI — balandligi EKRANGA moslashadigan flex konteyner.
 *
 * ★ NIMA UCHUN BOR (2026-08-13, talab: *"chat writing part should be stuck
 * in its place"*): chat ekranida yozish paneli DOIM ko'rinib turishi kerak.
 * Buning yagona ishonchli yo'li — ustunning balandligini BIR MARTA
 * chegaralash va ichkarisini flex'ga hisoblatish: xabarlar ro'yxati
 * (`flex-auto`) qolgan joyni oladi, sarlavha va yozish paneli (`shrink-0`)
 * o'z balandligini o'zi belgilaydi. O'quvchi chati (`StudentChatPage`) shu
 * naqsh bilan allaqachon barqaror ishlaydi.
 *
 * ★ O'LCHOVNING O'ZI ENDI `useFillHeight` DA (2026-08-13, R28) — sababi
 * o'sha faylning izohida. Bu komponent o'lchovni FAQAT flex ustunga
 * bog'laydi; setka (`display:grid`) kerak bo'lgan joyda composable
 * to'g'ridan-to'g'ri chaqiriladi (`TeacherGroupChatsPage`).
 */
const props = withDefaults(
  defineProps<{
    /** Ustun ostida qoladigan bo'shliq (`useFillHeight` izohi). */
    gap?: number
    /** Past ekran uchun pol (`useFillHeight` izohi). */
    minHeight?: number
  }>(),
  { gap: 28, minHeight: 320 },
)

const root = ref<HTMLElement | null>(null)

const height = useFillHeight(root, {
  // Getter — prop kelajakda dinamik bo'lsa ham o'lchov yangi qiymatni oladi.
  gap: () => props.gap,
  minHeight: () => props.minHeight,
})
</script>

<template>
  <!--
    `min-h-0` — ichkaridagi skroll sohasi o'z mazmunidan KICHRAYA olishi
    uchun shart (flex bolaning avtomatik minimal o'lchami aks holda mazmun
    bo'yicha hisoblanadi va ro'yxat ustundan toshib ketardi).
  -->
  <div
    ref="root"
    class="flex min-h-0 flex-col"
    :style="{ height }"
  >
    <slot />
  </div>
</template>
