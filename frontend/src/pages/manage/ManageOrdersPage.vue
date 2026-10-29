<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  confirmShopOrder,
  fetchStaffOrders,
  orderStatusLabel,
  orderStatusTone,
  rejectShopOrder,
} from '@/entities/shop'
import type { PurchaseOrderStatusName, StaffOrderDto } from '@/entities/shop'
import { PLAN_KIND_LABELS } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import { formatPhone } from '@/shared/lib/phone'
import { showToast } from '@/shared/lib/useToast'
import { BaseBadge, BaseButton, BaseCard, DataStatus, PageHeader } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  ONLAYN BUYURTMALAR (2026-10-29)
 * ════════════════════════════════════════════════════════════════════════
 *
 * O'quvchi saytda tarif tanlab, Payme/Click'da to'lagan. Kassir to'lov
 * xabarini (Payme/Click Telegram guruhi) ko'rib, shu yerda yoki xodimlar
 * Telegram guruhidagi tugma orqali tasdiqlaydi.
 *
 * ★ TASDIQLASH NIMA QILADI:
 *   • mustaqil o'qish — obuna ochiladi, darslar darhol ochiladi;
 *   • guruh tarifi — pul o'quvchi balansiga yoziladi; o'quvchini
 *     «Guruhlar» bo'limidan guruhga qo'shasiz, oy ochilganda balansdan yopiladi.
 * ★ Ikki kassir bir vaqtda bossa ham pul bir marta yoziladi (server qoidasi).
 * ════════════════════════════════════════════════════════════════════════
 */
const queryClient = useQueryClient()

type Filter = PurchaseOrderStatusName | 'All'
const FILTERS: { value: Filter, label: string }[] = [
  { value: 'Pending', label: 'Kutilmoqda' },
  { value: 'Confirmed', label: 'Tasdiqlangan' },
  { value: 'Rejected', label: 'Rad etilgan' },
  { value: 'All', label: 'Hammasi' },
]
const filter = ref<Filter>('Pending')

const listQuery = useQuery({
  queryKey: computed(() => ['shop', 'orders', filter.value]),
  queryFn: ({ signal }) => fetchStaffOrders(filter.value === 'All' ? null : filter.value, { signal }),
  refetchInterval: 30_000,
})

const rows = computed(() => listQuery.data.value ?? [])
const listError = computed(() => (listQuery.error.value === null ? null : toUserMessage(listQuery.error.value)))

const rejectingId = ref<number | null>(null)
const rejectReason = ref('')

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['shop', 'orders'] })
}

const confirm = useMutation({
  mutationFn: (order: StaffOrderDto) => confirmShopOrder(order.id, null),
  onSuccess: (order) => {
    showToast(
      order.kind === 'SelfStudy'
        ? `${order.code} tasdiqlandi — obuna ochildi`
        : `${order.code} tasdiqlandi — pul balansga tushdi, o‘quvchini guruhga qo‘shing`,
      'success',
    )
    refresh()
  },
  onError: (error) => {
    showToast(toUserMessage(error), 'error')
    refresh()
  },
})

const reject = useMutation({
  mutationFn: (order: StaffOrderDto) => rejectShopOrder(order.id, rejectReason.value.trim() || null),
  onSuccess: (order) => {
    showToast(`${order.code} rad etildi`, 'success')
    rejectingId.value = null
    rejectReason.value = ''
    refresh()
  },
  onError: (error) => {
    showToast(toUserMessage(error), 'error')
    refresh()
  },
})

function startReject(order: StaffOrderDto): void {
  rejectingId.value = order.id
  rejectReason.value = 'To‘lov topilmadi'
}
</script>

<template>
  <div>
    <PageHeader
      title="Buyurtmalar"
      subtitle="Saytda tarif sotib olgan o‘quvchilar. Payme/Click’da to‘lovni toping (summa va buyurtma kodi bo‘yicha) va tasdiqlang."
    />

    <div class="mb-4 flex flex-wrap gap-2">
      <BaseButton
        v-for="item in FILTERS"
        :key="item.value"
        size="sm"
        :variant="filter === item.value ? 'primary' : 'secondary'"
        @click="filter = item.value"
      >
        {{ item.label }}
      </BaseButton>
    </div>

    <DataStatus
      :pending="listQuery.isPending.value"
      :error="listError"
      :empty="rows.length === 0"
      empty-title="Buyurtma yo‘q"
      empty-text="Tanlangan holat bo‘yicha buyurtma topilmadi."
      empty-icon="clipboard"
      :skeleton-rows="4"
      @retry="listQuery.refetch()"
    >
      <div class="space-y-3">
        <BaseCard
          v-for="order in rows"
          :key="order.id"
        >
          <div class="flex flex-wrap items-start gap-3">
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <span class="text-base font-extrabold tracking-wider text-brand-300">{{ order.code }}</span>
                <BaseBadge :tone="orderStatusTone(order.status)">
                  {{ orderStatusLabel(order.status) }}
                </BaseBadge>
                <BaseBadge :tone="order.kind === 'Group' ? 'accent' : 'neutral'">
                  {{ PLAN_KIND_LABELS[order.kind] }}
                </BaseBadge>
              </div>
              <p class="mt-1 text-sm font-medium text-slate-100">
                {{ order.studentName }}
                <span class="font-normal text-slate-400">
                  · {{ formatPhone(order.studentPhone) || '—' }}
                  <template v-if="order.studentTelegramUsername">
                    · @{{ order.studentTelegramUsername }}
                  </template>
                </span>
              </p>
              <p class="mt-0.5 text-xs text-slate-400">
                {{ order.planName }} · {{ order.courseName }} · {{ formatDateTimeNumeric(order.createdAt) }}
              </p>
              <p
                v-if="order.decidedAt !== null"
                class="mt-0.5 text-xs text-slate-500"
              >
                {{ order.decidedByName ?? 'O‘quvchi' }} · {{ formatDateTimeNumeric(order.decidedAt) }}
                <template v-if="order.decisionNote">
                  · {{ order.decisionNote }}
                </template>
              </p>
            </div>
            <p class="text-xl font-extrabold tabular-nums text-slate-100">
              {{ formatMoney(order.amount) }}
            </p>
          </div>

          <div
            v-if="order.status === 'Pending'"
            class="mt-3 border-t border-line pt-3"
          >
            <form
              v-if="rejectingId === order.id"
              class="flex flex-wrap items-center gap-2"
              @submit.prevent="reject.mutate(order)"
            >
              <input
                v-model="rejectReason"
                maxlength="300"
                placeholder="Sabab (o‘quvchiga yuboriladi)"
                class="min-w-0 flex-1 rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
              >
              <BaseButton
                size="sm"
                variant="ghost"
                type="button"
                @click="rejectingId = null"
              >
                Bekor
              </BaseButton>
              <BaseButton
                size="sm"
                variant="danger"
                type="submit"
                :loading="reject.isPending.value"
              >
                Rad etish
              </BaseButton>
            </form>
            <div
              v-else
              class="flex flex-wrap justify-end gap-2"
            >
              <BaseButton
                size="sm"
                variant="secondary"
                @click="startReject(order)"
              >
                To‘lov topilmadi
              </BaseButton>
              <BaseButton
                size="sm"
                :loading="confirm.isPending.value && confirm.variables.value?.id === order.id"
                :disabled="confirm.isPending.value"
                @click="confirm.mutate(order)"
              >
                To‘lovni tasdiqlash
              </BaseButton>
            </div>
          </div>
        </BaseCard>
      </div>
    </DataStatus>
  </div>
</template>
