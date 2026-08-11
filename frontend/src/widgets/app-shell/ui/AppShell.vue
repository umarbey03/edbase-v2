<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { AppIcon } from '@/shared/ui'

import AppSidebar from './AppSidebar.vue'

/**
 * Ilova karkasi: yon menyu + kontent.
 *
 * RESPONSIVE QAROR — drawer, "bottom nav" emas:
 * rol menyulari 3 tadan boshlanadi, lekin boshqaruv paneli kelajakda o'sadi
 * (foydalanuvchilar, guruhlar, darslar, kurslar, hisobotlar...). Pastki
 * navigatsiya 5 elementdan keyin siqilib qoladi va rollar orasida
 * ikki xil naqsh paydo bo'lardi. Drawer esa har qanday uzunlikdagi
 * menyuni ko'taradi va desktop ustuni bilan BIR XIL mazmunni ko'rsatadi.
 *
 * Eski loyihaning `.layout{height:100vh}` + qat'iy 230px sidebar'i
 * ATAYLAB ko'chirilmadi — telefonda ishlamaydi.
 */
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

/*
  ★ ROL BO'YICHA TEMA — `StudentShell` bilan BIR XIL mexanizm: atribut
  `<html>` ga qo'yiladi (teleport qilingan modal va toast ham temada qolsin),
  komponentlar nusxalanmaydi — faqat token qiymatlari almashadi.

  ★ 2026-08-10: ilova YAGONA yorug' temaga o'tdi, ya'ni `style.css` dagi
  `[data-theme='teacher']` va `[data-theme='manage']` bloklari BO'SHATILDI
  (rol bo'yicha rang farqi qolmadi). Xarita ATAYLAB saqlanadi — u ikki
  ishni bajaradi:
   1) `theme-color` meta'sini o'rnatadi (mobil brauzer manzil paneli);
   2) rolni yana ajratish kerak bo'lganda `style.css` da bitta
      `--color-brand-500` yozuvi yetadi, bu yerdagi kodga tegilmaydi.

  `color` uchala rolda bir xil (`ink-950` = #f4f6fb) — bu HOZIR shunday,
  kelajakda ajralishi mumkin, shuning uchun xarita tuzilishi buzilmadi.
*/
const THEME_BY_ROLE: Record<string, { theme: string; color: string }> = {
  Teacher: { theme: 'teacher', color: '#f4f6fb' },
  Assistant: { theme: 'teacher', color: '#f4f6fb' },
  Academic: { theme: 'manage', color: '#f4f6fb' },
  Admin: { theme: 'manage', color: '#f4f6fb' },
}

const shellTheme = computed(() =>
  auth.role !== null ? THEME_BY_ROLE[auth.role] : undefined,
)

let previousThemeColor: string | null = null

function applyTheme(): void {
  const current = shellTheme.value
  if (current === undefined) return

  document.documentElement.dataset['theme'] = current.theme

  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta !== null) {
    previousThemeColor ??= meta.content
    meta.content = current.color
  }
}

onMounted(applyTheme)

// Sessiya tiklanganda rol KEYINROQ ma'lum bo'ladi — o'shanda ham qo'llanadi.
watch(shellTheme, applyTheme)

onBeforeUnmount(() => {
  delete document.documentElement.dataset['theme']
  const meta = document.querySelector<HTMLMetaElement>('meta[name="theme-color"]')
  if (meta !== null && previousThemeColor !== null) meta.content = previousThemeColor
})

const drawerOpen = ref(false)
/** Drawer yopilgandan keyin fokus shu tugmaga qaytadi (klaviatura foydalanuvchisi uchun). */
const burgerButton = ref<HTMLButtonElement | null>(null)
const drawerPanel = ref<HTMLElement | null>(null)

const pageTitle = computed(() => {
  const title = route.meta.title
  return typeof title === 'string' ? title : 'Zin-Nur'
})

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') closeDrawer()
}

function openDrawer(): void {
  drawerOpen.value = true
}

function closeDrawer(): void {
  drawerOpen.value = false
}

watch(drawerOpen, (isOpen) => {
  if (isOpen) {
    document.addEventListener('keydown', handleKeydown)
    // Ochilgan panelga fokus beramiz — Esc va Tab shu yerdan boshlansin.
    void nextTick(() => drawerPanel.value?.focus())
  } else {
    document.removeEventListener('keydown', handleKeydown)
    burgerButton.value?.focus()
  }
})

// Sahifa almashsa drawer yopiladi (telefonda menyu ochiq qolib ketmasin).
watch(() => route.fullPath, closeDrawer)

onBeforeUnmount(() => {
  document.removeEventListener('keydown', handleKeydown)
})

async function handleLogout(): Promise<void> {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="flex min-h-dvh bg-ink-950">
    <!-- ===================== Desktop: doimiy ustun ====================== -->
    <aside
      class="sticky top-0 hidden h-dvh w-[230px] shrink-0 border-r border-line lg:block"
    >
      <AppSidebar @logout="handleLogout" />
    </aside>

    <!-- ============ Telefon/planshet: chekkadan chiquvchi drawer ========= -->
    <div
      v-if="drawerOpen"
      class="fixed inset-0 z-50 lg:hidden"
    >
      <!--
        Qoraytiruvchi qatlam: `bg-black/65` YORUG' temada juda og'ir chiqadi
        (sahifa "o'chgandek" ko'rinadi). `slate-900` — `style.css` dagi
        "scrim bandi" (neytral to'q ko'k #101828), 35% + yengil blur.
      -->
      <div
        class="absolute inset-0 bg-slate-900/35 backdrop-blur-sm"
        aria-hidden="true"
        @click="closeDrawer"
      />
      <div
        ref="drawerPanel"
        class="absolute inset-y-0 left-0 w-[264px] max-w-[82vw] animate-slide-in-left border-r border-line shadow-lg"
        role="dialog"
        aria-modal="true"
        aria-label="Menyu"
        tabindex="-1"
      >
        <AppSidebar
          @navigate="closeDrawer"
          @logout="handleLogout"
        />
      </div>
    </div>

    <!-- ============================ Kontent ============================= -->
    <!-- `min-w-0`: ichkaridagi keng jadval butun sahifani cho'zib yubormasin. -->
    <div class="flex min-w-0 flex-1 flex-col">
      <header
        class="sticky top-0 z-30 flex shrink-0 items-center gap-2 border-b border-line bg-ink-900/95 px-2 py-2 backdrop-blur lg:hidden"
      >
        <button
          ref="burgerButton"
          type="button"
          class="tap-target flex items-center justify-center rounded-lg text-slate-300 transition-colors hover:bg-ink-800 hover:text-slate-100"
          aria-label="Menyuni ochish"
          :aria-expanded="drawerOpen"
          @click="openDrawer"
        >
          <AppIcon
            name="menu"
            :size="20"
          />
        </button>
        <p
          class="min-w-0 flex-1 truncate text-sm font-semibold"
          v-text="pageTitle"
        />
        <p class="shrink-0 pr-2 text-base font-bold">
          Zin<span class="text-brand-500">-Nur</span>
        </p>
      </header>

      <main class="min-w-0 flex-1 px-4 py-5 sm:px-6 lg:px-8 lg:py-6.5">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>
    </div>
  </div>
</template>
