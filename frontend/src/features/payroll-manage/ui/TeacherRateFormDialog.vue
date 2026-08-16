<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchUsers } from '@/entities/user'
import {
  createTeacherRate,
  PAYROLL_ROLE_OPTIONS,
  payrollRoleLabel,
  todayIsoDate,
  updateTeacherRate,
} from '@/entities/payroll'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { TeacherRateDto, UserRoleName } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Stavka yaratish/tahrirlash — `TariffFormDialog` bilan AYNI naqsh va AYNI
 * sabab: narx TARIXI, tahrirlash o'tgan oy hisobotini jimgina qayta yozadi.
 *
 * ★ `PUT` — TO'LIQ ALMASHTIRISH: forma HAMMA maydonni yuklaydi va qaytaradi.
 */
const props = defineProps<{ open: boolean; rate: TeacherRateDto | null }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const MAX_AMOUNT = 1_000_000_000

const role = ref<UserRoleName>('Teacher')
const userId = ref<number | null>(null)
const perSessionRateText = ref('')
const perStudentBonusRateText = ref('')
/** ★ 2026-08-16 — asosan kurator uchun: baza oylik + KPI (`TeacherRate.BaseSalary`/`ActiveStudentBonusRate`). */
const baseSalaryText = ref('')
const activeStudentBonusRateText = ref('')
/** ★ 2026-08-16 — dam olish/bayram kuni asosiy stavkaga ko'paytiruvchi (masalan 1.5). Bo'sh — ustama yo'q. */
const weekendMultiplierText = ref('')
const activeFrom = ref(todayIsoDate())
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.rate !== null)
/** Baza oylik/KPI maydonlari FAQAT kurator (`Assistant`) uchun ko'rinadi — ustoz uchun ma'nosiz. */
const showCuratorFields = computed(() => role.value === 'Assistant')

function resetForm(): void {
  const rate = props.rate
  role.value = rate?.role ?? 'Teacher'
  userId.value = rate?.userId ?? null
  perSessionRateText.value = rate === null ? '' : String(rate.perSessionRate)
  perStudentBonusRateText.value = rate === null ? '' : String(rate.perStudentBonusRate)
  baseSalaryText.value = rate === null || rate.baseSalary === 0 ? '' : String(rate.baseSalary)
  activeStudentBonusRateText.value =
    rate === null || rate.activeStudentBonusRate === 0 ? '' : String(rate.activeStudentBonusRate)
  weekendMultiplierText.value =
    rate?.weekendHolidayMultiplier == null ? '' : String(rate.weekendHolidayMultiplier)
  activeFrom.value = rate?.activeFrom ?? todayIsoDate()
  isActive.value = rate?.isActive ?? true
  errorMessage.value = null
}

watch(() => [props.open, props.rate], resetForm, { immediate: true })

/*
  Xodim ro'yxati TANLANGAN ROLGA qarab so'raladi: Ustozlar ro'yxatida
  kurator ko'rinsa, saqlashda server "rol mos emas" deb 400 qaytarardi va
  bu xodimni tanlab bo'lgach sodir bo'lardi — kechroq, tushunarsiz joyda.
*/
const usersQuery = useQuery({
  queryKey: ['users', 'by-role', role],
  queryFn: ({ signal }) =>
    fetchUsers({ role: role.value, isActive: true, pageSize: 200 }, { signal }),
  enabled: computed(() => props.open),
})

const users = computed(() => usersQuery.data.value?.items ?? [])

/* Tahrirlanayotgan stavka arxivlangan xodimga bog'langan bo'lishi mumkin. */
const missingUserOption = computed(() => {
  const rate = props.rate
  if (rate?.userId == null) return null
  if (users.value.some((item) => item.id === rate.userId)) return null
  return { id: rate.userId, name: `${rate.userName ?? 'Xodim'} (ro‘yxatda yo‘q)` }
})

/* Rol o'zgarsa, boshqa rolga tegishli xodim tanlovi endi ma'nosiz. */
watch(role, () => {
  userId.value = null
})

const perSessionRate = computed(() => parseMoneyInput(perSessionRateText.value))
const perStudentBonusRate = computed(() => parseMoneyInput(perStudentBonusRateText.value))

