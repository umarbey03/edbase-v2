<script setup lang="ts">
import { computed } from 'vue'

import { groupTypeLabel } from '@/entities/group'
import { formatDateWithYear } from '@/shared/lib/datetime'
import type { ProfileTaughtGroupDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard } from '@/shared/ui'

/**
 * USTOZ/KURATOR O'QITADIGAN GURUHLAR (2026-09-09).
 *
 * O'quvchi profilidagi «Guruhlar» bo'limidan FARQI: u yerda A'ZOLIK tarixi
 * (qo'shilgan, chiqarilgan, pauza), bu yerda esa XODIMNING YUKI — nechta
 * guruh, ularda nechta faol o'quvchi, ustozmi yoki kuratormi. Arxiv
 * guruhlar pastda, xira.
 */
const props = defineProps<{ groups: ProfileTaughtGroupDto[] }>()

const emit = defineEmits<{ open: [groupId: number] }>()

const activeGroups = computed(() => props.groups.filter((group) => group.isActive))
const studentTotal = computed(() =>
  activeGroups.value.reduce((sum, group) => sum + group.activeStudentCount, 0),
)

function roleInGroupLabel(role: ProfileTaughtGroupDto['roleInGroup']): string {
  return role === 'Assistant' ? 'Kurator' : 'Ustoz'
}
</script>

<template>
  <BaseCard title="O‘qitadigan guruhlar">
    <template #actions>
      <span
        v-if="props.groups.length > 0"
        class="text-xs text-slate-400"
      >
        {{ activeGroups.length }} faol · {{ studentTotal }} o‘quvchi
      </span>
    </template>

    <p
      v-if="props.groups.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hozircha birorta guruhga biriktirilmagan. Guruh kartasida ustoz yoki
      kurator sifatida tanlang.
    </p>

    <ul
      v-else
      class="divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="group in props.groups"
        :key="group.groupId"
        class="p-3"
        :class="group.isActive ? '' : 'opacity-60'"
      >
        <div class="flex flex-wrap items-center gap-x-2 gap-y-1.5">
          <button
            type="button"
            class="tap-target inline-flex min-w-0 items-center gap-1.5 rounded-lg text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
            @click="emit('open', group.groupId)"
          >
            <span
              class="truncate"
              v-text="group.groupName"
            />
            <AppIcon
              name="chevron-right"
              :size="14"
            />
          </button>

          <BaseBadge :tone="group.roleInGroup === 'Assistant' ? 'accent' : 'success'">
            {{ roleInGroupLabel(group.roleInGroup) }}
          </BaseBadge>
          <BaseBadge tone="neutral">
            {{ groupTypeLabel(group.type) }}
          </BaseBadge>
          <BaseBadge
            v-if="!group.isActive"
            tone="neutral"
          >
            Arxiv
          </BaseBadge>
        </div>

        <p class="mt-1 text-xs text-slate-400">
          {{ group.activeStudentCount }} faol o‘quvchi
          <span v-if="group.courseName !== null">
            · {{ group.courseName }}
          </span>
          · boshlangan {{ formatDateWithYear(group.startDate) }}
        </p>
      </li>
    </ul>
  </BaseCard>
</template>
