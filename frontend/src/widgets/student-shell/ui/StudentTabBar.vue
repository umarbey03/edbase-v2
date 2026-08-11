<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { navItemsForRole } from '@/entities/user'
import { AppIcon } from '@/shared/ui'

/**
 * Pastki 5 tab (eski `.tabbar`).
 *
 * Faol tab: brend rangi, ikonka `translateY(-2px) scale(1.12)` va tepasida
 * 26×3 px indikator — uchalasi ham eski ilovadan aynan (u yerda rang oltin
 * edi, endi indigo).
 *
 * 🔴 FON YORUG' TEMAGA O'TKAZILDI (2026-08-11). Bu fayl BLOK A (rang
 * migratsiyasi) da E'TIBORDAN CHETDA QOLGAN edi: `background` va
 * `border-top-color` inline `style` ichida QOTIB QOLGAN qiymatlar bo'lib
 * turgan — `rgb(5 30 45 / .96)` (eski navy) va `rgb(245 183 49 / .15)`
 * (eski oltin). Natijada butun ilova oq bo'lib, o'quvchi Mini App'ining
 * pastki paneli QORONG'I navy qolgan edi, ustiga indigo yozuv bilan:
 *   • faol tab (`text-brand-500` #4f4de8) navy fonda 2.90:1 — 10px qalin
 *     matn uchun WCAG AA'dan past;
 *   • nofaol tab (`text-dim` #767f95) 4.26:1 — u ham past.
 * Endi fon oq (`ink-900`, 96% — blur ishlashi uchun) va chegara `line`:
 *   • faol 5.90:1 ✓
 *   • nofaol — `text-dim` OQ fonda ham 4.01:1 berardi (kontrast auditi shu
 *     qatorda yiqildi), shuning uchun `text-slate-400` (#656d80, 5.20:1).
 *     10px qalin yozuv "uchinchi darajali" siyoh bilan o'qilmaydi; nav
 *     yorlig'i esa ikkilamchi matn — `slate-400` aynan shuning tokeni.
 *
 * ★ Inline `style` SAQLANADI: `padding` da `env(safe-area-inset-bottom)` bor
 * (iPhone'dagi "home indicator"), uni Tailwind utility bilan berib
 * bo'lmaydi. Fon esa `color-mix` bilan TOKENGA bog'landi — 96% shaffoflik
 * `backdrop-blur` uchun kerak (to'liq xira fonda blur ko'rinmaydi).
 *
 * `RouterLink` EMAS, `router-link` bilan qo'lda `isActive` hisoblanadi: "O'quv"
 * tabi `/oquv` dan tashqari `/oquv/vazifalarim` va `/oquv/testlarim` da ham
 * yonib turishi kerak (eski ilovada vazifa/test "O'quv" ichida edi), buni
 * `exact-active-class` bera olmaydi.
 */
const route = useRoute()

const items = computed(() => navItemsForRole('Student'))

function isActive(routeName: string): boolean {
  if (route.name === routeName) return true
  // Pastki sahifalar: `student-assignments`, `student-tests`, `student-test-take`
  // ota-tab sifatida "O'quv" ni yoqadi.
  if (routeName !== 'student-learn') return false
  return typeof route.name === 'string' && route.name.startsWith('student-')
    && route.path.startsWith('/oquv')
}
</script>

<template>
  <nav
    class="fixed bottom-0 left-1/2 z-40 flex w-full max-w-[520px] -translate-x-1/2 border-t backdrop-blur-[22px]"
    style="
      background: color-mix(in oklab, var(--color-ink-900) 96%, transparent);
      border-top-color: var(--color-line);
      padding: 9px 0 calc(9px + env(safe-area-inset-bottom, 0px));
    "
    aria-label="Asosiy menyu"
  >
    <RouterLink
      v-for="item in items"
      :key="item.routeName"
      :to="{ name: item.routeName }"
      class="relative flex min-h-11 flex-1 flex-col items-center gap-1 text-[10px] font-semibold transition-colors"
      :class="isActive(item.routeName) ? 'text-brand-500' : 'text-slate-400'"
      :aria-current="isActive(item.routeName) ? 'page' : undefined"
    >
      <!-- Faol tab ustidagi 3px indikator (eski `.tabbar button.on::before`;
           u yerda oltin edi, endi brend indigosi). -->
      <span
        v-if="isActive(item.routeName)"
        class="absolute -top-[9px] h-[3px] w-[26px] rounded-[3px] bg-brand-500"
        aria-hidden="true"
      />
      <AppIcon
        :name="item.icon"
        :size="22"
        class="transition-transform duration-[250ms]"
        :class="isActive(item.routeName) ? '-translate-y-0.5 scale-[1.12]' : ''"
      />
      <span v-text="item.label" />
    </RouterLink>
  </nav>
</template>
