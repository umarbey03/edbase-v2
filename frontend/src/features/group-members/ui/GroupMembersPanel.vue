<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  fetchGroupMembers,
  memberStatusLabel,
  memberStatusTone,
  removeMember,
  resumeMember,
} from '@/entities/group'
import { canSeeStudentContact } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatDateWithYear } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { GroupMemberDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus, IconButton } from '@/shared/ui'

import AddMemberDialog from './AddMemberDialog.vue'
import MoveMemberDialog from './MoveMemberDialog.vue'
import PauseMemberDialog from './PauseMemberDialog.vue'

/**
 * Guruh o'quvchilari va ular ustidagi amallar.
 *
 * RUXSAT: amallarni faqat o'quv bo'limi va admin bajaradi (server
 * `[Authorize(Roles = "Academic,Admin")]` bilan qulflagan). `canManage`
 * tugmalarni YASHIRADI, lekin qoidani TAKRORLAMAYDI — haqiqiy tekshiruv
 * serverda; ustoz sahifani ochsa ro'yxatni ko'radi, o'zgartira olmaydi.
 *
 * ══════════════════════════════════════════════════════════════════════
 *  AMALLAR IKONKA KO'RINISHIDA (talab: *"har bir o'quvchi bo'yicha actions
 *  buttonlar icon ko'rinishida bo'lgani ma'qul"*)
 * ══════════════════════════════════════════════════════════════════════
 *
 *  | ikonka             | amal                  | tasdiq              |
 *  |--------------------|-----------------------|---------------------|
 *  | `user`             | profilni ochish       | — (o'zgarish yo'q)  |
 *  | `pause` / `play`   | pauza / tiklash       | dialog / `warning`  |
 *  | `arrow-right-left` | boshqa guruhga ko'chirish | dialog          |
 *  | `user-x`           | guruhdan chiqarish    | `danger`            |
 *  | `wallet`           | to'lov holati         | — (o'qish)          |
 *
 * ★ TASDIQ QAYERDA VA NEGA (reja B2 jadvali bo'yicha qabul qilingan qaror):
 *
 *  • "Chiqarish" — dialogsiz, bir bosishda bajariladigan QAYTARIB
 *    BO'LMAYDIGAN amal, shuning uchun `danger` tasdiq SHART.
 *  • "Tiklash" (`play`) — ham bir bosishli, `warning` tasdiq bilan:
 *    o'quvchi darsga qaytadi va davomat/to'lov hisobi yana yuritiladi.
 *  • "Pauza" va "Ko'chirish" — mavjud DIALOGLARNI ochadi
 *    (`PauseMemberDialog` sanani, `MoveMemberDialog` nishon guruhni
 *    so'raydi). Ular oldidan qo'shimcha tasdiq QO'YILMADI: dialogning o'zi
 *    tasdiq rolini o'ynaydi ("Bekor qilish" tugmasi bilan) va ikki ketma-ket
 *    oyna foydalanuvchini "nima uchun ikki marta so'raladi?" degan holatga
 *    tushirardi (reja B2: har checkbox uchun oyna interfeysni
 *    foydalanishga yaramas qiladi).
 *  • Profil va to'lov — O'QISH amallari, tasdiq talab qilmaydi.
 *
 * ★ `ConfirmDeleteDialog` ORNIGA `useConfirm`: u faqat server sababini
 * oynada USHLAB TURISH kerak bo'lganda afzal. `DELETE .../members/{id}`
 * esa idempotent va 409 qaytarmaydi (`GroupService.RemoveMemberAsync`),
 * ya'ni ushlab turadigan sabab yo'q; xato ro'yxat ustidagi bannerda
 * ko'rsatiladi va qator joyida qoladi.
 */
const props = defineProps<{
  groupId: number
  canManage: boolean
}>()

const emit = defineEmits<{
  /** O'quvchi profilini ochish (sahifa hal qiladi — drawer boshqa feature'da). */
  'open-profile': [studentId: number]
  /** O'quvchining to'lov holatini ochish. */
  'open-wallet': [studentId: number]
}>()

const queryClient = useQueryClient()
const confirm = useConfirm()
const auth = useAuthStore()

