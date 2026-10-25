<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchCourses } from '@/entities/course'
import { createSubscriptionPlan, fetchSubscriptionPlans, updateSubscriptionPlan } from '@/entities/subscription'
import type { SaveSubscriptionPlanRequest, SubscriptionPlanDto } from '@/entities/subscription'
import { toUserMessage } from '@/shared/api'
import { formatMoney } from '@/shared/lib/money'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseBadge, BaseButton, BaseCard, BaseSpinner } from '@/shared/ui'

/**
 * MUSTAQIL O'QISH REJALARI (2026-10-23) — "Moliya" sahifasida, tariflar yonida.
 *
 * Reja = kurs + muddat (oy) + narx. O'chirilgan reja yangi obunaga
 * berilmaydi, lekin u bilan sotilgan davrlar saqlanadi (narx har davrda
 * alohida yozilgan) — shuning uchun o'chirish yo'q, faqat nofaol qilish.
 */
const queryClient = useQueryClient()

const plansQuery = useQuery({
  queryKey: ['subscriptions', 'plans'],
  queryFn: ({ signal }) => fetchSubscriptionPlans({ signal }),
})
const coursesQuery = useQuery({
  queryKey: ['courses', 'for-plans'],
  queryFn: ({ signal }) => fetchCourses({ isActive: true, page: 1, pageSize: 100 }, { signal }),
})

const plans = computed(() => plansQuery.data.value ?? [])
const courses = computed(() => coursesQuery.data.value?.items ?? [])

const editingId = ref<number | null>(null)
const formOpen = ref(false)
const form = ref<SaveSubscriptionPlanRequest>(emptyForm())

function emptyForm(): SaveSubscriptionPlanRequest {
  return { name: '', courseId: 0, months: 1, price: 0, isActive: true }
}

function openCreate(): void {
  editingId.value = null
  form.value = emptyForm()
  formOpen.value = true
}

function openEdit(plan: SubscriptionPlanDto): void {
  editingId.value = plan.id
  form.value = {
    name: plan.name,
    courseId: plan.courseId,
    months: plan.months,
    price: plan.price,
    isActive: plan.isActive,
  }
  formOpen.value = true
}

const save = useMutation({
  mutationFn: () =>
    editingId.value === null
      ? createSubscriptionPlan(form.value)
      : updateSubscriptionPlan(editingId.value, form.value),
  onSuccess: () => {
    showToast('Reja saqlandi', 'success')
    formOpen.value = false
    void queryClient.invalidateQueries({ queryKey: ['subscriptions', 'plans'] })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})
</script>

<template>
  <BaseCard title="Mustaqil o‘qish rejalari">
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

    <form
      v-if="formOpen"
      class="mb-3 grid gap-2.5 rounded-xl border border-line bg-ink-900 p-3 sm:grid-cols-2"
      @submit.prevent="save.mutate()"
    >
      <label class="block space-y-1 sm:col-span-2">
        <span class="text-xs text-slate-400">Nomi</span>
        <input
          v-model="form.name"
          required
          maxlength="100"
          placeholder="Masalan: Oylik paket"
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
      </label>
      <label class="block space-y-1 sm:col-span-2">
        <span class="text-xs text-slate-400">Kurs</span>
        <select
          v-model.number="form.courseId"
          required
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
          <option
            :value="0"
            disabled
          >
            Tanlang
          </option>
          <option
            v-for="course in courses"
            :key="course.id"
            :value="course.id"
          >
            {{ course.name }}
          </option>
        </select>
      </label>
      <label class="block space-y-1">
        <span class="text-xs text-slate-400">Muddat (oy)</span>
        <input
          v-model.number="form.months"
          type="number"
          min="1"
          max="12"
          required
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
      </label>
      <label class="block space-y-1">
        <span class="text-xs text-slate-400">Narx (so‘m)</span>
        <input
          v-model.number="form.price"
          type="number"
          min="0"
          step="1000"
          required
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
      </label>
      <label class="flex items-center gap-2 text-sm text-slate-300 sm:col-span-2">
        <input
          v-model="form.isActive"
          type="checkbox"
        >
        Faol (yangi obunaga beriladi)
      </label>
      <div class="flex justify-end gap-2 sm:col-span-2">
        <BaseButton
          size="sm"
          variant="ghost"
          type="button"
          @click="formOpen = false"
        >
          Bekor
        </BaseButton>
        <BaseButton
          size="sm"
          type="submit"
          :loading="save.isPending.value"
          :disabled="form.courseId === 0"
        >
          Saqlash
        </BaseButton>
      </div>
    </form>

    <div
      v-if="plansQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="plans.length === 0"
      class="text-xs text-slate-400"
    >
      Reja yo‘q — guruhsiz o‘quvchiga obuna berish uchun avval reja yarating.
    </p>

    <ul
      v-else
      class="divide-y divide-line"
    >
      <li
        v-for="plan in plans"
        :key="plan.id"
        class="flex items-center gap-3 py-2.5 first:pt-0 last:pb-0"
        :class="plan.isActive ? '' : 'opacity-50'"
      >
        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <span class="truncate text-sm font-medium text-slate-100">{{ plan.name }}</span>
            <BaseBadge :tone="plan.isActive ? 'success' : 'neutral'">
              {{ plan.isActive ? 'Faol' : 'Nofaol' }}
            </BaseBadge>
          </div>
          <p class="mt-0.5 text-[11px] text-slate-400">
            {{ plan.courseName }} · {{ plan.months }} oy · {{ formatMoney(plan.price) }}
          </p>
        </div>
        <BaseButton
          size="sm"
          variant="ghost"
          @click="openEdit(plan)"
        >
          Tahrirlash
        </BaseButton>
      </li>
    </ul>
  </BaseCard>
</template>
