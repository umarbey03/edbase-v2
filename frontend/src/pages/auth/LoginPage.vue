<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseButton } from '@/shared/ui'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

const email = ref('')
const password = ref('')
const showPassword = ref(false)
const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)

const sessionExpired = computed(() => route.query['sabab'] === 'sessiya-tugadi')
const canSubmit = computed(
  () => email.value.trim().length > 0 && password.value.length > 0 && !isSubmitting.value,
)

function redirectTarget(): string {
  const raw = route.query['redirect']
  const value = Array.isArray(raw) ? raw[0] : raw
  // Faqat ichki yo'llarga yo'naltiramiz (ochiq redirect zaifligining oldini olish).
  if (typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')) return value
  return '/darslar'
}

async function handleSubmit(): Promise<void> {
  if (!canSubmit.value) return
  isSubmitting.value = true
  errorMessage.value = null
  try {
    await auth.login({ email: email.value.trim(), password: password.value })
    await router.replace(redirectTarget())
  } catch (error) {
    errorMessage.value = toUserMessage(error)
  } finally {
    isSubmitting.value = false
  }
}
</script>

<template>
  <div class="flex min-h-dvh items-center justify-center bg-ink-950 px-4 py-10">
    <!-- Fon nuri -->
    <div
      class="pointer-events-none fixed inset-0 opacity-60"
      aria-hidden="true"
      style="
        background:
          radial-gradient(60rem 40rem at 20% -10%, rgba(99, 102, 241, 0.14), transparent 60%),
          radial-gradient(40rem 30rem at 90% 110%, rgba(16, 185, 129, 0.1), transparent 60%);
      "
    />

    <div class="relative w-full max-w-sm">
      <div class="mb-7 text-center">
        <div
          class="mx-auto flex size-12 items-center justify-center rounded-2xl bg-brand-600 text-lg font-bold text-white shadow-lg shadow-brand-600/30"
        >
          Z
        </div>
        <h1 class="mt-4 text-2xl font-semibold tracking-tight text-slate-50">Zin-Nur</h1>
        <p class="mt-1 text-sm text-slate-400">Jonli darslar platformasi</p>
      </div>

      <form
        class="rounded-2xl bg-ink-900 p-6 shadow-2xl shadow-black/40 ring-1 ring-inset ring-line"
        novalidate
        @submit.prevent="handleSubmit"
      >
        <div
          v-if="sessionExpired"
          class="mb-4 rounded-xl bg-amber-500/10 px-3 py-2 text-xs text-amber-200 ring-1 ring-inset ring-amber-500/25"
        >
          Sessiya muddati tugadi. Iltimos, qaytadan kiring.
        </div>

        <label class="block">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">Elektron pochta</span>
          <div class="relative">
            <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
              <AppIcon name="mail" :size="17" />
            </span>
            <input
              v-model="email"
              type="email"
              name="email"
              autocomplete="email"
              required
              placeholder="ism@zinnur.uz"
              class="h-11 w-full rounded-xl bg-ink-850 pl-10 pr-3 text-sm text-slate-100 ring-1 ring-inset ring-line transition-colors placeholder:text-slate-600 focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
          </div>
        </label>

        <label class="mt-4 block">
          <span class="mb-1.5 block text-xs font-medium text-slate-400">Parol</span>
          <div class="relative">
            <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
              <AppIcon name="lock" :size="17" />
            </span>
            <input
              v-model="password"
              :type="showPassword ? 'text' : 'password'"
              name="password"
              autocomplete="current-password"
              required
              placeholder="••••••••"
              class="h-11 w-full rounded-xl bg-ink-850 pl-10 pr-16 text-sm text-slate-100 ring-1 ring-inset ring-line transition-colors placeholder:text-slate-600 focus:outline-none focus:ring-2 focus:ring-brand-500"
            />
            <button
              type="button"
              class="absolute inset-y-0 right-2 my-auto h-7 rounded-lg px-2 text-[11px] font-medium text-slate-400 transition-colors hover:bg-white/5 hover:text-slate-200"
              @click="showPassword = !showPassword"
            >
              {{ showPassword ? 'Yashirish' : 'Ko‘rsatish' }}
            </button>
          </div>
        </label>

        <p
          v-if="errorMessage !== null"
          class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
          role="alert"
          v-text="errorMessage"
        />

        <BaseButton class="mt-6" type="submit" size="lg" block :loading="isSubmitting" :disabled="!canSubmit">
          Kirish
        </BaseButton>
      </form>

      <p class="mt-6 text-center text-xs text-slate-600">
        Parolni unutdingizmi? Kuratoringizga murojaat qiling.
      </p>
    </div>
  </div>
</template>
