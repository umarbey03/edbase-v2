<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchGroups, GROUP_SEARCH_MIN, groupDisplayName } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import type { GroupDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseField, DataStatus } from '@/shared/ui'

/**
 * Xabar nishoni — BIR NECHTA guruh tanlanadi (`AddMemberDialog` dagi
 * qidiruv+ro'yxat naqshi bilan AYNI, faqat ko'p tanlovli).
 *
 * ★ FAQAT FAOL GURUHLAR: arxivlangan guruhga xabar server 409 bilan
 * to'sadi (`GroupBroadcastService.SendAsync`) — ro'yxatda ularni umuman
 * ko'rsatmaslik xatoni OLDINDAN oldini oladi.
 */
const props = defineProps<{ modelValue: GroupDto[] }>()
const emit = defineEmits<{ 'update:modelValue': [value: GroupDto[]] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)

const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < GROUP_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= GROUP_SEARCH_MIN ? searchTerm.value : undefined,
)

const groupsQuery = useQuery({
  queryKey: ['groups', 'broadcast-picker', effectiveSearch],
  queryFn: ({ signal }) =>
    fetchGroups({ search: effectiveSearch.value, isActive: true, pageSize: 25 }, { signal }),
})

const groups = computed(() => groupsQuery.data.value?.items ?? [])

const listError = computed(() =>
  groupsQuery.error.value !== null ? toUserMessage(groupsQuery.error.value) : null,
)

function isSelected(id: number): boolean {
  return props.modelValue.some((group) => group.id === id)
}

function toggle(group: GroupDto): void {
  if (isSelected(group.id)) {
    emit('update:modelValue', props.modelValue.filter((g) => g.id !== group.id))
    return
  }
  emit('update:modelValue', [...props.modelValue, group])
}

function remove(id: number): void {
  emit('update:modelValue', props.modelValue.filter((g) => g.id !== id))
}
</script>

<template>
  <div>
    <!-- Tanlanganlar — chip qatori, ro'yxatdan TASHQARI ko'rinadi. -->
    <div
      v-if="props.modelValue.length > 0"
      class="mb-2.5 flex flex-wrap gap-1.5"
    >
      <span
        v-for="group in props.modelValue"
        :key="group.id"
        class="inline-flex items-center gap-1.5 rounded-full border border-brand-500/25 bg-brand-500/12 py-1 pl-2.5 pr-1.5 text-xs font-medium text-brand-400"
      >
        {{ groupDisplayName(group) }}
        <button
          type="button"
          class="flex size-4 items-center justify-center rounded-full text-brand-400/70 transition-colors hover:bg-brand-500/20 hover:text-brand-400"
          :aria-label="`${groupDisplayName(group)} ni olib tashlash`"
          @click="remove(group.id)"
        >
          <AppIcon
            name="close"
            :size="10"
          />
        </button>
      </span>
    </div>

    <BaseField
      label="Guruhlarni qidirish"
      :hint="searchTooShort ? `Kamida ${GROUP_SEARCH_MIN} belgi kiriting.` : 'Nom bo‘yicha qidiring va ro‘yxatdan belgilang'"
    >
      <input
        v-model="search"
        class="zn-input"
        placeholder="Masalan: ATF-1"
      >
    </BaseField>

    <div class="mt-2.5">
      <DataStatus
        :pending="groupsQuery.isPending.value"
        :error="listError"
        :empty="groups.length === 0"
        :retrying="groupsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="users"
        empty-title="Guruh topilmadi"
        @retry="groupsQuery.refetch()"
      >
        <ul class="max-h-64 space-y-1.5 overflow-y-auto scrollbar-slim">
          <li
            v-for="group in groups"
            :key="group.id"
          >
            <label
              class="flex min-h-11 cursor-pointer items-center gap-2.5 rounded-lg border border-line bg-ink-950 px-3 py-2 transition-colors hover:border-brand-500/40"
              :class="isSelected(group.id) ? 'border-brand-500/50 bg-brand-500/8' : ''"
            >
              <input
                type="checkbox"
                class="size-4 shrink-0 accent-brand-500"
                :checked="isSelected(group.id)"
                @change="toggle(group)"
              >
              <span
                class="min-w-0 flex-1 truncate text-sm text-slate-200"
                v-text="groupDisplayName(group)"
              />
              <BaseBadge tone="neutral">
                {{ group.memberCount }} o‘quvchi
              </BaseBadge>
            </label>
          </li>
        </ul>
      </DataStatus>
    </div>
  </div>
</template>
