<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { addMember } from '@/entities/group'
import { createUser, fetchUsers, USER_SEARCH_MIN } from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { useDebounced } from '@/shared/lib/debounce'
import {
  formatPhone,
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { useConfirm } from '@/shared/lib/useConfirm'
// ⚠️ `UserDetailsDto` — `fetchUsers` AYNAN shuni qaytaradi. `UserDto` (auth
//    shakli) BOSHQA tur: u kirgan foydalanuvchining O'ZI uchun va uning
//    maydonlari `null` bo'lmaydi.
import type { GroupMemberDto, UserDetailsDto } from '@/shared/types'
import { BaseBadge, BaseButton, BaseField, BaseModal, DataStatus } from '@/shared/ui'

/**
 * Guruhga o'quvchi qo'shish — IKKI REJIM:
 *
 *   1) «Bazadan tanlash» — mavjud o'quvchini qidirib qo'shish.
 *   2) «Yangi o'quvchi» — o'quvchini SHU OYNADA yaratib, darhol qo'shish
 *      (2026-09-09, loyiha egasining talabi). Ilgari xodim avval
 *      «Foydalanuvchilar» sahifasiga borib yaratar, keyin guruhga qaytib
 *      qidirib qo'shardi — yangi o'quvchi bilan gaplashib turgan xodim
 *      uchun bu uch ekranlik yo'l edi.
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

type Mode = 'existing' | 'new'

const MODES: ReadonlyArray<{ value: Mode; label: string }> = [
  { value: 'existing', label: 'Bazadan tanlash' },
  { value: 'new', label: 'Yangi o‘quvchi' },
]

const mode = ref<Mode>('existing')
const search = ref('')
const debouncedSearch = useDebounced(search)
const errorMessage = ref<string | null>(null)

/* ------------------------------------------------- yangi o'quvchi formasi */

const fullName = ref('')
const email = ref('')
const phone = ref('')

watch(
  () => props.open,
  (isOpen) => {
    if (!isOpen) return
    mode.value = 'existing'
    search.value = ''
    fullName.value = ''
    email.value = ''
    phone.value = ''
    errorMessage.value = null
  },
)

// Rejim almashganda eski xato boshqa rejimga tegishli bo'lib qoladi —
// «Yangi o'quvchi» formasi ostida qidiruv xatosi turmasin.
watch(mode, () => {
  errorMessage.value = null
})

/* ------------------------------------------------------- mavjudni qidirish */

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
  enabled: computed(() => props.open && mode.value === 'existing'),
})

const students = computed(() => studentsQuery.data.value?.items ?? [])

const listError = computed(() =>
  studentsQuery.error.value !== null ? toUserMessage(studentsQuery.error.value) : null,
)

const queryClient = useQueryClient()

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

const ADD_DETAILS = [
  'O‘quvchi keyingi darslar davomatiga va guruh chatiga qo‘shiladi.',
  'To‘lov hisobi shu guruh tarifi bo‘yicha yuritila boshlaydi.',
  'Xato qo‘shilsa uni chiqarish mumkin, lekin yozuv “Chiqarilgan” holatida ro‘yxatda qoladi.',
]

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
    details: ADD_DETAILS,
  })
  if (!ok) return

  addMutation.mutate(student.id)
}

/**
 * Qidiruvda hech kim topilmadi — «Yangi o'quvchi» rejimiga o'tish.
 * Yozilgan matn ISM bo'lsa formaga ko'chadi (email/telefon bo'lsa o'z
 * maydoniga), xodim uni qayta termaydi.
 */
function switchToNewFromSearch(): void {
  const term = search.value.trim()
  if (term.includes('@')) email.value = term
  else if (/^[+\d\s()-]+$/.test(term) && term.length > 0) phone.value = term
  else fullName.value = term
  mode.value = 'new'
}

/* ------------------------------------------------- yaratish va qo'shish */

/**
 * Serverga boradigan telefon — maydondagi bo'shliqlarsiz, bo'sh bo'lsa
 * `null` (`UserFormDialog` bilan AYNI qoida: bo'sh satr emas).
 */
const phonePayload = computed<string | null>(() => {
  const value = stripPhoneFormatting(phone.value)
  return value.length > 0 ? value : null
})

/**
 * Yaratish + qo'shish — IKKI so'rov, bitta amal.
 *
 * ★ Ikkinchi so'rov yiqilsa (masalan, tarmoq uzildi) foydalanuvchi
 * ALLAQACHON yaratilgan. Bu holda xatoni "yaratildi, lekin qo'shilmadi"
 * deb aniq aytamiz va «Bazadan tanlash» rejimiga o'tib, qidiruvga
 * email'ni qo'yamiz — xodim uni bir bosishda topib qo'shadi. Aks holda u
 * formani qayta yuborib, "email band" xatosiga duch kelardi va nima
 * bo'lganini tushunmasdi.
 */
/** Foydalanuvchi yaratildi, lekin ikkinchi so'rov (guruhga qo'shish) yiqildi. */
class CreatedButNotAddedError extends Error {
  constructor(
    readonly user: UserDetailsDto,
    readonly inner: unknown,
  ) {
    super('created-but-not-added')
  }
}

