<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, reactive, ref } from 'vue'

import {
  createAnalysisCriterion,
  deleteAnalysisCriterion,
  fetchAnalysisCriteria,
  updateAnalysisCriterion,
} from '@/entities/analysis-criterion'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import type { AnalysisCriterionDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseField, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS TAHLILI MEZONLARI — dinamik boshqaruv (R29/R30 kengaytmasi)
 * ════════════════════════════════════════════════════════════════════════
 *
 * O'quv bo'limi (yoki Admin) shu yerda "Metodika", "Vaqt boshqaruvi" kabi
 * mezonlarni va ularning maksimal balini belgilaydi. Ro'yxat dinamik:
 * xohlagancha qo'shish/tahrirlash/o'chirish mumkin — `SessionReviewModal`
 * (dars tahlili formasi) shu katalogdan ball tanlaydi.
 *
 * ★ O'CHIRISH XAVFSIZ: allaqachon yozilgan tahlillar mezon nomi va
 * maksimal balini O'ZIDA saqlaydi (server snapshot qiladi), ya'ni bu
 * yerdagi o'zgarish ESKI tahlillarga ta'sir qilmaydi.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

const QUERY_KEY = ['analysis-criteria']

const criteriaQuery = useQuery({
  queryKey: QUERY_KEY,
  queryFn: ({ signal }) => fetchAnalysisCriteria({ signal }),
})

const criteria = computed(() => criteriaQuery.data.value ?? [])

const errorMessage = computed(() =>
  criteriaQuery.error.value !== null ? toUserMessage(criteriaQuery.error.value) : null,
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: QUERY_KEY })
}

// -------------------------------------------------------------- qo'shish

const draftName = ref('')
const draftMaxScore = ref(10)
const createError = ref<string | null>(null)

/** Yangi mezon ro'yxat OXIRIGA tushadi — tartib qayta chizilmaydi. */
const nextSortOrder = computed(() => {
  const last = criteria.value.at(-1)
  return last === undefined ? 0 : last.sortOrder + 1
})

const createMutation = useMutation({
  mutationFn: () =>
    createAnalysisCriterion({
      name: draftName.value.trim(),
      maxScore: draftMaxScore.value,
      sortOrder: nextSortOrder.value,
    }),
  onSuccess: () => {
    createError.value = null
    draftName.value = ''
    draftMaxScore.value = 10
    refresh()
    showToast('Mezon qo‘shildi')
  },
  onError: (error: Error) => {
    createError.value = toUserMessage(error)
  },
})

function submitCreate(): void {
  if (draftName.value.trim().length === 0) return
  createError.value = null
  createMutation.mutate()
}

// -------------------------------------------------------------- tahrirlash

const editingId = ref<number | null>(null)
const editDraft = reactive({ name: '', maxScore: 10 })
const editError = ref<string | null>(null)

function startEdit(criterion: AnalysisCriterionDto): void {
  editingId.value = criterion.id
  editDraft.name = criterion.name
  editDraft.maxScore = criterion.maxScore
  editError.value = null
}

function cancelEdit(): void {
  editingId.value = null
  editError.value = null
}

const updateMutation = useMutation({
  mutationFn: (criterion: AnalysisCriterionDto) =>
    updateAnalysisCriterion(criterion.id, {
      name: editDraft.name.trim(),
      maxScore: editDraft.maxScore,
      sortOrder: criterion.sortOrder,
    }),
  onSuccess: () => {
    editingId.value = null
    editError.value = null
    refresh()
    showToast('Mezon yangilandi')
  },
  onError: (error: Error) => {
    editError.value = toUserMessage(error)
  },
})

function submitEdit(criterion: AnalysisCriterionDto): void {
  if (editDraft.name.trim().length === 0) return
  editError.value = null
  updateMutation.mutate(criterion)
}

// -------------------------------------------------------------- o'chirish

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteAnalysisCriterion(id),
  onSuccess: () => {
    refresh()
    showToast('Mezon o‘chirildi')
  },
  onError: (error: Error) => {
    showToast(toUserMessage(error), 'error')
  },
})

