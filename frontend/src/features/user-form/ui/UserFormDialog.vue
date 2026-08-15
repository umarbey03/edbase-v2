<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createUser, ROLE_OPTIONS, updateUser } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import {
  formatPhoneInput,
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  phoneDigits,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { UserDetailsDto, UserRoleName } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Foydalanuvchi yaratish/tahrirlash oynasi.
 *
 * ══════════════════════════════════════════════════════════════════════
 * ⚠️ PAROL MAYDONI OLIB TASHLANDI (2026-08-13, loyiha egasining qarori).
 *
 * Ilgari bu yerda ixtiyoriy parol maydoni bor edi va server bo'sh
 * qoldirilganda vaqtinchalik parol qaytarardi (u ekranda ko'rsatilardi).
 * Endi parol bilan kirish YO'Q — ya'ni o'sha satr foydalanuvchiga
 * uzatiladigan "kirish ma'lumoti" emas, hech qayerda ishlamaydigan
 * belgilar to'plami bo'lib qolardi. Xodim uni foydalanuvchiga aytardi,
 * u esa kirish ekranida qaerga yozishni topa olmasdi.
 *
 * 🔴 YANGI FOYDALANUVCHI QANDAY KIRADI: botga `/start` yozib,
 *    «Raqamni ulashish» tugmasini bosadi — shundan keyin saytda telefon
 *    raqamini kiritib, Telegramga keladigan kod bilan kiradi. Shuning
 *    uchun TELEFON endi eng muhim maydon va xodim rollari uchun u
 *    MAJBURIY (server ham talab qiladi).
 * ══════════════════════════════════════════════════════════════════════
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi foydalanuvchi rejimi. */
  user: UserDetailsDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const confirm = useConfirm()

const fullName = ref('')
const email = ref('')
const phone = ref('')
const role = ref<UserRoleName>('Student')
const isActive = ref(true)
const errorMessage = ref<string | null>(null)

const isEdit = computed(() => props.user !== null)

/**
 * 🔴 XODIM ROLLARI UCHUN TELEFON MAJBURIY.
 *
 * ★ NIMA UCHUN MIJOZDA HAM TEKSHIRILADI (server baribir 400 qaytaradi):
 *   serverning javobi maydon YONIDA emas, forma ostida umumiy xato
 *   sifatida chiqadi va xodim qaysi maydon aybdor ekanini darrov
 *   tushunmasdi. Bu yerdagi tekshiruv — QULAYLIK; HAQIQIY qoida esa
 *   serverda va u yagona (`UserService.RequirePhoneForStaff`).
 */
const phoneRequired = computed(() => role.value !== 'Student')

const phoneMissing = computed(
  () => phoneRequired.value && phoneDigits(phone.value).length === 0,
)

/**
 * Serverga boradigan qiymat — maydondagi bo'shliqlarsiz.
 *
 * ★ COMPUTED, ikkita mutatsiyada takror chaqiruv EMAS: `create` va
 * `update` AYNI qiymatni yuborishi shart. Ikki joyda qo'lda yozilsa,
 * biri o'zgarganda ikkinchisi eski holida qolardi.
 *
 * Bo'sh maydon `null` bo'ladi — bo'sh SATR emas: server uchun "raqam
 * yo'q" va "raqam bo'sh satr" bir xil narsa emas (birinchisi ustunni
 * tozalaydi, ikkinchisi noto'g'ri qiymat yozardi).
 */
const phonePayload = computed<string | null>(() => {
  const value = stripPhoneFormatting(phone.value)
  return value.length > 0 ? value : null
})

/** Backend `role` ni oddiy `string` sifatida yuboradi — qat'iy turga tekshiramiz. */
function isRoleName(value: string | null): value is UserRoleName {
  return ROLE_OPTIONS.some((option) => option.value === value)
}

/** Tasdiq matnida rol KODI emas, xodim ko'radigan yorliq turishi kerak. */
function roleLabel(value: UserRoleName): string {
  return ROLE_OPTIONS.find((option) => option.value === value)?.label ?? value
}

function resetForm(): void {
  const user = props.user
  const rawRole = user?.role ?? null
  fullName.value = user?.fullName ?? ''
  email.value = user?.email ?? ''
  // Serverdagi qiymat `+998901234567` — maydonga formatlangan holda
  // tushadi, ya'ni tahrirlash oynasi ochilishi bilanoq yangi hisob
  // yaratish oynasi bilan BIR XIL ko'rinadi.
  phone.value = formatPhoneInput(user?.phone ?? '')
  role.value = isRoleName(rawRole) ? rawRole : 'Student'
  isActive.value = user?.isActive ?? true
  errorMessage.value = null
}

watch(() => [props.open, props.user], resetForm, { immediate: true })

const createMutation = useMutation({
  mutationFn: () =>
    createUser({
      fullName: fullName.value.trim(),
      email: email.value.trim(),
      role: role.value,
      phone: phonePayload.value,
      isActive: isActive.value,
    }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const updateMutation = useMutation({
  mutationFn: (id: number) =>
    updateUser(id, {
      fullName: fullName.value.trim(),
      email: email.value.trim(),
      phone: phonePayload.value,
      role: role.value,
    }),
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

const isPending = computed(() => createMutation.isPending.value || updateMutation.isPending.value)
const canSubmit = computed(
  () => fullName.value.trim().length > 0
    && email.value.trim().length > 0
    && !phoneMissing.value
    && !isPending.value,
)

/**
 * R4 — bu yerda FAQAT ROL ALMASHUVI tasdiqlanadi.
 *
 * ★ NEGA HAR SAQLASH EMAS: xodim oynani ataylab ochib, "Saqlash" ni ataylab
 * bosdi — ism yoki telefonni tuzatishga ikkinchi bosish qo'shish himoya emas,
 * ishqalanish (yozib qo'yilgan qoida: tasodifiy ZARARdan himoya, har amalga
 * qadam qo'shish emas). Ism/telefon/email xatosi bir zumda qaytariladi.
 *
 * ★ ROL ESA BOSHQA GAP: u — RUXSAT. "Ustoz" ni "Administrator" ga aylantirish
 * butun moliya va sozlamalarni ochadi; teskarisi esa odamni o'z paneliDAN
 * chiqarib yuboradi. Select bitta g'ildirak harakati bilan almashadi va
 * o'zgarish formada hech qanday ogohlantirish bermasdi. `warning` — amal
 * qaytariladi (rolni qayta tanlash mumkin), lekin oralig'da odam noto'g'ri
 * ruxsat bilan yuradi.
 *
 * ★ YARATISHDA tasdiq YO'Q — yangi yozuv hech narsani almashtirmaydi
 * (`LessonEditDrawer` dagi bilan bir xil qoida).
 */
async function handleSubmit(): Promise<void> {
  if (!canSubmit.value) return

  const user = props.user
  if (user === null) {
    errorMessage.value = null
    createMutation.mutate()
    return
  }

  const previous = user.role
  if (isRoleName(previous) && previous !== role.value) {
    const name = user.fullName ?? 'Foydalanuvchi'
    const ok = await confirm({
      title: 'Rolni o‘zgartirish',
      message:
        `${name} “${roleLabel(previous)}” dan “${roleLabel(role.value)}” ga o‘tkaziladi. `
        + 'Ruxsatlari darhol almashadi.',
      confirmLabel: 'O‘zgartirish',
      tone: 'warning',
      details: [`${roleLabel(previous)} → ${roleLabel(role.value)}`],
    })
    if (!ok) return
  }

  errorMessage.value = null
  updateMutation.mutate(user.id)
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Foydalanuvchini tahrirlash' : 'Yangi foydalanuvchi'"
    @close="emit('close')"
  >
    <form
      novalidate
      @submit.prevent="handleSubmit"
    >
      <BaseField label="To‘liq ism">
        <input
          v-model="fullName"
          class="zn-input"
          autocomplete="name"
          required
        >
      </BaseField>

      <div class="mt-3">
        <BaseField label="Elektron pochta">
          <input
            v-model="email"
            class="zn-input"
            type="email"
            autocomplete="email"
            required
          >
        </BaseField>
      </div>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <!--
          🔴 TELEFON — KIRISH KALITI, shunchaki kontakt emas. Xodim uni
             kiritmasa yaratilgan profil hech qachon tizimga kira olmaydi.
        -->
        <BaseField
          :label="phoneRequired ? 'Telefon (majburiy)' : 'Telefon'"
          :hint="phoneRequired
            ? 'Kirish kodi shu raqamga ulangan Telegram hisobiga yuboriladi.'
            : undefined"
        >
          <!--
            ★ `:value` + `@input`, `v-model` EMAS — kursor har bosishda
            satr oxiriga sakramasin (sabab `maskPhoneField` izohida).
          -->
          <input
            :value="phone"
            class="zn-input"
            type="tel"
            inputmode="tel"
            :required="phoneRequired"
            :maxlength="PHONE_INPUT_MAXLENGTH"
            placeholder="+998 90 123 45 67"
            @input="phone = maskPhoneField($event.target as HTMLInputElement)"
          >
        </BaseField>
        <BaseField label="Rol">
          <select
            v-model="role"
            class="zn-input"
          >
            <option
              v-for="option in ROLE_OPTIONS"
              :key="option.value"
              :value="option.value"
            >
              {{ option.label }}
            </option>
          </select>
        </BaseField>
      </div>

      <p
        v-if="phoneMissing"
        class="mt-3 rounded-lg bg-amber-500/10 px-3 py-2 text-xs text-amber-300 ring-1 ring-inset ring-amber-500/25"
      >
        Xodim uchun telefon raqami majburiy — tizimga kirish faqat telefon
        orqali bo‘ladi.
      </p>

      <label
        v-if="!isEdit"
        class="mt-3 flex min-h-11 items-center gap-2.5 text-sm text-slate-300"
      >
        <input
          v-model="isActive"
          type="checkbox"
          class="size-4 accent-brand-500"
        >
        Faol holatda yaratilsin
      </label>

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
        :loading="isPending"
        @click="handleSubmit"
      >
        {{ isEdit ? 'Saqlash' : 'Yaratish' }}
      </BaseButton>
    </template>
  </BaseModal>
</template>
