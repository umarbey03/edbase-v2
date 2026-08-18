<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  deliveryLabel,
  deliveryTone,
  fetchAbsenceNotices,
  fetchAbsenceNoticeSummary,
} from '@/entities/absentee'
import { DELIVERY_OPTIONS } from '@/entities/absentee/model/delivery'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
import type { AbsenceDeliveryName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseCard,
  DataStatus,
  PaginationBar,
} from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  KELMAGANLARGA YUBORILGAN XABARLAR — TARIX (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ BITTA KOMPONENT, IKKI JOYDA: "Darsga kirmaganlar" panelida (ish
 * o'sha yerda bajariladi — yubordim, ertaga qaytib javobini ko'raman) va
 * "Xabarlar" panelida (markaz nomidan ketgan BARCHA xabarlar arxivi).
 * Ikki nusxa yozilsa, biri o'zgarib ikkinchisi eskirib qolardi.
 *
 * ★ YETKAZILISH HOLATI HAQIQIY, NAVBATDAN O'QILADI: "yuborildi" deb
 * yozib qo'yish yolg'on bo'lardi — Telegram xabarni rad etishi mumkin
 * (bot bloklangan, chat topilmadi). Kurator sababni ko'rib qo'ng'iroq
 * qiladi.
 *
 * ⚠️ "Yuborildi" — TELEGRAM QABUL QILDI degani, "o'quvchi o'qidi" EMAS:
 * o'qilganlik belgisi Telegram Bot API'da umuman mavjud emas.
 */
const props = withDefaults(
  defineProps<{
    /** Tashqi davr cheklovi (kirmaganlar panelida — o'sha ekrandagi oraliq). */
    from?: string
    to?: string
    /** Sarlavha ko'rsatilsinmi (Xabarlar panelida kerak emas — u yerda tab bor). */
    titled?: boolean
  }>(),
  { from: undefined, to: undefined, titled: true },
)

const search = ref('')
const debouncedSearch = useDebounced(search)
const delivery = ref<'' | AbsenceDeliveryName>('')

const page = ref(1)
const pageSize = ref(20)
const PAGE_SIZE_OPTIONS = [10, 20, 50, 100] as const

const effectiveSearch = computed(() => {
  const term = debouncedSearch.value.trim()
  return term.length > 0 ? term : undefined
})

const filters = computed(() => ({
  from: props.from,
  to: props.to,
  search: effectiveSearch.value,
  delivery: delivery.value === '' ? undefined : delivery.value,
}))

watch([filters, pageSize], () => {
  page.value = 1
})

const listQuery = useQuery({
  queryKey: ['absence-notices', 'list', filters, page, pageSize],
  queryFn: ({ signal }) =>
    fetchAbsenceNotices(
      { ...filters.value, page: page.value, pageSize: pageSize.value },
      { signal },
    ),
})

const summaryQuery = useQuery({
  queryKey: ['absence-notices', 'summary', filters],
  queryFn: ({ signal }) => fetchAbsenceNoticeSummary(filters.value, { signal }),
})

const rows = computed(() => listQuery.data.value?.items ?? [])
const total = computed(() => listQuery.data.value?.total ?? 0)
const totalPages = computed(() => listQuery.data.value?.totalPages ?? 1)
const summary = computed(() => summaryQuery.data.value ?? null)

const loadError = computed(() =>
  listQuery.error.value !== null ? toUserMessage(listQuery.error.value) : null,
)
</script>

<template>
  <div>
    <!-- ═════════════════════ YIG'MA ═════════════════════ -->
    <div
      v-if="summary !== null && summary.total > 0"
      class="mb-4 grid grid-cols-2 gap-2.5 lg:grid-cols-4"
    >
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-slate-100"
          v-text="summary.total"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Jami xabar
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-sky-500 bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-sky-400"
          v-text="summary.delivered"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Telegram qabul qildi
        </p>
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-rose-400"
          v-text="summary.failed"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Yetkazilmadi
        </p>
      </div>
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p
          class="text-xl font-bold tabular-nums text-slate-300"
          v-text="summary.withoutTelegram"
        />
        <p class="mt-0.5 text-[11px] text-slate-400">
          Telegramsiz — qo‘ng‘iroq kerak
        </p>
      </div>
    </div>

    <!-- ═════════════════════ FILTR ═════════════════════ -->
    <div class="mb-4 grid gap-2.5 sm:grid-cols-2">
      <div class="relative">
        <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
          <AppIcon
            name="search"
            :size="16"
          />
        </span>
        <input
          v-model="search"
          class="zn-input pl-9"
          placeholder="O‘quvchi, guruh yoki matn bo‘yicha"
        >
      </div>

      <select
        v-model="delivery"
        class="zn-input"
        aria-label="Yetkazilish holati bo‘yicha filtr"
      >
        <option value="">
          Barcha holatlar
        </option>
        <option
          v-for="option in DELIVERY_OPTIONS"
          :key="option.value"
          :value="option.value"
        >
          {{ option.label }}
        </option>
      </select>
    </div>

    <BaseCard
      :title="props.titled ? 'Yuborilgan xabarlar' : undefined"
      flush
    >
      <DataStatus
        :pending="listQuery.isPending.value"
        :error="loadError"
        :empty="rows.length === 0"
        :retrying="listQuery.isFetching.value"
        :skeleton-rows="4"
        empty-icon="send"
        empty-title="Xabar yuborilmagan"
        empty-text="Darsga kelmagan o‘quvchilarga hali xabar yuborilmagan."
        @retry="listQuery.refetch()"
      >
        <ul class="divide-y divide-line">
          <li
            v-for="row in rows"
            :key="row.id"
            class="px-4 py-3"
          >
            <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
              <span
                class="font-medium text-slate-100"
                v-text="row.studentName"
              />
              <span
                class="text-xs text-dim"
                v-text="row.groupName"
              />
              <BaseBadge :tone="deliveryTone(row.deliveryStatus)">
                {{ deliveryLabel(row.deliveryStatus) }}
              </BaseBadge>
              <span
                class="ml-auto text-xs tabular-nums text-dim"
                v-text="formatDateTimeNumeric(row.sentAt)"
              />
            </div>

            <p
              class="mt-1.5 whitespace-pre-wrap rounded-lg bg-ink-800 px-3 py-2 text-xs text-slate-300"
              v-text="row.body"
            />

            <div class="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] text-dim">
              <span v-text="`Qoldirilgan dars: ${formatDateTimeNumeric(row.sessionStart)}`" />
              <span v-text="`Yubordi: ${row.sentByName}`" />
              <span
                v-if="row.studentPhone !== null"
                v-text="formatPhone(row.studentPhone)"
              />
              <!--
                ★ XATO SABABI KO'RSATILADI: "yetkazilmadi" so'zining o'zi
                nima qilish kerakligini aytmaydi. "chat not found" esa
                aniq: o'quvchi bot bilan hali gaplashmagan.
              -->
              <span
                v-if="row.deliveryError !== null"
                class="text-rose-400"
                v-text="row.deliveryError"
              />
            </div>
          </li>
        </ul>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          :page-size="pageSize"
          :page-size-options="PAGE_SIZE_OPTIONS"
          @update:page="page = $event"
          @update:page-size="pageSize = $event"
        />
      </DataStatus>
    </BaseCard>
  </div>
</template>
