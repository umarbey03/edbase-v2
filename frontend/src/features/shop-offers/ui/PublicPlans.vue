<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchShopOffers } from '@/entities/shop'
import type { ShopPlanDto } from '@/entities/shop'
import { PLAN_KIND_LABELS } from '@/entities/subscription'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon } from '@/shared/ui'

/**
 * SAYTDAGI TARIFLAR (2026-10-29, onlayn sotuv) — landing sahifada.
 *
 * Ma'lumot BAZADAN (`GET /api/v1/shop/offers`): o'quv bo'limi «Moliya»
 * sahifasida «Saytda ko'rsatish» belgilagan faol rejalar. Narx yoki taklif
 * matni o'zgarsa landing qayta yig'ilmaydi.
 *
 * ★ «SOTIB OLISH» — KIRISHGA: kirgan o'quvchi to'g'ridan-to'g'ri «Tariflar»
 *   sahifasiga qaytadi (`?redirect=/tariflar`), yangi odam esa Telegram
 *   bot orqali ro'yxatdan o'tadi.
 * ★ TARIF YO'Q YOKI SO'ROV YIQILDI — BO'LIM UMUMAN CHIZILMAYDI: landing'ning
 *   qolgani (statik narx bo'limi, ariza formasi) baribir ishlaydi.
 */
const query = useQuery({
  queryKey: ['shop', 'offers'],
  queryFn: ({ signal }) => fetchShopOffers({ signal }),
  staleTime: 5 * 60_000,
})

const plans = computed(() => query.data.value?.plans ?? [])

function priceSuffix(plan: ShopPlanDto): string {
  return plan.months === 1 ? 'oyiga' : `${plan.months} oyga`
}

const buyLink = { name: 'login', query: { redirect: '/tariflar' } }
</script>

<template>
  <section
    v-if="plans.length > 0"
    id="tariflar"
    class="mx-auto max-w-6xl scroll-mt-20 px-4 py-16 sm:px-6 sm:py-24"
  >
    <div class="mx-auto max-w-2xl text-center">
      <span class="text-xs font-bold uppercase tracking-[1.4px] text-brand-500">
        Onlayn tariflar
      </span>
      <h2
        class="mt-3 font-display text-3xl font-semibold tracking-tight text-slate-100 sm:text-[2.5rem] sm:leading-[1.1]"
      >
        O‘zingizga mos tarifni tanlang
      </h2>
      <p class="mx-auto mt-4 max-w-[60ch] text-base leading-relaxed text-slate-400">
        Ustoz bilan guruhda jonli darslar yoki o‘zingizga qulay vaqtda video darslar.
        To‘lov Payme yoki Click orqali, darslar to‘lov tasdiqlangach ochiladi.
      </p>
    </div>

    <div
      class="mt-12 grid items-stretch gap-5"
      :class="plans.length >= 3 ? 'lg:grid-cols-3' : 'md:grid-cols-2'"
    >
      <article
        v-for="plan in plans"
        :key="plan.id"
        class="flex flex-col rounded-3xl p-7 sm:p-9"
        :class="plan.kind === 'Group'
          ? 'plan-featured surface-brand'
          : 'border border-line bg-ink-900 shadow-xs'"
      >
        <span
          class="self-start rounded-full border border-line-strong px-3 py-1 text-[12px] font-bold uppercase tracking-[0.1em] text-slate-300"
        >
          {{ PLAN_KIND_LABELS[plan.kind] }}
        </span>

        <h3 class="mt-4 font-display text-2xl font-semibold tracking-tight text-slate-100">
          {{ plan.name }}
        </h3>
        <p class="mt-1 text-sm text-slate-400">
          {{ plan.courseName }}
        </p>

        <div class="mt-5 flex flex-wrap items-baseline gap-x-2 gap-y-1">
          <span class="font-display text-4xl font-semibold tracking-tight text-slate-100 sm:text-5xl">
            {{ formatMoney(plan.price) }}
          </span>
          <span class="text-sm text-slate-400">{{ priceSuffix(plan) }}</span>
        </div>

        <ul
          v-if="plan.features.length > 0"
          class="mt-6 flex-1"
        >
          <li
            v-for="feature in plan.features"
            :key="feature"
            class="flex items-start gap-3 border-b border-dashed border-line py-3 last:border-b-0"
          >
            <AppIcon
              class="mt-0.5 shrink-0 text-brand-500"
              name="check"
              :size="16"
            />
            <span class="text-sm leading-relaxed text-slate-300">{{ feature }}</span>
          </li>
        </ul>
        <div
          v-else
          class="flex-1"
        />

        <RouterLink
          :to="buyLink"
          class="mt-7 inline-flex h-12 items-center justify-center gap-2 rounded-full px-7 text-base font-semibold"
          :class="plan.kind === 'Group'
            ? 'bg-white text-ink-950 hover:bg-slate-100'
            : 'bg-brand-500 text-on-brand hover:bg-brand-600'"
        >
          Sotib olish
          <AppIcon
            name="chevron-right"
            :size="17"
          />
        </RouterLink>
      </article>
    </div>
  </section>
</template>

<style scoped>
/*
  Guruh tarifi — landing'dagi narx kartasi bilan AYNI fon (`LandingPage.vue`,
  `.price-card`). `surface-brand` ichida `--color-green-*` to'q ohanglarga
  aylanadi, ya'ni gradient to'q yashil chiqadi va matn tokenlari unga mos.
*/
.plan-featured {
  background: linear-gradient(
    155deg,
    var(--color-green-200),
    var(--color-green-100) 48%,
    var(--color-green-50)
  );
  box-shadow: 0 34px 80px -28px rgb(2 42 26 / 0.55);
}
</style>
