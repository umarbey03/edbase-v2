<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { useShopMe } from '@/entities/shop'
import { useMySubscription } from '@/entities/subscription'
import { useAvatar } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { ClassroomModal } from '@/features/classroom'
import MyPointsCard from '@/features/points/ui/MyPointsCard.vue'
import MilliyPaymentCard from '@/features/student-payment/ui/MilliyPaymentCard.vue'
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

/*
  ★ MILLIY (GURUH) O'QUVCHISI (2026-10-31): unga ham shu sahifa — avatar
    varag'idagi «Profil va to'lovlar» orqali (pastki menyuda 5 tab band).
    Unda obuna yo'q, o'rniga to'lov kartasi (balans, nechta darsga yetadi,
    qarz) va «Guruhim» — ustoz, kurator, guruhdoshlar.
*/
const isGroupStudent = computed(() =>
  !isGuest.value && subscription.isLoaded.value && !subscription.isSelfStudy.value,
)
const classroomOpen = ref(false)
const router = useRouter()
function goPay(): void {
  void router.push({ name: 'student-plans' })
}

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
        <p
          v-if="isGroupStudent"
          class="mt-1 inline-flex rounded-full bg-brand-500/12 px-2.5 py-0.5 text-[11px] font-bold text-brand-300"
        >
          Milliy tarif
        </p>
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

    <template v-if="isGroupStudent && auth.user !== null">
      <MilliyPaymentCard
        :student-id="auth.user.id"
        @pay="goPay"
      />

      <button
        type="button"
        class="group flex min-h-11 w-full items-center gap-3 rounded-xl border border-line bg-ink-900 p-[15px] text-left transition-colors hover:border-line-strong hover:bg-ink-800"
        @click="classroomOpen = true"
      >
        <span
          class="flex size-[42px] shrink-0 items-center justify-center rounded-xl bg-emerald-500/12 text-emerald-300"
          aria-hidden="true"
        >
          <AppIcon
            name="users"
            :size="20"
          />
        </span>
        <span class="min-w-0 flex-1">
          <b class="block text-base">Guruhim</b>
          <span class="block text-xs text-dim">Asosiy ustoz, support teacher, guruhdoshlar</span>
        </span>
        <AppIcon
          name="chevron-right"
          :size="20"
          class="shrink-0 text-dim"
        />
      </button>
    </template>

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

    <ClassroomModal
      v-if="isGroupStudent"
      :open="classroomOpen"
      @close="classroomOpen = false"
    />
  </div>
</template>
