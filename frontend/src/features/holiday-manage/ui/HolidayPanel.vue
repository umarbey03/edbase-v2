<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { createHoliday, deleteHoliday, fetchHolidays } from '@/entities/holiday'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { HolidayDto } from '@/shared/types'
import { BaseButton, BaseField, DataStatus } from '@/shared/ui'

/**
 * ============================================================================
 *  BAYRAM KALENDARI (2026-08-16) — UMUMIY, O'quv bo'limi/admin boshqaradi
 * ============================================================================
 *
 * Talab (loyiha egasi): *"bizda ba'zi bayram kunlari e'lon qilinadi, bu
 * kunlari darslar ham o'tilmaydi ... 8 oylik dars qoldirilganiga qarab
 * surilishi kerak oldinga, va bundan tashqari o'tilmagan dars uchun
 * o'quvchidan pul yechib olinmasligi kerak"*.
 *
 * ★ NAQSH `GroupCategoryPanel` bilan O'XSHASH (ALWAYS-INLINE, drawer yo'q),
 * lekin TAHRIRLASH YO'Q: bayram sanasi/nomi qo'shilgach o'zgartirilmaydi —
 * faqat o'chiriladi (xato kiritilgan bo'lsa). Sabab: sana o'zgartirilsa
 * allaqachon bekor qilingan darslar bilan MOSLIKDAN chiqib ketardi (bekor
 * qilish YANGI sanaga emas, ESKI sanaga bog'langan holda amalga oshgan).
 *
 * ★ QO'SHISH NATIJASI (`HolidayImpactDto`) DARHOL ko'rsatiladi: "N guruh,
 * M dars bekor qilindi" — xodim natijani ko'rmasdan keyingi bayramni
 * kiritishga o'tmasin.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

const holidaysQuery = useQuery({
  queryKey: ['holidays'],
  queryFn: ({ signal }) => fetchHolidays({ signal }),
})

const holidays = computed<HolidayDto[]>(() => holidaysQuery.data.value ?? [])

const listError = computed(() =>
  holidaysQuery.error.value !== null ? toUserMessage(holidaysQuery.error.value) : null,
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['holidays'] })

  // 🔴 GURUH JADVALLARI HAM ESKIRADI: bayram e'lon qilinganda ta'sirlangan
  // guruhlarning darslari bekor qilinadi va jadval qayta tuziladi
  // (`HolidayService.CreateAsync`) — ochiq "Darslar" tabi eski holatda
  // qolib ketmasin.
  void queryClient.invalidateQueries({ queryKey: ['group'] })
}

/* ------------------------------------------------------------ yaratish */

const newDate = ref('')
const newLabel = ref('')
const createError = ref<string | null>(null)
const impactNote = ref<string | null>(null)

const createMutation = useMutation({
  mutationFn: (input: { date: string; label: string }) =>
    createHoliday({ date: input.date, label: input.label }),
  onSuccess: (impact) => {
    newDate.value = ''
    newLabel.value = ''
    createError.value = null
    impactNote.value =
      impact.affectedGroupCount === 0
        ? 'Bayram qo‘shildi. Hozircha bu sanaga to‘g‘ri keladigan rejalashtirilgan dars yo‘q edi.'
        : `Bayram qo‘shildi: ${impact.affectedGroupCount} ta guruhning ${impact.cancelledSessionCount} ta darsi bekor qilindi va jadval oldinga surildi.`
    refresh()
  },
  onError: (error: unknown) => {
    impactNote.value = null
    createError.value = toUserMessage(error)
  },
})

function onCreate(): void {
  createError.value = null
  impactNote.value = null

  if (newDate.value.length === 0) {
    createError.value = 'Bayram sanasini kiriting.'
    return
  }

  const label = newLabel.value.trim()
  if (label.length === 0) {
    createError.value = 'Bayram nomini kiriting.'
    return
  }

  createMutation.mutate({ date: newDate.value, label })
}

