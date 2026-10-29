<script setup lang="ts">
import AppIcon from './AppIcon.vue'

/**
 * Yuborishdan oldingi yumshoq ogohlantirish (`shared/lib/sensitive-words.ts`).
 * Ayblamaydi — nima qilish kerakligini aytadi: so'zni olib tashlash yoki
 * almashtirish. `role="alert"` — ekran o'qigich ham darhol aytadi.
 */
defineProps<{ words: string[] }>()
</script>

<template>
  <p
    class="flex items-start gap-2 rounded-lg bg-amber-500/10 px-3 py-2 text-xs leading-relaxed text-amber-200 ring-1 ring-inset ring-amber-500/30"
    role="alert"
  >
    <AppIcon
      name="alert"
      :size="15"
      class="mt-px shrink-0"
    />
    <span>
      Bu so‘zni olib tashlang yoki boshqa so‘z bilan almashtiring:
      <b
        v-for="(word, index) in words"
        :key="word"
      >«{{ word }}»{{ index < words.length - 1 ? ', ' : '' }}</b>.
      Platformada diniy atamalar ishlatilmaydi.
    </span>
  </p>
</template>
