<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { deletePenaltyCategory, fetchPenaltyCategories } from '@/entities/penalty'
import { toUserMessage } from '@/shared/api'
import { formatMoney } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { PenaltyCategoryDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import PenaltyCategoryDialog from './PenaltyCategoryDialog.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  JARIMA TARIFLARI — sozlamalar (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi talabi: jarima turlarini dastur qayta yozilmasdan qo'shish
 * mumkin bo'lsin. Ilgari ikkita tarif "Sozlamalar" sahifasidagi qattiq
 * kalitlar edi — endi ular shu jadvaldagi TIZIM tariflari.
 *
 * ★ NEGA "SOZLAMALAR" SAHIFASIDA EMAS, SHU YERDA: tarif — jarimaning
 * ajralmas qismi. Operator "nega bu jarima 75 000?" degan savolga javobni
 * boshqa sahifaga o'tmasdan, qo'shni tabdan topadi.
 *
 * ★ TAHRIRLASH FAQAT ADMINDA (server ham 403 qaytaradi). O'quv bo'limi
 * ro'yxatni KO'RADI — jarima kiritishda qaysi tarif borligini bilishi kerak.
 */
const props = defineProps<{ canManage: boolean }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

const categoriesQuery = useQuery({
  // Barchasi — arxivlanganlar ham (boshqaruv jadvali).
  queryKey: ['penalty-categories', 'all'],
  queryFn: ({ signal }) => fetchPenaltyCategories(false, { signal }),
})

const rows = computed(() => categoriesQuery.data.value ?? [])

const loadError = computed(() =>
  categoriesQuery.error.value !== null ? toUserMessage(categoriesQuery.error.value) : null,
)

const actionError = ref<string | null>(null)
const dialogOpen = ref(false)
const editing = ref<PenaltyCategoryDto | null>(null)

function openCreate(): void {
  editing.value = null
  dialogOpen.value = true
}

function openEdit(row: PenaltyCategoryDto): void {
  editing.value = row
  dialogOpen.value = true
}

const deleteMutation = useMutation({
  mutationFn: (id: number) => deletePenaltyCategory(id),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['penalty-categories'] })
  },
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

async function askDelete(row: PenaltyCategoryDto): Promise<void> {
  actionError.value = null

  // ★ OGOHLANTIRISH ISHLATILISH SONI BILAN: ishlatilgan tarif
  //   o'chirilmaydi — ARXIVLANADI (server hal qiladi). Operator
  //   nima bo'lishini OLDINDAN bilsin.
  const used = row.usageCount > 0

  const ok = await confirm({
    title: used ? 'Tarifni arxivlash' : 'Tarifni o‘chirish',
    message: `“${row.label}”`,
    confirmLabel: used ? 'Arxivlash' : 'O‘chirish',
    tone: 'danger',
    details: used
      ? [
          `Bu tarif ${row.usageCount} ta jarimada ishlatilgan.`,
          'Shuning uchun o‘chirilmaydi — yangi jarimada tanlanmaydigan bo‘ladi.',
          'Eski jarimalarda nomi va summasi o‘zgarishsiz qoladi.',
        ]
      : ['Bu tarif hech qayerda ishlatilmagan — butunlay o‘chiriladi.'],
  })

  if (!ok) return

  deleteMutation.mutate(row.id)
}
</script>

<template>
  <BaseCard
    title="Jarima tariflari"
    subtitle="Tarif tanlanganda jarima summasi avtomatik hisoblanadi."
    flush
  >
    <template #actions>
      <BaseButton
        v-if="props.canManage"
        size="sm"
        @click="openCreate"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Tarif qo‘shish
      </BaseButton>
    </template>

    <p
      v-if="actionError !== null"
      class="mx-4 mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      role="alert"
      v-text="actionError"
    />

    <DataStatus
      :pending="categoriesQuery.isPending.value"
      :error="loadError"
      :empty="rows.length === 0"
      :retrying="categoriesQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="sliders"
      empty-title="Tarif yo‘q"
      empty-text="Birinchi tarifni qo‘shing — masalan “Darsga kechikish”."
      @retry="categoriesQuery.refetch()"
    >
      <div class="scroll-x-safe scrollbar-slim">
        <table class="zn-table">
          <thead>
            <tr>
              <th class="w-10">
                #
              </th>
              <th>Nomi</th>
              <th>Summa</th>
              <th>Ishlatilgan</th>
              <th
                v-if="props.canManage"
                class="w-32"
              />
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
                <span class="mt-1 flex flex-wrap gap-1">
                  <BaseBadge
                    v-if="row.isSystem"
                    tone="accent"
                  >Avtomatik</BaseBadge>
                  <BaseBadge
                    v-if="!row.isActive"
                    tone="neutral"
                  >Arxivlangan</BaseBadge>
                  <!--
                    ★ SUMMASI 0 — JIMGINA O'CHIQ: bu holat aynan
                    ko'rsatilmasa, "nega jarima yozilmayapti?" degan
                    savolga javob topilmasdi.
                  -->
                  <BaseBadge
                    v-if="row.isSystem && row.amount === 0"
                    tone="warning"
                  >O‘chiq</BaseBadge>
                </span>
              </td>
              <td class="whitespace-nowrap font-semibold tabular-nums text-slate-100">
                {{ formatMoney(row.amount) }}
                <span
                  v-if="row.perUnit"
                  class="text-xs font-normal text-slate-400"
                >so‘m / {{ row.unitLabel ?? 'dona' }}</span>
                <span
                  v-else
                  class="text-xs font-normal text-slate-400"
                >so‘m</span>
              </td>
              <td
                class="tabular-nums text-slate-400"
                v-text="row.usageCount > 0 ? `${row.usageCount} ta` : '—'"
              />
              <td v-if="props.canManage">
                <div class="flex gap-2">
                  <BaseButton
                    size="sm"
                    variant="secondary"
                    @click="openEdit(row)"
                  >
                    Tahrirlash
                  </BaseButton>
                  <BaseButton
                    v-if="!row.isSystem && row.isActive"
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

    <PenaltyCategoryDialog
      :open="dialogOpen"
      :category="editing"
      @close="dialogOpen = false"
    />
  </BaseCard>
</template>
