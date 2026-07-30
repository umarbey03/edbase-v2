<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { attendanceStatusLabel } from '@/entities/attendance'
import { fetchConversations, waitingHours } from '@/entities/direct-message'
import {
  fetchGroups,
  groupDisplayName,
  groupScheduleSummary,
  groupTypeLabel,
  groupTypeTone,
} from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { GroupDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

import { useAbsentees } from '../model/use-absentees'
import CuratorGroupMembers from './CuratorGroupMembers.vue'

/**
 * "Kuratorlik" — eski `teacher.html` dagi `#curator-hub`.
 *
 * ★ ESKI EKRANDAN NIMA KO'CHIRILDI: "Oxirgi darsni qoldirgan o'quvchilar"
 * ro'yxati (ism, guruh/dars, sabab yoki "Sababsiz", qo'ng'iroq tugmasi),
 * ism bo'yicha qidiruv va uchta ko'rsatkich kartochkasi.
 *
 * ★ NIMA KO'CHIRILMADI (v2 da endpoint yo'q, sabab ekranda ham yozilgan):
 *  • Telegram orqali yordamchi darsga TAKLIFNOMA yuborish
 *    (`/curator/invite-absentee`) — shuning uchun uchinchi ko'rsatkich
 *    "Taklif yuborilganlar" emas, "Javob kutayotgan savollar";
 *  • kurator QAYDLARI (`/students/{id}/notes`) — "Qayd qo'shish" tugmasi.
 * Ularning o'rniga BOR imkoniyat beriladi: qo'ng'iroq va shaxsiy yozishma.
 */
const router = useRouter()
const now = useNow()

const groupsQuery = useQuery({
  queryKey: ['groups', 'mine'],
  queryFn: ({ signal }) => fetchGroups({ page: 1, pageSize: 50 }, { signal }),
})

/**
 * Server ro'yxatni ROLGA qarab o'zi cheklaydi (ustoz -> `TeacherId`,
 * kurator -> `AssistantId` yoki bog'langan kurator guruhi), shuning uchun
 * bu yerda qo'shimcha ruxsat filtri yo'q.
 */
const groups = computed<GroupDto[]>(() => groupsQuery.data.value?.items ?? [])

const groupsError = computed(() =>
  groupsQuery.error.value !== null ? toUserMessage(groupsQuery.error.value) : null,
)

const absentees = useAbsentees(groups)

/** Suhbatlar KO'RSATKICH uchun: "javob kutayotgan savollar". */
const conversationsQuery = useQuery({
  queryKey: ['dm', 'conversations'],
  queryFn: ({ signal }) => fetchConversations({ signal }),
})

const activeGroups = computed(() => groups.value.filter((group) => group.isActive))

const stats = computed(() => ({
  students: activeGroups.value.reduce((sum, group) => sum + group.memberCount, 0),
  unexplained: absentees.rows.value.filter((row) => row.reason === null).length,
  waiting: (conversationsQuery.data.value ?? []).filter(
    (conversation) => waitingHours(conversation, now.value) !== null,
  ).length,
}))

/* ------------------------------------------- dars qoldirganlar ro'yxati */

const absenteeSearch = ref('')

const filteredAbsentees = computed(() => {
  const needle = absenteeSearch.value.trim().toLowerCase()
  if (needle.length === 0) return absentees.rows.value
  return absentees.rows.value.filter((row) => row.studentName.toLowerCase().includes(needle))
})

/**
 * Savollar bo'limiga o'tish. `peerId` bo'yicha to'g'ridan-to'g'ri suhbat
 * ochish MARSHRUTI yo'q (tab ichidagi holat), shuning uchun bo'lim ochiladi
 * va kurator o'quvchini ro'yxatdan tanlaydi.
 */
function openInbox(): void {
  void router.push({ name: 'teacher-chat' })
}

/* --------------------------------------------------- nazoratdagi guruhlar */

const groupSearch = ref('')

const filteredGroups = computed(() => {
  const needle = groupSearch.value.trim().toLowerCase()
  if (needle.length === 0) return groups.value
  return groups.value.filter((group) => groupDisplayName(group).toLowerCase().includes(needle))
})

/** Ochilgan qator — bir vaqtda bittasi (eski `viewSubs` naqshi). */
const openGroupId = ref<number | null>(null)

function toggle(groupId: number): void {
  openGroupId.value = openGroupId.value === groupId ? null : groupId
}

function openGroup(groupId: number): void {
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}

/** Kurator kim: bevosita biriktirilgan yoki bog'langan kurator guruhi orqali. */
function curatorLabel(group: GroupDto): string {
  if (group.assistantName !== null && group.assistantName.length > 0) return group.assistantName
  if (group.curatorGroupName !== null && group.curatorGroupName.length > 0) {
    return `${group.curatorGroupName} guruhi orqali`
  }
  return 'Biriktirilmagan'
}
</script>

<template>
  <div>
    <!-- ============================ Ko'rsatkichlar =========================== -->
    <div class="mb-5 grid gap-3 sm:grid-cols-3">
      <div class="rounded-xl border border-line border-l-[3px] border-l-brand-500 bg-ink-900 p-3.5">
        <p class="text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
          Nazoratdagi talabalar
        </p>
        <p
          class="mt-1 text-[22px] font-bold tabular-nums text-slate-100"
          v-text="stats.students"
        />
      </div>
      <div class="rounded-xl border border-line border-l-[3px] border-l-rose-500 bg-ink-900 p-3.5">
        <p class="text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
          Sababsiz qoldirganlar
        </p>
        <p
          class="mt-1 text-[22px] font-bold tabular-nums text-rose-400"
          v-text="stats.unexplained"
        />
      </div>
      <button
        type="button"
        class="rounded-xl border border-line border-l-[3px] border-l-green-500 bg-ink-900 p-3.5 text-left transition-colors hover:bg-ink-850"
        @click="openInbox"
      >
        <p class="text-[11px] font-semibold uppercase tracking-[0.5px] text-slate-400">
          Javob kutayotgan savollar
        </p>
        <p
          class="mt-1 text-[22px] font-bold tabular-nums text-green-400"
          v-text="stats.waiting"
        />
      </button>
    </div>

    <!-- ==================== Oxirgi darsni qoldirganlar ====================== -->
    <BaseCard
      class="mb-4"
      flush
      title="Oxirgi darsni qoldirgan o‘quvchilar"
      subtitle="Har guruhning oxirgi yakunlangan darsi bo‘yicha."
    >
      <template #actions>
        <div class="relative w-full sm:w-56">
          <label
            class="sr-only"
            for="absentee-search"
          >
            Ism bo‘yicha qidiruv
          </label>
          <AppIcon
            class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
            name="search"
            :size="15"
          />
          <input
            id="absentee-search"
            v-model="absenteeSearch"
            class="zn-input pl-9 text-[13px]"
            type="search"
            placeholder="Ism bo‘yicha qidiruv..."
          >
        </div>
      </template>

      <div class="p-3.5 sm:p-5">
        <!--
          Yetishmayotgan amallar OSHKORA aytiladi: eski ilovada kurator
          shu jadvaldan Telegram taklifnomasi yuborardi va qayd yozardi.
          Tugmalarning jimgina yo'qolishi "ilova buzildi" degan xulosaga
          olib kelardi.
        -->
        <p
          class="mb-3.5 flex items-start gap-2 rounded-lg border border-amber-500/30 bg-amber-500/10 p-3 text-xs leading-relaxed text-amber-200"
        >
          <AppIcon
            class="mt-0.5 shrink-0"
            name="alert"
            :size="15"
          />
          <span>
            Telegram orqali yordamchi darsga taklifnoma yuborish va o‘quvchi
            haqida qayd qoldirish hozircha ishlamaydi — serverda bu ikki
            endpoint yozilmagan. Bog‘lanish uchun telefon va shaxsiy yozishma
            ishlatiladi.
          </span>
        </p>

        <p
          v-if="absentees.failedGroups.value.length > 0"
          class="mb-3 rounded-lg border border-line bg-ink-950 p-3 text-xs text-slate-400"
        >
          Ba’zi guruhlar ma’lumoti o‘qilmadi:
          {{ absentees.failedGroups.value.join(', ') }}.
        </p>

        <DataStatus
          :pending="groupsQuery.isPending.value || absentees.pending.value"
          :error="groupsError ?? absentees.errorMessage.value"
          :empty="filteredAbsentees.length === 0"
          :retrying="absentees.fetching.value"
          :skeleton-rows="2"
          empty-icon="user-check"
          empty-title="Nazoratdagi dars qoldirgan o‘quvchilar topilmadi."
          empty-text="Guruhlaringizda yakunlangan dars bo‘lgach, unga kelmaganlar shu yerda ko‘rinadi."
          @retry="absentees.refetch()"
        >
          <ul class="space-y-2">
            <li
              v-for="row in filteredAbsentees"
              :key="row.key"
              class="flex flex-wrap items-center gap-3 rounded-lg border border-line bg-ink-950 p-3"
            >
              <div class="min-w-0 flex-1">
                <b
                  class="block truncate text-[13.5px] text-slate-100"
                  v-text="row.studentName"
                />
                <span
                  v-if="row.phone !== null"
                  class="text-[11px] text-slate-400"
                  v-text="row.phone"
                />
              </div>

              <div class="min-w-0">
                <BaseBadge tone="teacher">
                  {{ row.groupName }}
                </BaseBadge>
                <p class="mt-[3px] text-[11px] tabular-nums text-slate-400">
                  {{ formatDateTime(row.sessionStart) }}
                </p>
              </div>

              <div class="min-w-0 flex-1">
                <span
                  v-if="row.reason !== null"
                  class="text-[12.5px] leading-snug text-slate-300"
                  v-text="row.reason"
                />
                <BaseBadge
                  v-else
                  tone="danger"
                >
                  Sababsiz
                </BaseBadge>
                <p class="mt-[3px] text-[11px] text-dim">
                  {{ attendanceStatusLabel(row.status) }}
                </p>
              </div>

              <div class="flex shrink-0 items-center gap-1.5">
                <!-- Yashil = qo'ng'iroq: eski jadvaldagi asosiy kurator amali. -->
                <a
                  v-if="row.phone !== null && row.phone.length > 0"
                  class="tap-target inline-flex items-center justify-center rounded-[9px] border border-transparent bg-green-500/15 px-2.5 text-green-400 transition-colors hover:border-green-500"
                  :href="`tel:${row.phone}`"
                  :title="`Qo‘ng‘iroq: ${row.phone}`"
                >
                  <AppIcon
                    name="phone"
                    :size="15"
                  />
                </a>
                <BaseButton
                  size="sm"
                  variant="secondary"
                  @click="openInbox"
                >
                  Yozish
                </BaseButton>
              </div>
            </li>
          </ul>
        </DataStatus>
      </div>
    </BaseCard>

    <!-- ========================= Nazoratdagi guruhlar ======================== -->
    <BaseCard
      flush
      title="Nazoratdagi guruhlar"
      subtitle="Guruh, uning ustozi va biriktirilgan kuratori."
    >
      <template #actions>
        <div class="relative w-full sm:w-56">
          <label
            class="sr-only"
            for="curator-group-search"
          >
            Guruh nomi bo‘yicha qidirish
          </label>
          <AppIcon
            class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
            name="search"
            :size="15"
          />
          <input
            id="curator-group-search"
            v-model="groupSearch"
            class="zn-input pl-9 text-[13px]"
            type="search"
            placeholder="Guruh nomi bo‘yicha..."
          >
        </div>
      </template>

      <div class="p-3.5 sm:p-5">
        <DataStatus
          :pending="groupsQuery.isPending.value"
          :error="groupsError"
          :empty="filteredGroups.length === 0"
          :retrying="groupsQuery.isFetching.value"
          :skeleton-rows="3"
          empty-icon="users"
          empty-title="Nazoratdagi guruh topilmadi."
          @retry="groupsQuery.refetch()"
        >
          <ul class="space-y-2">
            <li
              v-for="group in filteredGroups"
              :key="group.id"
              class="rounded-lg border border-line bg-ink-950"
            >
              <div class="flex flex-wrap items-center gap-2.5 p-3">
                <div class="min-w-0 flex-1">
                  <div class="flex flex-wrap items-center gap-2">
                    <b
                      class="min-w-0 truncate text-sm text-slate-100"
                      v-text="groupDisplayName(group)"
                    />
                    <BaseBadge :tone="groupTypeTone(group.type)">
                      {{ groupTypeLabel(group.type) }}
                    </BaseBadge>
                    <BaseBadge
                      v-if="!group.isActive"
                      tone="neutral"
                    >
                      Arxiv
                    </BaseBadge>
                  </div>
                  <p class="mt-1 text-[11px] text-slate-400">
                    Ustoz: {{ group.teacherName ?? '—' }} · Kurator: {{ curatorLabel(group) }}
                  </p>
                  <p class="text-[11px] tabular-nums text-dim">
                    {{ groupScheduleSummary(group) }} · {{ group.memberCount }} o‘quvchi
                  </p>
                </div>

                <div class="flex shrink-0 items-center gap-1.5">
                  <button
                    type="button"
                    class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2.5 text-xs font-semibold text-slate-300 transition-colors hover:bg-ink-800"
                    :aria-expanded="openGroupId === group.id"
                    @click="toggle(group.id)"
                  >
                    O‘quvchilar
                    <AppIcon
                      :name="openGroupId === group.id ? 'chevron-down' : 'chevron-right'"
                      :size="14"
                    />
                  </button>
                  <button
                    type="button"
                    class="inline-flex min-h-11 items-center gap-1 rounded-lg px-2.5 text-xs font-semibold text-brand-500 transition-colors hover:bg-brand-500/10"
                    @click="openGroup(group.id)"
                  >
                    Ochish
                    <AppIcon
                      name="chevron-right"
                      :size="14"
                    />
                  </button>
                </div>
              </div>

              <div
                v-if="openGroupId === group.id"
                class="border-t border-line p-3"
              >
                <CuratorGroupMembers :group-id="group.id" />
              </div>
            </li>
          </ul>
        </DataStatus>
      </div>
    </BaseCard>
  </div>
</template>
