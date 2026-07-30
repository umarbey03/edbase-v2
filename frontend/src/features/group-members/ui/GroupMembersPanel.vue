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
import { toUserMessage } from '@/shared/api'
import { formatDateTime, formatDateWithYear } from '@/shared/lib/datetime'
import type { GroupMemberDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseCard,
  ConfirmDeleteDialog,
  DataStatus,
} from '@/shared/ui'

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
 */
const props = defineProps<{
  groupId: number
  canManage: boolean
}>()

const queryClient = useQueryClient()

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

const resumeMutation = useMutation({
  mutationFn: (studentId: number) => resumeMember(props.groupId, studentId),
  onSuccess: refresh,
  onError: (error: Error) => {
    actionError.value = toUserMessage(error)
  },
})

const removeTarget = ref<GroupMemberDto | null>(null)
const removeError = ref<string | null>(null)

const removeMutation = useMutation({
  mutationFn: (studentId: number) => removeMember(props.groupId, studentId),
  onSuccess: () => {
    removeTarget.value = null
    refresh()
  },
  onError: (error: Error) => {
    removeError.value = toUserMessage(error)
  },
})

function askRemove(member: GroupMemberDto): void {
  removeError.value = null
  removeTarget.value = member
}

function confirmRemove(): void {
  const member = removeTarget.value
  if (member === null) return
  removeError.value = null
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
        <!-- Telefon: kartochka -->
        <ul class="space-y-2 md:hidden">
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

            <div
              v-if="props.canManage && !isHistorical(member)"
              class="mt-2.5 flex flex-wrap items-center gap-2"
            >
              <BaseButton
                v-if="isPaused(member)"
                size="sm"
                variant="secondary"
                :loading="resumeMutation.isPending.value"
                @click="resumeMutation.mutate(member.studentId)"
              >
                Davom ettirish
              </BaseButton>
              <BaseButton
                v-else
                size="sm"
                variant="secondary"
                @click="pauseTarget = member"
              >
                Pauza
              </BaseButton>
              <BaseButton
                size="sm"
                variant="secondary"
                @click="moveTarget = member"
              >
                Ko‘chirish
              </BaseButton>
              <BaseButton
                size="sm"
                variant="danger"
                @click="askRemove(member)"
              >
                Chiqarish
              </BaseButton>
            </div>
          </li>
        </ul>

        <!-- Desktop: jadval. Konteyner o'zi skroll qiladi, sahifa emas. -->
        <div class="scroll-x-safe scrollbar-slim hidden md:block">
          <table class="zn-table">
            <thead>
              <tr>
                <th>Ism</th>
                <th>Email</th>
                <th>Telefon</th>
                <th>Holat</th>
                <th>Qo‘shilgan</th>
                <th v-if="props.canManage" />
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
                  class="text-slate-400"
                  v-text="member.email ?? '—'"
                />
                <td
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
                  <div
                    v-if="!isHistorical(member)"
                    class="flex items-center justify-end gap-1.5"
                  >
                    <BaseButton
                      v-if="isPaused(member)"
                      size="sm"
                      variant="secondary"
                      :loading="resumeMutation.isPending.value"
                      @click="resumeMutation.mutate(member.studentId)"
                    >
                      Davom ettirish
                    </BaseButton>
                    <BaseButton
                      v-else
                      size="sm"
                      variant="secondary"
                      @click="pauseTarget = member"
                    >
                      Pauza
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      variant="secondary"
                      @click="moveTarget = member"
                    >
                      Ko‘chirish
                    </BaseButton>
                    <BaseButton
                      size="sm"
                      variant="danger"
                      @click="askRemove(member)"
                    >
                      Chiqarish
                    </BaseButton>
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

    <ConfirmDeleteDialog
      :open="removeTarget !== null"
      title="Guruhdan chiqarish"
      :message="`${removeTarget?.fullName ?? 'O‘quvchi'} guruhdan chiqariladi. Yozuv o‘chirilmaydi — holati “Chiqarilgan” bo‘ladi va davomat/to‘lov tarixi saqlanadi.`"
      confirm-label="Chiqarish"
      :pending="removeMutation.isPending.value"
      :error="removeError"
      @close="removeTarget = null"
      @confirm="confirmRemove"
    />
  </BaseCard>
</template>
