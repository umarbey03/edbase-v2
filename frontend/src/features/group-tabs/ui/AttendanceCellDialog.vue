<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  ATTENDANCE_CHOICES,
  ATTENDANCE_REASON_MAX,
  attendanceStatusLabel,
  attendanceStatusTone,
  durationLabel,
  setExcused,
  setFreeLesson,
  updateAttendance,
} from '@/entities/attendance'
import type { AttendanceRowDto, AttendanceStatusName, FreeLessonStatusDto } from '@/entities/attendance'
import { sessionTypeLabel } from '@/entities/session'
import { isManagerRole } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatTime } from '@/shared/lib/datetime'
import { formatMoney } from '@/shared/lib/money'
import type { SessionStatusName } from '@/shared/types'
import { BaseBadge, BaseButton, BaseModal } from '@/shared/ui'

/**
 * Davomat katagi — eski `#att-modal`.
 *
 * Ikki qism eski ilovadagidek yonma-yon turadi:
 *  • PLATFORMA O'LCHOVI (kirgan/chiqqan vaqti, davomiyligi) — o'zgarmaydi;
 *  • USTOZ QARORI (status va sabab) — shu yerdan tuzatiladi.
 * Ular BIR-BIRIGA ZID bo'lishi MUMKIN ("Qatnashmagan" + 60 daqiqa) va bu
 * xato emas: qaror o'lchovdan ustun.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. */
  cell: {
    sessionId: number
    sessionTitle: string
    sessionType: string
    sessionStatus: SessionStatusName
    sessionStart: string
    canEdit: boolean
    studentId: number
    studentName: string
    row: AttendanceRowDto | null
    /** Butun DARSGA tegishli — shu student bilan bog'liq emas (izoh: pastdagi bo'lim). */
    isFreeLesson: boolean
    freeLessonReason: string | null
    payrollExcluded: boolean
  } | null
}>()

const emit = defineEmits<{
  close: []
  saved: [row: AttendanceRowDto]
  /** "Bepul dars" saqlangach: BUTUN varaq (hamma o'quvchi) qayta so'ralishi kerak. */
  freeLessonSaved: []
}>()

const auth = useAuthStore()

const picked = ref<AttendanceStatusName | null>(null)
const reason = ref('')
const errorMessage = ref<string | null>(null)

/* ---------------------------------------------------------- "sababli" (2026-08-16) */

const excusedPicked = ref(false)
const excuseReason = ref('')
const excuseError = ref<string | null>(null)

watch(
  () => props.cell,
  (cell) => {
    excusedPicked.value = cell?.row?.isExcused ?? false
    excuseReason.value = cell?.row?.excuseReason ?? ''
    excuseError.value = null
  },
  { immediate: true },
)

/**
 * ★ FAQAT ACADEMIC/ADMIN — `canEdit` DAN TORROQ (u Teacher/Assistant ga
 * ham `true` beradi, chunki server "ko'ra oladigan tuzata ham oladi"
 * qoidasini davomat HOLATI uchun ishlatadi). "Sababli" esa to'lovga
 * ta'sir qiladi, shuning uchun serverdagi (`AttendanceService.
 * SetExcusedAsync`) bilan AYNI, TORROQ ro'yxat kerak.
 */
const canExcuse = computed(
  () => auth.role !== null && isManagerRole(auth.role) && !isCancelled.value,
)

const excuseMutation = useMutation({
  mutationFn: (payload: { sessionId: number; studentId: number; excused: boolean; reason: string | null }) =>
    setExcused(payload.sessionId, payload.studentId, {
      excused: payload.excused,
      reason: payload.reason,
    }),
  onSuccess: (row) => {
    excuseError.value = null
    emit('saved', row)
  },
  onError: (error: Error) => {
    excuseError.value = toUserMessage(error)
  },
})

function saveExcuse(): void {
  const cell = props.cell
  if (cell === null) return
  const trimmed = excuseReason.value.trim()
  excuseMutation.mutate({
    sessionId: cell.sessionId,
    studentId: cell.studentId,
    excused: excusedPicked.value,
    reason: trimmed.length > 0 ? trimmed : null,
  })
}

/**
 * ★ PUT TUZOG'I: server tanani TO'LIQ ALMASHTIRADI — `reason` yuborilmasa
 * avvalgi sabab o'chadi. Shuning uchun oyna ochilganda MAVJUD qiymatlar
 * maydonlarga yuklanadi va saqlashda HAMMASI qaytariladi.
 */
watch(
  () => props.cell,
  (cell) => {
    picked.value = cell?.row?.status ?? null
    reason.value = cell?.row?.reason ?? ''
    errorMessage.value = null
  },
  { immediate: true },
)