/* ------------------------------------------------------------ o'chirish */

const deleting = ref<HolidayDto | null>(null)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteHoliday(id),
  onSuccess: () => {
    deleting.value = null
    deleteError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    deleteError.value = toUserMessage(error)
  },
})

async function askDelete(holiday: HolidayDto): Promise<void> {
  deleteError.value = null

  const ok = await confirm({
    title: 'Bayramni o‘chirish',
    message: `“${holiday.label}” (${formatDateNumeric(holiday.date)}) kalendardan o‘chiriladi.`,
    confirmLabel: 'O‘chirish',
    tone: 'danger',
    details: [
      'Allaqachon bekor qilingan darslar TIKLANMAYDI — tarix o‘zgarmaydi.',
      'Faqat kelajakda shu sanaga yangi dars rejalashtirilishining oldi olinmaydi.',
    ],
  })
  if (!ok) return

  deleting.value = holiday
  deleteMutation.mutate(holiday.id)
}
</script>

<template>
  <div>
    <p class="mb-4 text-xs text-slate-400">
      E'lon qilingan kun BARCHA guruhlarning o'sha kundagi darsini bekor qiladi va jadvalni
      avtomatik oldinga suradi — o'quvchidan shu dars uchun pul yechib olinmaydi.
    </p>

    <!-- ─────────────────────── YANGI BAYRAM ─────────────────────── -->
    <div class="mb-5 space-y-2.5 rounded-xl border border-line bg-ink-900 p-3.5">
      <div class="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
        <BaseField label="Sana">
          <input
            v-model="newDate"
            class="zn-input"
            type="date"
          >
        </BaseField>
        <BaseField
          label="Nomi"
          hint="Masalan: Mustaqillik kuni"
        >
          <input
            v-model="newLabel"
            class="zn-input"
            maxlength="150"
            placeholder="Bayram nomi"
            @keyup.enter="onCreate"
          >
        </BaseField>
      </div>

      <p
        v-if="createError !== null"
        class="text-xs text-rose-400"
        role="alert"
        v-text="createError"
      />
      <p
        v-else-if="impactNote !== null"
        class="text-xs text-green-400"
        v-text="impactNote"
      />

      <div class="flex justify-end">
        <BaseButton
          :loading="createMutation.isPending.value"
          @click="onCreate"
        >
          Bayram qo‘shish
        </BaseButton>
      </div>
    </div>

    <!-- ───────────────────────── RO'YXAT ───────────────────────── -->
    <DataStatus
      :pending="holidaysQuery.isPending.value"
      :error="listError"
      :empty="holidays.length === 0"
      :retrying="holidaysQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="calendar"
      empty-title="Bayram qo‘shilmagan"
      empty-text="Birinchi bayramni yuqoridagi maydondan qo‘shing."
      @retry="holidaysQuery.refetch()"
    >
      <ul class="divide-y divide-line rounded-xl border border-line">
        <li
          v-for="holiday in holidays"
          :key="holiday.id"
          class="flex flex-wrap items-center gap-2 p-3.5"
        >
          <span
            class="shrink-0 text-xs font-semibold tabular-nums text-slate-300"
            v-text="formatDateNumeric(holiday.date)"
          />
          <span
            class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
            v-text="holiday.label"
          />
          <span
            v-if="holiday.createdByName !== null"
            class="shrink-0 text-xs text-dim"
          >
            {{ holiday.createdByName }}
          </span>
          <BaseButton
            size="sm"
            variant="danger"
            :loading="deleteMutation.isPending.value && deleting?.id === holiday.id"
            @click="askDelete(holiday)"
          >
            O‘chirish
          </BaseButton>
        </li>
      </ul>
    </DataStatus>

    <p
      v-if="deleteError !== null"
      class="mt-3 text-xs text-rose-400"
      role="alert"
      v-text="deleteError"
    />
  </div>
</template>
