<script setup lang="ts">
import { computed } from 'vue'

import BaseSpinner from './BaseSpinner.vue'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'success' | 'warning'
type ButtonSize = 'sm' | 'md' | 'lg'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: ButtonSize
    type?: 'button' | 'submit' | 'reset'
    loading?: boolean
    disabled?: boolean
    block?: boolean
  }>(),
  {
    variant: 'primary',
    size: 'md',
    type: 'button',
    loading: false,
    disabled: false,
    block: false,
  },
)

/*
  Yorug' (iOS uslubidagi) tugma: indigo `primary`, oq konturli `secondary`,
  12px radius, yengil soya.

  `primary` da `text-white` EMAS, `text-on-brand`: brend fonidagi matn rangi
  tokenda turadi, ya'ni brend rangi almashsa tugma o'z-o'zidan to'g'ri
  qoladi (hozir ikkisi ham oq).

  🔴 `secondary` fon `ink-900` (OQ), `ink-800` EMAS: yorug' temada `ink-800`
  (#f2f4f9) kartochkadan deyarli ajralmaydi — tugma "yo'q"dek ko'rinardi.
  Oq fon + `line-strong` kontur ekran suratidagi naqsh.

  🔴 `danger`/`success`/`warning` da 500 EMAS, 400 asos:
  yangi shkala teskari (`style.css` boshidagi izoh) va 500 — TINT darajasi.
  `bg-green-500` (#0a8055) ustida oq matn 4.96:1 bo'lardi, `bg-red-500`
  (#d92d20) ustida 4.83:1 — ikkisi ham chegarada. 400 esa 5.7:1 / 6.6:1.
  Hover 500 ga (yorug'lashadi — "javob berdi"), active 600 ga (to'qlashadi
  — "bosildi") o'tadi.

  🔴 `warning` (amber) — YANGI (2026-08-11), `ConfirmDialog` uchun.

  ⚠️ CHEKINISH — hover/active YO'NALISHI TOPSHIRIQDAGIDAN BOSHQA.
  Topshiriqda "asos `amber-400`, hover `amber-500`, active `amber-600`"
  deyilgan (`danger` naqshi bo'yicha). Asos to'g'ri: `amber-400` (#b54708)
  to'q "amber-jigarrang", oq matn ustida 5.43:1. Lekin `amber-500`
  (#f79009) — TO'YINGAN SARIQ va oq matn u ustida 2.42:1 beradi, ya'ni
  sichqoncha ustiga kelganda tugma YOZUVI YO'QOLARDI. Sariq eng yorug' rang
  va bu fizika, tanlov emas (`style.css` dagi amber izohi ham shu haqda:
  aynan shuning uchun `amber-400` to'q jigarrang qilingan).

  Ikki muqobil ko'rildi:
   a) hoverda matnni `slate-100` ga o'tkazish (`amber-500` ustida 7.13:1) —
      ishlaydi, lekin tugma bosilganda YOZUV RANGI sakraydi, bu esa
      "boshqa tugma bo'lib qoldi" degan taassurot beradi;
   b) hover/active'ni TO'QLASHTIRISH (`amber-300` → `amber-200`), oq matn
      hamma holatda saqlanadi (7.52:1 / 9.4:1).

  (b) tanlandi, chunki bu YO'NALISH shu komponentda ALLAQACHON bor:
  `secondary` ham `ink-900` → hover `ink-800` → active `ink-750`, ya'ni
  yorug' sirtdagi tugma hoverda TO'QLASHADI. Ya'ni chekinish tizimga
  qarshi emas, tizimning ikkinchi naqshiga mos.
*/
const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-brand-500 text-on-brand shadow-xs hover:bg-brand-600 active:bg-brand-700',
  secondary:
    'bg-ink-900 text-slate-100 border border-line-strong shadow-xs hover:bg-ink-800 active:bg-ink-750',
  ghost: 'bg-transparent text-slate-300 hover:bg-ink-800 active:bg-ink-750',
  danger: 'bg-red-400 text-white shadow-xs hover:bg-red-500 active:bg-red-600',
  success: 'bg-green-400 text-white shadow-xs hover:bg-green-500 active:bg-green-600',
  warning: 'bg-amber-400 text-white shadow-xs hover:bg-amber-300 active:bg-amber-200',
}

/*
  Balandliklar teginish nishoniga moslangan: `md`/`lg` >= 44px. `sm` faqat
  zich jadval qatorlarida ishlatiladi — u yerda qatorning o'zi barmoqqa
  yetarli maydon beradi, aks holda jadval o'qib bo'lmas darajada cho'ziladi.

  Radius `rounded-xl` = 0.75rem (iOS uslubi; ilgari `rounded-lg` = 0.5rem
  edi). O'quvchi ilovasida u 1rem ga o'sadi — `[data-theme='student']`
  `--radius-xl` ni qayta belgilaydi va eski Mini App aynan shunday
  yumshoq edi.
*/
const SIZES: Record<ButtonSize, string> = {
  sm: 'h-9 px-3 text-xs gap-1.5 rounded-xl',
  md: 'h-11 px-4 text-sm gap-2 rounded-xl',
  lg: 'h-12 px-5 text-base gap-2.5 rounded-xl',
}

const isDisabled = computed(() => props.disabled || props.loading)

const classes = computed(() => [
  'inline-flex select-none items-center justify-center font-semibold transition-colors duration-150',
  'disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none',
  VARIANTS[props.variant],
  SIZES[props.size],
  props.block ? 'w-full' : '',
])
</script>

<template>
  <button
    :type="props.type"
    :class="classes"
    :disabled="isDisabled"
  >
    <BaseSpinner
      v-if="props.loading"
      size="sm"
    />
    <slot
      v-else
      name="icon"
    />
    <slot />
  </button>
</template>
