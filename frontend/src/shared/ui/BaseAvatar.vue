<script setup lang="ts">
import { computed } from 'vue'

import { colorIndex, initials } from '@/shared/lib/text'

type AvatarSize = 'xs' | 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    name: string
    size?: AvatarSize
    /** Gapirayotgan/qo'l ko'targan ishtirokchini ajratib ko'rsatish uchun halqa. */
    ring?: boolean
  }>(),
  { size: 'sm', ring: false },
)

const SIZES: Record<AvatarSize, string> = {
  xs: 'size-6 text-[10px]',
  sm: 'size-8 text-xs',
  md: 'size-10 text-sm',
  lg: 'size-14 text-lg',
}

/*
  Barqaror palitra — bir xil ism DOIM bir xil rangda ko'rinadi (`colorIndex`
  ismdan barqaror hash oladi; server hech narsa bermaydi, ya'ni ro'yxat
  qayta yuklansa ham rang o'zgarmaydi).

  ★ ILGARI SHAFFOF TINT + TO'Q HARF EDI (`bg-rose-500/20 text-rose-200`):
  qorong'i fonda bu yumshoq ko'rinardi, YORUG' fonda esa 20% tint oq
  kartochkada deyarli yo'qoladi va avatar "bo'sh doira" bo'lib qolardi.
  Ekran suratlaridagi ko'rinish — TO'Q PASTEL TO'LDIRISH + OQ HARF.

  Har bir `-400` daraja oq matn bilan >= 4.7:1 beradi (eng past — sky
  4.74:1), ya'ni 10px bosh harf ham o'qiladi.
*/
const PALETTE = [
  'bg-indigo-400 text-white',
  'bg-emerald-400 text-white',
  'bg-amber-400 text-white',
  'bg-rose-400 text-white',
  'bg-sky-400 text-white',
  'bg-violet-400 text-white',
  'bg-teal-400 text-white',
  'bg-orange-400 text-white',
] as const

const label = computed(() => initials(props.name))
const tone = computed(() => PALETTE[colorIndex(props.name, PALETTE.length)] ?? PALETTE[0])
</script>

<template>
  <span
    class="inline-flex shrink-0 select-none items-center justify-center rounded-full font-semibold"
    :class="[SIZES[props.size], tone, props.ring ? 'ring-2 ring-brand-400/70' : '']"
    :title="props.name"
    aria-hidden="true"
    v-text="label"
  />
</template>
