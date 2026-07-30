<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { addMember } from '@/entities/group'
import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import type { GroupMemberDto } from '@/shared/types'
import { BaseBadge, BaseButton, BaseField, BaseModal, DataStatus } from '@/shared/ui'

/**
 * Guruhga o'quvchi qo'shish.
 *
 * NEGA QIDIRUV, "hammasi" RO'YXATI EMAS: bazada 1500+ foydalanuvchi bor —
 * ularni bitta `select` ga solish telefonda ochilmaydigan ro'yxat beradi.
 * Qidiruv SERVERDA (`role=Student`), ya'ni ro'yxat hech qachon to'liq
 * yuklanmaydi.
 */
const props = defineProps<{
  open: boolean
  groupId: number
  /** Allaqachon a'zo bo'lganlar — takror qo'shishga urinmaslik uchun. */
  existingStudentIds: number[]
}>()

const emit = defineEmits<{ close: []; saved: [] }>()

const search = ref('')
const debouncedSearch = useDebounced(search)
const errorMessage = ref<string | null>(null)

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    search.value = ''
    errorMessage.value = null
  },
)

const searchTerm = computed(() => debouncedSearch.value.trim())
const searchTooShort = computed(
  () => searchTerm.value.length > 0 && searchTerm.value.length < USER_SEARCH_MIN,
)
const effectiveSearch = computed(() =>
  searchTerm.value.length >= USER_SEARCH_MIN ? searchTerm.value : undefined,
)

const studentsQuery = useQuery({
  queryKey: ['users', 'students', 'picker', effectiveSearch],
  queryFn: ({ signal }) =>
    fetchUsers({ role: 'Student', isActive: true, search: effectiveSearch.value, pageSize: 25 }, { signal }),
  enabled: computed(() => props.open),
})

const students = computed(() => studentsQuery.data.value?.items ?? [])

const listError = computed(() =>
  studentsQuery.error.value !== null ? toUserMessage(studentsQuery.error.value) : null,
)

const addMutation = useMutation({
  mutationFn: (studentId: number) => addMember(props.groupId, { studentId }),
  onSuccess: (_member: GroupMemberDto) => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    // 409: allaqachon a'zo yoki holat mos emas — sabab serverdan keladi.
    errorMessage.value = toUserMessage(error)
  },
})

function isMember(studentId: number): boolean {
  return props.existingStudentIds.includes(studentId)
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    title="Guruhga o‘quvchi qo‘shish"
    @close="emit('close')"
  >
    <BaseField
      label="O‘quvchini qidirish"
      :hint="searchTooShort ? `Kamida ${USER_SEARCH_MIN} belgi kiriting.` : 'Ism, email yoki telefon'"
    >
      <input
        v-model="search"
        class="zn-input"
        placeholder="Ism yoki email"
      >
    </BaseField>

    <div class="mt-3">
      <DataStatus
        :pending="studentsQuery.isPending.value"
        :error="listError"
        :empty="students.length === 0"
        :retrying="studentsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="users"
        empty-title="O‘quvchi topilmadi"
        empty-text="Qidiruvni o‘zgartiring yoki avval foydalanuvchi yarating."
        @retry="studentsQuery.refetch()"
      >
        <ul class="max-h-80 space-y-2 overflow-y-auto scrollbar-slim">
          <li
            v-for="student in students"
            :key="student.id"
            class="flex items-center justify-between gap-2 rounded-lg border border-line bg-ink-950 p-3"
          >
            <div class="min-w-0">
              <p
                class="truncate text-sm font-medium text-slate-100"
                v-text="student.fullName ?? '—'"
              />
              <p
                class="truncate text-xs text-slate-400"
                v-text="student.email ?? student.phone ?? '—'"
              />
            </div>

            <BaseBadge
              v-if="isMember(student.id)"
              tone="success"
            >
              A‘zo
            </BaseBadge>
            <BaseButton
              v-else
              size="sm"
              :loading="addMutation.isPending.value && addMutation.variables.value === student.id"
              :disabled="addMutation.isPending.value"
              @click="addMutation.mutate(student.id)"
            >
              Qo‘shish
            </BaseButton>
          </li>
        </ul>
      </DataStatus>
    </div>

    <p
      v-if="errorMessage !== null"
      class="mt-3 text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <template #footer>
      <BaseButton
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>
