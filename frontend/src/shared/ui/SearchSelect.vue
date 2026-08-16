<script setup lang="ts">
import { computed, ref } from 'vue'

import AppIcon from './AppIcon.vue'
import BaseSpinner from './BaseSpinner.vue'

/**
 * QIDIRIB TANLASH (combobox) — bitta maydon, ikkitasi EMAS.
 *
 * ★★ NEGA BU KOMPONENT PAYDO BO'LDI (loyiha egasi, 2026-08-15: "guruhni
 * qidirish search ishlamayapti"): eski naqsh IKKI ALOHIDA elementdan
 * iborat edi — matn maydoni (faqat serverga so'rov yuborardi) va undan
 * PASTDA/YONDA turgan `<select>` (natijani ko'rsatardi). Ma'lumot oqimi
 * TO'G'RI ishlardi, lekin foydalanuvchi matn maydoniga yozganda EKRANDA
 * hech narsa o'zgarmasdi — natijani ko'rish uchun IKKINCHI elementni
 * qo'lda ochish kerak edi. Bu "qidiruv ishlamayapti" degan taassurot
 * qoldiradi, garchi server javob berayotgan bo'lsa ham.
 *
 * Bu komponent ikkalasini BITTA maydonga birlashtiradi: yozasiz — pastda
 * ro'yxat DARHOL ko'rinadi, bosasiz — tanlanadi. Ma'lumot olish (qidiruv
 * matnini serverga yuborish, natijani keshlash) ChAQIRUVCHIDA qoladi —
 * bu komponent FAQAT taqdimot qatlami: `search`/`options` ikkalasi ham
 * tashqaridan keladi (`v-model:search` + `options` prop), ya'ni mavjud
 * `useDebounced`/`useQuery` liniyasi TEGILMAYDI.
 */
export interface SearchSelectOption {
  id: number
  name: string
}

const props = withDefaults(
  defineProps<{
    /** Tanlangan variant. `null` — hech narsa tanlanmagan. */
    modelValue: SearchSelectOption | null
    /** Qidiruv maydonidagi XOM matn (kechiktirish/so'rov chaqiruvchida). */
    search: string
    /** Hozir ko'rsatiladigan variantlar (chaqiruvchi allaqachon qidiruvga mos filtrlagan). */
    options: readonly SearchSelectOption[]
    placeholder?: string
    /** Ro'yxat boshidagi "tozalash" qatori matni (masalan "Barcha guruhlar"). */
    emptyLabel?: string
    /** So'rov ketayotganini ko'rsatadi (bo'sh ro'yxat "topilmadi" bilan ADASHTIRILMASIN). */
    loading?: boolean
    /**
     * Kirish maydonining `aria-label`i.
     *
     * ★ NEGA ALOHIDA PROP, `aria-label` ATRIBUTI EMAS: Vue `aria-*`/`data-*`
     * atributlarni har doim FALLTHROUGH sifatida ko'radi (mos `camelCase`
     * prop bo'lsa ham) va ular ILDIZ elementga tushadi — bu komponentning
     * ildizi esa butun blokni o'raydigan `<div>`, tashqi `<input>` emas.
     * Chaqiruvchi `aria-label="..."` yozsa, u INPUT emas, TASHQI div'ga
     * yopishib qolardi va ekran o'qigich hamon nomsiz maydonni o'qirdi.
     */
    label?: string
  }>(),
  { placeholder: 'Qidirish', emptyLabel: 'Hammasi', loading: false, label: '' },
)

const emit = defineEmits<{
  'update:modelValue': [value: SearchSelectOption | null]
  'update:search': [value: string]
}>()

/*
  ★ FOKUS HOLATI — nima ko'rsatilishini hal qiladi:
  fokusda XOM qidiruv matni (`search`) ko'rinadi (foydalanuvchi yozganini
  ko'rishi kerak), fokusdan tashqarida esa TANLANGAN nom (agar bor bo'lsa)
  — aks holda tanlangandan keyin ham eski qidiruv so'zi qolib, "nima
  tanlandi?" degan chalkashlik qoldirardi.
*/
const focused = ref(false)
const open = ref(false)

const displayValue = computed(() => {
  if (focused.value) return props.search
  return props.modelValue !== null ? props.modelValue.name : props.search
})

function onFocus(): void {
  focused.value = true
  open.value = true
}

/*
  ★ YOPISH KECHIKTIRILADI: ro'yxatdagi tugma bosilganda ham `blur` ishga
  tushadi (fokus INPUT'dan chiqadi). Agar ro'yxat DARHOL yopilsa, tugmaning
  `click` hodisasi hali yetib kelmasdan DOM'dan yo'qolib qoladi va tanlov
  UMUMAN ishlamaydi. 150ms — `click` uchun yetarli, foydalanuvchi buni
  sezmaydi.
*/
function onBlur(): void {
  focused.value = false
  window.setTimeout(() => {
    open.value = false
  }, 150)
}

function onInput(event: Event): void {
  emit('update:search', (event.target as HTMLInputElement).value)
  open.value = true
}

function select(option: SearchSelectOption | null): void {
  emit('update:modelValue', option)
  emit('update:search', '')
  open.value = false
}

function clear(): void {
  select(null)
}
</script>

<template>
  <div class="relative">
    <div class="relative">
      <input
        type="text"
        class="zn-input pr-8"
        :class="{ 'pl-8': $slots.icon }"
        :value="displayValue"
        :placeholder="placeholder"
        :aria-label="label.length > 0 ? label : placeholder"
        role="combobox"
        aria-autocomplete="list"
        :aria-expanded="open"
        @focus="onFocus"
        @blur="onBlur"
        @input="onInput"
        @keydown.escape="open = false"
      >

      <span
        v-if="$slots.icon"
        class="pointer-events-none absolute left-2.5 top-1/2 -translate-y-1/2 text-slate-500"
      >
        <slot name="icon" />
      </span>

      <BaseSpinner
        v-if="loading"
        class="absolute right-2.5 top-1/2 -translate-y-1/2"
        size="xs"
      />
      <button
        v-else-if="modelValue !== null"
        type="button"
        class="absolute right-2 top-1/2 flex -translate-y-1/2 items-center justify-center rounded p-1 text-slate-500 transition-colors hover:bg-ink-800 hover:text-slate-200"
        aria-label="Tanlovni bekor qilish"
        @mousedown.prevent="clear"
      >
        <AppIcon
          name="close"
          :size="12"
        />
      </button>
    </div>

    <div
      v-if="open"
      class="scrollbar-slim absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-lg border border-line bg-ink-900 shadow-lg"
      role="listbox"
    >
      <button
        type="button"
        class="block w-full px-3 py-2 text-left text-xs transition-colors"
        :class="modelValue === null ? 'text-brand-400' : 'text-slate-300 hover:bg-ink-800'"
        role="option"
        :aria-selected="modelValue === null"
        @mousedown.prevent="select(null)"
      >
        {{ emptyLabel }}
      </button>
      <button
        v-for="option in options"
        :key="option.id"
        type="button"
        class="block w-full truncate px-3 py-2 text-left text-xs transition-colors"
        :class="modelValue?.id === option.id ? 'text-brand-400' : 'text-slate-300 hover:bg-ink-800'"
        role="option"
        :aria-selected="modelValue?.id === option.id"
        @mousedown.prevent="select(option)"
        v-text="option.name"
      />
      <p
        v-if="!loading && options.length === 0"
        class="px-3 py-2 text-xs text-dim"
      >
        Hech narsa topilmadi.
      </p>
    </div>
  </div>
</template>