const createAndAddMutation = useMutation({
  mutationFn: async () => {
    const created = await createUser({
      fullName: fullName.value.trim(),
      email: email.value.trim(),
      role: 'Student',
      phone: phonePayload.value,
      isActive: true,
    })
    // Foydalanuvchilar ro'yxati va shu oynadagi qidiruv keshi eskirdi.
    void queryClient.invalidateQueries({ queryKey: ['users'] })

    try {
      return await addMember(props.groupId, { studentId: created.user.id })
    } catch (error) {
      throw new CreatedButNotAddedError(created.user, error)
    }
  },
  onSuccess: () => {
    emit('saved')
    emit('close')
  },
  onError: (error: Error) => {
    if (error instanceof CreatedButNotAddedError) {
      errorMessage.value =
        `${error.user.fullName ?? 'O‘quvchi'} yaratildi, lekin guruhga qo‘shilmadi: `
        + `${toUserMessage(error.inner)} Quyidagi ro‘yxatdan qayta qo‘shing.`
      search.value = error.user.email ?? error.user.fullName ?? ''
      mode.value = 'existing'
      return
    }
    errorMessage.value = toUserMessage(error)
  },
})

const canCreate = computed(
  () => fullName.value.trim().length > 0
    && email.value.trim().length > 0
    && !createAndAddMutation.isPending.value,
)

async function askCreateAndAdd(): Promise<void> {
  if (!canCreate.value) return

  const name = fullName.value.trim()
  const ok = await confirm({
    title: 'Yaratish va guruhga qo‘shish',
    message: `${name} yangi o‘quvchi sifatida yaratiladi va guruhga a’zo qilinadi.`,
    confirmLabel: 'Yaratish va qo‘shish',
    tone: 'warning',
    details: [
      'O‘quvchi tizimga botga telefon raqamini ulab, kod bilan kiradi.',
      ...ADD_DETAILS,
    ],
  })
  if (!ok) return

  errorMessage.value = null
  createAndAddMutation.mutate()
}
</script>

<template>
  <BaseModal
    :open="props.open"
    wide
    title="Guruhga o‘quvchi qo‘shish"
    @close="emit('close')"
  >
    <!--
      Rejim tanlagichi — ilovadagi boshqa segmentli tugmalar bilan bir
      xil naqshda (`GradesTab` dagi ko'rinish tanlagichi). 44px: barmoq.
    -->
    <div
      class="mb-3.5 inline-flex rounded-lg border border-line bg-ink-950 p-0.5"
      role="tablist"
      aria-label="O‘quvchi qo‘shish usuli"
    >
      <button
        v-for="item in MODES"
        :key="item.value"
        type="button"
        role="tab"
        class="min-h-11 rounded-md px-4 text-xs font-semibold transition-colors"
        :class="
          mode === item.value
            ? 'bg-brand-500 text-on-brand'
            : 'text-slate-300 hover:bg-ink-900'
        "
        :aria-selected="mode === item.value"
        @click="mode = item.value"
        v-text="item.label"
      />
    </div>

    <!-- ============================================== bazadan tanlash -->
    <template v-if="mode === 'existing'">
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
          empty-text="Qidiruvni o‘zgartiring yoki shu yerning o‘zida yangi o‘quvchi yarating."
          @retry="studentsQuery.refetch()"
        >
          <template #empty-action>
            <BaseButton
              size="sm"
              variant="secondary"
              @click="switchToNewFromSearch"
            >
              Yangi o‘quvchi yaratish
            </BaseButton>
          </template>

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
    </template>

    <!-- ============================================== yangi o'quvchi -->
    <form
      v-else
      novalidate
      @submit.prevent="askCreateAndAdd"
    >
      <BaseField label="To‘liq ism">
        <input
          v-model="fullName"
          class="zn-input"
          autocomplete="name"
          required
        >
      </BaseField>

      <div class="mt-3 grid gap-3 sm:grid-cols-2">
        <BaseField label="Elektron pochta">
          <input
            v-model="email"
            class="zn-input"
            type="email"
            autocomplete="email"
            required
          >
        </BaseField>

        <!--
          Telefon — o'quvchi uchun ixtiyoriy (server ham talab qilmaydi),
          lekin usiz u tizimga KIRA OLMAYDI: kirish faqat telefon orqali.
          ★ `:value` + `@input`, `v-model` EMAS — kursor har bosishda satr
          oxiriga sakramasin (sabab `maskPhoneField` izohida).
        -->
        <BaseField
          label="Telefon"
          hint="Kirish kodi shu raqamga ulangan Telegram hisobiga yuboriladi."
        >
          <input
            :value="phone"
            class="zn-input"
            type="tel"
            inputmode="tel"
            :maxlength="PHONE_INPUT_MAXLENGTH"
            placeholder="+998 90 123 45 67"
            @input="phone = maskPhoneField($event.target as HTMLInputElement)"
          >
        </BaseField>
      </div>

      <p class="mt-3 text-xs text-slate-400">
        O‘quvchi «O‘quvchi» roli bilan faol holatda yaratiladi va darhol shu
        guruhga qo‘shiladi.
      </p>
    </form>

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
        {{ mode === 'new' ? 'Bekor qilish' : 'Yopish' }}
      </BaseButton>
      <BaseButton
        v-if="mode === 'new'"
        :disabled="!canCreate"
        :loading="createAndAddMutation.isPending.value"
        @click="askCreateAndAdd"
      >
        Yaratish va qo‘shish
      </BaseButton>
    </template>
  </BaseModal>
</template>
