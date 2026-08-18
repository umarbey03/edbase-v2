<script setup lang="ts">
import { ref } from 'vue'

import { FreeTeachersPanel } from '@/features/teacher-availability'
import { AppIcon, PageHeader } from '@/shared/ui'
import { SessionBoard } from '@/widgets/session-board'

/**
 * JONLI DARSLAR — ikki qarash (2026-08-18).
 *
 * ★ "BO'SH USTOZLAR" TABI QO'SHILDI (loyiha egasi): *"14:00 da bugunni
 * belgilasam qaysi ustozlar bo'shligini ko'rsatsin, ind qo'yib
 * berayotganda birinchi shunga qarardim"*.
 *
 * Ikkalasi AYNI ma'lumotning ikki tomoni: darslar jadvali KIM DARS
 * O'TAYAPTI ni, bo'sh ustozlar esa KIM O'TMAYAPTI ni ko'rsatadi.
 * Shuning uchun ular alohida sahifa emas, shu sahifaning ikki tabi —
 * operator ular orasida tez-tez u yoq-bu yoqqa o'tadi.
 */
const TABS = [
  { key: 'sessions', label: 'Darslar', icon: 'play' },
  { key: 'free', label: 'Bo‘sh ustozlar', icon: 'graduation' },
] as const

const activeTab = ref<(typeof TABS)[number]['key']>('sessions')
</script>

<template>
  <div>
    <PageHeader
      title="Jonli darslar"
      subtitle="Platformadagi barcha rejalashtirilgan va jonli darslar"
    />

    <div
      class="mb-4 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="tab in TABS"
        :key="tab.key"
        type="button"
        role="tab"
        :aria-selected="activeTab === tab.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          activeTab === tab.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="activeTab = tab.key"
      >
        <AppIcon
          :name="tab.icon"
          :size="15"
        />
        {{ tab.label }}
      </button>
    </div>

    <SessionBoard
      v-if="activeTab === 'sessions'"
      searchable
    />
    <FreeTeachersPanel v-else />
  </div>
</template>
