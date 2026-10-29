<script setup lang="ts">
/**
 * Arabcha bo'lak (2026-10-29): `<bdi lang="ar" dir="rtl">` + Amiri.
 *
 * ★ `bdi` — aralash (o'zbekcha + arabcha) qatorda yo'nalish buzilmasin:
 *   arabcha bo'lak atrofidagi lotin matnni "tortib ketmaydi".
 */
withDefaults(defineProps<{ text: string | null | undefined; size?: 'sm' | 'md' | 'lg' | 'xl' | 'xxl' }>(), {
  size: 'md',
})
</script>

<template>
  <bdi
    lang="ar"
    dir="rtl"
    class="font-arabic leading-[1.9]"
    :class="{
      'text-lg': size === 'sm',
      'text-[22px]': size === 'md',
      'text-[26px]': size === 'lg',
      'text-[34px]': size === 'xl',
      'text-[64px] leading-[1.3]': size === 'xxl',
    }"
  >{{ text }}</bdi>
</template>
