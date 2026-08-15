<script setup lang="ts">
import { ref } from 'vue'

import { AnalysisCriteriaPanel } from '@/features/analysis-criteria-manage'
import { AppIcon, PageHeader } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/**
 * O'QUV BO'LIMI SOZLAMALARI — `Academic`/`Admin`.
 *
 * ★ NIMA UCHUN ALOHIDA `ManageSettingsPage`DAN (Admin'ning "Tizim
 * sozlamalari"): u yerdagi sozlamalar INFRATUZILMA (Telegram, LiveKit,
 * to'lov) va FAQAT Admin ko'radi. Bu sahifadagilar esa O'QUV JARAYONI
 * sozlamalari — o'quv bo'limi kundalik ishida boshqaradi.
 *
 * ★ BO'LIM (tab) TUZILMASI ATAYLAB: hozircha bitta bo'lim — "Mezonlar"
 * (dars tahlili mezonlari, R29/R30 kengaytmasi) — lekin sahifa keyingi
 * o'quv bo'limi sozlamalari uchun TAYYOR joy sifatida quriladi.
 * `SECTIONS` massiviga yangi band qo'shish yetarli, shablon o'zi
 * ko'paytiradi.
 *
 * ★ Sahifada BOSHQA BIZNES MANTIQ YO'Q: har bo'lim o'z panelini
 * (`features/*`) chizadi, bu yerda faqat tanlagich va joylashuv.
 */
interface SettingsSection {
  key: string
  label: string
  icon: IconName
}

const SECTIONS: SettingsSection[] = [
  { key: 'criteria', label: 'Mezonlar', icon: 'check-square' },
]

const active = ref<string>(SECTIONS[0]!.key)
</script>

<template>
  <div>
    <PageHeader
      title="O‘quv bo‘limi sozlamalari"
      subtitle="O‘quv jarayoniga tegishli sozlamalar."
    />

    <div
      class="mb-5 inline-flex gap-1 rounded-2xl border border-line bg-ink-900 p-1"
      role="tablist"
    >
      <button
        v-for="section in SECTIONS"
        :key="section.key"
        type="button"
        role="tab"
        :aria-selected="active === section.key"
        class="flex items-center gap-1.5 rounded-xl px-4 py-2 text-sm font-semibold transition-colors"
        :class="
          active === section.key
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        @click="active = section.key"
      >
        <AppIcon
          :name="section.icon"
          :size="15"
        />
        {{ section.label }}
      </button>
    </div>

    <AnalysisCriteriaPanel v-if="active === 'criteria'" />
  </div>
</template>
