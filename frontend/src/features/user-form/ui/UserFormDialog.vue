<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { createUser, ROLE_OPTIONS, updateUser } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import type { UserDetailsDto, UserRoleName } from '@/shared/types'
import { BaseButton, BaseField, BaseModal } from '@/shared/ui'

/**
 * Foydalanuvchi yaratish/tahrirlash oynasi.
 *
 * Yaratishda parol IXTIYORIY: bo'sh qoldirilsa server vaqtinchalik parol
 * generatsiya qiladi va uni FAQAT BIR MARTA qaytaradi — shuning uchun
 * javob kelgach oyna yopilmaydi, parol ekranda ko'rsatiladi.
 */
const props = defineProps<{
  open: boolean
  /** `null` — yangi foydalanuvchi rejimi. */
  user: UserDetailsDto | null
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const fullName = ref('')
const email = ref('')
const phone = ref('')
const role = ref<UserRoleName>('Student')
const password = ref('')
const isActive = ref(true)
const errorMessage = ref<string | null>(null)
const temporaryPassword = ref<string | null>(null)

const isEdit = computed(() => props.user !== null)

/** Backend `role` ni oddiy `string` sifatida yuboradi — qat'iy turga tekshiramiz. */
function isRoleName(value: string | null): value is UserRoleName {
  return ROLE_OPTIONS.some((option) => option.value === value)
}

function resetForm(): void {
  const user = props.user
  const rawRole = user?.role ?? null
  fullName.value = user?.fullName ?? ''
  email.value = user?.email ?? ''
  phone.value = user?.phone ?? ''
  role.value = isRoleName(rawRole) ? rawRole : 'Student'
  password.value = ''
  isActive.value = user?.isActive ?? true
  errorMessage.value = null
  temporaryPassword.value = null
}

watch(() => [props.open, props.user], resetForm, { immediate: true })

const createMutation = useMutation({
  mutationFn: () =>
    createUser({
      fullName: fullName.value.trim(),
      email: email.value.trim(),
      role: role.value,
      phone: phone.value.trim().length > 0 ? phone.value.trim() : null,
      password: password.value.length > 0 ? password.value : null,
      isActive: isActive.value,
    }),
  onSuccess: (response) => {
    emit('saved')
    if (response.temporaryPassword !== null && response.temporaryPassword.length > 0) {
      temporaryPassword.value = response.temporaryPassword
    } else {
      emit('close')
    }
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
      phone: phone.value.trim().length > 0 ? phone.value.trim() : null,
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
  () => fullName.value.trim().length > 0 && email.value.trim().length > 0 && !isPending.value,
)

function handleSubmit(): void {
  if (!canSubmit.value) return
  errorMessage.value = null
  const user = props.user
  if (user !== null) updateMutation.mutate(user.id)
  else createMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    :title="isEdit ? 'Foydalanuvchini tahrirlash' : 'Yangi foydalanuvchi'"
    @close="emit('close')"
  >
    <!-- Vaqtinchalik parol: qayta olib bo'lmaydi, shuning uchun alohida ajratilgan. -->
    <div
      v-if="temporaryPassword !== null"
      class="rounded-lg border border-brand-500/30 bg-brand-500/10 p-4"
    >
      <p class="text-sm font-semibold text-brand-300">
        Foydalanuvchi yaratildi
      </p>
      <p class="mt-1 text-xs text-slate-300">
        Vaqtinchalik parolni hoziroq nusxalang — u boshqa ko‘rsatilmaydi.
      </p>
      <p
        class="mt-3 select-all break-all rounded-lg bg-ink-950 px-3 py-2 font-mono text-sm text-slate-100"
        v-text="temporaryPassword"
      />
    </div>

    <form
      v-else
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
        <BaseField label="Telefon">
          <input
            v-model="phone"
            class="zn-input"
            type="tel"
            inputmode="tel"
            placeholder="+998…"
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

      <div
        v-if="!isEdit"
        class="mt-3"
      >
        <BaseField
          label="Parol"
          hint="Bo‘sh qoldirsangiz server vaqtinchalik parol yaratadi."
        >
          <input
            v-model="password"
            class="zn-input"
            type="text"
            autocomplete="new-password"
          >
        </BaseField>
      </div>

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
      <template v-if="temporaryPassword !== null">
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
          :disabled="!canSubmit"
          :loading="isPending"
          @click="handleSubmit"
        >
          {{ isEdit ? 'Saqlash' : 'Yaratish' }}
        </BaseButton>
      </template>
    </template>
  </BaseModal>
</template>
