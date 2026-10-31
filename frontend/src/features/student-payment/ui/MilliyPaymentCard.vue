<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import {
  currentPeriod,
  fetchLessonCharges,
  fetchStudentAccount,
  isOutgoingTransaction,
  transactionKindLabel,
} from '@/entities/payment'
import { formatDateNumeric } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { AppIcon, BaseSpinner } from '@/shared/ui'

/**
 * ══════════════════════════════════════════════════════════════════════════
 * MILLIY O'QUVCHISINING TO'LOV KARTASI (loyiha egasi, 2026-10-31)
 * ══════════════════════════════════════════════════════════════════════════
 *
 * Milliy narxi 8 ta asosiy ustoz darsi uchun (540 000 so'm); har dars
 * o'tganda balansdan bitta dars narxi yechiladi. Ilgari bu raqamlarni faqat
 * xodim ko'rardi — o'quvchi "pulim qachon tugaydi?" deb so'rashga majbur edi.
 *
 * ★ "N TA DARSGA YETADI" — BALANSDAN, PAKET SANAGICHIDAN EMAS: serverda
 *   "paket" degan yozuv yo'q, bor narsa — balans va dars-dars yechimlar.
 *   Dars narxi oxirgi yechilgan darsdan olinadi (chegirmagacha), bo'lmasa
 *   540 000 / 8. Shu bilan ko'rsatilgan raqam hech qachon o'ylab topilgan
 *   bo'lmaydi.
 * ★ ESLATMA: balans bir paketdan (8 dars) kam qolsa — kulrang izoh; 2 ta
 *   darsdan kam qolsa — sariq, to'lash tugmasi bilan. Qarz bo'lsa — qizil.
 *   Ayblash yo'q: nima qilish kerakligi aytiladi.
 */
const props = defineProps<{ studentId: number }>()
const emit = defineEmits<{ pay: [] }>()

/** Milliy paketi — loyiha egasi belgilagan narx (`landing/model/content.ts` PRICE). */
const PACKAGE_LESSONS = 8
const PACKAGE_PRICE = 540_000

const accountQuery = useQuery({
  queryKey: computed(() => ['payments', 'student', props.studentId]),
  queryFn: ({ signal }) => fetchStudentAccount(props.studentId, { signal }),
})

const chargesQuery = useQuery({
  queryKey: computed(() => ['payments', 'student', props.studentId, 'charges', currentPeriod()]),
  queryFn: ({ signal }) => fetchLessonCharges(props.studentId, { period: currentPeriod() }, { signal }),
})

const account = computed(() => accountQuery.data.value ?? null)

const perLesson = computed(() => {
  const charges = chargesQuery.data.value ?? []
  const last = [...charges]
    .filter((c) => c.stickerAmount > 0)
    .sort((a, b) => b.scheduledStart.localeCompare(a.scheduledStart))[0]
  return last?.stickerAmount ?? PACKAGE_PRICE / PACKAGE_LESSONS
})

const lessonsLeft = computed(() =>
  account.value === null ? null : Math.floor(account.value.balance / perLesson.value),
)

const monthCharged = computed(() => (chargesQuery.data.value ?? []).filter((c) => c.chargedAmount > 0))
const monthSum = computed(() => monthCharged.value.reduce((sum, c) => sum + c.chargedAmount, 0))

type Tone = 'debt' | 'low' | 'soon' | 'ok'
const tone = computed<Tone>(() => {
  const a = account.value
  if (a === null) return 'ok'
  if (a.debt > 0) return 'debt'
  const left = lessonsLeft.value ?? 0
  if (left <= 2) return 'low'
  if (left < PACKAGE_LESSONS) return 'soon'
  return 'ok'
})

const recent = computed(() => (account.value?.recentTransactions ?? []).slice(0, 5))
</script>

