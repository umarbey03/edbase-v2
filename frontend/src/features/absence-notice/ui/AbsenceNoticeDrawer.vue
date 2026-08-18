<script setup lang="ts">
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { ref, watch } from 'vue'

import { deliveryLabel, deliveryTone, markNoticeCalled } from '@/entities/absentee'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import { formatPhone } from '@/shared/lib/phone'
import { showToast } from '@/shared/lib/useToast'
import type { AbsenceNoticeRowDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseDrawer, BaseField } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  YUBORILGAN XABAR — TO'LIQ MA'LUMOT (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasi: jadvalda qisqa ma'lumot, *"see button bo'lishi kerak,
 * bosilganda to'liq ma'lumot ochilishi kerak — yuborilgan xabar va
 * o'quvchi yuborgan javob, ya'ni sabab"*.
 *
 * ★ NEGA JADVALDA EMAS: xabar matni bir necha qatorli va sabab ham
 * uzun bo'lishi mumkin. Ularni ustunga siqish jadvalni o'qib
 * bo'lmaydigan qilardi; kesib qo'yish esa aynan kerakli ma'lumotni
 * yashirardi.
 *
 * ★ "QO'NG'IROQ QILINDI" TUGMASI AYNAN SHU YERDA: kurator to'liq
 * ma'lumotni ko'rib, qo'ng'iroq qiladi va darhol izini yozib qo'yadi.
 * Jadvalda bo'lsa, tasodifan bosilishi oson bo'lardi.
 *
 * ★ PROFIL BU YERDAN OCHILMAYDI (jadvaldan ochiladi): ikkita `BaseDrawer`
 * ichma-ich ochilishi loyihada TAQIQLANGAN (`useModalHost` dev'da
 * ogohlantiradi).
 */
const props = defineProps<{ open: boolean; notice: AbsenceNoticeRowDto | null }>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()

const note = ref('')
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  (open) => {
    if (!open) return

    note.value = props.notice?.callNote ?? ''
    errorMessage.value = null
  },
)

const calledMutation = useMutation({
  mutationFn: () => markNoticeCalled(props.notice!.id, { note: note.value.trim() }),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['absence-notices'] })
    showToast('Qo‘ng‘iroq qayd etildi')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})
</script>

