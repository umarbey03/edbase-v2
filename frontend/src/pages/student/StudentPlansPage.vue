<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, onBeforeUnmount, watch } from 'vue'

import {
  cancelShopOrder,
  claimShopOrderPaid,
  createShopOrder,
  fetchShopOffers,
  orderStatusLabel,
  orderStatusTone,
  payShopOrderFromBalance,
  SHOP_ME_KEY,
  useShopMe,
} from '@/entities/shop'
import type { ShopOrderDto, ShopPlanDto } from '@/entities/shop'
import { MY_SUBSCRIPTION_KEY, PLAN_KIND_LABELS } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatDate, formatDateTimeNumeric } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
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

/*
  ══════════════════════════════════════════════════════════════════════
   TO'LOV KARTASI — UCH QADAM (audit, 2026-10-29)
  ══════════════════════════════════════════════════════════════════════
  Ilgari karta to'lovdan oldin ham, keyin ham «To'lov kutilmoqda» deb
  turardi — to'lagan odam "yetib bordimi?" deb qolardi, «Bekor qilish»
  esa sarlavha yonida edi va adashib bosilishi mumkin edi.

  ★ Qadamlar: Buyurtma → To'lov → Tasdiq. «To'ladim» — TASDIQ EMAS,
    signal: xodimlar guruhiga eslatma boradi, karta esa "tekshirilmoqda"
    holatiga o'tadi. Tasdiqni baribir xodim beradi.
  ★ «Bekor qilish» pastda, kichik va tasdiq so'raydi; «To'ladim» dan
    keyin umuman yo'q — to'langan buyurtmani o'quvchi o'zi o'chirmasin.
*/
const confirm = useConfirm()

