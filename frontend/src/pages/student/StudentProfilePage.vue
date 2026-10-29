<script setup lang="ts">
import { computed } from 'vue'

import { useShopMe } from '@/entities/shop'
import { useMySubscription } from '@/entities/subscription'
import { useAvatar } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import MyPointsCard from '@/features/points/ui/MyPointsCard.vue'
import SubscriptionStatusCard from '@/features/subscription/ui/SubscriptionStatusCard.vue'
import { formatPhone } from '@/shared/lib/phone'
import { AppIcon, BaseButton } from '@/shared/ui'
import { useStudentShellActions } from '@/widgets/student-shell/ui/shellActions'

/**
 * PROFIL SAHIFASI (2026-10-23) — mustaqil o'quvchining 4-tabi.
 *
 * Guruh o'quvchisida pastki 5 tab muzlatilgan, u profilni yuqoridagi
 * avatar orqali (varaq) ochadi. Mustaqil o'quvchida Kalendar va Chat yo'q,
 * shuning uchun profil alohida tab — obuna holati va to'lovlar tarixi
 * aynan shu yerda turadi.
 *
 * Ism va telefon faqat o'qish uchun (2026-08-17 qarori: ularni o'quv
 * bo'limi o'zgartiradi); tahrirlashda faqat rasm almashadi.
 */
const auth = useAuthStore()
const actions = useStudentShellActions()
const subscription = useMySubscription()
// Mehmonda ball ham, reyting ham yo'q — karta boshi berk ko'chaga olib borardi.
const { isGuest } = useShopMe()

const fullName = computed(() => auth.user?.fullName ?? '')
const initial = computed(() => (fullName.value.trim()[0] ?? '?').toUpperCase())
const phone = computed(() => {
  const value = formatPhone(auth.user?.phone)
  return value.length > 0 ? value : 'Kiritilmagan'
})
const avatarUrl = useAvatar(
  computed(() => auth.user?.id ?? null),
  computed(() => auth.user?.avatarUpdatedAt ?? null),
)
</script>

<template>
  <div class="mx-auto max-w-[640px] space-y-4 pt-2">
    <section class="flex items-center gap-4 rounded-xl border border-line bg-ink-900 p-[18px] lg:p-5">
      <img
        v-if="avatarUrl !== null"
        :src="avatarUrl"
        class="size-16 rounded-full object-cover shadow-md"
        alt=""
      >
      <div
        v-else
        class="flex size-16 items-center justify-center rounded-full bg-gradient-to-br from-brand-500 to-violet-400 text-2xl font-bold text-white shadow-md"
        aria-hidden="true"
      >
        {{ initial }}
      </div>
      <div class="min-w-0 flex-1">
        <h1
          class="truncate text-lg font-extrabold text-slate-100"
          v-text="fullName"
        />
        <p
          class="text-sm text-slate-400"
          v-text="phone"
        />
      </div>
      <BaseButton
        variant="secondary"
        size="sm"
        @click="actions.openProfileEdit"
      >
        <AppIcon
          name="edit"
          :size="14"
        />
        Rasm
      </BaseButton>
    </section>

    <SubscriptionStatusCard
      v-if="subscription.data.value?.hasSubscription === true"
      :data="subscription.data.value"
      show-history
    />

    <MyPointsCard v-if="!isGuest" />

    <button
      type="button"
      class="tap-target mx-auto flex items-center justify-center gap-2 py-2 text-[13px] font-semibold text-slate-400 transition-colors hover:text-rose-400"
      @click="actions.logout"
    >
      <AppIcon
        name="logout"
        :size="15"
      />
      Chiqish
    </button>
  </div>
</template>
