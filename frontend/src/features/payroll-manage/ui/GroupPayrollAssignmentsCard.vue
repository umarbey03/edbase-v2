<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchGroups } from '@/entities/group'
import {
  fetchGroupPayrollAssignments,
  fetchPayrollRules,
  GROUP_PAYROLL_MODE_OPTIONS,
  groupPayrollModeLabel,
  setGroupPayrollAssignment,
} from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { GroupPayrollAssignmentDto, GroupPayrollModeName } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  BaseField,
  BaseSpinner,
} from '@/shared/ui'

/**
 * GURUH DARAJASIDAGI TAYINLASH — HolliHop'dagi «ставка в карточке УЕ».
 *
 * ★ NIMA UCHUN KERAK: avtomatik moslash (rol/kurs/kategoriya) hayotdagi
 * istisnolarni qoplamaydi — "bu bitta guruh ustozning okladiga kiradi",
 * "bu guruhga kelishuv bo'yicha boshqa stavka". Bu ekransiz admin butun
 * qoida to'plamini bitta guruh uchun buzishga majbur bo'lardi.
 *
 * ★ RO'YXAT FAQAT ISTISNOLARNI ko'rsatadi: "Avtomatik" — standart holat va
 * guruhlar soni yuzlab bo'lishi mumkin. Ro'yxat "nima odatdagidan farq
 * qiladi?" degan savolga javob berishi kerak.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

const formOpen = ref(false)
const editingGroupId = ref<number | null>(null)

const groupId = ref<number | null>(null)
const mode = ref<GroupPayrollModeName>('IncludedInSalary')
const ruleId = ref<number | null>(null)
const errorMessage = ref<string | null>(null)

const assignmentsQuery = useQuery({
  queryKey: ['payroll', 'group-assignments'],
  queryFn: ({ signal }) => fetchGroupPayrollAssignments({ signal }),
})

const assignments = computed(() => assignmentsQuery.data.value ?? [])

const listError = computed(() =>
  assignmentsQuery.error.value !== null ? toUserMessage(assignmentsQuery.error.value) : null,
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'payroll-assignment-picker'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 200 }, { signal }),
  enabled: computed(() => formOpen.value),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const rulesQuery = useQuery({
  queryKey: ['payroll', 'rules'],
  queryFn: ({ signal }) => fetchPayrollRules({ signal }),
  enabled: computed(() => formOpen.value),
})

/**
 * ★ FAQAT DARS QAMROVIDAGI qoidalar tanlanadi: oklad guruhga bog'lanmaydi
 * (server ham rad etadi). Ro'yxatda ko'rsatib, keyin 400 qaytarish adminni
 * "nega bo'lmaydi?" degan savol bilan qoldirardi.
 */
const selectableRules = computed(() =>
  (rulesQuery.data.value ?? []).filter((rule) => rule.isSessionScoped && rule.isActive),
)

function openCreate(): void {
  editingGroupId.value = null
  groupId.value = null
  mode.value = 'IncludedInSalary'
  ruleId.value = null
  errorMessage.value = null
  formOpen.value = true
}

function openEdit(assignment: GroupPayrollAssignmentDto): void {
  editingGroupId.value = assignment.groupId
  groupId.value = assignment.groupId
  mode.value = assignment.mode
  ruleId.value = assignment.ruleId
  errorMessage.value = null
  formOpen.value = true
}

const canSubmit = computed(
  () => groupId.value !== null && (mode.value !== 'FixedRule' || ruleId.value !== null),
)

const mutation = useMutation({
  mutationFn: (payload: { groupId: number; mode: GroupPayrollModeName; ruleId: number | null }) =>
    setGroupPayrollAssignment(payload.groupId, {
      mode: payload.mode,
      ruleId: payload.mode === 'FixedRule' ? payload.ruleId : null,
    }),
  onSuccess: () => {
    errorMessage.value = null
    formOpen.value = false
    void queryClient.invalidateQueries({ queryKey: ['payroll'] })
  },
  onError: (error: unknown) => {
    errorMessage.value = toUserMessage(error)
  },
})

function submit(): void {
  const id = groupId.value
  if (id === null || !canSubmit.value || mutation.isPending.value) return

  errorMessage.value = null
  mutation.mutate({ groupId: id, mode: mode.value, ruleId: ruleId.value })
}

/**
 * Tayinlashni olib tashlash = "Avtomatik" ga qaytarish.
 *
 * ★ "O'CHIRISH" DEB ATALMAYDI: guruh o'chmaydi, faqat istisno bekor
 * bo'ladi. "O'chirish" so'zi adminni guruhning o'zi yo'qoladi deb
 * o'ylashga majbur qilardi.
 */
