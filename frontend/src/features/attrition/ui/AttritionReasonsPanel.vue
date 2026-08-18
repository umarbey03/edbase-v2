<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  createAttritionReason,
  deleteAttritionReason,
  fetchAttritionReasonCatalogue,
  updateAttritionReason,
} from '@/entities/attrition'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { AttritionReasonDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseField,
  BaseModal,
  DataStatus,
} from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  TO'KILISH SABABLARI — sozlamalar (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * O'quv bo'limi (Dilrabo): *"To'kilish sabablarini foizda qilib berishni
 * iloji bormi?"*.
 *
 * ★ SHU RO'YXAT — FOIZ HISOBOTINING ASOSI. Erkin matn bo'yicha foiz
 * hisoblab bo'lmaydi: "Moliyaviy", "pul yo'q", "to'lay olmadi" — bir xil
 * sabab, uchta har xil satr. Shuning uchun operator chiqarish/muzlatish
 * oynasida AYNAN shu ro'yxatdan tanlaydi, erkin matn esa qo'shimcha
 * izoh bo'lib qoladi.
 *
 * ★ ISHLATILGAN SABAB O'CHIRILMAYDI — ARXIVLANADI (server hal qiladi):
 * hodisa jurnali unga havola qiladi va qator yo'qolsa o'tgan oyning
 * hisoboti "nomsiz" ulushga aylanardi.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

const reasonsQuery = useQuery({
  queryKey: ['attrition-reasons', 'all'],
  queryFn: ({ signal }) => fetchAttritionReasonCatalogue(false, { signal }),
})

const rows = computed(() => reasonsQuery.data.value ?? [])

const loadError = computed(() =>
  reasonsQuery.error.value !== null ? toUserMessage(reasonsQuery.error.value) : null,
)

const actionError = ref<string | null>(null)

/* ------------------------------------------------------------ tahrirlash */

const dialogOpen = ref(false)
const editing = ref<AttritionReasonDto | null>(null)
const label = ref('')
const isActive = ref(true)
const formError = ref<string | null>(null)

watch(dialogOpen, (open) => {
  if (!open) return

  label.value = editing.value?.label ?? ''
  isActive.value = editing.value?.isActive ?? true
  formError.value = null
})

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(row: AttritionReasonDto): void {
  editing.value = row
  dialogOpen.value = true
}

function invalidate(): void {
  void queryClient.invalidateQueries({ queryKey: ['attrition-reasons'] })

  // 🔴 HISOBOT HAM ESKIRADI: sabab nomi to'kilishlar panelida va foiz
  // hisobotida ko'rsatiladi — qayta nomlangach eski nom qolib ketmasin.
  void queryClient.invalidateQueries({ queryKey: ['attrition'] })
}

const saveMutation = useMutation({
  mutationFn: () => {
    const body = { label: label.value.trim(), isActive: isActive.value }

    return editing.value === null
      ? createAttritionReason(body)
      : updateAttritionReason(editing.value.id, body)
  },
  onSuccess: () => {
    invalidate()
    dialogOpen.value = false
  },
  onError: (error: Error) => {
    formError.value = toUserMessage(error)
  },
})

