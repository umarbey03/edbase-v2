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
import LessonChargesDialog from '@/features/payment-actions/ui/LessonChargesDialog.vue'
import RecordPaymentDialog from '@/features/payment-actions/ui/RecordPaymentDialog.vue'
import ReversePaymentDialog from '@/features/payment-actions/ui/ReversePaymentDialog.vue'
import StaffGroupsSection from '@/features/staff-profile/ui/StaffGroupsSection.vue'
import StaffPayrollSection from '@/features/staff-profile/ui/StaffPayrollSection.vue'
import StudentNotesSection from '@/features/student-notes/ui/StudentNotesSection.vue'
import { toUserMessage } from '@/shared/api'
import type { UserRoleName } from '@/shared/types'
import { AppIcon, BaseAvatar, BaseBadge, BaseDrawer, DataStatus, SectionLoader } from '@/shared/ui'

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

/*
  ═══════════════════════════════════════════════════════════════════════
   USTOZ/KURATOR PROFILI — O'QUVCHINIKIDAN BOSHQA KO'RINISH (2026-09-09,
   loyiha egasi: "ustoz profili o'quvchi profilidan farq qilishi kerak").
  ═══════════════════════════════════════════════════════════════════════
  Belgi — serverdan kelgan `staff` bloki (`null` = o'quvchi). Xodim uchun
  bo'limlar: Shaxsiy → O'qitadigan guruhlar → Oylik va stavkalar (faqat
  Admin: oylik endpointlari Admin darvozasi ortida). To'lovlar, o'quv
  natijalari va izohlar bo'limlari xodimda MA'NOSIZ va chizilmaydi.
*/
const isStaffProfile = computed(() => profile.value?.staff != null)

const staffRole = computed<UserRoleName>(() =>
  roleName.value === 'Assistant' ? 'Assistant' : 'Teacher',
)

const staffGroupsActive = computed(
  () => profile.value?.staff?.groups.filter((group) => group.isActive) ?? [],
)
const staffStudentTotal = computed(() =>
  staffGroupsActive.value.reduce((sum, group) => sum + group.activeStudentCount, 0),
)

const canManagePayroll = computed(() => isAdminRole(auth.role ?? ''))

const subtitle = computed(() => {
  const data = profile.value
  if (data === null) return ''
  const status = data.user.isActive ? 'Faol' : 'Bloklangan'
  return isStaffProfile.value
    ? `Xodim profili · ${roleLabel(data.user.role ?? '')} · ${status}`
    : `${roleLabel(data.user.role ?? '')} · ${status}`
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
const lessonChargesOpen = ref(false)
const lessonChargesGroupId = ref<number | null>(null)
const lessonChargesPeriod = ref<string | null>(null)

function openLessonCharges(groupId: number, period: string): void {
  lessonChargesGroupId.value = groupId
  lessonChargesPeriod.value = period
  lessonChargesOpen.value = true
}

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
    lessonChargesOpen.value = false
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
        <!--
          Xodim uchun kartaning o'zi boshqacha: brend rangli chegara,
          «ustoz» ikonkasi va yuk ko'rsatkichi (guruh/o'quvchi soni) —
          ro'yxatdan ochilganda birinchi qarashda "bu o'quvchi emas"
          ekani bilinsin.
        -->
        <div
          class="flex items-center gap-3 rounded-2xl border p-3.5"
          :class="
            isStaffProfile
              ? 'border-brand-500/40 bg-brand-500/10'
              : 'border-line bg-ink-800'
          "
        >
          <div class="relative shrink-0">
            <BaseAvatar
              :name="displayName"
              size="lg"
            />
            <span
              v-if="isStaffProfile"
              class="absolute -bottom-1 -right-1 flex size-5 items-center justify-center rounded-full bg-brand-500 text-on-brand ring-2 ring-ink-900"
              aria-hidden="true"
            >
              <AppIcon
                name="graduation"
                :size="11"
              />
            </span>
          </div>
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
            <p
              v-if="isStaffProfile"
              class="mt-1.5 text-xs text-slate-400"
            >
              {{ staffGroupsActive.length }} faol guruh · {{ staffStudentTotal }} o‘quvchi
            </p>
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

        <!-- ═══════════════════════════════ XODIM: guruhlar + oylik ═══ -->
        <template v-if="isStaffProfile && profile.staff !== null">
          <StaffGroupsSection
            :groups="profile.staff.groups"
            @open="openGroup"
          />

          <!-- 🔴 Oylik endpointlari FAQAT Admin — o'quv bo'limi bo'limni ko'rmaydi. -->
          <StaffPayrollSection
            v-if="canManagePayroll && props.userId !== null"
            :user-id="props.userId"
            :user-name="displayName"
            :role="staffRole"
          />
        </template>

        <!-- ═══════════════════════════════ O'QUVCHI: to'lov, guruh, o'quv, izoh ═══ -->
        <template v-else>
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
            @open-lesson-charges="openLessonCharges"
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
        </template>
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

  <LessonChargesDialog
    v-if="lessonChargesOpen"
    :open="lessonChargesOpen"
    :student-id="props.userId"
    :group-id="lessonChargesGroupId"
    :period="lessonChargesPeriod"
    @close="lessonChargesOpen = false"
  />
</template>
