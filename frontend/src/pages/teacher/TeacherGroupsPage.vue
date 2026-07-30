<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { fetchGroups, groupDisplayName } from '@/entities/group'
import GroupCard from '@/entities/group/ui/GroupCard.vue'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { AppIcon, DataStatus, PageHeader, TodayPill } from '@/shared/ui'

/**
 * Ustoz/kurator guruhlari — eski `teacher.html` dagi `#groups`.
 *
 * Ro'yxatni backend rolga qarab cheklaydi (ustoz -> `TeacherId`, kurator ->
 * `AssistantId` yoki kurator guruhi), shuning uchun bu yerda qo'shimcha
 * ruxsat filtri YO'Q. Jadval o'rniga kartochka setkasi: kartochka telefonda
 * ham, desktopda ham bir xil o'qiladi.
 *
 * Eski ekrandan ko'chirilgan uch element: "Bugun" tabletkasi, "Jami: N ta
 * guruh" hisoblagichi va nom bo'yicha qidiruv. Qidiruv KLIENTDA — eski
 * `filterGroups()` ham shunday edi; serverdagi `Search` esa kamida 2 belgi
 * talab qiladi va bitta harf yozilganda ro'yxat "sakrab" ketardi.
 */
const router = useRouter()
const auth = useAuthStore()

const groupsQuery = useQuery({
  queryKey: ['groups', 'mine'],
  queryFn: ({ signal }) => fetchGroups({ page: 1, pageSize: 50 }, { signal }),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const search = ref('')

const visibleGroups = computed(() => {
  const needle = search.value.trim().toLowerCase()
  if (needle.length === 0) return groups.value
  return groups.value.filter((group) => groupDisplayName(group).toLowerCase().includes(needle))
})

const errorMessage = computed(() =>
  groupsQuery.error.value !== null ? toUserMessage(groupsQuery.error.value) : null,
)

function openGroup(groupId: number): void {
  void router.push({ name: 'teacher-group', params: { groupId: String(groupId) } })
}
</script>

<template>
  <div>
    <!--
      Sarlavha ROLGA qarab o'zgaradi, chunki yon menyudagi band ham shunday
      nomlanadi ("Kurator guruhlari") — menyu bir nom, sahifa boshqa nom
      ko'rsatsa foydalanuvchi qayerdaligini yo'qotadi.
    -->
    <PageHeader
      :title="auth.role === 'Assistant' ? 'Kurator guruhlari' : 'Guruhlarim'"
      subtitle="Sizga biriktirilgan guruhlar"
    />
    <TodayPill />

    <div class="mb-3.5 flex flex-wrap items-center gap-3">
      <span
        class="rounded-[20px] border border-brand-500/20 bg-brand-500/14 px-3 py-1 text-[13px] font-semibold text-brand-500"
      >
        Jami: {{ groups.length }} ta guruh
      </span>
      <div class="relative min-w-[180px] flex-1 sm:max-w-xs">
        <label
          class="sr-only"
          for="group-search"
        >
          Guruh nomini qidirish
        </label>
        <AppIcon
          class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
          name="search"
          :size="15"
        />
        <input
          id="group-search"
          v-model="search"
          class="zn-input pl-9 text-[13px]"
          type="search"
          placeholder="Guruh nomini qidiring…"
        >
      </div>
    </div>

    <DataStatus
      :pending="groupsQuery.isPending.value"
      :error="errorMessage"
      :empty="visibleGroups.length === 0"
      :retrying="groupsQuery.isFetching.value"
      empty-icon="users"
      :empty-title="groups.length === 0 ? 'Guruh biriktirilmagan' : 'Guruh topilmadi.'"
      :empty-text="
        groups.length === 0
          ? 'O‘quv bo‘limi sizni guruhga biriktirgach shu yerda ko‘rinadi.'
          : ''
      "
      @retry="groupsQuery.refetch()"
    >
      <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
        <GroupCard
          v-for="group in visibleGroups"
          :key="group.id"
          :group="group"
          @open="openGroup"
        />
      </div>
    </DataStatus>
  </div>
</template>
