<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchCourses } from '@/entities/course'
import { fetchGroups } from '@/entities/group'
import { fetchGroupCategories } from '@/entities/group-category'
import {
  createPayrollRule,
  isPercentKind,
  isSessionScopedKind,
  PAYROLL_BASIS_OPTIONS,
  PAYROLL_ROLE_OPTIONS,
  PAYROLL_RULE_KIND_OPTIONS,
  payrollRoleLabel,
  payrollRuleKindHint,
  supportsGroupTargeting,
  todayIsoDate,
  updatePayrollRule,
  usesStudentCount,
} from '@/entities/payroll'
import { fetchUsers } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type {
  GroupTypeName,
  PayrollBasisName,
  PayrollRuleDto,
  PayrollRuleKindName,
  PayrollRuleTierInput,
  UserRoleName,
} from '@/shared/types'
import { AppIcon, BaseButton, BaseDrawer, BaseField } from '@/shared/ui'

/**
 * OYLIK QOIDASINI yaratish/tahrirlash.
 *
 * ★ NEGA DRAWER, `BaseModal` EMAS: qoidada ettita tur va har turda o'z
 * maydonlari bor (bosqichlar jadvali, reja, shartlar, maqsad matritsasi).
 * Markazdagi modalda bu forma ikki ekran skroll bo'lardi va admin qaysi
 * bo'limda ekanini yo'qotardi.
 *
 * ★ MAYDONLAR TURGA QARAB KO'RINADI: "tushumdan foiz" da akademik soat
 * maydonini ko'rsatish adminni "buni ham to'ldirishim kerakmi?" degan
 * savolga qo'yardi. Ko'rinmaydigan maydon SAQLASHDA HAM yuborilmaydi —
 * server ularni baribir tozalaydi (`PayrollRuleService.Apply`), lekin ikki
 * qatlam bir xil gapni aytgani yaxshi.
 *
 * ★ `PUT` — TO'LIQ ALMASHTIRISH: forma HAMMA maydonni yuklaydi va qaytaradi.
 */
const props = defineProps<{ open: boolean; rule: PayrollRuleDto | null }>()

const emit = defineEmits<{ close: []; saved: [] }>()

const MAX_AMOUNT = 1_000_000_000

/* ------------------------------------------------------------- holat */

const name = ref('')
const kind = ref<PayrollRuleKindName>('PerSession')
const role = ref<UserRoleName>('Teacher')
const userId = ref<number | null>(null)

const courseId = ref<number | null>(null)
const groupId = ref<number | null>(null)
const categoryId = ref<number | null>(null)
const groupType = ref<GroupTypeName | null>(null)

const amountText = ref('')
const academicHourText = ref('45')
const basis = ref<PayrollBasisName>('Attended')

const minStudentsText = ref('')
const maxStudentsText = ref('')
const minDurationText = ref('')

const planAmountText = ref('')
const planPercentText = ref('')

const weekendMultiplierText = ref('')

const activeFrom = ref(todayIsoDate())
const activeTo = ref('')
const isActive = ref(true)

const tiers = ref<Array<{ studentCount: string; amount: string }>>([])

const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.rule !== null)

/* ------------------------------------------------- turga qarab ko'rinish */

const sessionScoped = computed(() => isSessionScopedKind(kind.value))
const percentKind = computed(() => isPercentKind(kind.value))
const showTargeting = computed(() => supportsGroupTargeting(kind.value))
const showBasis = computed(() => usesStudentCount(kind.value))
const showTiers = computed(() => kind.value === 'TieredByAttendance')
const showAcademicHour = computed(() => kind.value === 'PerAcademicHour')
const showPlan = computed(() => percentKind.value)

const amountLabel = computed(() =>
  percentKind.value ? 'Foiz (%)' : showTiers.value ? 'Asosiy summa (bosqichlar ustun)' : 'Summa (so‘m)',
)

