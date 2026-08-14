<script setup lang="ts">
import { computed } from 'vue'

import { navItemsForRole, roleLabel, roleTone } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { NotificationBell } from '@/features/notifications'
import { AppIcon, BaseAvatar, BaseBadge } from '@/shared/ui'

/**
 * Yon menyu MAZMUNI (logotip + rol menyusi + foydalanuvchi bloki).
 *
 * Alohida komponent, chunki AYNAN shu mazmun ikki joyda ko'rsatiladi:
 * desktopda doimiy ustun, telefon/planshetda esa chekka drawer. Nusxa
 * ko'chirilsa — menyu bir joyda yangilanib, ikkinchisida eskirib qolardi.
 */
const emit = defineEmits<{ navigate: []; logout: [] }>()

const auth = useAuthStore()

const items = computed(() => navItemsForRole(auth.role))

/** Eski panellardagi logo osti yozuvi. */
const PANEL_LABELS: Record<string, string> = {
  Teacher: 'Ustoz paneli',
  Assistant: 'Yordamchi paneli',
  Academic: "O'quv bo'limi",
  Admin: 'Admin',
}

const panelLabel = computed(() =>
  auth.role !== null ? (PANEL_LABELS[auth.role] ?? "Ta'lim platformasi") : "Ta'lim platformasi",
)
</script>

<template>
  <div class="flex h-full min-h-0 flex-col bg-ink-900">
    <!-- Logotip (eski `.logo`) -->
    <div class="flex shrink-0 items-center gap-3 border-b border-line px-4.5 py-5">
      <!--
        Indigo gradient plitka — ekran suratlaridagi belgi. Gradient
        TOKENLAR orqali (`from-brand-500 to-brand-700`), qotib qolgan
        HEX'siz: brend rangi almashsa plitka o'z-o'zidan moslashadi.
      -->
      <span
        class="flex size-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-base font-bold text-on-brand shadow-xs"
        aria-hidden="true"
      >
        Z
      </span>
      <div class="min-w-0">
        <!--
          R19 — brend nomi BITTA rangda. Ilgari "Zin" tanadan rang meros olardi
          (`slate-100`), "-Nur" esa `text-brand-500` edi: bitta so'z ikki xil
          rangda chizilardi. Endi butun so'z aksent tokenida.

          ★ Matn O'ZGARMADI — "Zin-Nur" aynan shundayligicha qoladi, faqat
          ikkiga bo'lingan `<span>` bitta rangli qatlamga yig'ildi.
        -->
        <p class="truncate text-lg font-bold tracking-tight text-brand-500">
          Zin-Nur
        </p>
        <!--
          Logo ostida ROL yoziladi — eski panellarda ham shunday edi
          (`{{ 'Yordamchi' if role=='assistant' else 'Ustoz' }} paneli`,
          `{{ 'Admin' if role=='admin' else "O'quv bo'limi" }}`). Umumiy
          "Ta'lim platformasi" matni xodimga qaysi panelda turganini
          aytmasdi — ayniqsa bir odam ikki rolda ishlaganda.
        -->
        <p
          class="mt-0.5 truncate text-[10px] uppercase tracking-[1.5px] text-dim"
          v-text="panelLabel"
        />
      </div>

      <!--
        R35/R36 — qo'ng'iroqcha DESKTOP yon menyusining tepasida.

        🔴 `hidden lg:flex` SHART: bu komponent IKKI joyda chiziladi —
        desktopdagi doimiy ustunda va telefondagi drawer'da. Drawer
        versiyasida qo'ng'iroqcha ko'rsatilsa, u `AppShell` sarlavhasidagi
        qo'ng'iroqcha bilan IKKILANARDI (ikkalasi ham telefonda). Bu klass
        uni faqat desktopga qoldiradi, ya'ni har o'lchamda AYNAN BITTA
        qo'ng'iroqcha ko'rinadi.

        ★ Panel CHAPGA tekislanadi: yon menyu ekranning chap chekkasida,
        o'ngga tekislangan panel menyuning tor ustunidan chiqib, kontent
        ustiga noto'g'ri tomondan tushardi.
      -->
      <NotificationBell
        align="left"
        class="ml-auto hidden shrink-0 lg:flex"
      />
    </div>

    <!-- Menyu (eski `.nav`) -->
    <nav
      class="scrollbar-slim min-h-0 flex-1 overflow-y-auto p-2.5"
      aria-label="Asosiy menyu"
    >
      <!--
        `active-class` da `!` SHART: aktiv va oddiy sinflar spetsifikligi bir xil,
        g'olibni CSS'dagi tartib hal qiladi — `text-slate-400` keyinroq chiqib,
        aktiv menyu elementi kulrang bo'lib qolardi.

        ★ FAOL ELEMENT — TO'LIQ INDIGO FON + OQ MATN (ekran suratlaridagidek),
        ilgari 16% tint + indigo matn edi. Yorug' temada tint variant juda
        bo'sh chiqadi: 274 ta kulrang matn orasida "hozir qaysi bo'limdaman"
        savoli bir qarashda javob olmasdi. Oq matn indigo fonda 5.9:1.

        `hover:bg-brand-600!` — faol elementning o'zi ustiga kelganda ham
        indigo qoladi (aks holda `hover:bg-ink-800` uni oqartirib yuborardi).
      -->
      <RouterLink
        v-for="item in items"
        :key="item.routeName"
        :to="{ name: item.routeName }"
        class="mb-0.5 flex min-h-11 items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
        active-class="bg-brand-500! font-semibold text-on-brand! shadow-xs hover:bg-brand-600! hover:text-on-brand!"
        @click="emit('navigate')"
      >
        <AppIcon
          :name="item.icon"
          :size="17"
        />
        <span
          class="truncate"
          v-text="item.label"
        />
      </RouterLink>
    </nav>

    <!-- Foydalanuvchi bloki (eski `.userbox`) -->
    <div class="shrink-0 border-t border-line px-4 py-3.5">
      <div class="flex items-center gap-2.5">
        <BaseAvatar
          :name="auth.displayName"
          size="sm"
        />
        <div class="min-w-0 flex-1">
          <p
            class="truncate text-[13px] font-semibold text-slate-100"
            v-text="auth.displayName"
          />
          <BaseBadge
            v-if="auth.role !== null"
            :tone="roleTone(auth.role)"
          >
            {{ roleLabel(auth.role) }}
          </BaseBadge>
        </div>
        <button
          type="button"
          class="tap-target flex items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
          title="Chiqish"
          @click="emit('logout')"
        >
          <AppIcon
            name="logout"
            :size="18"
          />
        </button>
      </div>
    </div>
  </div>
</template>
