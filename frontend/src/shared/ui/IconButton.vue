<script setup lang="ts">
import { computed } from 'vue'

import AppIcon from './AppIcon.vue'
import BaseSpinner from './BaseSpinner.vue'
import type { IconName } from './icon-names'

/**
 * IKONKALI AMAL TUGMASI.
 *
 * Talab: *"har bir o'quvchi bo'yicha actions buttonlar icon ko'rinishida
 * bo'lgani ma'qul"* — jadval qatorida 4-5 ta matnli tugma qatorni cho'zib
 * yuboradi va telefonda umuman sig'maydi.
 *
 * 🔴 `label` MAJBURIY PROP: ikonkali tugmada matn yo'q, ya'ni `aria-label`
 * bo'lmasa screen reader uni "button" deb o'qiydi — foydalanuvchi qatordagi
 * besh tugmani bir-biridan ajrata olmaydi. Shu bilan birga `title` ham
 * beriladi: sichqoncha bilan ishlaydigan xodim ikonka ma'nosini hoverda
 * biladi (eski panelda tugmalar matnli edi, o'rganish narxi shu bilan
 * qoplanadi).
 *
 * TEGINISH MAYDONI: ko'rinadigan o'lcham 36px (`md`) / 30px (`sm`), lekin
 * `tap-expand` ko'rinmas `::after` bilan bosiladigan maydonni kengaytiradi —
 * WCAG 2.5.5 talab qiladigan 44px shu yo'l bilan olinadi, tugma esa jadval
 * qatorini bo'ttirmaydi.
 *
 * 🔴 QATOR ICHIDA `gap-3` (12px) DAN KICHIK ORALIQ QO'YMANG. `tap-expand`
 * maydonni har tomondan 6px kengaytiradi, ya'ni oraliq 12px dan kichik bo'lsa
 * qo'shni tugmalarning KO'RINMAS maydonlari ustma-ust tushadi va chetga
 * bosilgan barmoq YONIDAGI tugmani ishga soladi ("Tahrirlash" o'rniga
 * "O'chirish"). Bu ikonkali tugmalar qatorida eng xavfli xato.
 */
type IconButtonTone = 'neutral' | 'brand' | 'danger' | 'success' | 'warning'
type IconButtonSize = 'sm' | 'md'

const props = withDefaults(
  defineProps<{
    /** Ikonka nomi (`AppIcon` to'plamidan). */
    icon: IconName
    /** MAJBURIY: `title` + `aria-label` ga ketadi. */
    label: string
    tone?: IconButtonTone
    size?: IconButtonSize
    loading?: boolean
    disabled?: boolean
    type?: 'button' | 'submit' | 'reset'
    /** Faol/tanlangan holat (masalan "yoqilgan" filtr yoki ochiq panel). */
    active?: boolean
  }>(),
  {
    tone: 'neutral',
    size: 'md',
    loading: false,
    disabled: false,
    type: 'button',
    active: false,
  },
)

/*
  Fon STANDART HOLATDA SHAFFOF: jadval qatorida yonma-yon turgan 5 ta ikonka
  fonli bo'lsa "tugmalar devori" bo'lib ko'rinadi va qatordagi ma'lumotdan
  diqqatni tortadi. Rang faqat hover/fokusda paydo bo'ladi.
*/
const TONES: Record<IconButtonTone, string> = {
  neutral: 'text-slate-400 hover:text-slate-100',
  brand: 'text-brand-500 hover:text-brand-600',
  danger: 'text-rose-500 hover:text-rose-600',
  success: 'text-green-500 hover:text-green-600',
  warning: 'text-amber-500 hover:text-amber-600',
}

const ACTIVE_TONES: Record<IconButtonTone, string> = {
  neutral: 'bg-ink-800 text-slate-100',
  brand: 'bg-brand-500/12 text-brand-500',
  danger: 'bg-rose-500/12 text-rose-500',
  success: 'bg-green-500/12 text-green-500',
  warning: 'bg-amber-500/12 text-amber-500',
}

const SIZES: Record<IconButtonSize, string> = {
  sm: 'size-[30px] rounded-lg icon-button-sm',
  md: 'size-9 rounded-lg',
}

const ICON_SIZES: Record<IconButtonSize, number> = { sm: 15, md: 17 }

const isDisabled = computed(() => props.disabled || props.loading)

const classes = computed(() => [
  'tap-expand inline-flex shrink-0 select-none items-center justify-center transition-colors duration-150',
  'disabled:cursor-not-allowed disabled:opacity-45 disabled:hover:bg-transparent',
  SIZES[props.size],
  props.active ? ACTIVE_TONES[props.tone] : `bg-transparent hover:bg-ink-800 ${TONES[props.tone]}`,
])
</script>

<template>
  <button
    :type="props.type"
    :class="classes"
    :title="props.label"
    :aria-label="props.label"
    :aria-busy="props.loading"
    :disabled="isDisabled"
  >
    <!--
      Spinner ikonka O'RNIGA chiqadi va tugma o'lchami qat'iy (`size-9` /
      `size-[30px]`) — yuklanish paytida qator "sakramaydi". Aynan shu sabab
      matn qo'shilmaydi ham.
    -->
    <BaseSpinner
      v-if="props.loading"
      size="xs"
      :label="props.label"
    />
    <AppIcon
      v-else
      :name="props.icon"
      :size="ICON_SIZES[props.size]"
    />
  </button>
</template>

<style scoped>
/*
  `tap-expand` maydonni har tomondan 6px kengaytiradi: 36 + 12 = 48px (`md`
  uchun WCAG 2.5.5 dagi 44px bajarildi). `sm` da esa 30 + 12 = 42px — 2px
  yetmaydi, shuning uchun VERTIKAL kengaytirish 7px ga oshiriladi
  (30 + 14 = 44px balandlik).

  Gorizontal 6px da QOLADI ataylab: qo'shni tugmalar bilan ustma-ust tushish
  xavfi (docblock'dagi `gap-3` qoidasi) `md` bilan bir xil bo'lib qolsin —
  ikki xil oraliq qoidasini eslab qolish mumkin emas.

  Utility'ning O'ZI o'zgartirilmaydi: uni `StudentAppBar` ham ishlatadi va u
  yerdagi chip balandligi boshqa.
*/
.icon-button-sm::after {
  inset: -7px -6px;
}
</style>
