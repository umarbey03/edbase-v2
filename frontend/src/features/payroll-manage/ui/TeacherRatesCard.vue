<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { deleteTeacherRate, fetchTeacherRates, rateScopeLabel } from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { TeacherRateDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseSpinner,
  ConfirmDeleteDialog,
} from '@/shared/ui'

import TeacherRateFormDialog from './TeacherRateFormDialog.vue'

/** Stavkalar ro'yxati — `TariffsCard` bilan AYNI naqsh. */
const queryClient = useQueryClient()

const formOpen = ref(false)
const editing = ref<TeacherRateDto | null>(null)
const deleting = ref<TeacherRateDto | null>(null)
const deleteError = ref<string | null>(null)

const ratesQuery = useQuery({
  queryKey: ['payroll', 'rates'],
  queryFn: ({ signal }) => fetchTeacherRates({ signal }),
})

const rates = computed(() => ratesQuery.data.value ?? [])

const errorMessage = computed(() =>
  ratesQuery.error.value !== null ? toUserMessage(ratesQuery.error.value) : null,
)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(rate: TeacherRateDto): void {
  editing.value = rate
  formOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['payroll'] })
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteTeacherRate(id),
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
  const rate = deleting.value
  if (rate === null) return
  deleteError.value = null
  deleteMutation.mutate(rate.id)
}
</script>

<template>
  <BaseCard
    title="Stavkalar"
    subtitle="Har dars yakunlanganda O‘SHA PAYTDAGI stavka bilan qotib qoladi (izoh: narx tarixi) — stavkani keyin tahrirlash yoki nofaol qilish o‘tgan oy hisobotini o‘zgartirmaydi, faqat keyingi darslarga ta’sir qiladi."
  >
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
      v-if="ratesQuery.isPending.value"
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
      v-else-if="rates.length === 0"
      class="text-xs text-slate-400"
    >
      Stavka qo‘shilmagan — hisobot hech kimga summa hisoblamaydi.
    </p>

    <ul
      v-else
      class="divide-y divide-line"
    >
      <li
        v-for="rate in rates"
        :key="rate.id"
        class="flex flex-wrap items-center gap-3 py-2.5 first:pt-0 last:pb-0"
        :class="rate.isActive ? '' : 'opacity-50'"
      >
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <span
              class="truncate text-sm font-medium text-slate-100"
              v-text="rateScopeLabel(rate)"
            />
            <BaseBadge :tone="rate.isActive ? 'success' : 'neutral'">
              {{ rate.isActive ? 'Faol' : 'Nofaol' }}
            </BaseBadge>
          </div>
          <p class="mt-0.5 text-[11px] text-slate-400">
            {{ formatDateWithYear(rate.activeFrom) }} dan
          </p>
        </div>
        <p class="shrink-0 text-right text-sm font-semibold tabular-nums text-slate-100">
          {{ formatMoney(rate.perSessionRate) }}
          <span class="block text-[11px] font-normal text-slate-400">
            + {{ formatMoney(rate.perStudentBonusRate) }} / o‘quvchi
          </span>
          <span
            v-if="rate.baseSalary > 0 || rate.activeStudentBonusRate > 0"
            class="block text-[11px] font-normal text-brand-300"
          >
            baza {{ formatMoney(rate.baseSalary) }} + KPI {{ formatMoney(rate.activeStudentBonusRate) }}
          </span>
          <span
            v-if="rate.weekendHolidayMultiplier !== null"
            class="block text-[11px] font-normal text-amber-400"
          >
            dam olish/bayram × {{ rate.weekendHolidayMultiplier }}
          </span>
        </p>
        <div class="flex shrink-0 items-center gap-2">
          <BaseButton
            size="sm"
            variant="secondary"
            @click="openEdit(rate)"
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
            @click="deleting = rate"
          >
            <AppIcon
              name="trash"
              :size="15"
            />
          </button>
        </div>
      </li>
    </ul>

    <TeacherRateFormDialog
      :open="formOpen"
      :rate="editing"
      @close="formOpen = false"
      @saved="refresh"
    />

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Stavkani o‘chirish"
      :message="`“${deleting === null ? '' : rateScopeLabel(deleting)}” stavkasi o‘chirilsinmi? O‘tgan oy hisobotlari o‘zgarmaydi — ular hisoblanganda stavkani nusxa qilib olgan. Narx tarixini saqlash uchun o‘chirish o‘rniga stavkani nofaol qilish tavsiya etiladi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="confirmDelete"
    />
  </BaseCard>
</template>
