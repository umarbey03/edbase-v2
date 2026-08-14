<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  canSeeStudentContact,
  fetchUserProfile,
  isAdminRole,
  isManagerRole,
  roleLabel,
  roleTone,
} from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import RecordPaymentDialog from '@/features/payment-actions/ui/RecordPaymentDialog.vue'
import ReversePaymentDialog from '@/features/payment-actions/ui/ReversePaymentDialog.vue'
import StudentNotesSection from '@/features/student-notes/ui/StudentNotesSection.vue'
import { toUserMessage } from '@/shared/api'
import { BaseAvatar, BaseBadge, BaseDrawer, DataStatus, SectionLoader } from '@/shared/ui'

import ProfileFinanceSection from './ProfileFinanceSection.vue'
import ProfileGroupsSection from './ProfileGroupsSection.vue'
import ProfilePersonalSection from './ProfilePersonalSection.vue'
import ProfileStudySection from './ProfileStudySection.vue'
import ProfileTransactionsDialog from './ProfileTransactionsDialog.vue'
import TelegramUnlinkDialog from './TelegramUnlinkDialog.vue'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  O'QUVCHI PROFILI — o'ngdan chiquvchi panel (BLOK E)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Bo'limlar tartibi LOYIHA EGASI bergan ro'yxatdan va o'zgartirilmaydi:
 *   1. Shaxsiy  2. To'lovlar  3. Guruhlar  4. O'quv natijalari  5. Izohlar.
 *
 * ★ BITTA SO'ROV (`GET /users/{id}/profile`), 7 ta emas: telefon internetida
 * yetti parallel so'rov 2–3 sekund BO'SH panel berardi. Yuklanish davomida
 * `SectionLoader`, xatoda `DataStatus` naqshi (qayta urinish tugmasi bilan).
 *
 * 🔴 ROL BO'YICHA KESISH SERVERDA, bu yerda faqat NULL'GA HURMAT:
 *   • `finance === null`  -> ustoz/kurator so'ragan  -> TO'LOVLAR BO'LIMI YO'Q;
 *   • `notes === null`    -> o'quvchining o'zi        -> IZOHLAR BO'LIMI YO'Q.
 * Bu maydonlarni "yashirish" mumkin emas, chunki ular javobda UMUMAN yo'q.
 *
 * 🔴 PUL KIRITISH/YECHISH — FAQAT `Admin` (loyiha egasi: *"bunisi faqat
 * admin panelda"*). `Academic` moliyani ko'radi, o'zgartira olmaydi.
 *
 * ★ QATLAM TARTIBI (haqiqiy xato edi, `ManagePaymentsPage` izohiga qarang):
 * `BaseModal`/`BaseDrawer` `Teleport to="body"` bilan chiziladi va hammasi
 * `z-50` da — ustma-ust tushganda DOM TARTIBI hal qiladi. Shu sababli ichki
 * oynalar `<BaseDrawer>` dan KEYIN e'lon qilingan VA `v-if` bilan: teleport
 * langari faqat oyna kerak bo'lganda yaratiladi, ya'ni panelning USTIGA
 * tushadi. `v-if` ni olib tashlasangiz oyna panel ORTIDA ochiladi va uni
 * bosib bo'lmaydi.
 */
const props = withDefaults(
  defineProps<{
    open: boolean
    /** `null` — panel bo'sh ochilmaydi (ro'yxat qatori bosilganda to'ladi). */
    userId: number | null
    /**
     * Ro'yxatdan kelgan ism — so'rov javobi kelmasdan sarlavhada ko'rinadi.
     * Aks holda panel ochilganda sarlavha bo'sh turardi va "yuklanmadimi?"
     * degan taassurot berardi.
     */
    fallbackName?: string
  }>(),
  { fallbackName: '' },
)

const emit = defineEmits<{
  close: []
  /** Ro'yxatni yangilash uchun (Telegram uzilgach `telegramUsername` o'zgaradi). */
  changed: []
}>()

const auth = useAuthStore()
const queryClient = useQueryClient()
const router = useRouter()

/* ------------------------------------------------------------- so'rov --- */

const enabled = computed(() => props.open && props.userId !== null)

const profileQuery = useQuery({
  queryKey: ['users', 'profile', computed(() => props.userId)],
  queryFn: ({ signal }) => fetchUserProfile(props.userId ?? 0, { signal }),
  enabled,
})

const profile = computed(() => profileQuery.data.value ?? null)

const errorMessage = computed(() =>
  profileQuery.error.value !== null ? toUserMessage(profileQuery.error.value) : null,
)

const displayName = computed(() => profile.value?.user.fullName ?? props.fallbackName)

const roleName = computed(() => profile.value?.user.role ?? '')

const subtitle = computed(() => {
  const data = profile.value
  if (data === null) return ''
  return `${roleLabel(data.user.role ?? '')} · ${data.user.isActive ? 'Faol' : 'Bloklangan'}`
})

/** To'lov oynalari `{ id, name }` shaklini kutadi (mavjud shartnoma). */
const student = computed(() =>
  props.userId === null ? null : { id: props.userId, name: displayName.value },
)

/* ------------------------------------------------------------- ruxsat --- */

/*
  KO'RINISH darvozalari. Serverdagi tekshiruvni ALMASHTIRMAYDI: Telegram
  uzish endpointi `[Authorize(Roles="Academic,Admin")]`, to'lov endpointlari
  esa o'z rollarini o'zi tekshiradi. Bu yerdagi shartlar faqat "bosib
  bo'lmaydigan tugma ko'rsatmaslik" uchun.
*/
const canUnlink = computed(() => isManagerRole(auth.role ?? ''))
const canManageMoney = computed(() => isAdminRole(auth.role ?? ''))

/*
  🔴 KONTAKT USTOZDAN KESILGAN (talab R27) — bu YO'QLIK, "yashirish" emas:
  `profile.user.phone/email` va `profile.telegram.telegramId/username`
  serverdan `null` bo'lib keladi (moliya bloki bilan aynan bir printsip).

  Bu yerdagi shart faqat SABABNI to'g'ri yozish uchun ("Ko'rsatilmaydi",
  "—" emas): bo'sh maydonni ko'rgan ustoz aks holda ma'lumot kiritilmagan
  deb o'ylardi.
*/
const contactHidden = computed(() => !canSeeStudentContact(auth.role ?? ''))

/* -------------------------------------------------------- ichki oynalar -- */

const unlinkOpen = ref(false)
const recordOpen = ref(false)
const reverseOpen = ref(false)
const transactionsOpen = ref(false)

// Panel yopilganda ichki oynalar ham yopiladi: aks holda keyingi ochilishda
// eski oyna "yopishib" chiqardi (ro'yxatdagi boshqa o'quvchi bilan).
watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) return
    unlinkOpen.value = false
    recordOpen.value = false
    reverseOpen.value = false
    transactionsOpen.value = false
  },
)

