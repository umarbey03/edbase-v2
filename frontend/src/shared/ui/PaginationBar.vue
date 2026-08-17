<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import AppIcon from './AppIcon.vue'

/**
 * Sahifalash. Raqamli tugmalar ATAYLAB yo'q — 505 sahifali ro'yxatda
 * ular telefon ekraniga sig'maydi; oldinga/orqaga + "N / M" yetarli.
 *
 * ══════════════════════════════════════════════════════════════════════════
 * ★ SAHIFA HAJMI TANLAGICHI — IXTIYORIY (2026-08-17, `ManageUsersPage` talabi)
 *
 * `pageSizeOptions` BERILMASA (standart — 11 dan ortiq mavjud chaqiruvchi),
 * tanlagich UMUMAN CHIZILMAYDI va komponent bugungidek ishlayveradi. Faqat
 * shu ikkita prop/hodisani BIRGA berish kerak bo'lgan sahifa (masalan
 * "Foydalanuvchilar") yangi imkoniyatni yoqadi — qolgan hamma joy bir bayt
 * ham o'zgarmaydi.
 *
 * 🔴 `v-if="totalPages > 1"` GA `|| pageSizeOptions !== undefined` QO'SHILDI:
 * aks holda hajmni 100 taga o'zgartirib, natija BITTA sahifaga sig'ib
 * qolsa, butun panel (shu jumladan hajmni QAYTA pasaytirish tugmasi ham)
 * yo'qolib qolardi — foydalanuvchi tanlovni "orqaga" qaytara olmasdi.
 * ══════════════════════════════════════════════════════════════════════════
 */
const props = withDefaults(
  defineProps<{
    page: number
    totalPages: number
    total: number
    pageSize?: number
    /** Berilsa — hajm tanlagichi chiqadi. Oxirgi variant "Boshqa..." bo'lib ko'rinadi. */
    pageSizeOptions?: readonly number[]
  }>(),
  { pageSize: undefined, pageSizeOptions: undefined },
)

const emit = defineEmits<{ 'update:page': [page: number]; 'update:pageSize': [size: number] }>()

const canPrev = computed(() => props.page > 1)
const canNext = computed(() => props.page < props.totalPages)

function go(delta: number): void {
  const next = props.page + delta
  if (next < 1 || next > props.totalPages) return
  emit('update:page', next)
}

/** Eng katta ruxsat etilgan CUSTOM qiymat — bekorga 100 000 ta qator so'ralmasin. */
const MAX_CUSTOM_PAGE_SIZE = 500

const CUSTOM_VALUE = 'custom'

/**
 * `<select>` qiymati — yoki tayyor variantlardan biri, yoki `CUSTOM_VALUE`.
 * Joriy `pageSize` ro'yxatda YO'Q bo'lsa (custom kiritilgan yoki chaqiruvchi
 * boshqacha standart bergan), tanlagich avtomatik "Boshqa..." holatiga
 * o'tadi — aks holda ekranda noto'g'ri variant "tanlangan" bo'lib ko'rinardi.
 */
const isKnownOption = computed(
  () => props.pageSizeOptions?.some((option) => option === props.pageSize) ?? false,
)
const selectValue = computed(() => (isKnownOption.value ? String(props.pageSize) : CUSTOM_VALUE))

const showCustomInput = computed(() => !isKnownOption.value && props.pageSizeOptions !== undefined)
const customDraft = ref(props.pageSize !== undefined ? String(props.pageSize) : '')

watch(
  () => props.pageSize,
  (value) => {
    if (showCustomInput.value) customDraft.value = value !== undefined ? String(value) : ''
  },
)

function onSelectChange(event: Event): void {
  const value = (event.target as HTMLSelectElement).value

  if (value === CUSTOM_VALUE) {
    customDraft.value = props.pageSize !== undefined ? String(props.pageSize) : ''
    return
  }

  const parsed = Number(value)
  if (Number.isInteger(parsed) && parsed > 0) emit('update:pageSize', parsed)
}

function commitCustom(): void {
  const parsed = Number(customDraft.value)

  if (!Number.isInteger(parsed) || parsed <= 0) {
    customDraft.value = props.pageSize !== undefined ? String(props.pageSize) : ''
    return
  }

  emit('update:pageSize', Math.min(parsed, MAX_CUSTOM_PAGE_SIZE))
}
</script>

<template>
  <div
    v-if="props.totalPages > 1 || props.pageSizeOptions !== undefined"
    class="flex flex-wrap items-center justify-between gap-3 border-t border-line px-3.5 py-3 sm:px-5"
  >
    <div class="flex flex-wrap items-center gap-3">
      <p class="text-xs text-slate-400">
        Jami: <span
          class="font-semibold text-slate-200"
          v-text="props.total"
        />
      </p>

      <!-- SAHIFA HAJMI — faqat `pageSizeOptions` berilganda. -->
      <label
        v-if="props.pageSizeOptions !== undefined"
        class="flex items-center gap-1.5 text-xs text-slate-400"
      >
        Sahifada:
        <select
          class="zn-input h-8 w-auto min-w-0 py-0 text-xs"
          :value="selectValue"
          @change="onSelectChange"
        >
          <option
            v-for="option in props.pageSizeOptions"
            :key="option"
            :value="option"
          >
            {{ option }}
          </option>
          <option :value="CUSTOM_VALUE">
            Boshqa…
          </option>
        </select>

        <input
          v-if="showCustomInput"
          v-model="customDraft"
          type="number"
          inputmode="numeric"
          min="1"
          :max="MAX_CUSTOM_PAGE_SIZE"
          class="zn-input h-8 w-16 py-0 text-center text-xs tabular-nums"
          aria-label="Sahifadagi qatorlar soni (qo'lda)"
          @keydown.enter.prevent="commitCustom"
          @blur="commitCustom"
        >
      </label>
    </div>

    <div class="flex items-center gap-2">
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 text-slate-300 transition-colors hover:bg-ink-750 disabled:opacity-40"
        :disabled="!canPrev"
        title="Oldingi sahifa"
        @click="go(-1)"
      >
        <AppIcon
          name="arrow-left"
          :size="16"
        />
      </button>
      <span class="min-w-16 text-center text-xs tabular-nums text-slate-300">
        {{ props.page }} / {{ props.totalPages }}
      </span>
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 text-slate-300 transition-colors hover:bg-ink-750 disabled:opacity-40"
        :disabled="!canNext"
        title="Keyingi sahifa"
        @click="go(1)"
      >
        <AppIcon
          name="chevron-right"
          :size="16"
        />
      </button>
    </div>
  </div>
</template>
