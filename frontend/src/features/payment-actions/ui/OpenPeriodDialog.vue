<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchGroups } from '@/entities/group'
import { currentPeriod, isValidPeriod, openPeriod, periodLabel } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatSum } from '@/shared/lib/money'
import type { OpenPeriodResult } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * "Joriy oy yozuvlarini yaratish" (eski ilovadagi `genPayments()`).
 *
 * ★ AMAL IDEMPOTENT: takror bosilsa yangi qator yaratilmaydi va xato ham
 * bermaydi. Shuning uchun tasdiqlash "xavfli amal" ohangida emas — foydalanuvchi
 * qo'rqmasdan qayta bosishi mumkin. Buning evaziga NATIJANI batafsil
 * ko'rsatish SHART, aks holda "bosdim, hech narsa bo'lmadi" degan taassurot
 * qoladi (eskisida shunday edi: faqat yaratilganlar soni chiqardi).
 *
 * ★ OY DOIM ANIQ YUBORILADI. Server `period: null` da MARKAZ vaqt zonasidagi
 * joriy oyni oladi, brauzer esa boshqa zonada bo'lishi mumkin — oyning
 * birinchi/oxirgi kunida ikkalasi HAR XIL oyni ko'rsatardi va kassir
 * o'zi ko'rgan oydan boshqasini ochib yuborardi.
 */
const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{ close: []; done: [] }>()

const period = ref(currentPeriod())
const groupId = ref<number | null>(null)
const result = ref<OpenPeriodResult | null>(null)
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    period.value = currentPeriod()
    groupId.value = null
    result.value = null
    errorMessage.value = null
  },
  { immediate: true },
)

/* Guruh tanlovi — faqat oyna ochiq bo'lganda yuklanadi. */
const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: computed(() => props.open),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const periodValid = computed(() => isValidPeriod(period.value))

