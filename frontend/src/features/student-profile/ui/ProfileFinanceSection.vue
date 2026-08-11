<script setup lang="ts">
import { computed } from 'vue'

import {
  blockScopeShortLabel,
  isOutgoingTransaction,
  paymentMethodLabel,
  paymentStatusLabel,
  paymentStatusTone,
  periodLabel,
  transactionKindLabel,
  transactionKindTone,
} from '@/entities/payment'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { ProfileFinanceDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard } from '@/shared/ui'

/**
 * 2-BO'LIM: TO'LOVLAR — oylik davrlar jadvali + moliya jurnali.
 *
 * 🔴 BU KOMPONENT `finance === null` BO'LGANDA UMUMAN RENDER QILINMAYDI
 * (shart chaqiruvchi drawer'da). Sabab: ustoz/kurator javobida moliya bloki
 * SERVERDA kesiladi — ya'ni yashiriladigan ma'lumot yo'q, KELMAYDI. Shu
 * sababli bu yerda `null` shoxi ham yo'q: prop majburiy.
 *
 * 🔴 PUL KIRITISH/YECHISH TUGMALARI FAQAT ADMIN'DA (`canManageMoney`) —
 * loyiha egasi: *"bunisi faqat admin panelda"*. O'quv bo'limi raqamlarni
 * KO'RADI, lekin o'zgartira olmaydi. Tugmalarning O'ZI mavjud
 * `features/payment-actions` oynalarini ochadi (nusxa olinmaydi).
 *
 * ★ "QAYSI DARS UCHUN" SAVOLI: to'lov modeli OYLIK (`o'quvchi × guruh × oy`),
 * dars kesimi modelda YO'Q. Shuning uchun jadvalda oy · guruh · summa ·
 * to'langan · qoldiq · holat va O'SHA OYDAGI DARSLAR SONI ko'rsatiladi —
 * xodim "540 000 / 8 dars" deb tushuntira oladi.
 */
const props = defineProps<{
  finance: ProfileFinanceDto
  /** Faqat `Admin`: pul kiritish va yechish tugmalari. */
  canManageMoney: boolean
}>()

const emit = defineEmits<{ record: []; reverse: []; 'show-transactions': [] }>()

const periods = computed(() => props.finance.periods)

/**
 * Jurnal `null` bo'lishi XATO EMAS: o'quvchining o'zi so'raganda server uni
 * yubormaydi. Bo'lim qolgan qismi (balans, qarz, oylar) baribir ko'rinadi.
 */
const transactions = computed(() => props.finance.transactions)

const blocked = computed(() => props.finance.blockScope !== 'None')
</script>