function handleSubmit(): void {
  formError.value = null

  if (label.value.trim().length === 0) {
    formError.value = 'Sabab nomini kiriting.'
    return
  }

  saveMutation.mutate()
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteAttritionReason(id),
  onSuccess: invalidate,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

async function askDelete(row: AttritionReasonDto): Promise<void> {
  actionError.value = null

  const used = row.usageCount > 0

  const ok = await confirm({
    title: used ? 'Sababni arxivlash' : 'Sababni o‘chirish',
    message: `“${row.label}”`,
    confirmLabel: used ? 'Arxivlash' : 'O‘chirish',
    tone: 'danger',
    details: used
      ? [
          `Bu sabab ${row.usageCount} ta yozuvda ishlatilgan.`,
          'Shuning uchun o‘chirilmaydi — yangi yozuvda tanlanmaydigan bo‘ladi.',
          'Eski yozuvlarda va foiz hisobotida nomi o‘zgarishsiz qoladi.',
        ]
      : ['Bu sabab hech qayerda ishlatilmagan — butunlay o‘chiriladi.'],
  })

  if (!ok) return

  deleteMutation.mutate(row.id)
}
</script>

<template>
  <div>
    <p class="mb-4 text-xs text-slate-400">
      O‘quvchi guruhdan chiqarilayotganda, muzlatilayotganda yoki ko‘chirilayotganda
      sabab AYNAN shu ro‘yxatdan tanlanadi. “To‘kilishlar → Sabablar” bo‘limidagi
      foizlar shu tasnif bo‘yicha hisoblanadi — shuning uchun ro‘yxat qancha aniq
      bo‘lsa, hisobot ham shuncha foydali.
    </p>

    <div class="mb-4 flex justify-end">
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
        Sabab qo‘shish
      </BaseButton>
    </div>

    <p
      v-if="actionError !== null"
      class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <DataStatus
      :pending="reasonsQuery.isPending.value"
      :error="loadError"
      :empty="rows.length === 0"
      :retrying="reasonsQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="list"
      empty-title="Sabab yo‘q"
      empty-text="Birinchi sababni qo‘shing — masalan “Moliyaviy qiyinchilik”."
      @retry="reasonsQuery.refetch()"
    >
      <div class="scroll-x-safe scrollbar-slim">
        <table class="zn-table">
          <thead>
            <tr>
              <th class="w-10">
                #
              </th>
              <th>Nomi</th>
              <th>Ishlatilgan</th>
              <th class="w-32" />
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(row, index) in rows"
              :key="row.id"
              :class="row.isActive ? '' : 'opacity-55'"
            >
              <td
                class="tabular-nums text-dim"
                v-text="index + 1"
              />
              <td>
                <span class="font-medium text-slate-100">{{ row.label }}</span>
                <BaseBadge
                  v-if="!row.isActive"
                  tone="neutral"
                  class="ml-1.5"
                >
                  Arxivlangan
                </BaseBadge>
              </td>
              <td
                class="tabular-nums text-slate-400"
                v-text="row.usageCount > 0 ? `${row.usageCount} ta` : '—'"
              />
              <td>
                <div class="flex gap-2">
                  <BaseButton
                    size="sm"
                    variant="secondary"
                    @click="openEdit(row)"
                  >
                    Tahrirlash
                  </BaseButton>
                  <BaseButton
                    v-if="row.isActive"
                    size="sm"
                    variant="secondary"
                    :loading="deleteMutation.isPending.value"
                    @click="askDelete(row)"
                  >
                    O‘chirish
                  </BaseButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </DataStatus>

    <BaseModal
      :open="dialogOpen"
      :title="editing === null ? 'Yangi sabab' : 'Sababni tahrirlash'"
      @close="dialogOpen = false"
    >
      <div class="space-y-3">
        <BaseField
          label="Nomi"
          hint="Qisqa va aniq — hisobotda shu matn ko‘rinadi."
        >
          <input
            v-model="label"
            class="zn-input"
            maxlength="100"
            placeholder="masalan: Moliyaviy qiyinchilik"
            @keyup.enter="handleSubmit"
          >
        </BaseField>

        <label
          v-if="editing !== null"
          class="flex cursor-pointer items-start gap-2.5"
        >
          <input
            v-model="isActive"
            type="checkbox"
            class="mt-0.5"
          >
          <span class="text-sm text-slate-200">
            Faol
            <span class="block text-xs text-slate-400">
              O‘chirilsa yangi yozuvda tanlanmaydi, lekin hisobotda qoladi.
            </span>
          </span>
        </label>
      </div>

      <p
        v-if="formError !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="formError"
      />

      <template #footer>
        <BaseButton
          variant="secondary"
          @click="dialogOpen = false"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          :loading="saveMutation.isPending.value"
          @click="handleSubmit"
        >
          Saqlash
        </BaseButton>
      </template>
    </BaseModal>
  </div>
</template>