const mutation = useMutation({
  mutationFn: () => openPeriod({ period: period.value, groupId: groupId.value }),
  onSuccess: (data) => {
    result.value = data
    emit('done')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function submit(): void {
  if (!periodValid.value || mutation.isPending.value) return
  errorMessage.value = null
  mutation.mutate()
}

/*
  ★ "Hech narsa o'zgarmadi" holati ALOHIDA aytiladi.

  Server mavjud bo'lmagan guruh Id'si berilsa ham 200 va noldan iborat
  hisobot qaytaradi (guruh Id'si tekshirilmaydi). Bunday javobni "bajarildi"
  deb ko'rsatsak, foydalanuvchi yozuvlar ochildi deb o'ylab qolardi.
*/
const nothingHappened = computed(() => {
  const data = result.value
  if (data === null) return false
  return (
    data.created === 0 &&
    data.alreadyOpen === 0 &&
    data.skippedNoTariff === 0 &&
    data.monthsClosedFromBalance === 0
  )
})

const warnings = computed(() => {
  const list = result.value?.warnings ?? []
  // Server tartibi kafolatlanmagan (izoh: `PaymentService.OpenPeriodAsync`),
  // shuning uchun ekranda barqaror bo'lishi uchun o'zimiz tartiblaymiz.
  return [...list].sort((a, b) => a.localeCompare(b))
})
</script>

<template>
  <BaseModal
    :open="props.open"
    title="Joriy oy yozuvlarini yaratish"
    @close="emit('close')"
  >
    <!-- ---------------------------------------------------------- natija -->
    <div v-if="result !== null">
      <p
        v-if="nothingHappened"
        class="rounded-lg border border-amber-500/30 bg-amber-500/10 p-3.5 text-sm text-amber-200"
      >
        Hech narsa o‘zgarmadi: {{ periodLabel(result.period) }} uchun yangi yozuv ochilmadi.
        Guruhda faol o‘quvchi bormi va tarif sozlanganmi — tekshiring.
      </p>

      <template v-else>
        <p class="text-sm text-slate-300">
          <span
            class="font-semibold text-slate-100"
            v-text="periodLabel(result.period)"
          />
          uchun yozuvlar ochildi.
        </p>

        <dl class="mt-3 grid grid-cols-2 gap-2.5">
          <div class="rounded-lg border border-line bg-ink-800 p-3">
            <dd
              class="text-lg font-bold tabular-nums text-green-400"
              v-text="result.created"
            />
            <dt class="mt-0.5 text-[11px] text-slate-400">
              yangi yozuv yaratildi
            </dt>
          </div>
          <div class="rounded-lg border border-line bg-ink-800 p-3">
            <dd
              class="text-lg font-bold tabular-nums text-slate-200"
              v-text="result.alreadyOpen"
            />
            <dt class="mt-0.5 text-[11px] text-slate-400">
              allaqachon ochiq edi
            </dt>
          </div>
          <div class="rounded-lg border border-line bg-ink-800 p-3">
            <dd
              class="text-lg font-bold tabular-nums text-brand-400"
              v-text="result.monthsClosedFromBalance"
            />
            <dt class="mt-0.5 text-[11px] text-slate-400">
              oy balansdan yopildi
              <span
                v-if="result.balanceApplied > 0"
                class="text-dim"
              >({{ formatSum(result.balanceApplied) }})</span>
            </dt>
          </div>
          <div class="rounded-lg border border-line bg-ink-800 p-3">
            <dd
              class="text-lg font-bold tabular-nums"
              :class="result.skippedNoTariff > 0 ? 'text-amber-400' : 'text-slate-200'"
              v-text="result.skippedNoTariff"
            />
            <dt class="mt-0.5 text-[11px] text-slate-400">
              tarifsiz — ochilmadi
            </dt>
          </div>
        </dl>
      </template>

      <div
        v-if="warnings.length > 0"
        class="mt-3 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3.5"
      >
        <p class="text-xs font-semibold text-amber-200">
          Tarif topilmagan guruhlar
        </p>
        <ul class="mt-1.5 space-y-1">
          <li
            v-for="warning in warnings"
            :key="warning"
            class="text-[11px] leading-relaxed text-amber-100/90"
            v-text="warning"
          />
        </ul>
      </div>
    </div>

    <!-- ----------------------------------------------------------- forma -->
    <form
      v-else
      novalidate
      @submit.prevent="submit"
    >
      <p class="mb-3.5 text-xs leading-relaxed text-slate-400">
        Tanlangan oy uchun har bir faol o‘quvchiga to‘lov yozuvi ochiladi. Amal
        <span class="font-semibold text-slate-200">takrorlansa xavfsiz</span>: mavjud yozuvlar
        qayta yaratilmaydi. Oldindan to‘lagan o‘quvchining balansi avtomatik sarflanadi.
      </p>

      <BaseField
        label="Hisob oyi"
        :error="periodValid ? null : 'Oy YYYY-MM ko‘rinishida bo‘lishi kerak.'"
      >
        <input
          v-model="period"
          class="zn-input"
          type="month"
          required
        >
      </BaseField>

      <div class="mt-3">
        <BaseField
          label="Guruh"
          hint="Tanlanmasa — barcha faol guruhlar."
        >
          <select
            v-model="groupId"
            class="zn-input"
          >
            <option :value="null">
              Barcha faol guruhlar
            </option>
            <option
              v-for="group in groups"
              :key="group.id"
              :value="group.id"
            >
              {{ group.name }}
            </option>
          </select>
        </BaseField>
      </div>

      <p
        v-if="errorMessage !== null"
        class="mt-3 text-xs text-rose-400"
        role="alert"
        v-text="errorMessage"
      />
    </form>

    <template #footer>
      <template v-if="result !== null">
        <BaseButton @click="emit('close')">
          Yopish
        </BaseButton>
      </template>
      <template v-else>
        <BaseButton
          variant="secondary"
          @click="emit('close')"
        >
          Bekor qilish
        </BaseButton>
        <BaseButton
          :disabled="!periodValid"
          :loading="mutation.isPending.value"
          @click="submit"
        >
          Yaratish
        </BaseButton>
      </template>
    </template>
  </BaseModal>
</template>
