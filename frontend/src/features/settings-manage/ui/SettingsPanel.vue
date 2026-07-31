<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchSettings, SETTINGS_QUERY_KEY } from '@/entities/setting'
import { toUserMessage } from '@/shared/api'
import { DataStatus, PageHeader } from '@/shared/ui'

import SettingsGroupCard from './SettingsGroupCard.vue'

/**
 * TIZIM SOZLAMALARI paneli — butun mantiq shu yerda, sahifa faqat chaqiradi.
 *
 * ★ Bo'limlar SERVER TARTIBIDA chiziladi: `GET /settings` ularni
 * Umumiy -> Moliya -> Telegram -> LiveKit -> Ombor -> Xavfsizlik ketma-ketligida
 * qaytaradi, ya'ni "kundalik" sozlamalar tepada, infratuzilma sirlari pastda.
 * Mijozda qayta tartiblasak, backend bo'lim qo'shganda u kutilmagan joyda
 * paydo bo'lardi.
 *
 * ★ `staleTime` global sozlamadan (30s) meros: sozlamalar tez-tez
 * o'zgarmaydi, lekin saqlashdan keyin kesh JAVOB BILAN nuqtali yangilanadi
 * (`SettingRow` -> `replaceSettingInPage`), shuning uchun bu yerda
 * `invalidate` yoki qo'l bilan tozalash KERAK EMAS.
 *
 * ★ `onBeforeUnmount` yozilmagan: bu komponentda taymer ham, obuna ham yo'q
 * (ular `SettingRow` da va o'sha yerda tozalanadi), so'rovni esa TanStack
 * Query `gcTime` bo'yicha o'zi yig'ishtiradi. Bo'sh hook faqat chalg'itardi.
 */
const settingsQuery = useQuery({
  queryKey: SETTINGS_QUERY_KEY,
  queryFn: ({ signal }) => fetchSettings({ signal }),
})

const groups = computed(() => settingsQuery.data.value?.groups ?? [])

const errorMessage = computed(() =>
  settingsQuery.error.value !== null ? toUserMessage(settingsQuery.error.value) : null,
)

const settingsCount = computed(() =>
  groups.value.reduce((total, group) => total + group.items.length, 0),
)

const subtitle = computed(() =>
  settingsCount.value === 0
    ? 'Platformaning muhit sozlamalari.'
    : `${groups.value.length} ta bo‘limda ${settingsCount.value} ta sozlama. Har biri alohida saqlanadi.`,
)
</script>

<template>
  <div>
    <PageHeader
      title="Tizim sozlamalari"
      :subtitle="subtitle"
    />

    <DataStatus
      :pending="settingsQuery.isPending.value"
      :error="errorMessage"
      :empty="groups.length === 0"
      :retrying="settingsQuery.isFetching.value"
      empty-icon="grid"
      empty-title="Sozlama topilmadi"
      empty-text="Server hech qanday sozlama qaytarmadi."
      :skeleton-rows="6"
      @retry="settingsQuery.refetch()"
    >
      <div class="space-y-4">
        <SettingsGroupCard
          v-for="group in groups"
          :key="group.group"
          :group="group"
        />
      </div>
    </DataStatus>
  </div>
</template>