/* ------------------------------------------------------- "bepul dars" (2026-08-16) */

const freePicked = ref(false)
const freeReason = ref('')
const freePayrollExcluded = ref(false)
const freeError = ref<string | null>(null)

watch(
  () => props.cell,
  (cell) => {
    freePicked.value = cell?.isFreeLesson ?? false
    freeReason.value = cell?.freeLessonReason ?? ''
    freePayrollExcluded.value = cell?.payrollExcluded ?? false
    freeError.value = null
  },
  { immediate: true },
)

const freeLessonMutation = useMutation({
  mutationFn: (payload: {
    sessionId: number
    isFree: boolean
    payrollExcluded: boolean
    reason: string | null
  }) =>
    setFreeLesson(payload.sessionId, {
      isFree: payload.isFree,
      payrollExcluded: payload.payrollExcluded,
      reason: payload.reason,
    }),
  onSuccess: (status: FreeLessonStatusDto) => {
    freeError.value = null
    freePicked.value = status.isFreeLesson
    freePayrollExcluded.value = status.payrollExcluded
    emit('freeLessonSaved')
  },
  onError: (error: Error) => {
    freeError.value = toUserMessage(error)
  },
})

function saveFreeLesson(): void {
  const cell = props.cell
  if (cell === null) return
  const trimmed = freeReason.value.trim()
  freeLessonMutation.mutate({
    sessionId: cell.sessionId,
    isFree: freePicked.value,
    payrollExcluded: freePicked.value && freePayrollExcluded.value,
    reason: trimmed.length > 0 ? trimmed : null,
  })
}

/** Bekor qilingan darsni tahrirlab bo'lmaydi — server 409 qaytaradi. */
const isCancelled = computed(() => props.cell?.sessionStatus === 'Cancelled')
const canEdit = computed(() => props.cell?.canEdit === true && !isCancelled.value)

