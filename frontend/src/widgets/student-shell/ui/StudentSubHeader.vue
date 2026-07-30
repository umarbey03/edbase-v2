<script setup lang="ts">
import { AppIcon } from '@/shared/ui'

/**
 * "O'quv" tabining ichki sahifalari uchun sarlavha (vazifalar, testlar).
 *
 * Orqaga tugmasi ANIQ marshrutga olib boradi, `router.back()` ga EMAS:
 * o'quvchi bu sahifaga tashqi havoladan ham kirishi mumkin, bunda tarix bo'sh
 * bo'lib "orqaga" ilovadan chiqarib yuborardi.
 */
defineProps<{ title: string; subtitle?: string }>()
</script>

<template>
  <header class="mb-4 mt-2 flex items-center gap-3">
    <RouterLink
      :to="{ name: 'student-learn' }"
      class="tap-target flex shrink-0 items-center justify-center rounded-[11px] border border-line bg-ink-800 text-slate-100 transition-transform active:scale-90"
      aria-label="O‘quv bo‘limiga qaytish"
    >
      <AppIcon
        name="arrow-left"
        :size="18"
      />
    </RouterLink>
    <div class="min-w-0">
      <h1
        class="truncate text-[17px] font-extrabold leading-tight"
        v-text="title"
      />
      <p
        v-if="subtitle !== undefined && subtitle.length > 0"
        class="mt-0.5 truncate text-[12.5px] text-slate-400"
        v-text="subtitle"
      />
    </div>
  </header>
</template>
