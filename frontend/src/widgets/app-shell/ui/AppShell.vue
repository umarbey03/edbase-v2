<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { RouterView, useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { NotificationBell, useNotificationHub } from '@/features/notifications'
import { ProfileEditDialog } from '@/features/profile-edit'
import { AppIcon } from '@/shared/ui'
import { GlobalSearchBar } from '@/widgets/global-search'

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

/*
  ★ BILDIRISHNOMA KANALI KARKAS DARAJASIDA (R35/R36) — `StudentShell`
  dagi bilan AYNI qaror va AYNI sabab: qo'ng'iroqcha bu karkasda IKKI
  joyda chiziladi (mobil sarlavha va yon menyu), ulanish esa YAGONA
  bo'lishi kerak. Qo'ng'iroqchaning o'zi hub'ga ulanmaydi — u faqat
  TanStack Query keshidan o'qiydi.
*/
useNotificationHub()

const drawerOpen = ref(false)

/* ---------------------- YON MENYUNI YIG'ISH/YOYISH (2026-08-15) ----------- */

/**
 * Loyiha egasi: *"o'ng tarafdagi navbarga toggle qo'shishing kerak"* —
 * DESKTOP doimiy ustunining O'NG CHETIGA yig'ish/yoyish tugmasi.
 *
 * ★ FAQAT DESKTOP: telefon/planshetdagi drawer VAQTINCHALIK ustma-ust
 * panel (ochib-yopiladigan), uni siqish hech qanday joy tejamaydi va
 * faqat matnni bekor yo'qotardi. `AppSidebar` shu sabab drawer
 * nusxasiga `collapsed` UMUMAN uzatilmaydi (standart `false` qoladi).
 *
 * ★ `localStorage`DA SAQLANADI: xodim bir marta siqib qo'ysa, keyingi
 * kirishda ham siqiq holat kutiladi — har sahifa yangilanishida to'liq
 * ustunga "sakrab qaytish" tanlovni yo'qqa chiqargandek tuyulardi.
 */
const SIDEBAR_COLLAPSED_KEY = 'zn:sidebar-collapsed'

const sidebarCollapsed = ref(localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === '1')

function toggleSidebar(): void {
  sidebarCollapsed.value = !sidebarCollapsed.value
  localStorage.setItem(SIDEBAR_COLLAPSED_KEY, sidebarCollapsed.value ? '1' : '0')
}

/* ---------------------- PROFILNI TAHRIRLASH (2026-08-15) ----------------- */

/**
 * ⚠️ Oyna profilni har muvaffaqiyatli saqlashdan KEYIN o'zi yangilaydi
 * (`ProfileEditDialog`), shuning uchun bu yerda yopilishda qo'shimcha
 * `reloadProfile()` YO'Q — sabab o'sha komponent izohida.
 */
const editOpen = ref(false)

/**
 * Drawer ichidan ochilganda drawer YOPILADI.
 *
 * ★ Aks holda ikkita qatlam ustma-ust turardi va oyna yopilgach
 * foydalanuvchi yana ochiq menyuga qaytardi — `useModalHost` da
 * "ichma-ich drawer TAQIQLANGAN" degan qoida aynan shu tajriba
 * haqida.
 */
function openEditFromDrawer(): void {
  closeDrawer()
  editOpen.value = true
}

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
      class="sticky top-0 hidden h-dvh shrink-0 border-r border-line transition-[width] duration-200 lg:block"
      :class="sidebarCollapsed ? 'w-[76px]' : 'w-[230px]'"
    >
      <AppSidebar
        :collapsed="sidebarCollapsed"
        @logout="handleLogout"
        @edit="editOpen = true"
      />

      <!--
        YIG'ISH/YOYISH TUGMASI — ustunning O'NG CHETIDA, yarim tashqarida
        ("VS Code"/ko'p boshqaruv panellaridagi odatiy joylashuv): shu
        chegara chizig'i ustida turgani uchun "shu yerni bos" ma'nosi
        o'z-o'zidan tushunarli.

        `hidden lg:flex` — `aside` bilan BIR XIL chegara: mobil drawer'da
        bu tugma umuman yo'q (drawer allaqachon vaqtinchalik).
      -->
      <button
        type="button"
        class="absolute top-20 -right-3 z-10 hidden size-6 items-center justify-center rounded-full border border-line bg-ink-800 text-slate-400 shadow-sm transition-colors hover:bg-ink-700 hover:text-slate-100 lg:flex"
        :title="sidebarCollapsed ? 'Menyuni yoyish' : 'Menyuni yig‘ish'"
        :aria-label="sidebarCollapsed ? 'Menyuni yoyish' : 'Menyuni yig‘ish'"
        :aria-expanded="!sidebarCollapsed"
        @click="toggleSidebar"
      >
        <AppIcon
          :name="sidebarCollapsed ? 'chevron-right' : 'chevron-left'"
          :size="14"
        />
      </button>
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
          @edit="openEditFromDrawer"
        />
      </div>
    </div>

    <!--
      PROFILNI TAHRIRLASH — yon menyudagi foydalanuvchi bloki orqali
      ochiladi (xodim uchun yagona kirish nuqtasi).
    -->
    <ProfileEditDialog
      :open="editOpen"
      :user="auth.user"
      @close="editOpen = false"
    />

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
        <!--
          Telefonda qidiruv — ikonka holida (matnli tugmaga joy yo'q).
          Komponentning o'zi <sm ekranda yorliqni yashiradi.
        -->
        <GlobalSearchBar />
        <!--
          R35/R36 — qo'ng'iroqcha TELEFON/PLANSHET sarlavhasida.

          🔴 NEGA YON MENYUDAGISI YETARLI EMAS: telefonda yon menyu
          DRAWER ichida, ya'ni yopiq turadi. Bildirishnoma nishoni esa
          ta'rifi bo'yicha KO'RINIB turishi kerak — burger ostiga
          yashiringan nishon hech qanday xabar bermasdi. Shuning uchun
          bu yerda ham bor, `AppSidebar` dagisi esa `hidden lg:flex`
          bilan faqat DESKTOPDA chiziladi — ikkalasi hech qachon birga
          ko'rinmaydi.
        -->
        <NotificationBell />
        <!--
          R19 — yon panel bilan AYNAN bir xil rang (`text-brand-500`). Mobil
          sarlavhada nom yonma-yon `pageTitle` bilan turadi, shuning uchun
          ikki rangli so'z bu yerda ayniqsa ko'zga tashlanardi.
        -->
        <p class="shrink-0 pr-2 text-base font-bold text-brand-500">
          Zin-Nur
        </p>
      </header>

      <!--
        `max-w-[96rem]` + `mx-auto` — KENG MONITOR HIMOYASI. Ilgari `main`
        da hech qanday cheklov yo'q edi: 2560px li ekranda matn qatori butun
        kenglikni egallardi (sarlavha, izohlar, forma yorliqlari — ko'z bir
        qator oxiridan keyingisining boshiga qaytolmaydigan uzunlik).

        ★ 96rem = 1536px — `style.css` dagi `--breakpoint-2xl` bilan BIR XIL
        qiymat, ya'ni "kontent eng keng e'lon qilingan bosqichda to'xtaydi".
        Yon menyu (230px) hisobga olinsa cheklov faqat ~1766px dan keng
        ekranda ishga tushadi — oddiy noutbukda hech narsa o'zgarmaydi.

        ★ Qiymat ATAYLAB kichikroq (masalan 80rem) EMAS: bu panelning asosiy
        mazmuni 8-10 ustunli jadval, tor konteyner ularni gorizontal skrollga
        majburlardi — keng monitor afzalligi yo'qolardi.
      -->
      <!--
        ═════════════ DESKTOP YUQORI PANEL (2026-08-18) ═════════════
        Loyiha egasi: global qidiruv *"platformani yuqori qismidagi
        navbarda turishi kerak"*.

        ★ FAQAT ≥1024px: kichik ekranda mobil sarlavha allaqachon bor va
        u yerda ham qidiruv tugmasi turadi — ikkita panel bir vaqtda
        chiqmaydi (`lg:hidden` / `hidden lg:flex` juftligi, `AppSidebar`
        dagi qo'ng'iroqcha bilan AYNI naqsh).

        ★ ATAYLAB YUPQA VA FAQAT QIDIRUV: sahifa sarlavhasi har sahifada
        `PageHeader` da bor, uni bu yerda takrorlash ekranning eng
        qimmatli tepa qismini ikki marta yeyardi.
      -->
      <header
        class="sticky top-0 z-30 hidden shrink-0 items-center border-b border-line bg-ink-900/95 px-8 py-2.5 backdrop-blur lg:flex"
      >
        <div class="mx-auto w-full max-w-[96rem]">
          <GlobalSearchBar class="w-full max-w-sm" />
        </div>
      </header>

      <main class="mx-auto w-full min-w-0 max-w-[96rem] flex-1 px-4 py-5 sm:px-6 lg:px-8 lg:py-6.5">
        <RouterView v-slot="{ Component }">
          <component :is="Component" />
        </RouterView>
      </main>
    </div>
  </div>
</template>
