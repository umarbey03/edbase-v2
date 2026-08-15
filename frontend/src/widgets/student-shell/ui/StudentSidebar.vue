<script setup lang="ts">
import { computed } from 'vue'

import { useAvatar } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { AppIcon } from '@/shared/ui'

import { useStudentNav } from './useStudentNav'

/**
 * YON MENYU MAZMUNI — FAQAT DESKTOP (≥1024px).
 *
 * Bu pastki 5 tabning ustundagi ko'rinishi: AYNAN o'sha beshta manzil,
 * AYNAN o'sha tartibda (`useStudentNav` -> `STUDENT_NAV`). Telefonda va
 * Telegram Mini App'da bu komponent KO'RINMAYDI — `StudentShell` uni
 * `hidden lg:block` ostidagi `<aside>` ichida chizadi.
 *
 * ★ TUZILISH XODIM KARKASIDAN (`AppSidebar`) OLINGAN — logotip, menyu,
 * pastda foydalanuvchi bloki; kengligi ham o'sha 230px. Nusxa emas, NAQSH:
 * ikkala panel bitta ilovaning bo'lagidek ko'rinishi kerak, lekin o'quvchi
 * menyusi rolga qarab o'zgarmaydi (u doim `Student`) va bu yerda "chiqish"
 * tugmasi YO'Q — o'quvchida chiqish profil varag'ida, chunki Mini App'da u
 * oddiy chiqish emas (sessiyani tozalab, ilovani YOPADI, `StudentShell`
 * dagi izohga qarang). Ikkinchi chiqish yo'li ikkinchi xatti-harakat
 * demakdir.
 */
const props = defineProps<{ displayName: string }>()

const emit = defineEmits<{ 'open-profile': [] }>()

const { items, isActive } = useStudentNav()

/** `StudentAppBar` dagi avatar bilan bir xil qoida — bitta odam, bitta harf. */
const initial = computed(() => (props.displayName.trim()[0] ?? '?').toUpperCase())

/**
 * Profil rasmi — `null` bo'lsa ism harfi chiziladi.
 *
 * ★ `props` ORQALI UZATILMADI, store'dan O'QILADI: bu blok DOIM
 * chaqiruvchining O'ZINI ko'rsatadi (`displayName` ham shundan keladi),
 * ya'ni ikkinchi prop faqat karkasda takror uzatish bo'lardi.
 */
const auth = useAuthStore()

const avatarUrl = useAvatar(
  computed(() => auth.user?.id ?? null),
  computed(() => auth.user?.avatarUpdatedAt ?? null),
)
</script>

<template>
  <div class="flex h-full min-h-0 flex-col bg-ink-900">
    <!--
      Logotip. Yozuv `StudentAppBar` dagi "ZIN-NUR / TALABA" ning o'zi —
      appbar desktopda logotipni yashiradi (`lg:hidden`), ya'ni brend
      ekranda BIR marta chiziladi.
    -->
    <div class="flex shrink-0 items-center gap-3 border-b border-line px-4.5 py-5">
      <span
        class="flex size-9 shrink-0 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-base font-bold text-on-brand shadow-xs"
        aria-hidden="true"
      >
        Z
      </span>
      <div class="min-w-0">
        <!--
          R19 — brend nomi BITTA rangda. Ilgari "Zin" tanadan rang meros
          olardi, "-Nur" esa `text-brand-500` edi: bitta so'z ikki xil
          rangda chizilardi. Endi butun so'z aksent tokenida — `AppSidebar`
          va `StudentAppBar` bilan AYNAN bir xil.

          ★ Matn O'ZGARMADI — "Zin-Nur" aynan shundayligicha qoladi, faqat
          ikkiga bo'lingan `<span>` bitta rangli qatlamga yig'ildi.
        -->
        <p class="truncate text-lg font-bold tracking-tight text-brand-500">
          Zin-Nur
        </p>
        <p class="mt-0.5 truncate text-[10px] font-bold uppercase tracking-[1.5px] text-dim">
          Talaba
        </p>
      </div>
    </div>

    <!--
      Menyu. `aria-label` pastki tab paneliniki bilan bir xil ("Asosiy
      menyu") — bu ATAYLAB: ikkalasi bitta narsa, va ular hech qachon
      BIRGA ko'rinmaydi (biri `hidden lg:block`, ikkinchisi `lg:hidden`),
      shuning uchun ekran o'quvchisi ikkita bir xil nomli navigatsiyani
      ko'rmaydi.

      Faol band `active-class` bilan emas, `:class` ternari bilan
      beriladi: qoida "O'quv" ichki sahifalarini ham qamraydi
      (`useStudentNav`). Yon ta'siri foydali — `AppSidebar` dagi
      spetsifiklik urushi (`text-slate-400` faol rangni bosib ketishi) bu
      yerda umuman yuzaga kelmaydi, ya'ni `!` kerak emas.
    -->
    <nav
      class="scrollbar-slim min-h-0 flex-1 overflow-y-auto p-2.5"
      aria-label="Asosiy menyu"
    >
      <RouterLink
        v-for="item in items"
        :key="item.routeName"
        :to="{ name: item.routeName }"
        class="mb-0.5 flex min-h-11 items-center gap-2.5 rounded-xl px-3 py-2.5 text-sm transition-colors"
        :class="
          isActive(item.routeName)
            ? 'bg-brand-500 font-semibold text-on-brand shadow-xs hover:bg-brand-600'
            : 'text-slate-400 hover:bg-ink-800 hover:text-slate-100'
        "
        :aria-current="isActive(item.routeName) ? 'page' : undefined"
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

    <!--
      Foydalanuvchi bloki. Bu YANGI ekran emas — appbar'dagi avatar bilan
      BITTA narsani ochadi (`StudentProfileSheet`), shuning uchun profil
      ma'lumoti bu yerda takrorlanmaydi: ism, va "ochiladi" degan ishora.
    -->
    <div class="shrink-0 border-t border-line p-2.5">
      <button
        type="button"
        class="flex min-h-11 w-full items-center gap-2.5 rounded-xl px-3 py-2.5 text-left transition-colors hover:bg-ink-800"
        aria-label="Profil"
        @click="emit('open-profile')"
      >
        <img
          v-if="avatarUrl !== null"
          :src="avatarUrl"
          class="size-9 shrink-0 rounded-full object-cover"
          alt=""
        >
        <span
          v-else
          class="flex size-9 shrink-0 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-violet-400 text-sm font-bold text-white"
          aria-hidden="true"
        >
          {{ initial }}
        </span>
        <span class="min-w-0 flex-1">
          <span
            class="block truncate text-[13px] font-semibold text-slate-100"
            v-text="props.displayName"
          />
          <span class="block text-[11px] text-slate-400">Profil</span>
        </span>
        <AppIcon
          name="chevron-right"
          :size="16"
          class="shrink-0 text-slate-400"
        />
      </button>
    </div>
  </div>
</template>
