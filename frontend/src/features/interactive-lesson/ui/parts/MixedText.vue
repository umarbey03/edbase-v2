<script setup lang="ts">
import { computed } from 'vue'

/**
 * O'zbekcha matn ichidagi arabcha bo'laklar (2026-10-29): har arabcha qism
 * alohida `<bdi lang="ar" dir="rtl">` ga o'raladi, qolgani oddiy matn.
 * Shu tufayli "بُ qanday o'qiladi?" kabi qator teskari aylanib ketmaydi.
 */
const props = defineProps<{ text: string | null | undefined }>()

const ARABIC_RUN = /([؀-ۿݐ-ݿﭐ-﷿ﹰ-ﻼ][؀-ۿݐ-ݿﭐ-﷿ﹰ-ﻼ\s،؛؟.!]*)/u

const parts = computed(() =>
  (props.text ?? '')
    .split(ARABIC_RUN)
    .filter((p) => p.length > 0)
    // Arabcha bo'lak oxiridagi bo'shliq tashqariga chiqariladi — aks holda RTL
    // ichida u chap tomonga o'tib, keyingi o'zbekcha so'zga yopishib qoladi.
    .flatMap((p) => {
      if (!ARABIC_RUN.test(p)) return [{ text: p, ar: false }]
      const body = p.trimEnd()
      return body.length === p.length
        ? [{ text: p, ar: true }]
        : [{ text: body, ar: true }, { text: p.slice(body.length), ar: false }]
    }),
)
</script>

<template>
  <span>
    <template
      v-for="(p, i) in parts"
      :key="i"
    >
      <bdi
        v-if="p.ar"
        lang="ar"
        dir="rtl"
        class="font-arabic text-[1.3em] leading-[1.6]"
      >{{ p.text }}</bdi>
      <template v-else>{{ p.text }}</template>
    </template>
  </span>
</template>