const perSessionRateError = computed(() => {
  if (perSessionRateText.value.trim().length === 0) return null
  const value = perSessionRate.value
  if (value === null) return 'Stavkani raqam bilan kiriting (masalan 40000).'
  if (value > MAX_AMOUNT) return 'Stavka juda katta.'
  return null
})

const perStudentBonusRateError = computed(() => {
  if (perStudentBonusRateText.value.trim().length === 0) return null
  const value = perStudentBonusRate.value
  if (value === null) return 'Bonusni raqam bilan kiriting (masalan 3000).'
  if (value > MAX_AMOUNT) return 'Bonus juda katta.'
  return null
})

/** Bo'sh maydon = 0 (baza oylik/KPI yo'q) — `perSessionRate`dan farqli, bu ikkovi IXTIYORIY. */
const baseSalary = computed(() => (baseSalaryText.value.trim().length === 0 ? 0 : parseMoneyInput(baseSalaryText.value)))
const activeStudentBonusRate = computed(() =>
  activeStudentBonusRateText.value.trim().length === 0 ? 0 : parseMoneyInput(activeStudentBonusRateText.value),
)
const weekendMultiplier = computed(() =>
  weekendMultiplierText.value.trim().length === 0 ? null : parseMoneyInput(weekendMultiplierText.value),
)

const baseSalaryError = computed(() => {
  if (baseSalaryText.value.trim().length === 0) return null
  if (baseSalary.value === null) return 'Baza oylikni raqam bilan kiriting.'
  if (baseSalary.value > MAX_AMOUNT) return 'Baza oylik juda katta.'
  return null
})

const activeStudentBonusRateError = computed(() => {
  if (activeStudentBonusRateText.value.trim().length === 0) return null
  if (activeStudentBonusRate.value === null) return 'KPI bonusini raqam bilan kiriting.'
  if (activeStudentBonusRate.value > MAX_AMOUNT) return 'KPI bonusi juda katta.'
  return null
})

const weekendMultiplierError = computed(() => {
  if (weekendMultiplierText.value.trim().length === 0) return null
  const value = weekendMultiplier.value
  if (value === null) return 'Ko‘paytiruvchini raqam bilan kiriting (masalan 1.5).'
  if (value < 1 || value > 10) return 'Ko‘paytiruvchi 1..10 oralig‘ida bo‘lishi kerak.'
  return null
})

const canSubmit = computed(
  () =>
    perSessionRate.value !== null &&
    perSessionRateError.value === null &&
    perStudentBonusRate.value !== null &&
    perStudentBonusRateError.value === null &&
    baseSalary.value !== null &&
    baseSalaryError.value === null &&
    activeStudentBonusRate.value !== null &&
    activeStudentBonusRateError.value === null &&
    weekendMultiplierError.value === null &&
    activeFrom.value.length > 0,
)

