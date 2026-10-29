<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, watch } from 'vue'

import {
  cancelShopOrder,
  createShopOrder,
  fetchShopOffers,
  orderStatusLabel,
  orderStatusTone,
  SHOP_ME_KEY,
  useShopMe,
} from '@/entities/shop'
import type { ShopOrderDto, ShopPlanDto } from '@/entities/shop'
import { MY_SUBSCRIPTION_KEY, PLAN_KIND_LABELS } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseBadge, BaseButton, BaseSpinner } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  TARIFLAR (2026-10-29, onlayn sotuv)
 * ════════════════════════════════════════════════════════════════════════
 *
 * O'quvchi tarif tanlaydi → buyurtma kodi oladi → Payme/Click'da to'laydi →
 * o'quv bo'limi to'lovni ko'rib tasdiqlaydi. Tasdiqlangach:
 *   • mustaqil o'qish — darslar darhol ochiladi;
 *   • guruhda jonli dars — pul balansga tushadi, o'quv bo'limi guruhga joylaydi.
 *
 * ★ TO'LOV KUTILAYOTGAN BUYURTMA TEPADA: kod va summa — o'quvchi to'lov
 *   ilovasiga ko'chiradigan yagona ma'lumot, shuning uchun eng ko'zga
 *   tashlanadigan joyda turadi.
 * ★ HOLAT O'ZI YANGILANADI (30 s): kassir tasdiqlagan zahoti sahifa
 *   "Tasdiqlangan" ga o'tadi, o'quvchi qayta yuklashi shart emas.
 * ════════════════════════════════════════════════════════════════════════
 */
const queryClient = useQueryClient()

const offersQuery = useQuery({
  queryKey: ['shop', 'offers'],
  queryFn: ({ signal }) => fetchShopOffers({ signal }),
  staleTime: 60_000,
})
const me = useShopMe()

const offers = computed(() => offersQuery.data.value ?? null)
const plans = computed(() => offers.value?.plans ?? [])
const orders = computed(() => me.data.value?.orders ?? [])
const pending = computed(() => orders.value.filter((o) => o.status === 'Pending'))
const history = computed(() => orders.value.filter((o) => o.status !== 'Pending'))
const inGroup = computed(() => me.data.value?.inGroup === true)

// To'lov kutilayotgan bo'lsa holatni tez-tez so'raymiz — tasdiq darhol ko'rinsin.
const pollTimer = setInterval(() => {
  if (pending.value.length > 0) void queryClient.invalidateQueries({ queryKey: SHOP_ME_KEY })
}, 30_000)
onBeforeUnmount(() => clearInterval(pollTimer))

// Buyurtma tasdiqlandi — obuna holati ham yangilansin (tablar va bosh sahifa o'zgaradi).
const confirmedCount = computed(() => orders.value.filter((o) => o.status === 'Confirmed').length)
watch(confirmedCount, (now, before) => {
  if (now > before) void queryClient.invalidateQueries({ queryKey: MY_SUBSCRIPTION_KEY })
})

function blockedReason(plan: ShopPlanDto): string | null {
  if (plan.kind === 'SelfStudy' && inGroup.value) {
    return 'Siz guruhda o‘qiyapsiz — bu tarif guruhsiz o‘quvchilar uchun.'
  }
  return null
}

/**
 * ★ GURUH TARIFI (Milliy) OYGA EMAS, 8 TA DARSGA (loyiha egasi): "/ oy"
 *   deyilsa 540 000 "oylik to'lov" bo'lib o'qilardi (audit, 2026-10-29).
 */
function priceSuffix(plan: ShopPlanDto): string {
  if (plan.kind === 'Group') return '/ 8 ta dars'
  return plan.months === 1 ? '/ oy' : `/ ${plan.months} oy`
}

