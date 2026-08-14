<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { fetchCourses } from '@/entities/course'
import { fetchGroups } from '@/entities/group'
import { createTariff, todayIsoDate, updateTariff } from '@/entities/payment'
import { toUserMessage } from '@/shared/api'
import { formatSum, parseMoneyInput } from '@/shared/lib/money'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { TariffDto } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Tarif yaratish/tahrirlash.
 *
 * ★ `PUT /payments/tariffs/{id}` — TO'LIQ ALMASHTIRISH: yuborilmagan maydon
 * standart qiymatga tushadi. `courseId` yuborilmasa tarif jimgina "barcha
 * kurslar" ga aylanadi va butun markazning narxi o'zgarardi. Shuning uchun
 * forma mavjud tarifning HAMMA maydonini yuklaydi va HAMMASINI qaytaradi —
 * `isActive` va `lessonsCount` ham (ular ekranda ko'rinmasa ham yuborilishi
 * shart edi, lekin ko'rinadigan qilingan: yashirin maydon jimgina o'zgarsa
 * sababini topib bo'lmasdi).
 *
 * ★ NARX O'ZGARSA — YANGI TARIF. Mavjudini tahrirlash narx tarixini
 * yo'q qiladi: `Payment` yozuvi `baseAmount` ni yaratilganda NUSXA qiladi,
 * ya'ni o'tgan oylar o'zgarmaydi, lekin "qachondan qancha edi" degan savolga
 * javob qolmaydi.
 */
const props = defineProps<{ open: boolean; tariff: TariffDto | null }>()

const emit = defineEmits<{ close: []; saved: [] }>()

/** Server chegaralari: `PaymentService` — nom 150, summa 1e9, darslar 1..60. */
const NAME_MAX = 150
const MAX_AMOUNT = 1_000_000_000

const name = ref('')
const amountText = ref('')
const lessonsCount = ref(8)
const courseId = ref<number | null>(null)
const groupId = ref<number | null>(null)
const activeFrom = ref(todayIsoDate())
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.tariff !== null)

function resetForm(): void {
  const tariff = props.tariff
  name.value = tariff?.name ?? ''
  amountText.value = tariff === null ? '' : String(tariff.amount)
  lessonsCount.value = tariff?.lessonsCount ?? 8
  courseId.value = tariff?.courseId ?? null
  groupId.value = tariff?.groupId ?? null
  activeFrom.value = tariff?.activeFrom ?? todayIsoDate()
  isActive.value = tariff?.isActive ?? true
  errorMessage.value = null
}

watch(() => [props.open, props.tariff], resetForm, { immediate: true })

const optionsEnabled = computed(() => props.open)

const coursesQuery = useQuery({
  queryKey: ['courses', 'active', 'options'],
  queryFn: ({ signal }) => fetchCourses({ isActive: true, pageSize: 100 }, { signal }),
  enabled: optionsEnabled,
})

const groupsQuery = useQuery({
  queryKey: ['groups', 'active', 'options'],
  queryFn: ({ signal }) => fetchGroups({ isActive: true, pageSize: 100 }, { signal }),
  enabled: optionsEnabled,
})

const courses = computed(() => coursesQuery.data.value?.items ?? [])
const groups = computed(() => groupsQuery.data.value?.items ?? [])

/*
  Tahrirlanayotgan tarif arxivlangan kurs/guruhga bog'langan bo'lishi mumkin —
  u ro'yxatda bo'lmaydi. Variantni qo'shmasak, `select` bo'sh qolib, saqlashda
  bog'lanish jimgina uzilardi (guruh tarifi umumiy tarifga aylanardi).
*/
const missingCourseOption = computed(() => {
  const tariff = props.tariff
  if (tariff?.courseId == null) return null
  if (courses.value.some((item) => item.id === tariff.courseId)) return null
  return { id: tariff.courseId, name: `${tariff.courseName ?? 'Kurs'} (ro‘yxatda yo‘q)` }
})

const missingGroupOption = computed(() => {
  const tariff = props.tariff
  if (tariff?.groupId == null) return null
  if (groups.value.some((item) => item.id === tariff.groupId)) return null
  return { id: tariff.groupId, name: `${tariff.groupName ?? 'Guruh'} (ro‘yxatda yo‘q)` }
})

const amount = computed(() => parseMoneyInput(amountText.value))

const amountError = computed(() => {
  if (amountText.value.trim().length === 0) return null
  const value = amount.value
  if (value === null) return 'Narxni raqam bilan kiriting (masalan 540000).'
  // 0 ATAYLAB ruxsat etilgan: tekin o'qish holati (server ham `>= 0` talab qiladi).
  if (value > MAX_AMOUNT) return 'Tarif summasi juda katta.'
  return null
})

const lessonsError = computed(() =>
  lessonsCount.value < 1 || lessonsCount.value > 60
    ? 'Darslar soni 1..60 oralig‘ida bo‘lishi kerak.'
    : null,
)

const canSubmit = computed(
  () =>
    name.value.trim().length > 0 &&
    name.value.trim().length <= NAME_MAX &&
    amount.value !== null &&
    amountError.value === null &&
    lessonsError.value === null &&
    activeFrom.value.length > 0,
)

