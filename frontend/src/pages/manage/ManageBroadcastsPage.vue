<script setup lang="ts">
import { ref } from 'vue'

import { AbsenceNoticeHistory } from '@/features/absence-notice'
import { GroupBroadcastComposer, GroupBroadcastHistory } from '@/features/group-broadcast-send'
import { AppIcon, BaseButton, BaseModal, PageHeader } from '@/shared/ui'

/**
 * "XABARLAR" PANELI (2026-08-16) — o'quv bo'limi/admin tanlangan
 * guruhlarga (shablon yoki qo'lda yozilgan) xabar yuboradi.
 *
 * ★ FORMA MODALDA (2026-08-16, loyiha egasi: "xabar yuborish formasi modal
 * holatida ochilishi kerak") — "Yangi xabar" tugmasi bosilganda
 * `GroupBroadcastComposer` (`bare` prop bilan, o'z `BaseCard`isiz)
 * `BaseModal` ichida ochiladi. Yuborilgach (`@sent`) modal avtomatik
 * yopiladi; `GroupBroadcastHistory` o'z so'rovini mustaqil boshqaradi
 * (`['group-broadcasts']` kaliti) va composer ichida shu kalit
 * invalidate qilingani uchun tarix darhol yangilanadi.
 *
 * ════════════════════════════════════════════════════════════════════════
 * ★★ IKKI TAB (2026-08-18) — loyiha egasi so'rovi
 * ════════════════════════════════════════════════════════════════════════
 *
 * "Guruh xabarlari" va "Darsga kirmaganlar" — ikkalasi ham xabar, lekin
 * SAVOLI boshqa:
 *   • guruh xabarnomasi — "guruhga e'lon berdikmi?" (bitta qator = butun
 *     guruh, oluvchilar soni bilan);
 *   • kelmaganlik xabari — "AYNAN Doniyorga xabar bordimi?" (har
 *     oluvchiga alohida qator, yetkazilish holati bilan).
 * Bitta ro'yxatga qo'shilsa, ikkinchi savolga javob yo'qolardi.
 *
 * ★ KELMAGANLIK TARIXI SHU YERDA HAM, "Darsga kirmaganlar" PANELIDA HAM:
 * bitta komponent, ikki joyda. U yerda — ISH joyi (yubordim → ertaga
 * javobini ko'raman), bu yerda — markaz nomidan ketgan barcha xabarlar
 * ARXIVI. Nusxa emas, AYNI komponent — biri o'zgarib ikkinchisi eskirib
 * qolmaydi.
 *
 * RUXSAT: server `[Authorize(Roles = "Academic,Admin")]`
 * (`GroupBroadcastsController`/`MessageTemplatesController`) — marshrut
 * `MANAGERS` bilan qulflangan (`router/index.ts`).
 */
const TABS = [
  { key: 'groups', label: 'Guruh xabarlari', icon: 'send' },
  { key: 'absence', label: 'Darsga kirmaganlar', icon: 'user-x' },
] as const

const activeTab = ref<(typeof TABS)[number]['key']>('groups')
const composerOpen = ref(false)
</script>

<template>
  <div>
    <PageHeader
      title="Xabarlar"
      subtitle="Guruhlarga e'lon va darsga kelmagan o‘quvchilarga xabar"
    >
      <template #actions>
        <!--
          Tugma faqat guruh xabarlari tabida: kelmaganlik xabari
          "Darsga kirmaganlar" panelidan yuboriladi — u yerda kimga
          yuborilishi tanlanadi, bu yerda esa faqat arxiv ko'riladi.
        -->
        <BaseButton
          v-if="activeTab === 'groups'"
          @click="composerOpen = true"
        >
          Yangi xabar
        </BaseButton>
      </template>
    </PageHeader>

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

    <div
      v-if="activeTab === 'groups'"
      class="space-y-5"
    >
      <GroupBroadcastHistory />
    </div>

    <AbsenceNoticeHistory
      v-else
      :titled="false"
    />

    <BaseModal
      :open="composerOpen"
      title="Yangi xabar"
      wide
      @close="composerOpen = false"
    >
      <GroupBroadcastComposer
        bare
        @sent="composerOpen = false"
      />
    </BaseModal>
  </div>
</template>
