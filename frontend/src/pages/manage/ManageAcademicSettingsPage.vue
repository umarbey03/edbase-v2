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
 * ★ BO'LIM TUZILMASI: "Mezonlar" (dars tahlili mezonlari, R29/R30
 * kengaytmasi) va "Yo'nalishlar" (guruh kategoriyalari, R21b — ilgari
 * Guruhlar sahifasidagi alohida drawer edi, 2026-08-15 dan bu sahifaning
 * bo'limi bo'ldi). Navbatdagi o'quv bo'limi sozlamasi ham shu naqsh
 * bo'yicha — `SECTIONS` massiviga yangi band qo'shish yetarli, shablon
 * o'zi ko'paytiradi.
 *
 * ════════════════════════════════════════════════════════════════════════
 * ★★ JOYLASHUV — YON MENYU (desktop) / GORIZONTAL TAB (telefon), 2026-08-17
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: *"sahifa umuman eski/oddiy ko'rinadi, zamonaviyroq dizayn
 * kerak"*. Ilgari (va telefonda hamon) to'rttala bo'lim BITTA gorizontal
 * pill qatorida edi — 1600px lik sahifada bu tor, "eski" ko'rinardi va
 * ilovaning o'z YIRIK sozlamalar sahifasi (`AppSidebar`) uchun ishlatgan
 * naqshiga (vertikal menyu, faol bandda to'liq bo'yalgan fon) UMUMAN
 * o'xshamasdi.
 *
 * ★ Endi ikkalasi ham BOR, lekin BITTA HAKAM (CSS `lg:`) bilan almashadi
 * — `StudentShell`/`GroupChatRoom` dagi "bitta hakam" qoidasi bilan AYNI:
 *   • ≥1024px — chapda 240px lik doimiy menyu, `AppSidebar`dagi FAOL
 *     band uslubi bilan (to'liq indigo fon), yonida qisqa tavsif matni;
 *   • <1024px — pastki tab paneli kabi joy yo'q, shuning uchun
 *     GORIZONTAL SKROLL bilan pill qator (`GroupTabs.vue` naqshi) qoladi.
 * Ikkalasi ham AYNI `active` holatni o'qiydi/yozadi — ikki alohida
 * "qaysi bo'lim tanlangan" manbai YO'Q.
 */
interface SettingsSection {
  key: string
  label: string
  icon: IconName
  /** Yon menyudagi qisqa tavsif — faqat desktop ustunida ko'rinadi. */
  hint: string
}

const SECTIONS: SettingsSection[] = [
  {
    key: 'criteria',
    label: 'Mezonlar',
    icon: 'check-square',
    hint: 'Dars tahlili baholash mezonlari',
  },
  {
    key: 'categories',
    label: 'Yo‘nalishlar',
    icon: 'grid',
    hint: 'Guruhlarni saralash toifalari',
  },
  {
    key: 'templates',
    label: 'Xabar shablonlari',
    icon: 'send',
    hint: 'Guruhga yuboriladigan tayyor matnlar',
  },
  {
    key: 'holidays',
    label: 'Bayramlar',
    icon: 'calendar',
    hint: 'Darslar avtomatik bekor qilinadigan kunlar',
  },
]

const active = ref<string>(SECTIONS[0]!.key)

function activeSection(): SettingsSection {
  return SECTIONS.find((section) => section.key === active.value) ?? SECTIONS[0]!
}
</script>

<template>
  <div>
    <PageHeader
      title="Sozlamalar"
      subtitle="O‘quv jarayoniga tegishli sozlamalar."
    />

    <div class="lg:grid lg:grid-cols-[240px_minmax(0,1fr)] lg:items-start lg:gap-6">
      <!--
        ================= DESKTOP: DOIMIY YON MENYU (≥1024px) =================
        `AppSidebar` dagi FAOL band uslubi bilan AYNI (to'liq brend fon +
        oq matn) — sozlamalar ekrani ilovaning o'z tizimli navigatsiyasidan
        vizual jihatdan "begona" bo'lib qolmasin.
      -->
      <nav
        class="sticky top-24 mb-5 hidden rounded-2xl border border-line bg-ink-900 p-2.5 lg:mb-0 lg:block"
        aria-label="Sozlamalar bo‘limlari"
      >
        <button
          v-for="section in SECTIONS"
          :key="section.key"
          type="button"
          class="mb-1 flex w-full items-start gap-2.5 rounded-xl px-3 py-2.5 text-left text-sm transition-colors last:mb-0"
          :class="
            active === section.key
              ? 'bg-brand-500 text-on-brand shadow-xs'
              : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
          "
          :aria-current="active === section.key ? 'true' : undefined"
          @click="active = section.key"
        >
          <AppIcon
            :name="section.icon"
            :size="17"
            class="mt-0.5 shrink-0"
          />
          <span class="min-w-0">
            <span
              class="block font-semibold"
              v-text="section.label"
            />
            <span
              class="mt-0.5 block text-xs"
              :class="active === section.key ? 'text-on-brand/75' : 'text-dim'"
              v-text="section.hint"
            />
          </span>
        </button>
      </nav>

      <!--
        ================= TELEFON/PLANSHET: GORIZONTAL TAB (<1024px) =================
        `GroupTabs.vue` bilan AYNI naqsh — yon menyuga joy yo'q ekranlarda
        yagona amaliy yechim.
      -->
      <div
        class="scroll-x-safe scrollbar-none mb-5 border-b border-line pb-2.5 lg:hidden"
        role="tablist"
      >
        <div class="flex gap-2">
          <button
            v-for="section in SECTIONS"
            :key="section.key"
            type="button"
            role="tab"
            :aria-selected="active === section.key"
            class="inline-flex min-h-11 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-[20px] border px-[15px] text-[13px] transition-colors"
            :class="
              active === section.key
                ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
                : 'border-line bg-ink-900 font-medium text-slate-400 hover:border-line-strong hover:bg-ink-800 hover:text-slate-100'
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
      </div>

      <!--
        ================================ KONTENT ================================
        ★ KICHIK SARLAVHA — "hozir qaysi bo'limdaman" ni qatorlar orasida
        aylanib yurgan xodim ham (masalan telefonda pastga skroll qilib)
        bir qarashda ko'rsin. Matn TAKRORLANMAYDI: bo'lim TAVSIFI faqat
        yon menyuda (desktop) — bu yerda esa har panelning O'ZI allaqachon
        bitta qisqa izoh bilan boshlanadi.
      -->
      <div class="min-w-0 rounded-2xl border border-line bg-ink-900 p-4 sm:p-5">
        <h2 class="mb-4 flex items-center gap-2 text-sm font-bold text-slate-100">
          <AppIcon
            :name="activeSection().icon"
            :size="16"
            class="text-brand-400"
          />
          {{ activeSection().label }}
        </h2>

        <AnalysisCriteriaPanel v-if="active === 'criteria'" />
        <GroupCategoryPanel v-if="active === 'categories'" />
        <MessageTemplatePanel v-if="active === 'templates'" />
        <HolidayPanel v-if="active === 'holidays'" />
      </div>
    </div>
  </div>
</template>