<template>
  <section class="rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5">
    <p class="flex items-center gap-1.5 text-xs font-bold uppercase tracking-[1.4px] text-slate-400">
      <AppIcon
        name="wallet"
        :size="15"
      />
      To‘lov
    </p>

    <div
      v-if="accountQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="account === null"
      class="mt-3 text-sm text-slate-400"
    >
      To‘lov ma’lumotini yuklab bo‘lmadi. Birozdan keyin qayta oching.
    </p>

    <template v-else>
      <!-- Imtiyozli o'quvchi: raqamlarning unga ma'nosi yo'q. -->
      <p
        v-if="account.exempt"
        class="mt-3 text-sm text-slate-300"
      >
        Siz to‘lovdan ozod qilingansiz.
      </p>

      <template v-else>
        <div class="mt-3 flex flex-wrap items-baseline gap-x-2">
          <span
            class="text-[28px] font-extrabold leading-none tabular-nums"
            :class="tone === 'debt' ? 'text-rose-300' : 'text-slate-100'"
          >
            {{ formatMoney(tone === 'debt' ? account.debt : account.balance) }}
          </span>
          <span class="text-sm font-bold text-slate-400">
            so‘m · {{ tone === 'debt' ? 'qarz' : 'balans' }}
          </span>
        </div>

        <p
          v-if="tone !== 'debt' && lessonsLeft !== null"
          class="mt-1.5 text-sm text-slate-300"
        >
          Balansingiz yana <b>{{ lessonsLeft }} ta</b> darsga yetadi
          <span class="text-slate-500">· bitta dars {{ formatMoney(perLesson) }} so‘m</span>
        </p>

        <!-- Paket bo'yicha chiziq: balans 8 darsning qanchasini qoplaydi. -->
        <div
          v-if="tone !== 'debt' && lessonsLeft !== null"
          class="mt-3 flex gap-1"
          aria-hidden="true"
        >
          <span
            v-for="step in PACKAGE_LESSONS"
            :key="step"
            class="h-1.5 flex-1 rounded-full"
            :class="step <= Math.min(lessonsLeft, PACKAGE_LESSONS)
              ? (tone === 'low' ? 'bg-amber-400' : 'bg-brand-500')
              : 'bg-ink-750'"
          />
        </div>

        <div
          v-if="tone === 'debt' || tone === 'low'"
          class="mt-4 rounded-lg px-3 py-2.5 text-sm ring-1 ring-inset"
          :class="tone === 'debt'
            ? 'bg-rose-500/10 text-rose-200 ring-rose-500/25'
            : 'bg-amber-500/10 text-amber-200 ring-amber-500/30'"
          role="status"
        >
          <template v-if="tone === 'debt'">
            To‘lov bo‘yicha qarz bor. Darslar to‘xtab qolmasligi uchun to‘lovni amalga oshiring.
          </template>
          <template v-else>
            To‘lov vaqti yaqin: balans {{ lessonsLeft }} ta darsga yetadi.
            Keyingi paket — {{ formatMoney(PACKAGE_PRICE) }} so‘m ({{ PACKAGE_LESSONS }} ta dars).
          </template>
        </div>

        <p
          v-else-if="tone === 'soon'"
          class="mt-3 text-xs text-slate-400"
        >
          Keyingi paket — {{ formatMoney(PACKAGE_PRICE) }} so‘m ({{ PACKAGE_LESSONS }} ta dars).
          Balans 2 ta darsdan kam qolganda eslatamiz.
        </p>

        <button
          v-if="tone === 'debt' || tone === 'low'"
          type="button"
          class="mt-3 inline-flex min-h-11 items-center gap-2 rounded-lg bg-brand-500 px-4 text-sm font-bold text-on-brand hover:bg-brand-600"
          @click="emit('pay')"
        >
          To‘lash
          <AppIcon
            name="chevron-right"
            :size="16"
          />
        </button>

        <p class="mt-4 text-xs text-slate-400">
          Bu oy: <b class="text-slate-200">{{ monthCharged.length }} ta dars</b>
          · {{ formatMoney(monthSum) }} so‘m yechildi
        </p>
      </template>

      <!-- Oxirgi yozuvlar -->
      <ul
        v-if="recent.length > 0"
        class="mt-4 divide-y divide-line border-t border-line"
      >
        <li
          v-for="row in recent"
          :key="row.id"
          class="flex items-center justify-between gap-3 py-2.5 text-sm"
        >
          <span class="min-w-0">
            <span class="block truncate text-slate-200">{{ transactionKindLabel(row.kind) }}</span>
            <span class="text-xs text-slate-500">{{ formatDateNumeric(row.createdAt) }}</span>
          </span>
          <span class="shrink-0 font-bold tabular-nums text-slate-200">
            {{ isOutgoingTransaction(row.kind) ? '−' : '' }}{{ formatMoney(row.amount) }}
          </span>
        </li>
      </ul>
    </template>
  </section>
</template>
