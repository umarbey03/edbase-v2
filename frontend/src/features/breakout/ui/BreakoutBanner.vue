<script setup lang="ts">
import { computed } from 'vue'

import { formatCountdown } from '@/shared/lib/datetime'
import { AppIcon, BaseButton } from '@/shared/ui'

import type { BreakoutStateDto } from '../api/breakout-api'

/**
 * O'QUVCHI LENTASI — u qaysi guruhda ekani, kim bilan, qancha vaqt qoldi
 * va "ustozni chaqirish" tugmasi. Boshqa guruhlar ko'rsatilmaydi
 * (loyiha egasi qarori: o'quvchi faqat o'z guruhini ko'radi).
 */
const props = defineProps<{
  state: BreakoutStateDto
  remainingMs: number | null
  helpRaised: boolean
  busy: boolean
}>()

const emit = defineEmits<{ help: [] }>()

const room = computed(() => props.state.rooms[0] ?? null)
const countdown = computed(() => (props.remainingMs === null ? null : formatCountdown(props.remainingMs)))
const members = computed(() => room.value?.members.map((member) => member.displayName).join(', ') ?? '')
</script>

<template>
  <div class="flex flex-wrap items-center gap-x-3 gap-y-2 border-b border-brand-500/30 bg-brand-500/10 px-4 py-2 text-sm">
    <AppIcon
      name="users"
      :size="16"
      class="text-brand-300"
    />
    <template v-if="props.state.myRoom !== null && room !== null">
      <span class="font-semibold text-slate-100">Siz {{ props.state.myRoom }}-guruhdasiz</span>
      <span class="min-w-0 truncate text-slate-400">{{ members }}</span>
    </template>
    <span
      v-else
      class="text-slate-300"
    >Ustoz guruhlarga bo‘ldi — siz hozircha asosiy xonadasiz</span>

    <span
      v-if="countdown !== null"
      class="inline-flex items-center gap-1 tabular-nums text-slate-300"
    >
      <AppIcon
        name="clock"
        :size="13"
      />
      {{ countdown }}
    </span>

    <BaseButton
      v-if="props.state.myRoom !== null"
      class="ml-auto"
      size="sm"
      :variant="props.helpRaised ? 'warning' : 'secondary'"
      :loading="props.busy"
      @click="emit('help')"
    >
      <AppIcon
        name="hand"
        :size="14"
      />
      {{ props.helpRaised ? 'Chaqirildi — bekor qilish' : 'Ustozni chaqirish' }}
    </BaseButton>
  </div>
</template>
