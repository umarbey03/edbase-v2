<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchAttritionReasonCatalogue } from '@/entities/attrition'
import { BaseField } from '@/shared/ui'

/**
 * TO'KILISH SABABI — TASNIF TANLASH (2026-08-18).
 *
 * ★ NIMA UCHUN ALOHIDA KOMPONENT: aynan bir xil tanlov UCH oynada kerak
 * (chiqarish, muzlatish, ko'chirish). Uch joyda takrorlansa, ro'yxat
 * manbai yoki izoh matni biriga qo'shilib, ikkinchisiga qo'shilmay
 * qolardi.
 *
 * ★ FAQAT FAOL SABABLAR: arxivlangani yangi yozuvda tanlanmaydi (server
 * ham 409 qaytaradi).
 *
 * ★ TASNIF ERKIN MATNNI ALMASHTIRMAYDI: bu — hisobotdagi foiz uchun
 * TASNIF, matn esa shu holatning tafsiloti. Shuning uchun ikkalasi ham
 * oynada qoladi.
 */
const model = defineModel<number | null>({ required: true })

const props = withDefaults(defineProps<{ open?: boolean }>(), { open: true })

const reasonsQuery = useQuery({
  queryKey: ['attrition-reasons', 'active'],
  queryFn: ({ signal }) => fetchAttritionReasonCatalogue(true, { signal }),
  enabled: computed(() => props.open),
})

const reasons = computed(() => reasonsQuery.data.value ?? [])

/** `<select>` bo'sh qiymat sifatida `''` beradi, model esa `null` kutadi. */
const selected = computed({
  get: () => model.value ?? '',
  set: (value: number | '') => {
    model.value = value === '' ? null : Number(value)
  },
})
</script>

<template>
  <BaseField
    label="Sabab turi"
    hint="Hisobotdagi foizlar shu tasnif bo‘yicha hisoblanadi."
  >
    <select
      v-model="selected"
      class="zn-input"
    >
      <option value="">
        — Tanlanmagan —
      </option>
      <option
        v-for="reason in reasons"
        :key="reason.id"
        :value="reason.id"
      >
        {{ reason.label }}
      </option>
    </select>
  </BaseField>
</template>
