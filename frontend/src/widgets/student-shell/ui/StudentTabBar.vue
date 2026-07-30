<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

import { navItemsForRole } from '@/entities/user'
import { AppIcon } from '@/shared/ui'

/**
 * Pastki 5 tab (eski `.tabbar`).
 *
 * Faol tab: oltin rang, ikonka `translateY(-2px) scale(1.12)` va tepasida
 * 26×3 px oltin indikator — uchalasi ham eski ilovadan aynan.
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
      background: rgb(5 30 45 / 0.96);
      border-top-color: rgb(245 183 49 / 0.15);
      padding: 9px 0 calc(9px + env(safe-area-inset-bottom, 0px));
    "
    aria-label="Asosiy menyu"
  >
    <RouterLink
      v-for="item in items"
      :key="item.routeName"
      :to="{ name: item.routeName }"
      class="relative flex min-h-11 flex-1 flex-col items-center gap-1 text-[10px] font-semibold transition-colors"
      :class="isActive(item.routeName) ? 'text-brand-500' : 'text-dim'"
      :aria-current="isActive(item.routeName) ? 'page' : undefined"
    >
      <!-- Faol tab ustidagi 3px oltin indikator (eski `.tabbar button.on::before`). -->
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