const mutation = useMutation({
  mutationFn: (payload: {
    sessionId: number
    studentId: number
    status: AttendanceStatusName
    reason: string | null
  }) =>
    updateAttendance(payload.sessionId, payload.studentId, {
      status: payload.status,
      reason: payload.reason,
    }),
  onSuccess: (row) => {
    errorMessage.value = null
    emit('saved', row)
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function save(): void {
  const cell = props.cell
  if (cell === null) return
  if (picked.value === null) {
    errorMessage.value = 'Status tanlang'
    return
  }
  const trimmed = reason.value.trim()
  mutation.mutate({
    sessionId: cell.sessionId,
    studentId: cell.studentId,
    status: picked.value,
    // Bo'sh maydon — sababni ATAYLAB o'chirish (`null`).
    reason: trimmed.length > 0 ? trimmed : null,
  })
}

const measured = computed(() => {
  const row = props.cell?.row ?? null
  if (row === null) return []
  const items: { label: string; value: string }[] = []
  if (row.firstJoinAt !== null) {
    items.push({ label: 'Kirgan vaqti', value: formatTime(row.firstJoinAt) })
  }
  if (row.leftAt !== null) items.push({ label: 'Chiqqan vaqti', value: formatTime(row.leftAt) })
  const duration = durationLabel(row.durationSeconds)
  if (duration.length > 0) items.push({ label: 'Davomiyligi', value: duration })
  return items
})
</script>

<template>
  <BaseModal
    :open="props.cell !== null"
    :title="props.cell?.studentName ?? 'Davomat'"
    @close="emit('close')"
  >
    <template v-if="props.cell !== null">
      <p class="text-xs text-slate-400">
        {{ props.cell.sessionTitle || sessionTypeLabel(props.cell.sessionType) }} ·
        {{ formatDateTime(props.cell.sessionStart) }}
      </p>

      <dl class="mt-3.5 divide-y divide-line text-[13px]">
        <div class="flex items-center justify-between gap-3 py-2">
          <dt class="text-slate-400">
            Status
          </dt>
          <dd>
            <BaseBadge :tone="attendanceStatusTone(props.cell.row?.status ?? null)">
              {{ attendanceStatusLabel(props.cell.row?.status ?? null) }}
            </BaseBadge>
          </dd>
        </div>
        <div class="flex items-center justify-between gap-3 py-2">
          <dt class="text-slate-400">
            Belgilash
          </dt>
          <dd class="text-slate-200">
            {{
              props.cell.row === null || props.cell.row.status === null
                ? '—'
                : props.cell.row.isManual
                  ? 'Qo‘lda (xodim)'
                  : 'Avtomatik (platforma)'
            }}
          </dd>
        </div>
        <div
          v-for="item in measured"
          :key="item.label"
          class="flex items-center justify-between gap-3 py-2"
        >
          <dt
            class="text-slate-400"
            v-text="item.label"
          />
          <dd
            class="tabular-nums text-slate-200"
            v-text="item.value"
          />
        </div>
        <div class="flex items-start justify-between gap-3 py-2">
          <dt class="shrink-0 text-slate-400">
            Sabab
          </dt>
          <dd
            class="min-w-0 break-words text-right"
            :class="props.cell.row?.reason ? 'text-amber-400' : 'text-slate-500'"
            v-text="props.cell.row?.reason ?? 'Izoh kiritilmagan'"
          />
        </div>
        <!--
          "Sababli" (2026-08-16) — to'lovga ta'sir qiladi, shuning uchun
          HAR DOIM ko'rsatiladi (Teacher/Assistant ham ko'rishi kerak,
          faqat O'ZGARTIRA olmaydi — pastdagi bo'lim ularga umuman
          chizilmaydi).
        -->
        <div
          v-if="props.cell.row?.isExcused === true"
          class="flex items-start justify-between gap-3 py-2"
        >
          <dt class="shrink-0 text-slate-400">
            Sababli
          </dt>
          <dd class="min-w-0 text-right">
            <BaseBadge tone="success">
              Ha — bu dars uchun to‘lov yechilmaydi
            </BaseBadge>
            <p
              v-if="props.cell.row.excuseReason !== null"
              class="mt-1 break-words text-xs text-slate-400"
              v-text="props.cell.row.excuseReason"
            />
          </dd>
        </div>
        <!--
          "Qancha yechilgan" (2026-08-16) — `lessonAmount` hali `null`
          bo'lishi mumkin (dars yakunlanmagan yoki tarif sozlanmagan),
          shunda qator UMUMAN ko'rsatilmaydi ("0 so'm" bilan
          adashtirmaslik uchun).
        -->
        <div
          v-if="props.cell.row?.lessonAmount != null"
          class="flex items-center justify-between gap-3 py-2"
        >
          <dt class="text-slate-400">
            Shu darsdan yechilgan
          </dt>
          <dd class="text-right tabular-nums">
            <span
              class="font-semibold"
              :class="
                (props.cell.row.lessonChargedAmount ?? 0) > 0 ? 'text-slate-100' : 'text-green-400'
              "
            >{{ formatMoney(props.cell.row.lessonChargedAmount ?? 0) }}</span>
            <span
              v-if="(props.cell.row.lessonChargedAmount ?? 0) !== props.cell.row.lessonAmount"
              class="ml-1 text-[11px] text-dim"
            >/ {{ formatMoney(props.cell.row.lessonAmount) }}</span>
          </dd>
        </div>
        <div
          v-if="props.cell.row?.editedByName != null"
          class="flex items-center justify-between gap-3 py-2"
        >
          <dt class="text-slate-400">
            Tuzatgan
          </dt>
          <dd class="text-slate-200">
            {{ props.cell.row.editedByName }}
            <span
              v-if="props.cell.row.editedAt !== null"
              class="text-dim"
            >· {{ formatDateTime(props.cell.row.editedAt) }}</span>
          </dd>
        </div>
      </dl>

      <template v-if="canEdit">
        <p class="mt-4 text-[13px] font-semibold text-slate-200">
          Statusni tuzatish (qo‘lda)
        </p>
        <div class="mt-2 flex flex-wrap gap-1.5">
          <button
            v-for="choice in ATTENDANCE_CHOICES"
            :key="choice.value"
            type="button"
            class="min-h-11 flex-1 rounded-lg border px-3 text-xs font-semibold transition-colors"
            :class="
              picked === choice.value
                ? 'border-brand-500 bg-brand-500 text-on-brand'
                : 'border-line bg-ink-950 text-slate-300 hover:border-line-strong'
            "
            :aria-pressed="picked === choice.value"
            @click="picked = choice.value"
            v-text="choice.label"
          />
        </div>

        <label
          class="mt-3.5 block text-xs text-slate-400"
          for="att-reason"
        >
          Sabab (ixtiyoriy, {{ ATTENDANCE_REASON_MAX }} belgigacha)
        </label>
        <input
          id="att-reason"
          v-model="reason"
          class="zn-input mt-1.5"
          type="text"
          :maxlength="ATTENDANCE_REASON_MAX"
          placeholder="Masalan: interneti uzildi"
        >
        <p class="mt-1 text-[11px] text-dim">
          Maydon bo‘sh qoldirilsa avvalgi sabab o‘chadi.
        </p>
      </template>

      <p
        v-else
        class="mt-4 rounded-lg border border-line bg-ink-950 p-3 text-xs text-slate-400"
      >
        {{
          isCancelled
            ? 'Dars bekor qilingan — davomatni tuzatib bo‘lmaydi.'
            : 'Bu darsning davomatini tuzatishga ruxsatingiz yo‘q.'
        }}
      </p>

      <p
        v-if="errorMessage !== null"
        class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-2.5 text-xs text-rose-200"
        role="alert"
        v-text="errorMessage"
      />

      <!--
        "Sababli" belgisi — ALOHIDA bo'lim, ALOHIDA saqlash tugmasi bilan
        (yuqoridagi status bilan BIR mutatsiyaga qo'shilmagan): ikkalasi
        MUSTAQIL server yo'llari (`/attendance/{id}` vs
        `/attendance/{id}/excuse`), turli ruxsat bilan — birlashtirilsa
        Teacher/Assistant "Saqlash" tugmasini bosganda sababli bayrog'i
        ham jimgina o'zgarib qolar edi (ular buni ko'rmaydi ham). Shu
        sabab bu bo'lim status tahrirlash blokidan (yuqorida, `canEdit`
        bo'yicha v-if/v-else) TASHQARIDA, mustaqil `canExcuse` bilan.
      -->
      <template v-if="canExcuse">
        <div class="mt-4 rounded-lg border border-line bg-ink-950 p-3">
          <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-200">
            <input
              v-model="excusedPicked"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Sababli deb belgilash
          </label>
          <p class="mt-1 text-[11px] text-dim">
            Belgilansa, bu o‘quvchidan shu dars uchun pul yechib olinmaydi.
          </p>

          <template v-if="excusedPicked">
            <label
              class="mt-2.5 block text-xs text-slate-400"
              for="excuse-reason"
            >
              Izoh (ixtiyoriy)
            </label>
            <input
              id="excuse-reason"
              v-model="excuseReason"
              class="zn-input mt-1.5"
              type="text"
              :maxlength="ATTENDANCE_REASON_MAX"
              placeholder="Masalan: kasal, ma’lumotnoma bor"
            >
          </template>

          <p
            v-if="excuseError !== null"
            class="mt-2.5 text-xs text-rose-400"
            role="alert"
            v-text="excuseError"
          />

          <div class="mt-2.5 flex justify-end">
            <BaseButton
              size="sm"
              variant="secondary"
              :loading="excuseMutation.isPending.value"
              @click="saveExcuse"
            >
              Sababli holatini saqlash
            </BaseButton>
          </div>
        </div>

        <!--
          "Bepul dars" (2026-08-16) — "Sababli" dan FARQI: bu BUTUN darsga
          tegishli, faqat shu o'quvchiga emas (izoh: reja hujjati / real
          loyiha tahlili). Shu sabab izoh bilan ANIQ ogohlantiriladi —
          aks holda xodim "bir o'quvchini bepul qildim" deb o'ylab qolardi.
        -->
        <div class="mt-3 rounded-lg border border-line bg-ink-950 p-3">
          <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-200">
            <input
              v-model="freePicked"
              type="checkbox"
              class="size-4 accent-brand-500"
            >
            Butun darsni bepul deb belgilash
          </label>
          <p class="mt-1 text-[11px] text-dim">
            Belgilansa, BARCHA o‘quvchidan (faqat shu birontasidan emas) shu
            dars uchun pul yechilmaydi. Dars allaqachon yakunlangan bo‘lsa
            ham ishlaydi — avval yechilgan summa qaytariladi.
          </p>

          <template v-if="freePicked">
            <label class="mt-2.5 flex min-h-11 items-center gap-2.5 text-sm text-slate-200">
              <input
                v-model="freePayrollExcluded"
                type="checkbox"
                class="size-4 accent-brand-500"
              >
              Ustoz/kurator ham haq olmasin
            </label>

            <label
              class="mt-1 block text-xs text-slate-400"
              for="free-reason"
            >
              Sabab (ixtiyoriy)
            </label>
            <input
              id="free-reason"
              v-model="freeReason"
              class="zn-input mt-1.5"
              type="text"
              :maxlength="ATTENDANCE_REASON_MAX"
              placeholder="Masalan: sinov darsi"
            >
          </template>

          <p
            v-if="freeError !== null"
            class="mt-2.5 text-xs text-rose-400"
            role="alert"
            v-text="freeError"
          />

          <div class="mt-2.5 flex justify-end">
            <BaseButton
              size="sm"
              variant="secondary"
              :loading="freeLessonMutation.isPending.value"
              @click="saveFreeLesson"
            >
              Bepul dars holatini saqlash
            </BaseButton>
          </div>
        </div>
      </template>
    </template>

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
      <BaseButton
        v-if="canEdit"
        :loading="mutation.isPending.value"
        @click="save"
      >
        Saqlash
      </BaseButton>
    </template>
  </BaseModal>
</template>