const mutation = useMutation({
  mutationFn: () => {
    const sessionRate = perSessionRate.value
    const bonusRate = perStudentBonusRate.value
    if (sessionRate === null || bonusRate === null) throw new Error('Stavka kiritilmagan.')

    const payload = {
      role: role.value,
      perSessionRate: sessionRate,
      perStudentBonusRate: bonusRate,
      activeFrom: activeFrom.value,
      isActive: isActive.value,
      userId: userId.value,
      baseSalary: baseSalary.value ?? 0,
      activeStudentBonusRate: activeStudentBonusRate.value ?? 0,
      weekendHolidayMultiplier: weekendMultiplier.value,
    }
    const rate = props.rate
    return rate === null ? createTeacherRate(payload) : updateTeacherRate(rate.id, payload)
  },
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const confirm = useConfirm()

/** R4 — TASDIQ FAQAT TAHRIRLASHDA, `TariffFormDialog` bilan AYNI sabab. */
async function submit(): Promise<void> {
  if (!canSubmit.value || mutation.isPending.value) return

  const rate = props.rate
  if (rate !== null) {
    const sessionRate = perSessionRate.value
    const details: string[] = []

    if (sessionRate !== null && sessionRate !== rate.perSessionRate) {
      details.push(`Dars stavkasi: ${formatSum(rate.perSessionRate)} → ${formatSum(sessionRate)}`)
    }
    details.push('Stavka tarixi saqlanmaydi — eski qiymat hech qayerda qolmaydi.')

    const ok = await confirm({
      title: 'Stavkani tahrirlash',
      message:
        'Barcha maydon formadagi qiymatlar bilan ALMASHTIRILADI. Keyingi hisob-kitob shu stavkadan olinadi.',
      confirmLabel: 'Saqlash',
      tone: 'warning',
      details,
    })
    if (!ok) return
  }

  errorMessage.value = null
  mutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    :title="isEdit ? 'Stavkani tahrirlash' : 'Yangi stavka'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="submit"
    >
      <p class="mb-3.5 text-xs leading-relaxed text-slate-400">
        Stavka o‘zgarsa eskisini tahrirlamang —
        <span class="font-semibold text-slate-200">yangi stavka qo‘shing</span>, shunda haq tarixi
        saqlanadi. Stavka har DARSNING sanasiga qarab tanlanadi.
      </p>

      <div class="grid gap-3 sm:grid-cols-2">
        <BaseField label="Rol">
          <select
            v-model="role"
            class="zn-input"
          >
            <option
              v-for="option in PAYROLL_ROLE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>
        <BaseField
          :label="`Xodim (ixtiyoriy)`"
          hint="Tanlanmasa — shu rolning standart stavkasi."
        >
          <select
            v-model="userId"
            class="zn-input"
          >
            <option :value="null">
              — {{ payrollRoleLabel(role) }}larning standarti —
            </option>
            <option
              v-if="missingUserOption !== null"
              :value="missingUserOption.id"
            >
              {{ missingUserOption.name }}
            </option>
            <option
              v-for="item in users"
              :key="item.id"
              :value="item.id"
            >
              {{ item.fullName ?? `#${item.id}` }}
            </option>
          </select>
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Dars stavkasi (so‘m)"
          :error="perSessionRateError"
          :hint="perSessionRate === null ? '' : formatSum(perSessionRate)"
        >
          <input
            v-model="perSessionRateText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            placeholder="40000"
          >
        </BaseField>
        <BaseField
          label="O‘quvchi bonusi (so‘m)"
          :error="perStudentBonusRateError"
          :hint="perStudentBonusRate === null ? '' : formatSum(perStudentBonusRate)"
        >
          <input
            v-model="perStudentBonusRateText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            placeholder="3000"
          >
        </BaseField>
      </div>

      <div
        v-if="showCuratorFields"
        class="mt-3 rounded-lg border border-line bg-ink-950 p-3"
      >
        <p class="mb-2.5 text-xs text-slate-400">
          Kurator uchun — dars soniga bog'liq bo'lmagan qism (GetCourse/CIS bozorida odatiy model).
        </p>
        <div class="grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Baza oylik (so‘m, ixtiyoriy)"
            :error="baseSalaryError"
            :hint="baseSalary === null || baseSalary === 0 ? '' : formatSum(baseSalary)"
          >
            <input
              v-model="baseSalaryText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="0"
            >
          </BaseField>
          <BaseField
            label="KPI — faol o‘quvchi boshiga (so‘m, ixtiyoriy)"
            :error="activeStudentBonusRateError"
            :hint="activeStudentBonusRate === null || activeStudentBonusRate === 0 ? '' : formatSum(activeStudentBonusRate)"
          >
            <input
              v-model="activeStudentBonusRateText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="0"
            >
          </BaseField>
        </div>
      </div>

      <div class="mt-3">
        <BaseField
          label="Dam olish/bayram kuni ko‘paytiruvchisi (ixtiyoriy)"
          :error="weekendMultiplierError"
          hint="Masalan 1.5 = shanba/yakshanba yoki bayram kunidagi darsda asosiy stavka +50%. Bo‘sh — ustama yo‘q."
        >
          <input
            v-model="weekendMultiplierText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="decimal"
            autocomplete="off"
            placeholder="1.5"
          >
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField label="Qachondan kuchga kiradi">
          <input
            v-model="activeFrom"
            class="zn-input"
            type="date"
            required
          >
        </BaseField>
        <label class="flex min-h-11 items-center gap-2.5 self-end text-sm text-slate-300">
          <input
            v-model="isActive"
            type="checkbox"
            class="size-4 accent-brand-500"
          >
          Faol stavka
        </label>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Bekor qilish
      </BaseButton>
      <BaseButton
        :disabled="!canSubmit"
        :loading="mutation.isPending.value"
        @click="submit"
      >
        Saqlash
      </BaseButton>
    </template>
  </BaseModal>
</template>