/**
 * 🔴 KONTAKT USTUNLARI USTOZGA UMUMAN CHIZILMAYDI (talab R27).
 *
 * Server ustoz javobida `email` va `phone` ni `null` qilib yuboradi, ya'ni
 * ustunlar QOLDIRILSA butun jadval bo'ylab "—" ustuni turardi — bu "ma'lumot
 * yo'q/ilova buzilgan" degan taassurot berardi. Ustunni umuman chizmaslik
 * rostroq: ustoz uchun bu ma'lumot MAVJUD emas.
 *
 * ⚠️ KO'RINISH darvozasi, xavfsizlik chegarasi EMAS — haqiqiy kesish
 * serverda (`GroupService.ProjectMembers`).
 */
const showContact = computed(() => canSeeStudentContact(auth.role ?? ''))

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi (telefonda ko'rinmas jadval ham mount bo'lib, ma'lumot olardi).

  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi
  (`style.css` dagi "md va lg haqidagi asosiy qaror" izohi).
  ★ Pauza/ko'chirish dialoglarining nishoni (`pauseTarget`, `moveTarget`) SHU
  komponentda saqlanadi, almashinadigan daraxtdan TASHQARIDA — ekran
  o'lchami o'zgarsa ochiq dialog yopilib qolmaydi.
*/
const { isDesktop } = useBreakpoint()

const membersQuery = useQuery({
  queryKey: ['group', props.groupId, 'members'],
  queryFn: ({ signal }) => fetchGroupMembers(props.groupId, { signal }),
})

const members = computed(() => membersQuery.data.value ?? [])
const studentIds = computed(() => members.value.map((member) => member.studentId))

const membersError = computed(() =>
  membersQuery.error.value !== null ? toUserMessage(membersQuery.error.value) : null,
)

/** Amal xatolari — ro'yxat ustidagi banner (jadval yo'qolib ketmasin). */
const actionError = ref<string | null>(null)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['group', props.groupId] })
  // Guruhlar ro'yxatidagi "N o'quvchi" sanog'i ham eskiradi.
  void queryClient.invalidateQueries({ queryKey: ['groups'] })
}

/* --------------------------------------------------------------- amallar */

const addOpen = ref(false)

const pauseTarget = ref<GroupMemberDto | null>(null)
const moveTarget = ref<GroupMemberDto | null>(null)

/**
 * 🔴 QAYSI QATOR ISHLAYAPTI. `useMutation` ning `isPending` i BUTUN
 * mutatsiyaga tegishli, ya'ni u to'g'ridan-to'g'ri qatorga bog'lansa 30
 * o'quvchining HAMMASIDA spinner aylanardi. Amal boshlangan o'quvchining
 * Id'si shu yerda saqlanadi.
 */
const busyStudentId = ref<number | null>(null)

const resumeMutation = useMutation({
  mutationFn: (studentId: number) => resumeMember(props.groupId, studentId),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    busyStudentId.value = null
  },
})

const removeMutation = useMutation({
  mutationFn: (studentId: number) => removeMember(props.groupId, studentId),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
  onSettled: () => {
    busyStudentId.value = null
  },
})

function isBusy(member: GroupMemberDto, kind: 'resume' | 'remove'): boolean {
  if (busyStudentId.value !== member.studentId) return false
  return kind === 'resume' ? resumeMutation.isPending.value : removeMutation.isPending.value
}

/** Bitta o'quvchi ustida ikki amal bir vaqtda ishga tushmasin. */
function isRowLocked(member: GroupMemberDto): boolean {
  return busyStudentId.value === member.studentId
}

function memberName(member: GroupMemberDto): string {
  return member.fullName ?? 'O‘quvchi'
}

async function askResume(member: GroupMemberDto): Promise<void> {
  actionError.value = null
  const ok = await confirm({
    title: 'Pauzadan chiqarish',
    message: `${memberName(member)} guruhga qaytariladi.`,
    confirmLabel: 'Tiklash',
    tone: 'warning',
    details: [
      'O‘quvchi keyingi darslarga qaytadi.',
      'Davomat va to‘lov hisobi shu paytdan yana yuritiladi.',
    ],
  })
  if (!ok) return
  busyStudentId.value = member.studentId
  resumeMutation.mutate(member.studentId)
}

