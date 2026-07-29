<script setup lang="ts">
import { computed } from 'vue'

import { roleLabel, roleTone } from '@/entities/user'
import type { PresenceEntry } from '@/shared/types'
import { AppIcon, BaseAvatar, BaseBadge } from '@/shared/ui'

const props = defineProps<{
  participants: readonly PresenceEntry[]
  totalCount: number
  currentUserId: number | null
}>()

/**
 * DOM cheklovi: 200 kishilik darsda hammasini chizish shart emas — ro'yxat
 * baribir ekranga sig'maydi. Birinchi 50 tasi (ustoz/kurator eng tepada)
 * ko'rsatiladi, qolgani "+N" bo'lib qoladi.
 */
const VISIBLE_LIMIT = 50

const visible = computed(() => props.participants.slice(0, VISIBLE_LIMIT))
const hiddenCount = computed(() => Math.max(0, props.totalCount - visible.value.length))
</script>

<template>
  <div class="scrollbar-slim flex-1 overflow-y-auto py-2">
    <ul class="space-y-0.5 px-2">
      <li
        v-for="participant in visible"
        :key="participant.userId"
        class="flex items-center gap-2.5 rounded-lg px-2 py-1.5 transition-colors hover:bg-white/5"
      >
        <BaseAvatar :name="participant.displayName" size="sm" :ring="participant.handRaised" />
        <div class="min-w-0 flex-1">
          <p class="truncate text-sm text-slate-200">
            {{ participant.displayName }}
            <span v-if="participant.userId === props.currentUserId" class="text-slate-500">(siz)</span>
          </p>
        </div>
        <AppIcon v-if="participant.handRaised" name="hand" :size="15" class="text-amber-400" />
        <BaseBadge :tone="roleTone(participant.role)" size="xs">
          {{ roleLabel(participant.role) }}
        </BaseBadge>
      </li>
    </ul>

    <p v-if="hiddenCount > 0" class="px-4 py-3 text-center text-xs text-slate-500">
      yana {{ hiddenCount }} ta ishtirokchi
    </p>

    <p v-if="props.participants.length === 0" class="px-4 py-8 text-center text-sm text-slate-500">
      Ishtirokchilar ro‘yxati yuklanmoqda…
    </p>
  </div>
</template>
