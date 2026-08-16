<script setup lang="ts">
import { ref } from 'vue'

import { AnalysisCriteriaPanel } from '@/features/analysis-criteria-manage'
import { GroupCategoryPanel } from '@/features/group-category-manage'
import { HolidayPanel } from '@/features/holiday-manage'
import { MessageTemplatePanel } from '@/features/message-template-manage'
import { AppIcon, PageHeader } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/**
 * SOZLAMALAR (o'quv jarayoni) — `Academic`/`Admin`.
 *
 * ★ NIMA UCHUN ALOHIDA `ManageSettingsPage`DAN (Admin'ning "Tizim
 * sozlamalari"): u yerdagi sozlamalar INFRATUZILMA (Telegram, LiveKit,
 * to'lov) va FAQAT Admin ko'radi. Bu sahifadagilar esa O'QUV JARAYONI
 * sozlamalari — o'quv bo'limi kundalik ishida boshqaradi. Menyudagi va
 * sarlavhadagi nom endi shunchaki "Sozlamalar" (loyiha egasi, 2026-08-15:
 * "o'quv bo'limi sozlamalari" emas, shunchaki "sozlamalar" deb nomlanishi
 * kerak) — yuqoridagi INFRATUZILMA/O'QUV JARAYONI farqi esa kod darajasida
 * (marshrut nomi, ruxsat ro'yxati) o'zgarmasdan qoladi, faqat KO'RINADIGAN
 * yorliq qisqardi.
 *
 * ★ BO'LIM (tab) TUZILMASI: "Mezonlar" (dars tahlili mezonlari, R29/R30
 * kengaytmasi) va "Yo'nalishlar" (guruh kategoriyalari, R21b — ilgari
 * Guruhlar sahifasidagi alohida drawer edi, 2026-08-15 dan bu sahifaning
 * bo'limi bo'ldi: bu ro'yxat guruh yaratish paytida emas, tayyorgarlik
 * ishi sifatida to'ldiriladi, ya'ni sozlamalarga ko'proq mos). Navbatdagi
 * o'quv bo'limi sozlamasi ham shu naqsh bo'yicha — `SECTIONS` massiviga
 * yangi band qo'shish yetarli, shablon o'zi ko'paytiradi.
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
  { key: 'categories', label: 'Yo‘nalishlar', icon: 'grid' },
  { key: 'templates', label: 'Xabar shablonlari', icon: 'send' },
  { key: 'holidays', label: 'Bayramlar', icon: 'calendar' },
]

const active = ref<string>(SECTIONS[0]!.key)
</script>

<template>
  <div>
    <PageHeader
      title="Sozlamalar"
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
    <GroupCategoryPanel v-if="active === 'categories'" />
    <MessageTemplatePanel v-if="active === 'templates'" />
    <HolidayPanel v-if="active === 'holidays'" />
  </div>
</template>
