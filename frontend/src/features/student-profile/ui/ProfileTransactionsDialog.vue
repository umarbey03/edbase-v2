<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  fetchStudentTransactions,
  isOutgoingTransaction,
  paymentMethodLabel,
  transactionKindLabel,
  transactionKindTone,
} from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { BaseBadge, BaseModal, DataStatus, PaginationBar, SectionLoader } from '@/shared/ui'

/**
 * TO'LIQ MOLIYA JURNALI — "Hammasini ko'rish".
 *
 * Profil agregati OXIRGI 50 tasini beradi; to'liq ro'yxat mavjud
 * `GET /payments/students/{id}/transactions` endpointida (sahifalangan) va
 * shu yerda AYNAN o'sha funksiya qayta ishlatiladi (`entities/payment`).
 *
 * ★ NEGA MAVJUD `StudentAccountDialog` QAYTA ISHLATILMADI: u moliya
 * hisobining BUTUN ekrani (qarz, balans, oylar, blokdan istisno) va uch
 * amalni (`record`/`reverse`/`waive`) chaqiruvchidan kutadi — drawer'da u
 * profil moliya bo'limini IKKI MARTA ko'rsatardi va yana ikki oyna ulashni
 * talab qilardi. Shu yerda kerak bo'lgan narsa faqat sahifalangan jurnal.
 */
const props = defineProps<{ open: boolean; studentId: number | null; studentName: string }>()

const emit = defineEmits<{ close: [] }>()

const PAGE_SIZE = 25

const page = ref(1)

// Har ochilishda birinchi sahifadan: oldingi o'quvchining 3-sahifasi qolib
// ketsa "yozuv yo'q" degan yolg'on bo'sh holat ko'rinardi.
watch(
  () => [props.open, props.studentId] as const,
  () => {
    page.value = 1
  },
)

const enabled = computed(() => props.open && props.studentId !== null)

const query = useQuery({
  queryKey: ['payments', 'transactions', computed(() => props.studentId), page],
  queryFn: ({ signal }) =>
    fetchStudentTransactions(
      props.studentId ?? 0,
      { page: page.value, pageSize: PAGE_SIZE },
      { signal },
    ),
  enabled,
})

const items = computed(() => query.data.value?.items ?? [])
const total = computed(() => query.data.value?.total ?? 0)
const totalPages = computed(() => query.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  query.error.value !== null ? toUserMessage(query.error.value) : null,
)
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="`To‘lov tarixi — ${props.studentName}`"
    @close="emit('close')"
  >
    <SectionLoader
      v-if="query.isPending.value"
      variant="list"
      :rows="5"
      label="To‘lov tarixi yuklanmoqda"
    />

    <DataStatus
      v-else
      :pending="false"
      :error="errorMessage"
      :empty="items.length === 0"
      :retrying="query.isFetching.value"
      empty-icon="wallet"
      empty-title="Yozuv topilmadi"
      empty-text="Bu sahifada pul harakati yo‘q."
      @retry="query.refetch()"
    >
      <ul class="divide-y divide-line rounded-xl border border-line">
        <li
          v-for="item in items"
          :key="item.id"
          class="flex flex-wrap items-center gap-x-3 gap-y-1 p-3"
        >
          <BaseBadge :tone="transactionKindTone(item.kind)">
            {{ transactionKindLabel(item.kind) }}
          </BaseBadge>
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

      <PaginationBar
        :page="page"
        :total-pages="totalPages"
        :total="total"
        @update:page="page = $event"
      />
    </DataStatus>
  </BaseModal>
</template>