async function askReset(assignment: GroupPayrollAssignmentDto): Promise<void> {
  const ok = await confirm({
    title: 'Avtomatik rejimga qaytarish',
    message: `“${assignment.groupName}” guruhi odatdagi qoidalar bo‘yicha hisoblanadigan bo‘ladi.`,
    confirmLabel: 'Qaytarish',
    details: ['O‘tgan oy hisobotlari o‘zgarmaydi — ular allaqachon hisoblangan.'],
  })
  if (!ok) return

  mutation.mutate({ groupId: assignment.groupId, mode: 'Auto', ruleId: null })
}
</script>

<template>
  <BaseCard
    title="Guruh bo‘yicha istisnolar"
    subtitle="Odatda guruh darslari qoidalar bo‘yicha avtomatik hisoblanadi. Bu yerda faqat ISTISNOLAR ko‘rinadi: guruh xodimning okladiga kirsa yoki unga aniq bitta qoida majburlangan bo‘lsa."
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
      v-if="formOpen"
      class="mb-4 rounded-lg border border-line bg-ink-950 p-3"
    >
      <div class="grid gap-3 sm:grid-cols-3">
        <BaseField label="Guruh">
          <select
            v-model="groupId"
            class="zn-input"
            :disabled="editingGroupId !== null"
          >
            <option :value="null">
              — tanlang —
            </option>
            <option
              v-for="item in groups"
              :key="item.id"
              :value="item.id"
            >
              {{ item.name }}
            </option>
          </select>
        </BaseField>

        <BaseField label="Rejim">
          <select
            v-model="mode"
            class="zn-input"
          >
            <option
              v-for="option in GROUP_PAYROLL_MODE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>

        <BaseField
          label="Qoida"
          :hint="mode === 'FixedRule' ? 'Faqat dars uchun hisoblanadigan qoidalar.' : 'Faqat “Aniq qoida” rejimida kerak.'"
        >
          <select
            v-model="ruleId"
            class="zn-input"
            :disabled="mode !== 'FixedRule'"
          >
            <option :value="null">
              — tanlang —
            </option>
            <option
              v-for="rule in selectableRules"
              :key="rule.id"
              :value="rule.id"
            >
              {{ rule.name }}
            </option>
          </select>
        </BaseField>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-2 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />

      <div class="mt-3 flex justify-end gap-2">
        <BaseButton
          size="sm"
          variant="secondary"
          @click="formOpen = false"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          size="sm"
          :disabled="!canSubmit"
          :loading="mutation.isPending.value"
          @click="submit"
        >
          Saqlash
        </BaseButton>
      </div>
    </div>

    <div
      v-if="assignmentsQuery.isPending.value"
      class="flex justify-center py-6"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="listError !== null"
      class="text-xs text-rose-400"
      role="alert"
      v-text="listError"
    />

    <p
      v-else-if="assignments.length === 0"
      class="text-xs text-slate-400"
    >
      Istisno yo‘q — barcha guruhlar qoidalar bo‘yicha avtomatik hisoblanadi.
    </p>

    <ul
      v-else
      class="divide-y divide-line"
    >
      <li
        v-for="assignment in assignments"
        :key="assignment.groupId"
        class="flex flex-wrap items-center gap-3 py-2.5 first:pt-0 last:pb-0"
      >
        <div class="min-w-0 flex-1">
          <p
            class="truncate text-sm font-medium text-slate-100"
            v-text="assignment.groupName"
          />
          <p class="mt-0.5 text-[11px] text-slate-400">
            {{ groupPayrollModeLabel(assignment.mode) }}
            <template v-if="assignment.ruleName !== null">
              · {{ assignment.ruleName }}
            </template>
          </p>
        </div>

        <BaseBadge :tone="assignment.mode === 'IncludedInSalary' ? 'warning' : 'accent'">
          {{ assignment.mode === 'IncludedInSalary' ? 'Oklad ichida' : 'Aniq qoida' }}
        </BaseBadge>

        <div class="flex shrink-0 items-center gap-2">
          <BaseButton
            size="sm"
            variant="secondary"
            @click="openEdit(assignment)"
          >
            <template #icon>
              <AppIcon
                name="edit"
                :size="13"
              />
            </template>
            O‘zgartirish
          </BaseButton>
          <button
            type="button"
            class="tap-target flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-400"
            title="Avtomatik rejimga qaytarish"
            @click="askReset(assignment)"
          >
            <AppIcon
              name="trash"
              :size="15"
            />
          </button>
        </div>
      </li>
    </ul>
  </BaseCard>
</template>
