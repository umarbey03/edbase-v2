<script setup lang="ts">
import AppIcon from './AppIcon.vue'
import BaseButton from './BaseButton.vue'
import EmptyState from './EmptyState.vue'
import type { IconName } from './icon-names'

/**
 * Yuklanmoqda / xato / bo'sh holatlarining YAGONA ko'rinishi.
 *
 * Har sahifada shu uch shoxni qayta yozish — dizayn bir joyda skeleton,
 * boshqa joyda spinner bo'lib ketishiga olib keladi. Shu sababli bitta
 * komponentga yig'ilgan.
 */
withDefaults(
  defineProps<{
    pending: boolean
    /** `null` bo'lsa xato yo'q. */
    error: string | null
    empty: boolean
    emptyTitle?: string
    emptyText?: string
    emptyIcon?: IconName
    /** Skeleton qatorlari soni — ro'yxatning taxminiy uzunligiga qarab. */
    skeletonRows?: number
    retrying?: boolean
  }>(),
  {
    emptyTitle: 'Ma’lumot topilmadi',
    emptyText: '',
    emptyIcon: 'calendar',
    skeletonRows: 3,
    retrying: false,
  },
)

const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <div
    v-if="pending"
    class="space-y-3"
  >
    <div
      v-for="index in skeletonRows"
      :key="index"
      class="h-20 animate-pulse rounded-xl border border-line bg-ink-900"
    />
  </div>

  <div
    v-else-if="error !== null"
    class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center"
    role="alert"
  >
    <p
      class="text-sm text-rose-200"
      v-text="error"
    />
    <BaseButton
      class="mt-4"
      size="sm"
      variant="secondary"
      :loading="retrying"
      @click="emit('retry')"
    >
      <template #icon>
        <AppIcon
          name="refresh"
          :size="14"
        />
      </template>
      Qayta urinish
    </BaseButton>
  </div>

  <EmptyState
    v-else-if="empty"
    :icon="emptyIcon"
    :title="emptyTitle"
    :text="emptyText"
  >
    <!--
      Slot SHARTLI uzatiladi: aks holda `EmptyState` da `$slots.default` doim
      to'ldirilgan hisoblanib, bo'sh tugma konteyneri chizilardi.
    -->
    <template
      v-if="$slots['empty-action']"
      #default
    >
      <slot name="empty-action" />
    </template>
  </EmptyState>

  <slot v-else />
</template>