function resetForm(): void {
  const rule = props.rule

  name.value = rule?.name ?? ''
  kind.value = rule?.kind ?? 'PerSession'
  role.value = rule?.role ?? 'Teacher'
  userId.value = rule?.userId ?? null

  courseId.value = rule?.courseId ?? null
  groupId.value = rule?.groupId ?? null
  categoryId.value = rule?.categoryId ?? null
  groupType.value = rule?.groupType ?? null

  amountText.value = rule === null ? '' : String(rule.amount)
  academicHourText.value = String(rule?.academicHourMinutes ?? 45)
  basis.value = rule?.basis ?? 'Attended'

  minStudentsText.value = rule?.minStudents == null ? '' : String(rule.minStudents)
  maxStudentsText.value = rule?.maxStudents == null ? '' : String(rule.maxStudents)
  minDurationText.value = rule?.minDurationMinutes == null ? '' : String(rule.minDurationMinutes)

  planAmountText.value = rule?.planAmount == null ? '' : String(rule.planAmount)
  planPercentText.value = rule?.planReachedPercent == null ? '' : String(rule.planReachedPercent)

  weekendMultiplierText.value =
    rule?.weekendHolidayMultiplier == null ? '' : String(rule.weekendHolidayMultiplier)

  activeFrom.value = rule?.activeFrom ?? todayIsoDate()
  activeTo.value = rule?.activeTo ?? ''
  isActive.value = rule?.isActive ?? true

  tiers.value =
    rule === null
      ? [{ studentCount: '0', amount: '' }]
      : rule.tiers.map((tier) => ({
          studentCount: String(tier.studentCount),
          amount: String(tier.amount),
        }))

  errorMessage.value = null
}

watch(() => [props.open, props.rule], resetForm, { immediate: true })

/* Bosqichli turga o'tilganda kamida bitta bo'sh qator turishi kerak —
   aks holda admin "qayerga yozaman?" degan bo'sh jadvalni ko'rardi. */
watch(kind, (next) => {
  if (next === 'TieredByAttendance' && tiers.value.length === 0) {
    tiers.value = [{ studentCount: '0', amount: '' }]
  }
})

/* Rol o'zgarsa, boshqa rolga tegishli xodim tanlovi endi ma'nosiz. */
watch(role, () => {
  userId.value = null
})

/* ------------------------------------------------------------- ma'lumot */

/*
  Xodim ro'yxati TANLANGAN ROLGA qarab so'raladi: ustozlar ro'yxatida
  kurator ko'rinsa, saqlashda server "rol mos emas" deb 400 qaytarardi va
  bu xodimni tanlab bo'lgach sodir bo'lardi — kechroq, tushunarsiz joyda.
*/
const usersQuery = useQuery({
  queryKey: ['users', 'by-role', role],
  queryFn: ({ signal }) => fetchUsers({ role: role.value, isActive: true, pageSize: 200 }, { signal }),
  enabled: computed(() => props.open),
})

const users = computed(() => usersQuery.data.value?.items ?? [])

const coursesQuery = useQuery({
  queryKey: ['courses', 'payroll-picker'],
  queryFn: ({ signal }) => fetchCourses({ isActive: true, pageSize: 200 }, { signal }),
  enabled: computed(() => props.open && showTargeting.value),
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])