async function removeCriterion(criterion: AnalysisCriterionDto): Promise<void> {
  const ok = await confirm({
    title: 'Mezonni o‘chirish',
    message: `«${criterion.name}» mezoni o‘chiriladi. Allaqachon yozilgan tahlillar o‘zgarishsiz qoladi.`,
    confirmLabel: 'O‘chirish',
    tone: 'danger',
  })
  if (!ok) return
  deleteMutation.mutate(criterion.id)
}
</script>

<template>
  <div>
    <p class="mb-4 text-xs text-slate-400">
      Dars sifati tahlilida ball qo‘yiladigan mezonlar — dinamik ro‘yxat.
    </p>

    <!-- ------------------------------------------------------ qo'shish -->
    <form
      class="mb-5 rounded-xl border border-line bg-ink-900 p-4"
      novalidate
      @submit.prevent="submitCreate"
    >
      <div class="flex flex-wrap items-end gap-3">
        <BaseField
          label="Mezon nomi"
          class="min-w-[220px] flex-1"
        >
          <input
            v-model="draftName"
            type="text"
            class="zn-input"
            placeholder="Masalan: Metodika"
            maxlength="200"
          >
        </BaseField>

        <BaseField
          label="Maksimal ball"
          class="w-28"
        >
          <input
            v-model.number="draftMaxScore"
            type="number"
            min="1"
            max="100"
            class="zn-input"
          >
        </BaseField>

        <BaseButton
          type="submit"
          size="md"
          :loading="createMutation.isPending.value"
          :disabled="draftName.trim().length === 0"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="15"
            />
          </template>
          Qo‘shish
        </BaseButton>
      </div>

      <p
        v-if="createError !== null"
        class="mt-2 text-xs text-rose-400"
        role="alert"
        v-text="createError"
      />
    </form>

    <!-- ------------------------------------------------------ ro'yxat -->
    <DataStatus
      :pending="criteriaQuery.isPending.value"
      :error="errorMessage"
      :empty="criteria.length === 0"
      :retrying="criteriaQuery.isFetching.value"
      empty-icon="sliders"
      empty-title="Mezon yo‘q"
      empty-text="Yuqoridagi forma orqali birinchi mezonni qo‘shing."
      :skeleton-rows="4"
      @retry="criteriaQuery.refetch()"
    >
      <ul class="divide-y divide-line rounded-xl border border-line">
        <li
          v-for="criterion in criteria"
          :key="criterion.id"
          class="p-3.5"
        >
          <!-- ------------------------------------------- tahrirlash rejimi -->
          <div
            v-if="editingId === criterion.id"
            class="flex flex-wrap items-end gap-3"
          >
            <BaseField
              label="Mezon nomi"
              class="min-w-[220px] flex-1"
            >
              <input
                v-model="editDraft.name"
                type="text"
                class="zn-input"
                maxlength="200"
              >
            </BaseField>
            <BaseField
              label="Maksimal ball"
              class="w-28"
            >
              <input
                v-model.number="editDraft.maxScore"
                type="number"
                min="1"
                max="100"
                class="zn-input"
              >
            </BaseField>
            <BaseButton
              size="sm"
              :loading="updateMutation.isPending.value"
              :disabled="editDraft.name.trim().length === 0"
              @click="submitEdit(criterion)"
            >
              Saqlash
            </BaseButton>
            <BaseButton
              size="sm"
              variant="ghost"
              @click="cancelEdit"
            >
              Bekor qilish
            </BaseButton>
            <p
              v-if="editError !== null"
              class="w-full text-xs text-rose-400"
              role="alert"
              v-text="editError"
            />
          </div>

          <!-- ------------------------------------------- o'qish rejimi -->
          <div
            v-else
            class="flex items-center justify-between gap-3"
          >
            <div class="min-w-0">
              <p
                class="truncate text-sm font-semibold text-slate-100"
                v-text="criterion.name"
              />
              <p
                class="text-[11px] text-slate-400"
                v-text="`maks ${criterion.maxScore} ball`"
              />
            </div>
            <div class="flex shrink-0 items-center gap-2">
              <BaseButton
                size="sm"
                variant="secondary"
                @click="startEdit(criterion)"
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
                @click="removeCriterion(criterion)"
              >
                <AppIcon
                  name="trash"
                  :size="15"
                />
              </button>
            </div>
          </div>
        </li>
      </ul>
    </DataStatus>
  </div>
</template>