const order = useMutation({
  mutationFn: (planId: number) => createShopOrder(planId),
  onSuccess: () => {
    showToast('Buyurtma yaratildi — endi to‘lovni amalga oshiring', 'success')
    void queryClient.invalidateQueries({ queryKey: SHOP_ME_KEY })
    window.scrollTo({ top: 0, behavior: 'smooth' })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

const cancel = useMutation({
  mutationFn: (id: number) => cancelShopOrder(id),
  onSuccess: () => {
    showToast('Buyurtma bekor qilindi', 'success')
    void queryClient.invalidateQueries({ queryKey: SHOP_ME_KEY })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

async function copy(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    showToast('Nusxa olindi', 'success')
  } catch {
    showToast('Nusxa olib bo‘lmadi — qo‘lda yozib oling', 'error')
  }
}

function orderKindNote(item: ShopOrderDto): string {
  return item.kind === 'SelfStudy'
    ? 'Tasdiqlangach darslar shu zahoti ochiladi.'
    : 'Tasdiqlangach o‘quv bo‘limi sizni guruhga joylab, dars vaqtini xabar qiladi.'
}
</script>

<template>
  <div class="mx-auto max-w-[760px] space-y-4 pt-2">
    <header>
      <h1 class="text-xl font-extrabold text-slate-100">
        Tariflar
      </h1>
      <p class="mt-1 text-sm text-slate-400">
        O‘zingizga mos o‘qish shaklini tanlang. To‘lov tasdiqlangach darslar ochiladi.
      </p>
    </header>

    <!-- ═══════════════════════════════════ TO'LOV KUTILAYOTGAN BUYURTMA ═══ -->
    <section
      v-for="item in pending"
      :key="item.id"
      class="rounded-xl border border-amber-500/40 bg-amber-500/10 p-[18px] lg:p-5"
    >
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <p class="text-xs font-bold uppercase tracking-[1.4px] text-amber-200">
            To‘lov kutilmoqda
          </p>
          <p class="mt-1 truncate text-base font-bold text-slate-100">
            {{ item.planName }}
          </p>
          <p class="text-xs text-slate-400">
            {{ item.courseName }}
          </p>
        </div>
        <BaseButton
          size="sm"
          variant="ghost"
          :loading="cancel.isPending.value && cancel.variables.value === item.id"
          @click="cancel.mutate(item.id)"
        >
          Bekor qilish
        </BaseButton>
      </div>

      <div class="mt-4 grid gap-2 sm:grid-cols-2">
        <button
          type="button"
          class="flex items-center justify-between rounded-lg bg-ink-900 px-3 py-2.5 text-left ring-1 ring-inset ring-line"
          @click="copy(String(item.amount))"
        >
          <span>
            <span class="block text-[11px] text-slate-400">To‘lov summasi</span>
            <span class="text-lg font-extrabold tabular-nums text-slate-100">{{ formatMoney(item.amount) }}</span>
          </span>
          <AppIcon
            name="clipboard"
            :size="16"
            class="text-slate-400"
          />
        </button>
        <button
          type="button"
          class="flex items-center justify-between rounded-lg bg-ink-900 px-3 py-2.5 text-left ring-1 ring-inset ring-line"
          @click="copy(item.code)"
        >
          <span>
            <span class="block text-[11px] text-slate-400">Buyurtma kodi (izohga yozing)</span>
            <span class="text-lg font-extrabold tracking-wider text-brand-300">{{ item.code }}</span>
          </span>
          <AppIcon
            name="clipboard"
            :size="16"
            class="text-slate-400"
          />
        </button>
      </div>

      <p
        v-if="offers?.paymentInstructions"
        class="mt-3 text-sm text-slate-300"
        v-text="offers.paymentInstructions"
      />

      <div
        v-if="offers?.paymeUrl || offers?.clickUrl"
        class="mt-3 flex flex-wrap gap-2"
      >
        <a
          v-if="offers?.paymeUrl"
          :href="offers.paymeUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="inline-flex items-center rounded-lg bg-[#00CCCC] px-4 py-2 text-sm font-bold text-ink-950"
        >
          Payme orqali to‘lash
        </a>
        <a
          v-if="offers?.clickUrl"
          :href="offers.clickUrl"
          target="_blank"
          rel="noopener noreferrer"
          class="inline-flex items-center rounded-lg bg-[#0073FF] px-4 py-2 text-sm font-bold text-white"
        >
          Click orqali to‘lash
        </a>
      </div>

      <p class="mt-3 flex items-center gap-1.5 text-xs text-slate-400">
        <AppIcon
          name="clock"
          :size="13"
        />
        {{ orderKindNote(item) }} Holat shu sahifada o‘zi yangilanadi.
      </p>
    </section>

    <!-- ═════════════════════════════════════════════════════ TARIFLAR ═══ -->
    <div
      v-if="offersQuery.isPending.value"
      class="flex justify-center py-10"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="plans.length === 0"
      class="rounded-xl border border-line bg-ink-900 p-5 text-sm text-slate-400"
    >
      Hozircha sotuvda tarif yo‘q. O‘quv bo‘limi bilan bog‘laning.
    </p>

    <div
      v-else
      class="grid gap-3 sm:grid-cols-2"
    >
      <article
        v-for="plan in plans"
        :key="plan.id"
        class="flex flex-col rounded-xl border p-[18px] lg:p-5"
        :class="plan.kind === 'Group' ? 'border-brand-500/50 bg-brand-500/5' : 'border-line bg-ink-900'"
      >
        <BaseBadge
          :tone="plan.kind === 'Group' ? 'accent' : 'neutral'"
          class="self-start"
        >
          {{ PLAN_KIND_LABELS[plan.kind] }}
        </BaseBadge>
        <h2 class="mt-2 text-lg font-extrabold text-slate-100">
          {{ plan.name }}
        </h2>
        <p class="text-xs text-slate-400">
          {{ plan.courseName }}
        </p>

        <p class="mt-3">
          <span class="text-[26px] font-extrabold tabular-nums text-slate-100">{{ formatMoney(plan.price) }}</span>
          <span class="ml-1 text-sm text-slate-400">{{ priceSuffix(plan) }}</span>
        </p>

        <ul
          v-if="plan.features.length > 0"
          class="mt-3 flex-1 space-y-1.5"
        >
          <li
            v-for="feature in plan.features"
            :key="feature"
            class="flex items-start gap-2 text-sm text-slate-300"
          >
            <AppIcon
              name="check"
              :size="15"
              class="mt-0.5 shrink-0 text-emerald-400"
            />
            {{ feature }}
          </li>
        </ul>
        <div
          v-else
          class="flex-1"
        />

        <!-- Darslik hali to'liq emas — sotib olishdan OLDIN aytiladi (audit, 2026-10-29). -->
        <p
          v-if="plan.includesInteractiveBook"
          class="mt-3 text-xs leading-relaxed text-slate-400"
        >
          Interaktiv darslik ATF kursidan boshlab bosqichma-bosqich ochiladi.
        </p>

        <p
          v-if="blockedReason(plan) !== null"
          class="mt-4 text-xs text-slate-400"
          v-text="blockedReason(plan)"
        />
        <BaseButton
          v-else
          class="mt-4 w-full"
          :loading="order.isPending.value && order.variables.value === plan.id"
          :disabled="order.isPending.value"
          @click="order.mutate(plan.id)"
        >
          {{ pending.some((o) => o.planId === plan.id) ? 'To‘lov ma’lumotlari' : 'Sotib olish' }}
        </BaseButton>
      </article>
    </div>

    <!-- ═════════════════════════════════════════════════════ TARIX ═══ -->
    <section
      v-if="history.length > 0"
      class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5"
    >
      <h2 class="mb-2 text-[11px] font-bold uppercase tracking-[1.2px] text-dim">
        Buyurtmalarim
      </h2>
      <ul class="divide-y divide-line">
        <li
          v-for="item in history"
          :key="item.id"
          class="flex items-center gap-3 py-2.5 first:pt-0 last:pb-0"
        >
          <div class="min-w-0 flex-1">
            <p class="truncate text-sm font-medium text-slate-100">
              {{ item.planName }} · {{ formatMoney(item.amount) }}
            </p>
            <p class="text-[11px] text-slate-400">
              {{ item.code }} · {{ formatDateTimeNumeric(item.createdAt) }}
              <template v-if="item.decisionNote">
                · {{ item.decisionNote }}
              </template>
            </p>
          </div>
          <BaseBadge :tone="orderStatusTone(item.status)">
            {{ orderStatusLabel(item.status) }}
          </BaseBadge>
        </li>
      </ul>
    </section>
  </div>
</template>
