<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  ATTENDANCE_CHOICES,
  ATTENDANCE_REASON_MAX,
  attendanceStatusLabel,
  attendanceStatusTone,
  durationLabel,
  updateAttendance,
} from '@/entities/attendance'
import type { AttendanceRowDto, AttendanceStatusName } from '@/entities/attendance'
import { sessionTypeLabel } from '@/entities/session'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatTime } from '@/shared/lib/datetime'
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
  } | null
}>()

const emit = defineEmits<{ close: []; saved: [row: AttendanceRowDto] }>()

const picked = ref<AttendanceStatusName | null>(null)
const reason = ref('')
const errorMessage = ref<string | null>(null)

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