const mutation = useMutation({
  mutationFn: () => {
    const value = amount.value
    if (value === null) throw new Error('Narx kiritilmagan.')
    /* ★ HAMMA maydon — `PUT` to'liq almashtiradi. */
    const payload = {
      name: name.value.trim(),
      amount: value,
      activeFrom: activeFrom.value,
      lessonsCount: lessonsCount.value,
      isActive: isActive.value,
      courseId: courseId.value,
      groupId: groupId.value,
    }
    const tariff = props.tariff
    return tariff === null ? createTariff(payload) : updateTariff(tariff.id, payload)
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
 * R4 — TASDIQ FAQAT TAHRIRLASHDA, `warning` TONIDA.
 *
 * ★ YARATISHDA SO'RALMAYDI: yangi tarif hech narsani almashtirmaydi va
 * formaning O'ZI "narx o'zgarsa yangi tarif qo'shing" deb yo'naltiradi —
 * ya'ni tavsiya etilgan yo'lni tasdiq oynasi bilan sekinlashtirish
 * xodimni aynan XAVFLI yo'lga (mavjudini tahrirlash) itarardi.
 *
 * 🔴 TAHRIRLASH ESA NARX TARIXINI YO'Q QILADI (fayl boshidagi izoh):
 * `Payment` yozuvi `baseAmount` ni yaratilganda nusxa qiladi, ya'ni
 * o'tgan oylar o'zgarmaydi — lekin "qachondan qancha edi" degan savolga
 * javob beradigan yagona yozuv ustiga yoziladi. Shuning uchun eski va
 * yangi narx `details` da YONMA-YON ko'rsatiladi: bu tahrir kerakmi
 * yoki yangi tarif kerakmi degan savolga aynan shu ikki raqam javob
 * beradi.
 */
async function submit(): Promise<void> {
  if (!canSubmit.value || mutation.isPending.value) return

  const tariff = props.tariff
  if (tariff !== null) {
    const value = amount.value
    const details = [`Qamrov: ${courseId.value === null ? 'barcha kurslar' : 'tanlangan kurs'}`
      + `${groupId.value === null ? '' : ' · tanlangan guruh'}`]

    if (value !== null && value !== tariff.amount) {
      details.unshift(`Narx: ${formatSum(tariff.amount)} → ${formatSum(value)}`)
      details.push('Narx tarixi saqlanmaydi — eski qiymat hech qayerda qolmaydi.')
    }

    const ok = await confirm({
      title: 'Tarifni tahrirlash',
      message:
        `“${tariff.name}” tarifining barcha maydoni formadagi qiymatlar bilan ALMASHTIRILADI. `
        + 'Keyingi oy to‘lovlari shu tarifdan hisoblanadi.',
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
    :title="isEdit ? 'Tarifni tahrirlash' : 'Yangi tarif'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="submit"
    >
      <p class="mb-3.5 text-xs leading-relaxed text-slate-400">
        Narx o‘zgarsa eskisini tahrirlamang —
        <span class="font-semibold text-slate-200">yangi tarif qo‘shing</span>, shunda narx tarixi
        saqlanadi. Tarif oyning
        <span class="font-semibold text-slate-200">birinchi kuniga</span> qarab tanlanadi: oy
        o‘rtasida kuchga kiradigan tarif o‘sha oyga ta’sir qilmaydi.
      </p>

      <BaseField
        label="Nomi"
        :error="
          name.trim().length > NAME_MAX ? `Nom ${NAME_MAX} belgidan oshmasin.` : null
        "
      >
        <input
          v-model="name"
          class="zn-input"
          :maxlength="NAME_MAX"
          placeholder="Standart / Intensiv"
          required
        >
      </BaseField>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Oylik narx (so‘m)"
          :error="amountError"
          :hint="amount === null ? '' : formatSum(amount)"
        >
          <input
            v-model="amountText"
            class="zn-input tabular-nums"
            type="text"
            inputmode="numeric"
            autocomplete="off"
            placeholder="540000"
          >
        </BaseField>
        <BaseField
          label="Oyiga necha dars"
          :error="lessonsError"
        >
          <input
            v-model.number="lessonsCount"
            class="zn-input"
            type="number"
            min="1"
            max="60"
          >
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField
          label="Kurs (ixtiyoriy)"
          hint="Tanlanmasa — barcha kurslar."
        >
          <select
            v-model="courseId"
            class="zn-input"
          >
            <option :value="null">
              — Barcha kurslar —
            </option>
            <option
              v-if="missingCourseOption !== null"
              :value="missingCourseOption.id"
            >
              {{ missingCourseOption.name }}
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
        <BaseField
          label="Guruh (ixtiyoriy)"
          hint="Guruh tarifi kurs va umumiy tarifdan ustun turadi."
        >
          <select
            v-model="groupId"
            class="zn-input"
          >
            <option :value="null">
              — Barcha guruhlar —
            </option>
            <option
              v-if="missingGroupOption !== null"
              :value="missingGroupOption.id"
            >
              {{ missingGroupOption.name }}
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
          Faol tarif
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
