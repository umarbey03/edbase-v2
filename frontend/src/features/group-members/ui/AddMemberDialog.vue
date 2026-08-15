<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { addMember } from '@/entities/group'
import { fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import { formatPhone } from '@/shared/lib/phone'
import { useConfirm } from '@/shared/lib/useConfirm'
// ⚠️ `UserDetailsDto` — `fetchUsers` AYNAN shuni qaytaradi. `UserDto` (auth
//    shakli) BOSHQA tur: u kirgan foydalanuvchining O'ZI uchun va uning
//    maydonlari `null` bo'lmaydi.
import type { GroupMemberDto, UserDetailsDto } from '@/shared/types'
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

const confirm = useConfirm()

/**
 * R4 — GURUHGA QO'SHISH TASDIQLANADI, `warning` TONIDA.
 *
 * ★ NEGA KERAK: qidiruv natijalari — zich qatorlar ro'yxati va ularning
 * har birida bir xil "Qo'shish" tugmasi. Telefonda qatorlar bir-biriga
 * yaqin turadi, ismlar esa ko'pincha o'xshash (bitta familiya, bitta
 * ism) — ya'ni bu ekranda xato AYNAN "boshqa odam qo'shildi" ko'rinishida
 * bo'ladi va oyna darhol yopiladi, ya'ni xato SEZILMAY qoladi.
 *
 * ★ NEGA `danger` EMAS: yozuv o'chmaydi va qo'shilgan o'quvchini
 * `GroupMembersPanel` dan chiqarish mumkin. Lekin bu bepul emas —
 * chiqarilgan a'zo TARIX bo'lib qoladi ("Chiqarilgan" holati), ya'ni
 * xato bosish guruh ro'yxatida ko'rinadigan iz qoldiradi.
 */
async function askAdd(student: UserDetailsDto): Promise<void> {
  if (addMutation.isPending.value) return

  const ok = await confirm({
    title: 'Guruhga qo‘shish',
    // `fullName` tipda `null` bo'lishi mumkin — shablondagi bilan AYNI
    // zaxira, aks holda oynada "null guruhga a'zo qilinadi" chiqardi.
    message: `${student.fullName ?? 'O‘quvchi'} guruhga a’zo qilinadi.`,
    confirmLabel: 'Qo‘shish',
    tone: 'warning',
    details: [
      'O‘quvchi keyingi darslar davomatiga va guruh chatiga qo‘shiladi.',
      'To‘lov hisobi shu guruh tarifi bo‘yicha yuritila boshlaydi.',
      'Xato qo‘shilsa uni chiqarish mumkin, lekin yozuv “Chiqarilgan” holatida ro‘yxatda qoladi.',
    ],
  })
  if (!ok) return

  addMutation.mutate(student.id)
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
                v-text="student.email ?? (formatPhone(student.phone) || '—')"
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
              @click="askAdd(student)"
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
