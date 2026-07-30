<script setup lang="ts">
import { computed } from 'vue'

import { navItemsForRole, roleLabel, roleTone } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
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
    <div class="shrink-0 border-b border-line px-4.5 py-5">
      <p class="text-lg font-bold tracking-tight">
        Zin<span class="text-brand-500">-Nur</span>
      </p>
      <!--
        Logo ostida ROL yoziladi — eski panellarda ham shunday edi
        (`{{ 'Yordamchi' if role=='assistant' else 'Ustoz' }} paneli`,
        `{{ 'Admin' if role=='admin' else "O'quv bo'limi" }}`). Umumiy
        "Ta'lim platformasi" matni xodimga qaysi panelda turganini
        aytmasdi — ayniqsa bir odam ikki rolda ishlaganda.
      -->
      <p
        class="mt-0.5 text-[10px] uppercase tracking-[1.5px] text-dim"
        v-text="panelLabel"
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
      -->
      <RouterLink
        v-for="item in items"
        :key="item.routeName"
        :to="{ name: item.routeName }"
        class="mb-0.5 flex min-h-11 items-center gap-2.5 rounded-lg px-3 py-2.5 text-sm text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
        active-class="bg-brand-500/16! font-semibold text-brand-500! hover:text-brand-500!"
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
          class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
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