/**
 * Profil agregatini qayta o'qish.
 *
 * ★ Kalit `['users', 'profile']` (id'siz) — ro'yxat kaliti ham `['users']`
 * bilan boshlanadi, ya'ni ro'yxatni invalidatsiya qilish profilni ham
 * yangilaydi. Bu ataylab: Telegram uzilgach ikki joyda ham eski holat
 * qolmasligi kerak.
 */
function reloadProfile(): void {
  void queryClient.invalidateQueries({ queryKey: ['users', 'profile'] })
}

function onUnlinked(): void {
  reloadProfile()
  emit('changed')
}

/** To'lov kiritildi/qaytarildi -> profil ham, moliya ekranlari ham eskirdi. */
function onMoneySaved(): void {
  reloadProfile()
  void queryClient.invalidateQueries({ queryKey: ['payments'] })
  emit('changed')
}

/**
 * Guruh sahifasiga o'tish.
 *
 * Panel AVVAL yopiladi: navigatsiya sahifani almashtiradi va ochiq drawer
 * yangi sahifa ustida "osilib" qolardi (`useModalHost` uni unmount'da
 * tozalaydi, lekin foydalanuvchi bir lahza ikki ekranni birga ko'rardi).
 */
function openGroup(groupId: number): void {
  emit('close')
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}
</script>