const categoriesQuery = useQuery({
  queryKey: ['group-categories', 'payroll-picker'],
  queryFn: ({ signal }) => fetchGroupCategories({ isActive: true }, { signal }),
  enabled: computed(() => props.open && showTargeting.value),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const groupsQuery = useQuery({
  queryKey: ['groups', 'payroll-picker'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 200 }, { signal }),
  enabled: computed(() => props.open && showTargeting.value),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

/* Tahrirlanayotgan qoida arxivlangan xodimga bog'langan bo'lishi mumkin. */
const missingUserOption = computed(() => {
  const rule = props.rule
  if (rule?.userId == null) return null
  if (users.value.some((item) => item.id === rule.userId)) return null
  return { id: rule.userId, name: `${rule.userName ?? 'Xodim'} (ro‘yxatda yo‘q)` }
})

/* ------------------------------------------------------------ tekshirish */

const amount = computed(() => parseMoneyInput(amountText.value))

const amountError = computed(() => {
  if (amountText.value.trim().length === 0) return null
  const value = amount.value
  if (value === null) return 'Qiymatni raqam bilan kiriting.'
  if (percentKind.value && value > 100) return 'Foiz 0..100 oralig‘ida bo‘lishi kerak.'
  if (!percentKind.value && value > MAX_AMOUNT) return 'Summa juda katta.'
  return null
})

const academicHour = computed(() => parseMoneyInput(academicHourText.value))

const academicHourError = computed(() => {
  if (!showAcademicHour.value) return null
  const value = academicHour.value
  if (value === null) return 'Akademik soatni daqiqada kiriting (masalan 45).'
  if (value < 10 || value > 240) return 'Akademik soat 10..240 daqiqa oralig‘ida bo‘lishi kerak.'
  return null
})

function parseOptionalInt(text: string): number | null | 'invalid' {
  if (text.trim().length === 0) return null
  const value = parseMoneyInput(text)
  if (value === null || value < 0) return 'invalid'
  return value
}

const minStudents = computed(() => parseOptionalInt(minStudentsText.value))
const maxStudents = computed(() => parseOptionalInt(maxStudentsText.value))
const minDuration = computed(() => parseOptionalInt(minDurationText.value))

const studentRangeError = computed(() => {
  if (minStudents.value === 'invalid' || maxStudents.value === 'invalid') {
    return 'O‘quvchi sonini butun raqam bilan kiriting.'
  }
  const lo = minStudents.value
  const hi = maxStudents.value
  if (typeof lo === 'number' && typeof hi === 'number' && lo > hi) {
    return 'Eng kam son eng ko‘pidan katta bo‘lmaydi.'
  }
  return null
})

const minDurationError = computed(() =>
  minDuration.value === 'invalid' ? 'Davomiylikni daqiqada kiriting.' : null,
)

const planAmount = computed(() =>
  planAmountText.value.trim().length === 0 ? null : parseMoneyInput(planAmountText.value),
)
const planPercent = computed(() =>
  planPercentText.value.trim().length === 0 ? null : parseMoneyInput(planPercentText.value),
)

/**
 * ★ REJA IKKI MAYDONDAN IBORAT va ular BIRGA to'ldiriladi: faqat summasi
 * kiritilgan reja "yetgach nechchi foiz?" savolini javobsiz qoldirardi.
 * Server ham shu qoidani qo'llaydi — bu yerda faqat erta aytiladi.
 */
const planError = computed(() => {
  if (!showPlan.value) return null
  const hasAmount = planAmountText.value.trim().length > 0
  const hasPercent = planPercentText.value.trim().length > 0
  if (hasAmount !== hasPercent) return 'Reja summasi va reja foizi birga kiritiladi.'
  if (hasAmount && planAmount.value === null) return 'Reja summasini raqam bilan kiriting.'
  if (hasPercent && (planPercent.value === null || planPercent.value > 100)) {
    return 'Reja foizi 0..100 oralig‘ida bo‘lishi kerak.'
  }
  return null
})

const weekendMultiplier = computed(() =>
  weekendMultiplierText.value.trim().length === 0 ? null : parseMoneyInput(weekendMultiplierText.value),
)

const weekendMultiplierError = computed(() => {
  if (weekendMultiplierText.value.trim().length === 0) return null
  const value = weekendMultiplier.value
  if (value === null) return 'Ko‘paytiruvchini raqam bilan kiriting (masalan 1.5).'
  if (value < 1 || value > 10) return 'Ko‘paytiruvchi 1..10 oralig‘ida bo‘lishi kerak.'
  return null
})

const periodError = computed(() => {
  if (activeTo.value.length === 0) return null
  return activeTo.value < activeFrom.value ? 'Tugash sanasi boshlanishdan oldin bo‘lmaydi.' : null
})

const parsedTiers = computed<PayrollRuleTierInput[]>(() =>
  tiers.value
    .filter((tier) => tier.studentCount.trim().length > 0 || tier.amount.trim().length > 0)
    .map((tier) => ({
      studentCount: parseMoneyInput(tier.studentCount) ?? -1,
      amount: parseMoneyInput(tier.amount) ?? -1,
    })),
)

const tiersError = computed(() => {
  if (!showTiers.value) return null
  const rows = parsedTiers.value
  if (rows.length === 0) return 'Kamida bitta bosqich kiriting.'
  if (rows.some((row) => row.studentCount < 0 || row.amount < 0)) {
    return 'Har bosqichda o‘quvchi soni va summa raqam bo‘lishi kerak.'
  }
  const counts = new Set(rows.map((row) => row.studentCount))
  if (counts.size !== rows.length) return 'Bir xil o‘quvchi soni ikki marta kiritilgan.'
  return null
})

const canSubmit = computed(
  () =>
    name.value.trim().length > 0 &&
    amount.value !== null &&
    amountError.value === null &&
    academicHourError.value === null &&
    studentRangeError.value === null &&
    minDurationError.value === null &&
    planError.value === null &&
    weekendMultiplierError.value === null &&
    periodError.value === null &&
    tiersError.value === null &&
    activeFrom.value.length > 0,
)

/* ------------------------------------------------------------- saqlash */

function addTier(): void {
  const last = tiers.value[tiers.value.length - 1]
  const nextCount = last === undefined ? 0 : (parseMoneyInput(last.studentCount) ?? 0) + 1
  tiers.value.push({ studentCount: String(nextCount), amount: '' })
}

function removeTier(index: number): void {
  tiers.value.splice(index, 1)
}

const mutation = useMutation({
  mutationFn: () => {
    const value = amount.value
    if (value === null) throw new Error('Qiymat kiritilmagan.')

    const targeting = showTargeting.value

    const payload = {
      name: name.value.trim(),
      kind: kind.value,
      role: role.value,
      amount: value,
      activeFrom: activeFrom.value,
      userId: userId.value,

      // Ko'rinmaydigan maydon YUBORILMAYDI — izoh sinf boshida.
      courseId: targeting ? courseId.value : null,
      groupId: targeting ? groupId.value : null,
      categoryId: targeting ? categoryId.value : null,
      groupType: targeting ? groupType.value : null,

      academicHourMinutes: showAcademicHour.value ? (academicHour.value ?? 45) : 45,
      basis: showBasis.value ? basis.value : 'Attended',

      minStudents: sessionScoped.value && typeof minStudents.value === 'number' ? minStudents.value : null,
      maxStudents: sessionScoped.value && typeof maxStudents.value === 'number' ? maxStudents.value : null,
      minDurationMinutes:
        sessionScoped.value && typeof minDuration.value === 'number' ? minDuration.value : null,

      planAmount: showPlan.value ? planAmount.value : null,
      planReachedPercent: showPlan.value ? planPercent.value : null,

      weekendHolidayMultiplier: sessionScoped.value ? weekendMultiplier.value : null,

      activeTo: activeTo.value.length === 0 ? null : activeTo.value,
      isActive: isActive.value,
      tiers: showTiers.value ? parsedTiers.value : [],
    }

    const rule = props.rule
    return rule === null ? createPayrollRule(payload) : updatePayrollRule(rule.id, payload)
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

/**
 * R4 — TASDIQ FAQAT TAHRIRLASHDA.
 *
 * ★ ESLATMA MATNI ATAYLAB QAT'IY: qoidani JOYIDA tahrirlash "shu sanadan
 * boshlab" degan ma'noni bermaydi — u DARHOL kuchga kiradi. Stavka
 * o'zgarganda to'g'ri yo'l — eskisiga tugash sanasini qo'yib, YANGI qoida
 * qo'shish (o'tgan oy hisoboti baribir tegilmaydi, u snapshot'da).
 */
async function submit(): Promise<void> {
  if (!canSubmit.value || mutation.isPending.value) return

  const rule = props.rule
  if (rule !== null) {
    const details: string[] = []
    const value = amount.value

    if (value !== null && value !== rule.amount) {
      details.push(
        percentKind.value
          ? `Foiz: ${rule.amount}% → ${value}%`
          : `Summa: ${formatSum(rule.amount)} → ${formatSum(value)}`,
      )
    }

    if (rule.pinnedGroupCount > 0) {
      details.push(`Bu qoida ${rule.pinnedGroupCount} ta guruhga qo‘lda tayinlangan.`)
    }

    details.push('O‘tgan oylar o‘zgarmaydi — ular hisoblanganda natijani nusxa qilib olgan.')

    const ok = await confirm({
      title: 'Qoidani tahrirlash',
      message:
        'Barcha maydon formadagi qiymatlar bilan ALMASHTIRILADI va o‘zgarish DARHOL kuchga kiradi. ' +
        'Stavka o‘zgargan bo‘lsa, eskisiga tugash sanasini qo‘yib yangi qoida qo‘shish tavsiya etiladi.',
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
  <BaseDrawer
    :open="props.open"
    :title="isEdit ? 'Qoidani tahrirlash' : 'Yangi oylik qoidasi'"
    :subtitle="payrollRuleKindHint(kind)"
    persistent
    @close="emit('close')"
  >
    <form
      novalidate
      class="space-y-4"
      @submit.prevent="submit"
    >
      <!-- ============================================ asosiy -->
      <section class="space-y-3">
        <BaseField
          label="Qoida nomi"
          hint="Hisobotda shu nom ko‘rinadi — “Ustoz: arab tili, soatbay” kabi."
        >
          <input
            v-model="name"
            class="zn-input"
            type="text"
            maxlength="120"
            autocomplete="off"
            placeholder="Ustoz: arab tili, soatbay"
          >
        </BaseField>

        <div class="grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Hisoblash turi"
            :hint="payrollRuleKindHint(kind)"
          >
            <select
              v-model="kind"
              class="zn-input"
            >
              <option
                v-for="option in PAYROLL_RULE_KIND_OPTIONS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </option>
            </select>
          </BaseField>

          <BaseField
            :label="amountLabel"
            :error="amountError"
            :hint="percentKind || amount === null ? '' : formatSum(amount)"
          >
            <input
              v-model="amountText"
              class="zn-input tabular-nums"
              type="text"
              :inputmode="percentKind ? 'decimal' : 'numeric'"
              autocomplete="off"
              :placeholder="percentKind ? '10' : '40000'"
            >
          </BaseField>
        </div>

        <div
          v-if="showAcademicHour"
          class="grid gap-3 sm:grid-cols-2"
        >
          <BaseField
            label="Akademik soat (daqiqa)"
            :error="academicHourError"
            hint="80 daqiqalik dars 45 daqiqalik akademik soatda 1.78 soat beradi."
          >
            <input
              v-model="academicHourText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="45"
            >
          </BaseField>
        </div>
      </section>

      <!-- ============================================ bosqichlar -->
      <section
        v-if="showTiers"
        class="rounded-lg border border-line bg-ink-950 p-3"
      >
        <div class="mb-2 flex items-center justify-between gap-2">
          <p class="text-xs text-slate-400">
            O‘quvchi soniga qarab summa. Eng yuqori bosqichdan ko‘p kelsa — eng yuqori summa
            beriladi.
          </p>
          <BaseButton
            size="sm"
            variant="secondary"
            @click="addTier"
          >
            <template #icon>
              <AppIcon
                name="plus"
                :size="13"
              />
            </template>
            Bosqich
          </BaseButton>
        </div>

        <div class="space-y-2">
          <div
            v-for="(tier, index) in tiers"
            :key="index"
            class="flex items-end gap-2"
          >
            <BaseField
              label="O‘quvchi soni"
              class="w-32 shrink-0"
            >
              <input
                v-model="tier.studentCount"
                class="zn-input tabular-nums"
                type="text"
                inputmode="numeric"
                autocomplete="off"
                placeholder="0"
              >
            </BaseField>
            <BaseField
              label="Summa (so‘m)"
              class="flex-1"
            >
              <input
                v-model="tier.amount"
                class="zn-input tabular-nums"
                type="text"
                inputmode="numeric"
                autocomplete="off"
                placeholder="35000"
              >
            </BaseField>
            <button
              type="button"
              class="tap-target mb-1 flex items-center justify-center rounded-lg text-slate-400 transition-colors hover:bg-ink-800 hover:text-rose-400"
              title="Bosqichni olib tashlash"
              @click="removeTier(index)"
            >
              <AppIcon
                name="trash"
                :size="15"
              />
            </button>
          </div>
        </div>

        <p
          v-if="tiersError !== null"
          class="mt-2 text-xs text-rose-400"
          role="alert"
          v-text="tiersError"
        />
      </section>

      <!-- ============================================ reja -->
      <section
        v-if="showPlan"
        class="rounded-lg border border-line bg-ink-950 p-3"
      >
        <p class="mb-2.5 text-xs text-slate-400">
          Reja qo‘yilsa: rejagacha yuqoridagi foiz, reja bajarilgach pastdagi foiz ishlaydi. Reja
          xodimning BUTUN oylik tushumiga qaraladi. Bo‘sh qoldirilsa — doim bir xil foiz.
        </p>
        <div class="grid gap-3 sm:grid-cols-2">
          <BaseField
            label="Oylik reja (so‘m)"
            :error="planError"
          >
            <input
              v-model="planAmountText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="10000000"
            >
          </BaseField>
          <BaseField label="Reja bajarilgandagi foiz (%)">
            <input
              v-model="planPercentText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="decimal"
              autocomplete="off"
              placeholder="10"
            >
          </BaseField>
        </div>
      </section>

      <!-- ============================================ maqsad -->
      <section class="rounded-lg border border-line bg-ink-950 p-3">
        <p class="mb-2.5 text-xs text-slate-400">
          Qoida KIMGA tegishli. Bo‘sh qoldirilgan maydon — cheklov yo‘q.
          <span class="text-slate-500">
            Bir xil turdagi qoidalardan eng ANIQI ishlaydi: guruh &gt; xodim &gt; kurs &gt;
            kategoriya/tur.
          </span>
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
            label="Xodim (ixtiyoriy)"
            hint="Tanlanmasa — shu rolning barchasi."
          >
            <select
              v-model="userId"
              class="zn-input"
            >
              <option :value="null">
                — {{ payrollRoleLabel(role) }}larning barchasi —
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

        <div
          v-if="showTargeting"
          class="mt-3 grid gap-3 sm:grid-cols-2"
        >
          <BaseField label="Kurs (ixtiyoriy)">
            <select
              v-model="courseId"
              class="zn-input"
            >
              <option :value="null">
                — barcha kurslar —
              </option>
              <option
                v-for="item in courses"
                :key="item.id"
                :value="item.id"
              >
                {{ item.name }}
              </option>
            </select>
          </BaseField>

          <BaseField label="Guruh kategoriyasi (ixtiyoriy)">
            <select
              v-model="categoryId"
              class="zn-input"
            >
              <option :value="null">
                — barcha kategoriyalar —
              </option>
              <option
                v-for="item in categories"
                :key="item.id"
                :value="item.id"
              >
                {{ item.name }}
              </option>
            </select>
          </BaseField>

          <BaseField label="Guruh turi (ixtiyoriy)">
            <select
              v-model="groupType"
              class="zn-input"
            >
              <option :value="null">
                — barcha turlar —
              </option>
              <option value="Group">
                Guruh darsi
              </option>
              <option value="Individual">
                Yakka dars
              </option>
              <option value="Curator">
                Kurator guruhi
              </option>
            </select>
          </BaseField>

          <BaseField
            label="Aniq guruh (ixtiyoriy)"
            hint="Eng tor maqsad — boshqa hamma qoidadan ustun turadi."
          >
            <select
              v-model="groupId"
              class="zn-input"
            >
              <option :value="null">
                — barcha guruhlar —
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
        </div>

        <p
          v-else
          class="mt-2 text-[11px] text-slate-500"
        >
          Bu tur butun OYGA tegishli — uni guruh, kurs yoki kategoriyaga bog‘lab bo‘lmaydi.
        </p>
      </section>

      <!-- ============================================ shartlar -->
      <section
        v-if="sessionScoped"
        class="rounded-lg border border-line bg-ink-950 p-3"
      >
        <p class="mb-2.5 text-xs text-slate-400">
          Qo‘shimcha shartlar — bo‘sh qoldirilsa chegara yo‘q.
        </p>

        <div class="grid gap-3 sm:grid-cols-2">
          <BaseField
            v-if="showBasis"
            label="Kimlar sanaladi"
            class="sm:col-span-2"
          >
            <select
              v-model="basis"
              class="zn-input"
            >
              <option
                v-for="option in PAYROLL_BASIS_OPTIONS"
                :key="option.value"
                :value="option.value"
              >
                {{ option.label }}
              </option>
            </select>
          </BaseField>

          <BaseField
            label="Eng kam o‘quvchi"
            :error="studentRangeError"
          >
            <input
              v-model="minStudentsText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="—"
            >
          </BaseField>

          <BaseField label="Eng ko‘p o‘quvchi">
            <input
              v-model="maxStudentsText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="—"
            >
          </BaseField>

          <BaseField
            label="Eng kam davomiylik (daqiqa)"
            :error="minDurationError"
          >
            <input
              v-model="minDurationText"
              class="zn-input tabular-nums"
              type="text"
              inputmode="numeric"
              autocomplete="off"
              placeholder="—"
            >
          </BaseField>

          <BaseField
            label="Dam olish/bayram ko‘paytiruvchisi"
            :error="weekendMultiplierError"
            hint="1.5 = asosiy stavka +50%. Bonusga ta’sir qilmaydi."
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
      </section>

      <!-- ============================================ muddat -->
      <section class="grid gap-3 sm:grid-cols-3">
        <BaseField label="Qachondan kuchga kiradi">
          <input
            v-model="activeFrom"
            class="zn-input"
            type="date"
            required
          >
        </BaseField>

        <BaseField
          label="Qachongacha (ixtiyoriy)"
          :error="periodError"
          hint="Bo‘sh — muddatsiz."
        >
          <input
            v-model="activeTo"
            class="zn-input"
            type="date"
          >
        </BaseField>

        <label class="flex min-h-11 items-center gap-2.5 self-end text-sm text-slate-300">
          <input
            v-model="isActive"
            type="checkbox"
            class="size-4 accent-brand-500"
          >
          Faol qoida
        </label>
      </section>

      <p
        v-if="errorMessage !== null"
        class="text-xs text-rose-400"
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
  </BaseDrawer>
</template>