<template>
  <BaseCard title="To‘lovlar">
    <template
      v-if="props.canManageMoney"
      #actions
    >
      <BaseButton
        size="sm"
        @click="emit('record')"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        To‘lov kiritish
      </BaseButton>
      <BaseButton
        size="sm"
        variant="secondary"
        @click="emit('reverse')"
      >
        <template #icon>
          <AppIcon
            name="arrow-left"
            :size="14"
          />
        </template>
        Yechib olish
      </BaseButton>
    </template>

    <!-- --------------------------------------------------------- xulosa -->
    <dl class="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums"
          :class="props.finance.totalDue > 0 ? 'text-rose-400' : 'text-green-400'"
          v-text="formatMoney(props.finance.totalDue)"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          Joriy qarz
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums text-brand-400"
          v-text="formatMoney(props.finance.balance)"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          Balans
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd
          class="text-base font-bold tabular-nums text-green-400"
          v-text="formatMoney(props.finance.totalPaid)"
        />
        <dt class="mt-0.5 text-[11px] text-slate-400">
          Jami to‘langan
        </dt>
      </div>
      <div class="rounded-lg border border-line bg-ink-800 p-3">
        <dd class="mt-0.5">
          <BaseBadge :tone="blocked ? 'danger' : 'success'">
            {{ blocked ? blockScopeShortLabel(props.finance.blockScope) : 'Blok yo‘q' }}
          </BaseBadge>
        </dd>
        <dt class="mt-1.5 text-[11px] text-slate-400">
          Bloklash holati
        </dt>
      </div>
    </dl>

    <!-- ------------------------------------------------------ oylik davrlar -->
    <h3 class="mb-2 mt-4 text-xs font-semibold uppercase tracking-wide text-slate-400">
      Oylik hisob
    </h3>

    <!--
      ★ `EmptyState` ATAYLAB ISHLATILMADI: u 40px vertikal padding bilan
      keladi va drawer'da besh bo'lim ketma-ket turadi — har biriga katta
      bo'sh kartochka qo'yilsa panel cho'zilib, asosiy ma'lumot ekrandan
      chiqib ketardi. Bo'lim ICHIDAGI bo'sh holat bir qatorlik.
    -->
    <p
      v-if="periods.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hali birorta hisob oyi ochilmagan.
    </p>

    <template v-else>
      <!-- Telefon: kartochka ro'yxati (jadval 360px ekranga sig'maydi). -->
      <ul class="divide-y divide-line rounded-xl border border-line md:hidden">
        <li
          v-for="period in periods"
          :key="`${period.groupId}-${period.month}`"
          class="p-3"
        >
          <div class="flex items-start justify-between gap-2">
            <p class="min-w-0 flex-1">
              <span
                class="block text-sm font-medium text-slate-100"
                v-text="periodLabel(period.month)"
              />
              <span
                class="block truncate text-xs text-slate-400"
                v-text="period.groupName"
              />
            </p>
            <BaseBadge :tone="paymentStatusTone(period.status)">
              {{ paymentStatusLabel(period.status) }}
            </BaseBadge>
          </div>
          <p class="mt-1.5 text-xs tabular-nums text-slate-300">
            {{ formatMoney(period.paidAmount) }} / {{ formatMoney(period.amount) }}
            <span
              v-if="period.outstanding > 0"
              class="text-rose-400"
            >· qoldiq {{ formatMoney(period.outstanding) }}</span>
          </p>
          <p class="mt-0.5 text-[11px] text-slate-400">
            O‘tkazilgan dars: {{ period.sessionCount }}
          </p>
        </li>
      </ul>

      <!-- Desktop: jadval. Gorizontal skroll SHU konteynerda. -->
      <div class="scroll-x-safe scrollbar-slim hidden rounded-xl border border-line md:block">
        <table class="zn-table">
          <thead>
            <tr>
              <th>Oy</th>
              <th>Guruh</th>
              <th>Summa</th>
              <th>To‘langan</th>
              <th>Qoldiq</th>
              <th>Holat</th>
              <th>Dars</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="period in periods"
              :key="`${period.groupId}-${period.month}`"
            >
              <td
                class="font-medium text-slate-100"
                v-text="periodLabel(period.month)"
              />
              <td
                class="text-slate-400"
                v-text="period.groupName"
              />
              <td
                class="tabular-nums text-slate-300"
                v-text="formatMoney(period.amount)"
              />
              <td
                class="tabular-nums text-slate-300"
                v-text="formatMoney(period.paidAmount)"
              />
              <td
                class="tabular-nums"
                :class="period.outstanding > 0 ? 'text-rose-400' : 'text-slate-400'"
                v-text="formatMoney(period.outstanding)"
              />
              <td>
                <BaseBadge :tone="paymentStatusTone(period.status)">
                  {{ paymentStatusLabel(period.status) }}
                </BaseBadge>
              </td>
              <!-- ★ "O'sha oyda o'tkazilgan dars soni" — oylik summani
                   tushuntirish uchun (dars-bahosi modelda yo'q). -->
              <td
                class="tabular-nums text-slate-400"
                v-text="period.sessionCount"
              />
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <!-- ---------------------------------------------------------- jurnal -->
    <div class="mb-2 mt-4 flex flex-wrap items-center justify-between gap-2">
      <h3 class="text-xs font-semibold uppercase tracking-wide text-slate-400">
        To‘lov tarixi
      </h3>
      <!--
        "Hammasini ko'rish" FAQAT 50 tadan ko'p yozuv bo'lganda: agregat
        oxirgi 50 tasini beradi, to'liq ro'yxat esa mavjud
        `/payments/students/{id}/transactions` endpointida (sahifalangan).
      -->
      <BaseButton
        v-if="props.finance.hasMoreTransactions"
        size="sm"
        variant="ghost"
        @click="emit('show-transactions')"
      >
        Hammasini ko‘rish
      </BaseButton>
    </div>

    <p
      v-if="transactions === null"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      To‘lov jurnali bu ko‘rinishda ko‘rsatilmaydi.
    </p>

    <p
      v-else-if="transactions.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Pul harakati hali qayd etilmagan.
    </p>

    <ul
      v-else
      class="divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="item in transactions"
        :key="item.id"
        class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3"
      >
        <BaseBadge :tone="transactionKindTone(item.kind)">
          {{ transactionKindLabel(item.kind) }}
        </BaseBadge>
        <!-- Chiqim MINUS bilan: kassir "pul kirdi" deb o'qib qolmasin. -->
        <span
          class="text-sm font-semibold tabular-nums"
          :class="isOutgoingTransaction(item.kind) ? 'text-rose-400' : 'text-slate-100'"
        >
          {{ isOutgoingTransaction(item.kind) ? '−' : '' }}{{ formatMoney(item.amount) }}
        </span>
        <span class="text-xs text-slate-400">
          {{ formatDateTime(item.createdAt) }}
        </span>
        <span
          v-if="item.method !== null"
          class="text-xs text-slate-400"
        >
          · {{ paymentMethodLabel(item.method) }}
        </span>
        <span
          v-if="item.groupName !== null"
          class="truncate text-xs text-slate-400"
        >
          · {{ item.groupName }}
        </span>
        <span
          v-if="item.receiptNo !== null"
          class="font-mono text-[11px] text-slate-400"
        >
          · {{ item.receiptNo }}
        </span>
        <span
          v-if="item.actorName !== null"
          class="text-xs text-slate-400"
        >
          · {{ item.actorName }}
        </span>
        <p
          v-if="item.note !== null"
          class="w-full text-[11px] leading-relaxed text-slate-400"
          v-text="item.note"
        />
      </li>
    </ul>
  </BaseCard>
</template>
