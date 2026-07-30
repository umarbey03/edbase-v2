<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { deleteTariff, fetchTariffs, tariffScopeLabel } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { TariffDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseSpinner,
  ConfirmDeleteDialog,
} from '@/shared/ui'

import TariffFormDialog from './TariffFormDialog.vue'

/**
 * Tariflar ro'yxati.
 *
 * Ro'yxat SERVER TARTIBIDA chiziladi (aniqlik ↓, `activeFrom` ↓, `id` ↓) —
 * bu tanlash tartibi bilan AYNAN bir xil, ya'ni birinchi qator ayni paytda
 * qaysi tarif amalda ekanini ko'rsatadi. Mijozda qayta tartiblasak, bu
 * xossa yo'qolardi.
 */
const queryClient = useQueryClient()

const formOpen = ref(false)
const editing = ref<TariffDto | null>(null)
const deleting = ref<TariffDto | null>(null)
const deleteError = ref<string | null>(null)

const tariffsQuery = useQuery({
  queryKey: ['payments', 'tariffs'],
  queryFn: ({ signal }) => fetchTariffs({}, { signal }),
})

const tariffs = computed(() => tariffsQuery.data.value ?? [])

const errorMessage = computed(() =>
  tariffsQuery.error.value !== null ? toUserMessage(tariffsQuery.error.value) : null,
)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(tariff: TariffDto): void {
  editing.value = tariff
  formOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['payments'] })
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteTariff(id),
  onSuccess: () => {
    deleting.value = null
    deleteError.value = null
    refresh()
  },
  onError: (error: Error) => {
    deleteError.value = toUserMessage(error)
  },
})

function confirmDelete(): void {
  const tariff = deleting.value
  if (tariff === null) return
  deleteError.value = null
  deleteMutation.mutate(tariff.id)
}
</script>

<template>
  <BaseCard title="Tariflar">
    <template #actions>
      <BaseButton
        size="sm"
        @click="openCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Yangi
      </BaseButton>
    </template>

    <div
      v-if="tariffsQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="errorMessage !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <p
      v-else-if="tariffs.length === 0"
      class="text-xs text-slate-400"
    >
      Tarif qo‘shilmagan — oy yozuvlari ochilganda hech kimga summa hisoblanmaydi.
    </p>

    <ul
      v-else
      class="divide-y divide-line"
    >
      <li
        v-for="tariff in tariffs"
        :key="tariff.id"
        class="flex flex-wrap items-center gap-3 py-2.5 first:pt-0 last:pb-0"
        :class="tariff.isActive ? '' : 'opacity-50'"
      >
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <span
              class="truncate text-sm font-medium text-slate-100"
              v-text="tariff.name"
            />
            <BaseBadge :tone="tariff.isActive ? 'success' : 'neutral'">
              {{ tariff.isActive ? 'Faol' : 'Nofaol' }}
            </BaseBadge>
          </div>
          <p class="mt-0.5 text-[11px] text-slate-400">
            {{ tariffScopeLabel(tariff) }} · {{ tariff.lessonsCount }} dars ·
            {{ formatDateWithYear(tariff.activeFrom) }} dan
          </p>
        </div>
        <p
          class="shrink-0 text-sm font-semibold tabular-nums text-slate-100"
          v-text="formatMoney(tariff.amount)"
        />
        <div class="flex shrink-0 items-center gap-2">
          <BaseButton
            size="sm"
            variant="secondary"
            @click="openEdit(tariff)"
          >
            <template #icon>
              <AppIcon
                name="edit"
                :size="13"
              />
            </template>
            Tahrirlash
          </BaseButton>
          <button
            type="button"
            class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-400"
            title="O‘chirish"
            @click="deleting = tariff"
          >
            <AppIcon
              name="trash"
              :size="15"
            />
          </button>
        </div>
      </li>
    </ul>

    <TariffFormDialog
      :open="formOpen"
      :tariff="editing"
      @close="formOpen = false"
      @saved="refresh"
    />

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Tarifni o‘chirish"
      :message="`“${deleting?.name ?? ''}” o‘chirilsinmi? Ochilgan oylar o‘zgarmaydi — ular summani yaratilganda nusxa qilib olgan. Narx tarixini saqlash uchun o‘chirish o‘rniga tarifni nofaol qilish tavsiya etiladi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="confirmDelete"
    />
  </BaseCard>
</template>
