<script setup lang="ts">
import type { SettingGroupDto } from '@/shared/types'
import { BaseCard } from '@/shared/ui'

import SettingRow from './SettingRow.vue'

/**
 * Bitta bo'lim (Umumiy, Moliya, Telegram, LiveKit, Ombor, Xavfsizlik).
 *
 * Sarlavha va tavsif SERVERDAN olinadi (`group.name`, `group.description`) —
 * kodda o'zbekcha nomlar ro'yxatini yuritsak, backend yangi bo'lim
 * qo'shganda u ekranda ingliz enum nomi bilan chiqib qolardi.
 *
 * Qatorlar `divide-y` bilan ajratilgan: har qatorda o'z tugmasi bor va
 * chegara "bu maydonni saqlash qo'shnisiga tegmaydi" degan chegarani ko'z
 * bilan ko'rsatib turadi.
 */
const props = defineProps<{ group: SettingGroupDto }>()
</script>

<template>
  <BaseCard
    :title="props.group.name"
    :subtitle="props.group.description"
    flush
  >
    <div class="divide-y divide-line px-3.5 sm:px-5">
      <SettingRow
        v-for="item in props.group.items"
        :key="item.key"
        :setting="item"
      />
    </div>
  </BaseCard>
</template>
