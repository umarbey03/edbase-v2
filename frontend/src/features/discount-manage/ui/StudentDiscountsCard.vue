<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { deleteDiscount, discountValueLabel, fetchStudentDiscounts } from '@/entities/payment'
import StudentPicker from '@/features/payment-actions/ui/StudentPicker.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateWithYear } from '@/shared/lib/datetime'
import type { StudentDiscountDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseSpinner,
  ConfirmDeleteDialog,
} from '@/shared/ui'

import DiscountFormDialog from './DiscountFormDialog.vue'

/**
 * Chegirmalar — O'QUVCHI BO'YICHA.
 *
 * ★ NEGA umumiy ro'yxat YO'Q (eski ilovada bor edi): backendda chegirmalar
 * faqat `GET /payments/students/{id}/discounts` orqali o'qiladi, ya'ni
 * "markazdagi barcha chegirmalar" endpointi umuman mavjud emas. Uni mijozda
 * yig'ish uchun har bir o'quvchi bo'yicha alohida so'rov yuborish kerak
 * bo'lardi (mingdan ortiq so'rov). Shuning uchun avval o'quvchi tanlanadi.
 */
const queryClient = useQueryClient()

const student = ref<{ id: number; name: string } | null>(null)
const formOpen = ref(false)
const editing = ref<StudentDiscountDto | null>(null)
const deleting = ref<StudentDiscountDto | null>(null)
const deleteError = ref<string | null>(null)

const studentId = computed(() => student.value?.id ?? null)

const discountsQuery = useQuery({
  queryKey: ['payments', 'discounts', studentId],
  queryFn: ({ signal }) => fetchStudentDiscounts(studentId.value ?? 0, { signal }),
  enabled: computed(() => studentId.value !== null),
})

const discounts = computed(() => discountsQuery.data.value ?? [])

const errorMessage = computed(() =>
  discountsQuery.error.value !== null ? toUserMessage(discountsQuery.error.value) : null,
)

function openCreate(): void {
  editing.value = null
  formOpen.value = true
}

function openEdit(discount: StudentDiscountDto): void {
  editing.value = discount
  formOpen.value = true
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['payments'] })
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteDiscount(studentId.value ?? 0, id),
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
  const discount = deleting.value
  if (discount === null) return
  deleteError.value = null
  deleteMutation.mutate(discount.id)
}
</script>

<template>
  <BaseCard title="Chegirmalar">
    <template #actions>
      <BaseButton
        size="sm"
        :disabled="student === null"
        @click="openCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Berish
      </BaseButton>
    </template>

    <StudentPicker v-model="student" />

    <template v-if="student !== null">
      <div
        v-if="discountsQuery.isPending.value"
        class="flex justify-center py-6"
      >
        <BaseSpinner />
      </div>

      <p
        v-else-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />

      <p
        v-else-if="discounts.length === 0"
        class="mt-3 text-xs text-slate-400"
      >
        Chegirma berilmagan.
      </p>

      <ul
        v-else
        class="mt-3 divide-y divide-line border-t border-line"
      >
        <li
          v-for="discount in discounts"
          :key="discount.id"
          class="flex flex-wrap items-center gap-3 py-2.5"
          :class="discount.isActive ? '' : 'opacity-50'"
        >
          <div class="min-w-0 flex-1">
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="text-sm font-semibold text-slate-100"
                v-text="discountValueLabel(discount)"
              />
              <BaseBadge :tone="discount.isActive ? 'success' : 'neutral'">
                {{ discount.isActive ? 'Faol' : 'Nofaol' }}
              </BaseBadge>
            </div>
            <p class="mt-0.5 text-[11px] text-slate-400">
              {{ discount.groupName ?? 'Barcha guruhlar' }} ·
              {{ formatDateWithYear(discount.validFrom) }} dan
              <template v-if="discount.validTo !== null">
                {{ formatDateWithYear(discount.validTo) }} gacha
              </template>
              <template v-if="discount.reason !== null">
                · {{ discount.reason }}
              </template>
            </p>
          </div>
          <div class="flex shrink-0 items-center gap-2">
            <BaseButton
              size="sm"
              variant="secondary"
              @click="openEdit(discount)"
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
              @click="deleting = discount"
            >
              <AppIcon
                name="trash"
                :size="15"
              />
            </button>
          </div>
        </li>
      </ul>
    </template>

    <DiscountFormDialog
      :open="formOpen"
      :student="student"
      :discount="editing"
      @close="formOpen = false"
      @saved="refresh"
    />

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Chegirmani o‘chirish"
      message="Chegirma o‘chirilsinmi? Allaqachon ochilgan oylar o‘zgarmaydi — chegirma faqat keyingi yozuvlarga ta’sir qiladi."
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="confirmDelete"
    />
  </BaseCard>
</template>