async function askRemove(member: GroupMemberDto): Promise<void> {
  actionError.value = null
  const ok = await confirm({
    title: 'Guruhdan chiqarish',
    message: `${memberName(member)} guruhdan chiqariladi.`,
    confirmLabel: 'Chiqarish',
    tone: 'danger',
    details: [
      'Yozuv o‘chirilmaydi — holati “Chiqarilgan” bo‘ladi.',
      'Davomat va to‘lov tarixi saqlanadi.',
      'Qaytarish uchun o‘quvchini guruhga qaytadan qo‘shish kerak bo‘ladi.',
    ],
  })
  if (!ok) return
  busyStudentId.value = member.studentId
  removeMutation.mutate(member.studentId)
}

/** Pauzadagi o'quvchini qaytarish uchun alohida tugma kerak. */
function isPaused(member: GroupMemberDto): boolean {
  return member.status === 'Paused'
}

/** Chiqarilgan/ko'chirilgan yozuv ustida amal qilinmaydi — u TARIX. */
function isHistorical(member: GroupMemberDto): boolean {
  return member.status === 'Stopped' || member.status === 'Moved'
}
</script>

<template>
  <BaseCard
    flush
    title="O‘quvchilar"
    :subtitle="`Jami: ${members.length}`"
  >
    <template
      v-if="props.canManage"
      #actions
    >
      <BaseButton
        size="sm"
        @click="addOpen = true"
      >
        <template #icon>
          <AppIcon
            name="plus"
            :size="14"
          />
        </template>
        Qo‘shish
      </BaseButton>
    </template>

    <div class="p-3.5 sm:p-5">
      <div
        v-if="actionError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 p-3 text-xs text-rose-200"
        role="alert"
        v-text="actionError"
      />

      <DataStatus
        :pending="membersQuery.isPending.value"
        :error="membersError"
        :empty="members.length === 0"
        :retrying="membersQuery.isFetching.value"
        :skeleton-rows="2"
        empty-icon="users"
        empty-title="O‘quvchi yo‘q"
        :empty-text="props.canManage ? 'Guruhga o‘quvchi qo‘shing.' : ''"
        @retry="membersQuery.refetch()"
      >
        <!-- Telefon/planshet: kartochka -->
        <ul
          v-if="!isDesktop"
          class="space-y-2"
        >
          <li
            v-for="member in members"
            :key="member.id"
            class="rounded-lg border border-line bg-ink-950 p-3"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="member.fullName ?? '—'"
              />
              <BaseBadge :tone="memberStatusTone(member.status)">
                {{ memberStatusLabel(member.status) }}
              </BaseBadge>
            </div>
            <p
              v-if="showContact"
              class="mt-1 truncate text-xs text-slate-400"
              v-text="member.email ?? '—'"
            />
            <p
              v-if="member.phone !== null"
              class="text-xs text-dim"
              v-text="member.phone"
            />
            <p
              v-if="member.pausedUntil !== null"
              class="text-xs text-amber-400"
            >
              {{ formatDateWithYear(member.pausedUntil) }} gacha pauzada
            </p>

            <!--
              🔴 `gap-3` (12px) — `IconButton` ning `tap-expand` maydonlari
              ustma-ust tushmasligi uchun MINIMAL oraliq (13.1/24-tuzoq).
              Kichraytirilsa chetga bosilgan barmoq qo'shni tugmani ishga
              solardi ("Pauza" o'rniga "Chiqarish").

              ★ TELEFONDA HAM BESHTASI QATORDA QOLADI, "..." menyusiga
              yig'ilmaydi. O'lchov: 5 × 36px + 4 × 12px = 228px, 320px
              ekranda kartochka ichida ~260px joy bor. Amallar ALOHIDA
              qatorda (ism/email tepada) — ya'ni jadval katagidagi siqiq
              holat bu yerda yo'q. Yashirin menyu qo'shilsa u ochilganda
              kartochkadan tashqariga chiqib qirqilardi va yana bitta
              fokus-tuzoq mantig'i paydo bo'lardi.
            -->
            <div
              v-if="props.canManage"
              class="mt-2.5 flex flex-wrap items-center gap-3"
            >
              <IconButton
                icon="user"
                label="Profilni ochish"
                @click="emit('open-profile', member.studentId)"
              />
              <IconButton
                icon="wallet"
                label="To‘lov holati"
                @click="emit('open-wallet', member.studentId)"
              />
              <template v-if="!isHistorical(member)">
                <IconButton
                  v-if="isPaused(member)"
                  icon="play"
                  label="Pauzadan chiqarish"
                  tone="success"
                  :loading="isBusy(member, 'resume')"
                  :disabled="isRowLocked(member)"
                  @click="askResume(member)"
                />
                <IconButton
                  v-else
                  icon="pause"
                  label="Pauza qilish"
                  tone="warning"
                  :disabled="isRowLocked(member)"
                  @click="pauseTarget = member"
                />
                <IconButton
                  icon="arrow-right-left"
                  label="Boshqa guruhga ko‘chirish"
                  :disabled="isRowLocked(member)"
                  @click="moveTarget = member"
                />
                <IconButton
                  icon="user-x"
                  label="Guruhdan chiqarish"
                  tone="danger"
                  :loading="isBusy(member, 'remove')"
                  :disabled="isRowLocked(member)"
                  @click="askRemove(member)"
                />
              </template>
            </div>
          </li>
        </ul>

        <!-- Desktop (≥1024px): jadval. Konteyner o'zi skroll qiladi, sahifa emas. -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th>Ism</th>
                <th v-if="showContact">
                  Email
                </th>
                <th v-if="showContact">
                  Telefon
                </th>
                <th>Holat</th>
                <th>Qo‘shilgan</th>
                <th v-if="props.canManage">
                  <span class="sr-only">Amallar</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="member in members"
                :key="member.id"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="member.fullName ?? '—'"
                />
                <td
                  v-if="showContact"
                  class="text-slate-400"
                  v-text="member.email ?? '—'"
                />
                <td
                  v-if="showContact"
                  class="text-slate-400"
                  v-text="member.phone ?? '—'"
                />
                <td>
                  <BaseBadge :tone="memberStatusTone(member.status)">
                    {{ memberStatusLabel(member.status) }}
                  </BaseBadge>
                  <span
                    v-if="member.pausedUntil !== null"
                    class="ml-1.5 text-[11px] tabular-nums text-amber-400"
                  >
                    {{ formatDateWithYear(member.pausedUntil) }} gacha
                  </span>
                </td>
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTime(member.joinedAt)"
                />
                <td v-if="props.canManage">
                  <div class="flex items-center justify-end gap-3">
                    <IconButton
                      icon="user"
                      label="Profilni ochish"
                      @click="emit('open-profile', member.studentId)"
                    />
                    <IconButton
                      icon="wallet"
                      label="To‘lov holati"
                      @click="emit('open-wallet', member.studentId)"
                    />
                    <template v-if="!isHistorical(member)">
                      <IconButton
                        v-if="isPaused(member)"
                        icon="play"
                        label="Pauzadan chiqarish"
                        tone="success"
                        :loading="isBusy(member, 'resume')"
                        :disabled="isRowLocked(member)"
                        @click="askResume(member)"
                      />
                      <IconButton
                        v-else
                        icon="pause"
                        label="Pauza qilish"
                        tone="warning"
                        :disabled="isRowLocked(member)"
                        @click="pauseTarget = member"
                      />
                      <IconButton
                        icon="arrow-right-left"
                        label="Boshqa guruhga ko‘chirish"
                        :disabled="isRowLocked(member)"
                        @click="moveTarget = member"
                      />
                      <IconButton
                        icon="user-x"
                        label="Guruhdan chiqarish"
                        tone="danger"
                        :loading="isBusy(member, 'remove')"
                        :disabled="isRowLocked(member)"
                        @click="askRemove(member)"
                      />
                    </template>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </div>

    <AddMemberDialog
      :open="addOpen"
      :group-id="props.groupId"
      :existing-student-ids="studentIds"
      @close="addOpen = false"
      @saved="refresh"
    />

    <PauseMemberDialog
      :open="pauseTarget !== null"
      :group-id="props.groupId"
      :member="pauseTarget"
      @close="pauseTarget = null"
      @saved="refresh"
    />

    <MoveMemberDialog
      :open="moveTarget !== null"
      :group-id="props.groupId"
      :member="moveTarget"
      @close="moveTarget = null"
      @saved="refresh"
    />
  </BaseCard>
</template>
