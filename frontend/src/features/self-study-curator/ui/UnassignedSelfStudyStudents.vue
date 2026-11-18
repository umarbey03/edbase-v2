<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, reactive } from 'vue'

import {
  assignStudentCurator,
  CURATOR_ROLE_LABELS,
  fetchCuratorCandidates,
  fetchUnassignedSelfStudyStudents,
  SELF_STUDY_CURATOR_KEYS,
} from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseButton, DataStatus } from '@/shared/ui'

/**
 * O'QUV BO'LIMI: FAOL OBUNALI, LEKIN KURATORSIZ O'QUVCHILAR (2026-11-18).
 *
 * ★ NIMA UCHUN ALOHIDA RO'YXAT: obuna saytda o'zi sotib olinadi — buyurtma
 *   tasdiqlanganda kurator tanlanmaydi. Bu ro'yxat bo'lmasa, yangi o'quvchi
 *   jimgina kuratorsiz qolardi: vazifasini hech kim tekshirmasdi va qat'iy
 *   qoida tufayli 2-dars hech qachon ochilmasdi.
 */
const queryClient = useQueryClient()

const listQuery = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.unassigned,
  queryFn: ({ signal }) => fetchUnassignedSelfStudyStudents({ signal }),
  refetchInterval: 60_000,
})
const candidatesQuery = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.candidates,
  queryFn: ({ signal }) => fetchCuratorCandidates({ signal }),
})

const rows = computed(() => listQuery.data.value ?? [])
const candidates = computed(() => candidatesQuery.data.value ?? [])
const picked = reactive<Record<number, number | null>>({})

const assign = useMutation({
  mutationFn: (studentId: number) =>
    assignStudentCurator(studentId, { curatorId: picked[studentId] as number, backupCuratorId: null }),
  onSuccess: () => {
    showToast('Kurator biriktirildi', 'success')
    void queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.all })
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})
</script>

<template>
  <section class="space-y-2">
    <h2 class="flex items-center gap-2 text-sm font-semibold text-slate-300">
      <AppIcon
        name="alert"
        :size="15"
        :class="rows.length > 0 ? 'text-amber-300' : 'text-slate-500'"
      />
      Kuratorsiz o‘quvchilar
      <span
        v-if="rows.length > 0"
        class="rounded-full bg-amber-500/15 px-2 py-0.5 text-xs text-amber-200"
      >{{ rows.length }}</span>
    </h2>

    <DataStatus
      :pending="listQuery.isPending.value"
      :error="listQuery.isError.value ? toUserMessage(listQuery.error.value) : null"
      :empty="listQuery.isSuccess.value && rows.length === 0"
      :retrying="listQuery.isFetching.value"
      empty-icon="user-check"
      empty-title="Hammasiga kurator biriktirilgan"
      empty-text="Faol obunali har bir Mustaqil o‘quvchining kuratori bor."
      @retry="listQuery.refetch()"
    />

    <ul
      v-if="rows.length > 0"
      class="space-y-2"
    >
      <li
        v-for="row in rows"
        :key="row.studentId"
        class="flex flex-wrap items-center gap-3 rounded-xl border border-amber-500/30 bg-ink-900 px-4 py-3"
      >
        <span class="min-w-0 flex-1">
          <span class="block truncate text-sm font-semibold text-slate-100">{{ row.fullName }}</span>
          <span class="block text-xs text-slate-400">Obuna {{ formatDate(row.subscriptionEndsAt) }} gacha</span>
        </span>
        <select
          v-model="picked[row.studentId]"
          class="min-w-[14rem] rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
          :aria-label="`${row.fullName} uchun kurator`"
        >
          <option
            :value="undefined"
            disabled
          >
            Kurator tanlang
          </option>
          <option
            v-for="c in candidates"
            :key="c.id"
            :value="c.id"
          >
            {{ c.fullName }} — {{ CURATOR_ROLE_LABELS[c.role] ?? c.role }}, {{ c.activeStudents }} o‘quvchi
          </option>
        </select>
        <BaseButton
          size="sm"
          :disabled="picked[row.studentId] == null"
          :loading="assign.isPending.value && assign.variables.value === row.studentId"
          @click="assign.mutate(row.studentId)"
        >
          Biriktirish
        </BaseButton>
      </li>
    </ul>
  </section>
</template>