<template>
  <BaseDrawer
    :open="props.open"
    title="Yuborilgan xabar"
    :subtitle="props.notice?.studentName ?? ''"
    @close="emit('close')"
  >
    <div
      v-if="props.notice !== null"
      class="space-y-4"
    >
      <!-- ═══════════ KIM / QAYSI GURUH ═══════════ -->
      <div class="grid gap-3 sm:grid-cols-2">
        <div class="rounded-xl border border-line bg-ink-900 p-3.5">
          <p class="text-[11px] font-semibold text-slate-400">
            O‘quvchi
          </p>
          <p
            class="mt-0.5 font-medium text-slate-100"
            v-text="props.notice.studentName"
          />
          <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-dim">
            <a
              v-if="props.notice.studentPhone !== null"
              :href="`tel:${props.notice.studentPhone.replace(/\s/g, '')}`"
              class="text-slate-300 hover:text-slate-100"
            >{{ formatPhone(props.notice.studentPhone) }}</a>
            <a
              v-if="props.notice.studentTelegram !== null"
              :href="`https://t.me/${props.notice.studentTelegram}`"
              target="_blank"
              rel="noopener"
              class="text-sky-400 hover:text-sky-300"
            >@{{ props.notice.studentTelegram }}</a>
          </p>
        </div>

        <div class="rounded-xl border border-line bg-ink-900 p-3.5">
          <p class="text-[11px] font-semibold text-slate-400">
            Guruh
          </p>
          <p
            class="mt-0.5 font-medium text-slate-100"
            v-text="props.notice.groupName"
          />
          <p class="mt-1 space-x-3 text-xs text-dim">
            <span
              v-if="props.notice.teacherName !== null"
              v-text="`Ustoz: ${props.notice.teacherName}`"
            />
            <span
              v-if="props.notice.assistantName !== null"
              v-text="`Kurator: ${props.notice.assistantName}`"
            />
          </p>
        </div>
      </div>

      <!-- ═══════════ QOLDIRILGAN DARS ═══════════ -->
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p class="text-[11px] font-semibold text-slate-400">
          Qoldirilgan dars
        </p>
        <p
          class="mt-0.5 tabular-nums text-slate-200"
          v-text="formatDateTimeNumeric(props.notice.sessionStart)"
        />
      </div>

      <!-- ═══════════ YUBORILGAN XABAR ═══════════ -->
      <div>
        <div class="mb-1.5 flex flex-wrap items-center gap-2">
          <p class="text-[11px] font-semibold text-slate-400">
            Yuborilgan xabar
          </p>
          <BaseBadge :tone="deliveryTone(props.notice.deliveryStatus)">
            {{ deliveryLabel(props.notice.deliveryStatus) }}
          </BaseBadge>
          <span
            class="text-[11px] text-dim"
            v-text="`${props.notice.sentByName} · ${formatDateTimeNumeric(props.notice.sentAt)}`"
          />
        </div>
        <p
          class="whitespace-pre-wrap rounded-xl border border-line bg-ink-800 px-3.5 py-3 text-sm text-slate-200"
          v-text="props.notice.body"
        />
        <p
          v-if="props.notice.deliveryError !== null"
          class="mt-1.5 text-xs text-rose-400"
          v-text="props.notice.deliveryError"
        />
      </div>

      <!-- ═══════════ O'QUVCHI JAVOBI ═══════════ -->
      <div>
        <p class="mb-1.5 text-[11px] font-semibold text-slate-400">
          O‘quvchi yozgan sabab
        </p>

        <!--
          ★ JAVOB YO'QLIGI ALOHIDA TA'KIDLANADI: aynan shu holat
          kuratorga "qo'ng'iroq qilish kerak" deb aytadi. Bo'sh joy
          qoldirilsa, u e'tibordan chetda qolardi.
        -->
        <div
          v-if="props.notice.replyText === null"
          class="rounded-xl border border-amber-500/25 bg-amber-500/10 px-3.5 py-3 text-sm text-amber-200"
        >
          Javob kelmagan — sababini aniqlash uchun qo‘ng‘iroq qilish kerak.
        </div>

        <template v-else>
          <p
            class="whitespace-pre-wrap rounded-xl border border-emerald-500/25 bg-emerald-500/10 px-3.5 py-3 text-sm text-emerald-100"
            v-text="props.notice.replyText"
          />
          <p
            v-if="props.notice.repliedAt !== null"
            class="mt-1 text-[11px] text-dim"
            v-text="`Telegramda yozgan · ${formatDateTimeNumeric(props.notice.repliedAt)}`"
          />
        </template>
      </div>

      <!-- ═══════════ QO'NG'IROQ ═══════════ -->
      <div class="rounded-xl border border-line bg-ink-900 p-3.5">
        <p class="mb-2 flex items-center gap-2 text-[11px] font-semibold text-slate-400">
          <AppIcon
            name="phone"
            :size="13"
          />
          Qo‘ng‘iroq
        </p>

        <p
          v-if="props.notice.calledAt !== null"
          class="mb-2 text-xs text-slate-300"
          v-text="`${props.notice.calledByName} · ${formatDateTimeNumeric(props.notice.calledAt)}`"
        />
        <p
          v-else
          class="mb-2 text-xs text-dim"
        >
          Hali qo‘ng‘iroq qilinmagan.
        </p>

        <BaseField
          label="Izoh"
          hint="Qo‘ng‘iroqda aniqlangan sabab — ixtiyoriy."
        >
          <textarea
            v-model="note"
            class="zn-input min-h-16"
            maxlength="500"
            rows="2"
            placeholder="Masalan: onasi javob berdi, kasal ekan"
          />
        </BaseField>

        <BaseButton
          class="mt-2.5"
          size="sm"
          variant="secondary"
          :loading="calledMutation.isPending.value"
          @click="calledMutation.mutate()"
        >
          {{ props.notice.calledAt === null ? 'Qo‘ng‘iroq qilindi' : 'Qayta qayd etish' }}
        </BaseButton>

        <p
          v-if="errorMessage !== null"
          class="mt-2 text-xs text-rose-400"
          role="alert"
          v-text="errorMessage"
        />
      </div>
    </div>
  </BaseDrawer>
</template>