const claimPaid = useMutation({
  mutationFn: (id: number) => claimShopOrderPaid(id),
  onSuccess: () => {
    showToast('Rahmat! To‘lovni tekshirib, tez orada tasdiqlaymiz', 'success')
    void queryClient.invalidateQueries({ queryKey: SHOP_ME_KEY })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

async function askCancel(item: ShopOrderDto): Promise<void> {
  const ok = await confirm({
    title: 'Buyurtmani bekor qilasizmi?',
    message: 'Agar to‘lov qilgan bo‘lsangiz, bekor qilmang — «To‘ladim» tugmasini bosing.',
    confirmLabel: 'Bekor qilish',
    cancelLabel: 'Ortga',
    tone: 'danger',
  })
  if (ok) cancel.mutate(item.id)
}

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

/*
  ══════════════════════════════════════════════════════════════════════
   JORIY TARIF VA TARIF ALMASHTIRISH (2026-10-29, loyiha egasi qarori Q-1)
  ══════════════════════════════════════════════════════════════════════
  🔴 ILGARI: Mustaqil → Mustaqil+ oy o'rtasida sotib olinsa, yangi tarif
    keyingi oyda boshlanardi va buni hech kim aytmasdi (audit P1-6).
  ★ ENDI: boshqa mustaqil tarif DARHOL boshlanadi, joriy tarifning
    ishlatilmagan kunlari puli balansga o'tadi (server hisoblaydi).
    Joriy tarifni uzaytirish esa avvalgidek — u tugagan kundan.
  ★ Matn TO'LOVDAN OLDIN aytiladi — o'quvchi nima olishini biladi.
*/
const balance = computed(() => me.data.value?.balance ?? 0)
const currentPlanId = computed(() => me.data.value?.currentPlanId ?? null)
const currentEndsAt = computed(() => me.data.value?.currentPlanEndsAt ?? null)

function isCurrent(plan: ShopPlanDto): boolean {
  return plan.kind === 'SelfStudy' && plan.id === currentPlanId.value
}

function switchNote(plan: ShopPlanDto): string | null {
  if (plan.kind !== 'SelfStudy' || currentPlanId.value === null || blockedReason(plan) !== null) return null
  if (isCurrent(plan)) {
    return currentEndsAt.value === null
      ? null
      : `Uzaytirish joriy davr tugagach — ${formatDate(currentEndsAt.value)} dan boshlanadi.`
  }
  return 'To‘lov tasdiqlangan zahoti shu tarif boshlanadi. Joriy tarifning ishlatilmagan kunlari puli balansingizga o‘tadi.'
}

/*
  ★ BALANSDAN TO'LASH (2026-10-29, Q-4): guruhdan qolgan yoki tarif
    almashtirishda tushgan pul yangi Payme to'lovisiz ishlatiladi. Xodim
    tasdig'i kerak emas — pul allaqachon markazda. Ochiq buyurtma bo'lsa
    taklif qilinmaydi: u Payme'da to'lanib qo'ygan bo'lishi mumkin va pul
    ikki marta olinardi (server ham shunday rad etadi).
*/
function canPayFromBalance(plan: ShopPlanDto): boolean {
  return plan.kind === 'SelfStudy'
    && blockedReason(plan) === null
    && pending.value.length === 0
    && balance.value >= plan.price
}

const payFromBalance = useMutation({
  mutationFn: (planId: number) => payShopOrderFromBalance(planId),
  onSuccess: () => {
    showToast('To‘landi — tarif faollashdi', 'success')
    void queryClient.invalidateQueries({ queryKey: SHOP_ME_KEY })
    void queryClient.invalidateQueries({ queryKey: MY_SUBSCRIPTION_KEY })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

async function askPayFromBalance(plan: ShopPlanDto): Promise<void> {
  const note = switchNote(plan)
  const ok = await confirm({
    title: 'Balansdan to‘lash',
    message: `«${plan.name}» uchun balansingizdan ${formatMoney(plan.price)} yechiladi.`,
    confirmLabel: 'To‘lash',
    cancelLabel: 'Ortga',
    tone: 'primary',
    details: [
      `Balansda qoladi: ${formatMoney(balance.value - plan.price)}`,
      note ?? 'Darslar shu zahoti ochiladi.',
    ],
  })
  if (ok) payFromBalance.mutate(plan.id)
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
      <p
        v-if="balance > 0"
        class="mt-2 inline-flex items-center gap-1.5 rounded-full bg-ink-900 px-3 py-1 text-xs text-slate-300 ring-1 ring-inset ring-line"
      >
        <AppIcon
          name="wallet"
          :size="13"
          class="text-slate-400"
        />
        Balansingiz: <span class="font-bold tabular-nums text-slate-100">{{ formatMoney(balance) }}</span>
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
            {{ item.paidClaimedAt === null ? 'To‘lov kutilmoqda' : 'To‘lov tekshirilmoqda' }}
          </p>
          <p class="mt-1 truncate text-base font-bold text-slate-100">
            {{ item.planName }}
          </p>
          <p class="text-xs text-slate-400">
            {{ item.courseName }}
          </p>
        </div>
      </div>

      <!-- Qadamlar: Buyurtma → To'lov → Tasdiq -->
      <ol
        class="mt-3 flex items-center gap-1.5 text-[11px] font-semibold"
        aria-label="Buyurtma bosqichlari"
      >
        <li
          v-for="(step, index) in ['Buyurtma', 'To‘lov', 'Tasdiq']"
          :key="step"
          class="flex items-center gap-1.5"
        >
          <span
            class="rounded-full px-2.5 py-1"
            :class="index < (item.paidClaimedAt === null ? 1 : 2)
              ? 'bg-brand-500 text-on-brand'
              : 'bg-ink-900 text-slate-400 ring-1 ring-inset ring-line'"
            :aria-current="index === (item.paidClaimedAt === null ? 1 : 2) ? 'step' : undefined"
          >{{ index + 1 }}. {{ step }}</span>
          <span
            v-if="index < 2"
            class="text-slate-500"
            aria-hidden="true"
          >→</span>
        </li>
      </ol>

      <!-- To'ladim bosilgan — endi kutish, bekor qilish yo'q. -->
      <p
        v-if="item.paidClaimedAt !== null"
        class="mt-4 rounded-lg bg-ink-900 px-3 py-2.5 text-sm text-slate-300 ring-1 ring-inset ring-line"
      >
        To‘lovingiz haqida xabar oldik. Ish vaqtida odatda 1 soat ichida tasdiqlaymiz —
        tasdiqlangach shu sahifa va Telegram orqali bilasiz.
      </p>

      <template v-else>
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
            class="inline-flex items-center rounded-lg bg-[#00CCCC] px-4 py-2 text-sm font-bold text-[#062a2a]"
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

        <div class="mt-4 flex flex-wrap items-center justify-between gap-2">
          <BaseButton
            :loading="claimPaid.isPending.value && claimPaid.variables.value === item.id"
            @click="claimPaid.mutate(item.id)"
          >
            To‘ladim
          </BaseButton>
          <button
            type="button"
            class="text-xs font-medium text-slate-400 underline underline-offset-2 hover:text-slate-200"
            :disabled="cancel.isPending.value"
            @click="askCancel(item)"
          >
            Buyurtmani bekor qilish
          </button>
        </div>
      </template>
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
        <div class="flex flex-wrap items-center gap-1.5">
          <BaseBadge :tone="plan.kind === 'Group' ? 'accent' : 'neutral'">
            {{ PLAN_KIND_LABELS[plan.kind] }}
          </BaseBadge>
          <BaseBadge
            v-if="isCurrent(plan)"
            tone="success"
          >
            Joriy tarif
          </BaseBadge>
        </div>
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

        <!-- Qachon boshlanadi — to'lovdan OLDIN aytiladi (Q-1). -->
        <p
          v-if="switchNote(plan) !== null"
          class="mt-3 rounded-lg bg-ink-900/60 px-3 py-2 text-xs leading-relaxed text-slate-300 ring-1 ring-inset ring-line"
          v-text="switchNote(plan)"
        />

        <p
          v-if="blockedReason(plan) !== null"
          class="mt-4 text-xs text-slate-400"
          v-text="blockedReason(plan)"
        />
        <template v-else>
          <BaseButton
            class="mt-4 w-full"
            :loading="order.isPending.value && order.variables.value === plan.id"
            :disabled="order.isPending.value || payFromBalance.isPending.value"
            @click="order.mutate(plan.id)"
          >
            {{
              pending.some((o) => o.planId === plan.id)
                ? 'To‘lov ma’lumotlari'
                : isCurrent(plan) ? 'Uzaytirish' : 'Sotib olish'
            }}
          </BaseButton>
          <BaseButton
            v-if="canPayFromBalance(plan)"
            class="mt-2 w-full"
            variant="secondary"
            :loading="payFromBalance.isPending.value && payFromBalance.variables.value === plan.id"
            :disabled="order.isPending.value || payFromBalance.isPending.value"
            @click="askPayFromBalance(plan)"
          >
            Balansdan to‘lash
          </BaseButton>
        </template>
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
