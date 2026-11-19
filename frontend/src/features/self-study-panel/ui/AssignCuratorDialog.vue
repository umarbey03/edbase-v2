<script setup lang="ts">
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { SELF_STUDY_CURATOR_KEYS, assignStudentCurator, unassignStudentCurator } from '@/entities/self-study-curator'
import {
  SELF_STUDY_PANEL_KEYS,
  bulkAssignSelfStudyCurator,
  type BulkAssignFailureDto,
  type SelfStudyCuratorRowDto,
} from '@/entities/self-study-panel'
import { toUserMessage } from '@/shared/api'
import { showToast } from '@/shared/lib/useToast'
import { BaseButton, BaseModal } from '@/shared/ui'

/**
 * KURATOR BIRIKTIRISH (Mustaqil paneli, 2026-11-19).
 *
 * BITTA o'quvchi — mavjud `PUT /self-study/curators/students/{id}` (u
 * biriktirish ham, almashtirish ham); bu yerda "olib qo'yish" ham bor.
 * BIR NECHTA — `POST /self-study/panel/students/assign`: har o'quvchi
 * alohida hal qilinadi va kim biriktirilmagani sababi bilan ko'rsatiladi.
 *
 * Qoidalar (guruh o'quvchisiga emas, obuna shart, zaxira ≠ asosiy) —
 * SERVERDA; bu oyna faqat tanlov beradi.
 */
const props = defineProps<{
  open: boolean
  /** Tanlangan o'quvchilar (bitta yoki bir nechta). */
  students: { id: number, name: string, curatorId: number | null, backupCuratorId: number | null }[]
  curators: SelfStudyCuratorRowDto[]
}>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()

const curatorId = ref<number | ''>('')
const backupId = ref<number | ''>('')
const failures = ref<BulkAssignFailureDto[]>([])
const error = ref<string | null>(null)

const single = computed(() => (props.students.length === 1 ? (props.students[0] ?? null) : null))

watch(
  () => props.open,
  (open) => {
    if (!open) return
    failures.value = []
    error.value = null
    curatorId.value = single.value?.curatorId ?? ''
    backupId.value = single.value?.backupCuratorId ?? ''
  },
)

/** Kuratorlar avval (Assistant), keyin ustozlar — ikkalasi ham kurator bo'la oladi. */
const options = computed(() =>
  [...props.curators].sort((a, b) =>
    a.role === b.role ? a.fullName.localeCompare(b.fullName) : a.role === 'Assistant' ? -1 : 1),
)

function loadLabel(row: SelfStudyCuratorRowDto): string {
  const role = row.role === 'Assistant' ? 'kurator' : 'ustoz'
  return `${row.fullName} · ${role} · ${row.primaryStudents} o‘quvchi`
}

const backupInvalid = computed(() => backupId.value !== '' && backupId.value === curatorId.value)

async function invalidate(): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: SELF_STUDY_PANEL_KEYS.all }),
    queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.all }),
  ])
}

const save = useMutation({
  mutationFn: async () => {
    if (curatorId.value === '') throw new Error('Kuratorni tanlang.')
    const backup = backupId.value === '' ? null : backupId.value

    if (single.value !== null) {
      await assignStudentCurator(single.value.id, { curatorId: curatorId.value, backupCuratorId: backup })
      return { assigned: 1, failed: [] as BulkAssignFailureDto[] }
    }

    return bulkAssignSelfStudyCurator({
      studentIds: props.students.map((s) => s.id),
      curatorId: curatorId.value,
      backupCuratorId: backup,
    })
  },
  onSuccess: async (result) => {
    await invalidate()
    failures.value = result.failed
    if (result.assigned > 0) showToast(`${result.assigned} ta o‘quvchiga kurator biriktirildi`)
    if (result.failed.length === 0) emit('close')
  },
  onError: (err) => {
    error.value = toUserMessage(err)
  },
})

const remove = useMutation({
  mutationFn: () => unassignStudentCurator(single.value!.id),
  onSuccess: async () => {
    await invalidate()
    showToast('Kurator olib qo‘yildi')
    emit('close')
  },
  onError: (err) => {
    error.value = toUserMessage(err)
  },
})
</script>

<template>
  <BaseModal
    :open="open"
    :title="single !== null ? `Kurator — ${single.name}` : `${students.length} ta o‘quvchiga kurator`"
    @close="emit('close')"
  >
    <div class="space-y-4">
      <label class="block">
        <span class="mb-1 block text-xs font-semibold text-slate-300">Asosiy kurator</span>
        <select
          v-model="curatorId"
          class="zn-input"
        >
          <option value="">
            Tanlang
          </option>
          <option
            v-for="row in options"
            :key="row.id"
            :value="row.id"
          >
            {{ loadLabel(row) }}
          </option>
        </select>
        <span class="mt-1 block text-[11px] text-dim">
          O‘quvchi faqat asosiy kurator bilan yozishadi; u vazifalarni tekshiradi va darslarni ochadi.
        </span>
      </label>

      <label class="block">
        <span class="mb-1 block text-xs font-semibold text-slate-300">Zaxira kurator (ixtiyoriy)</span>
        <select
          v-model="backupId"
          class="zn-input"
        >
          <option value="">
            Yo‘q
          </option>
          <option
            v-for="row in options"
            :key="row.id"
            :value="row.id"
            :disabled="row.id === curatorId"
          >
            {{ loadLabel(row) }}
          </option>
        </select>
        <span
          v-if="backupInvalid"
          class="mt-1 block text-[11px] text-rose-400"
        >
          Zaxira kurator asosiy kuratordan boshqa odam bo‘lishi kerak.
        </span>
        <span
          v-else
          class="mt-1 block text-[11px] text-dim"
        >
          Asosiy kurator yo‘q paytda vazifalarni tekshiradi.
        </span>
      </label>

      <div
        v-if="students.length > 1"
        class="max-h-32 overflow-y-auto rounded-lg border border-line bg-ink-900 p-2 text-xs text-slate-300 scrollbar-slim"
      >
        <span
          v-for="student in students"
          :key="student.id"
          class="mr-2 inline-block"
          v-text="student.name"
        />
      </div>

      <div
        v-if="failures.length > 0"
        class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs text-amber-100"
        role="alert"
      >
        <p class="mb-1 font-semibold">
          Quyidagilar biriktirilmadi:
        </p>
        <ul class="list-disc space-y-0.5 pl-4">
          <li
            v-for="item in failures"
            :key="item.studentId"
          >
            <span class="font-medium">{{ item.fullName ?? `#${item.studentId}` }}</span> — {{ item.error }}
          </li>
        </ul>
      </div>

      <p
        v-if="error !== null"
        class="rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
        role="alert"
        v-text="error"
      />
    </div>

    <template #footer>
      <div class="flex w-full flex-wrap items-center justify-between gap-2">
        <BaseButton
          v-if="single !== null && single.curatorId !== null"
          variant="ghost"
          :loading="remove.isPending.value"
          @click="remove.mutate()"
        >
          Kuratorni olib qo‘yish
        </BaseButton>
        <span v-else />
        <div class="flex gap-2">
          <BaseButton
            variant="secondary"
            @click="emit('close')"
          >
            Bekor qilish
          </BaseButton>
          <BaseButton
            :loading="save.isPending.value"
            :disabled="curatorId === '' || backupInvalid"
            @click="error = null; save.mutate()"
          >
            Saqlash
          </BaseButton>
        </div>
      </div>
    </template>
  </BaseModal>
</template>