<template>
  <BaseDrawer
    :open="props.open"
    :title="displayName.length > 0 ? displayName : 'Profil'"
    :subtitle="subtitle"
    @close="emit('close')"
  >
    <SectionLoader
      v-if="profileQuery.isPending.value"
      variant="card"
      :rows="6"
      label="Profil yuklanmoqda"
    />

    <!--
      `DataStatus` FAQAT xato uchun: `pending` yuqorida `SectionLoader` bilan
      qoplangan (skeleton kontentning kelajak shaklini beradi), `empty` esa
      bu yerda ma'nosiz — profil bo'sh bo'lishi mumkin emas.
    -->
    <DataStatus
      v-else
      :pending="false"
      :error="errorMessage"
      :empty="false"
      :retrying="profileQuery.isFetching.value"
      @retry="profileQuery.refetch()"
    >
      <div
        v-if="profile !== null"
        class="space-y-4"
      >
        <!-- ------------------------------------------------------ xulosa -->
        <div class="flex items-center gap-3 rounded-2xl border border-line bg-ink-800 p-3.5">
          <BaseAvatar
            :name="displayName"
            size="lg"
          />
          <div class="min-w-0">
            <p
              class="truncate text-base font-semibold text-slate-100"
              v-text="displayName"
            />
            <div class="mt-1.5 flex flex-wrap items-center gap-2">
              <BaseBadge :tone="roleTone(roleName)">
                {{ roleLabel(roleName) }}
              </BaseBadge>
              <BaseBadge :tone="profile.user.isActive ? 'success' : 'danger'">
                {{ profile.user.isActive ? 'Faol' : 'Bloklangan' }}
              </BaseBadge>
            </div>
          </div>
        </div>

        <!-- 1 ---------------------------------------------------- shaxsiy -->
        <ProfilePersonalSection
          :user="profile.user"
          :telegram="profile.telegram"
          :can-unlink="canUnlink"
          :contact-hidden="contactHidden"
          @unlink="unlinkOpen = true"
        />

        <!-- 2 --------------------------------------------------- to'lovlar -->
        <!--
          🔴 `finance === null` -> BO'LIM UMUMAN YO'Q (ustoz/kurator).
          Ma'lumot serverdan kelmaydi, ya'ni "yashirish" emas — yo'qlik.
        -->
        <ProfileFinanceSection
          v-if="profile.finance !== null"
          :finance="profile.finance"
          :can-manage-money="canManageMoney"
          @record="recordOpen = true"
          @reverse="reverseOpen = true"
          @show-transactions="transactionsOpen = true"
        />

        <!-- 3 ---------------------------------------------------- guruhlar -->
        <ProfileGroupsSection
          :groups="profile.groups"
          @open="openGroup"
        />

        <!-- 4 --------------------------------------------- o'quv natijalari -->
        <ProfileStudySection :study="profile.study" />

        <!-- 5 ----------------------------------------------------- izohlar -->
        <!-- 🔴 `notes === null` -> o'quvchining o'zi ko'rayapti: bo'lim yo'q. -->
        <StudentNotesSection
          v-if="profile.notes !== null && props.userId !== null"
          :student-id="props.userId"
          :notes="profile.notes"
          :groups="profile.groups"
          @changed="reloadProfile"
        />
      </div>
    </DataStatus>
  </BaseDrawer>

  <!--
    ══════════════════════════════════════════════════════════════════════
     ICHKI OYNALAR — TARTIB VA `v-if` MUHIM (yuqoridagi izoh)
    ══════════════════════════════════════════════════════════════════════
    Hammasi `<BaseDrawer>` dan KEYIN va `v-if` bilan: teleport langari faqat
    kerak bo'lganda yaratiladi va oyna panel USTIGA chiqadi.
  -->
  <TelegramUnlinkDialog
    v-if="unlinkOpen"
    :open="unlinkOpen"
    :user-id="props.userId"
    :user-name="displayName"
    :username="profile?.telegram.username ?? null"
    @close="unlinkOpen = false"
    @unlinked="onUnlinked"
  />

  <RecordPaymentDialog
    v-if="recordOpen"
    :open="recordOpen"
    :student="student"
    @close="recordOpen = false"
    @saved="onMoneySaved"
  />

  <ReversePaymentDialog
    v-if="reverseOpen"
    :open="reverseOpen"
    :student="student"
    @close="reverseOpen = false"
    @saved="onMoneySaved"
  />

  <ProfileTransactionsDialog
    v-if="transactionsOpen"
    :open="transactionsOpen"
    :student-id="props.userId"
    :student-name="displayName"
    @close="transactionsOpen = false"
  />
</template>
