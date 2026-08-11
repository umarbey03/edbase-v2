<script setup lang="ts">
import { useMutation } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  createStudentNote,
  deleteStudentNote,
  NOTE_BODY_MAX,
  updateStudentNote,
} from '@/entities/user'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { ProfileGroupDto, StudentNoteDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseButton, BaseCard, BaseField, IconButton } from '@/shared/ui'

/**
 * 5-BO'LIM: ICHKI IZOHLAR (ustoz/kurator/o'quv bo'limi yozuvlari).
 *
 * 🔴 O'QUVCHI BU BO'LIMNI KO'RMAYDI: server `Student` roliga izohlar
 * endpointidan 403 beradi va profil agregatida `notes` bloki `null` bo'ladi.
 * Shu sababli bo'lim `notes === null` bo'lganda UMUMAN render qilinmaydi
 * (shart chaqiruvchi drawer'da) — bu yerda prop majburiy.
 *
 * ★ RO'YXAT ALOHIDA SO'ROV BILAN OLINMAYDI: izohlar profil agregatidan
 * `props.notes` bo'lib keladi, mutatsiyadan keyin esa chaqiruvchi agregatni
 * qayta o'qiydi (`changed` emiti). Ikkinchi manba bo'lsa ikkisi bir-biriga
 * mos kelmagan holat paydo bo'lardi (izoh yozildi — ro'yxatda yo'q).
 *
 * ★ GURUH TANLOVI mavjud a'zoliklardan: server `groupId` ni tekshiradi va
 * begona guruh uchun **400** (`problem.errors.groupId`) beradi. Ro'yxatni
 * o'quvchining O'Z guruhlaridan olsak, bu xato umuman yuz bermaydi.
 *
 * 🔴 `canEdit` — FAQAT KO'RINISH uchun: haqiqiy tekshiruv serverda, har
 * `PUT`/`DELETE` da. Ustoz faqat O'Z izohini tahrirlaydi, o'quv bo'limi
 * hammasini (xodim ishdan ketganda izohlarini tozalash kerak bo'ladi).
 */
const props = defineProps<{
  studentId: number
  notes: StudentNoteDto[]
  /** Guruh konteksti tanlovi uchun — o'quvchining a'zoliklari. */
  groups: ProfileGroupDto[]
}>()

const emit = defineEmits<{ changed: [] }>()

const confirm = useConfirm()

/* --------------------------------------------------------- yangi izoh --- */

const draftBody = ref('')
const draftGroupId = ref<number | null>(null)
const errorMessage = ref<string | null>(null)

const draftTrimmed = computed(() => draftBody.value.trim())
const draftTooLong = computed(() => draftBody.value.length > NOTE_BODY_MAX)

const createMutation = useMutation({
  mutationFn: () =>
    createStudentNote(props.studentId, {
      body: draftTrimmed.value,
      groupId: draftGroupId.value,
    }),
  onSuccess: () => {
    draftBody.value = ''
    draftGroupId.value = null
    errorMessage.value = null
    emit('changed')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

function submitDraft(): void {
  if (draftTrimmed.value.length === 0 || draftTooLong.value) return
  if (createMutation.isPending.value) return
  errorMessage.value = null
  createMutation.mutate()
}

/* --------------------------------------------------------- tahrirlash --- */

/** Hozir tahrirlanayotgan izoh (`null` — hech biri). */
const editingId = ref<number | null>(null)
const editingBody = ref('')

const editingTrimmed = computed(() => editingBody.value.trim())
const editingTooLong = computed(() => editingBody.value.length > NOTE_BODY_MAX)

function startEdit(note: StudentNoteDto): void {
  editingId.value = note.id
  editingBody.value = note.body
  errorMessage.value = null
}

function cancelEdit(): void {
  editingId.value = null
  editingBody.value = ''
}

const updateMutation = useMutation({
  mutationFn: (noteId: number) =>
    updateStudentNote(props.studentId, noteId, { body: editingTrimmed.value }),
  onSuccess: () => {
    cancelEdit()
    errorMessage.value = null
    emit('changed')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
})

/**
 * Tasdiq `primary` tonda: bu ma'lumotni ALMASHTIRUVCHI saqlash (B2 jadvali).
 * `danger` emas — matn qaytarib bo'lmas darajada yo'qolmaydi, lekin boshqa
 * xodim o'qiydigan yozuv o'zgaradi, ya'ni "shunchaki forma" ham emas.
 */
async function saveEdit(noteId: number): Promise<void> {
  if (editingTrimmed.value.length === 0 || editingTooLong.value) return
  if (updateMutation.isPending.value) return

  const ok = await confirm({
    title: 'Izohni saqlash',
    message: 'Izoh matni yangilanadi. Muallif va guruh konteksti o‘zgarmaydi.',
    confirmLabel: 'Saqlash',
    tone: 'primary',
  })
  if (!ok) return

  updateMutation.mutate(noteId)
}

/* ----------------------------------------------------------- o'chirish --- */

const deletingId = ref<number | null>(null)

const deleteMutation = useMutation({
  mutationFn: (noteId: number) => deleteStudentNote(props.studentId, noteId),
  onSuccess: () => {
    errorMessage.value = null
    emit('changed')
  },
  onError: (error: Error) => {
    errorMessage.value = toUserMessage(error)
  },
  onSettled: () => {
    deletingId.value = null
  },
})

/** O'chirish QAYTARILMAYDI (server qattiq o'chiradi) -> `danger` tasdiq. */
async function removeNote(note: StudentNoteDto): Promise<void> {
  if (deleteMutation.isPending.value) return

  const ok = await confirm({
    title: 'Izohni o‘chirish',
    message: 'Izoh butunlay o‘chiriladi va tiklanmaydi.',
    confirmLabel: 'O‘chirish',
    tone: 'danger',
    details: [note.body.length > 120 ? `${note.body.slice(0, 120)}…` : note.body],
  })
  if (!ok) return

  deletingId.value = note.id
  deleteMutation.mutate(note.id)
}
</script>

<template>
  <BaseCard
    title="Izohlar"
    subtitle="Xodimlarning ichki yozuvlari — o‘quvchiga ko‘rsatilmaydi."
  >
    <!-- ------------------------------------------------------ yangi izoh -->
    <form
      novalidate
      @submit.prevent="submitDraft"
    >
      <BaseField
        label="Yangi izoh"
        :error="draftTooLong ? `Izoh ${NOTE_BODY_MAX} belgidan oshmasin.` : null"
        :hint="`${draftBody.length} / ${NOTE_BODY_MAX}`"
      >
        <textarea
          v-model="draftBody"
          class="zn-input"
          rows="3"
          :maxlength="NOTE_BODY_MAX"
          placeholder="Masalan: darsga kech qoldi, otasi bilan gaplashildi"
        />
      </BaseField>

      <div class="mt-2.5 flex flex-wrap items-end gap-2.5">
        <div class="min-w-48 flex-1">
          <BaseField
            label="Guruh (ixtiyoriy)"
            hint="Izoh qaysi guruhdagi xatti-harakatga tegishli."
          >
            <select
              v-model="draftGroupId"
              class="zn-input"
            >
              <option :value="null">
                Guruhsiz
              </option>
              <!--
                Faqat o'quvchining A'ZOLIKLARI: begona guruh Id'sini server
                400 bilan rad etadi (`errors.groupId`).
              -->
              <option
                v-for="group in props.groups"
                :key="group.groupId"
                :value="group.groupId"
              >
                {{ group.groupName }}
              </option>
            </select>
          </BaseField>
        </div>
        <BaseButton
          type="submit"
          :disabled="draftTrimmed.length === 0 || draftTooLong"
          :loading="createMutation.isPending.value"
        >
          <template #icon>
            <AppIcon
              name="plus"
              :size="15"
            />
          </template>
          Qo‘shish
        </BaseButton>
      </div>
    </form>

    <p
      v-if="errorMessage !== null"
      class="mt-2.5 text-xs text-rose-400"
      role="alert"
      v-text="errorMessage"
    />

    <!-- --------------------------------------------------------- ro'yxat -->
    <p
      v-if="props.notes.length === 0"
      class="mt-3.5 rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hali izoh yozilmagan.
    </p>

    <ul
      v-else
      class="mt-3.5 divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="note in props.notes"
        :key="note.id"
        class="p-3"
      >
        <div class="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span
            class="text-xs font-semibold text-slate-200"
            v-text="note.authorName"
          />
          <span class="text-[11px] text-slate-400">
            {{ formatDateTime(note.createdAt) }}
            <template v-if="note.updatedAt !== null"> · tahrirlangan</template>
          </span>
          <BaseBadge
            v-if="note.groupName !== null"
            tone="neutral"
          >
            {{ note.groupName }}
          </BaseBadge>
          <span class="flex-1" />
          <!--
            🔴 `gap-3` — `IconButton` ning ko'rinmas teginish maydoni har
            tomondan 6px kengayadi (24-tuzoq): kichikroq oraliqda "Tahrirlash"
            o'rniga "O'chirish" bosilib ketardi.
          -->
          <div
            v-if="note.canEdit"
            class="flex shrink-0 items-center gap-3"
          >
            <IconButton
              icon="edit"
              label="Izohni tahrirlash"
              size="sm"
              :disabled="editingId === note.id"
              @click="startEdit(note)"
            />
            <IconButton
              icon="trash"
              label="Izohni o‘chirish"
              tone="danger"
              size="sm"
              :loading="deleteMutation.isPending.value && deletingId === note.id"
              @click="removeNote(note)"
            />
          </div>
        </div>

        <!-- Tahrirlash AYNI QATORDA: alohida oyna drawer ustida uchinchi
             qatlam bo'lardi va matn konteksti ko'rinmasdi. -->
        <template v-if="editingId === note.id">
          <textarea
            v-model="editingBody"
            class="zn-input mt-2"
            rows="3"
            :maxlength="NOTE_BODY_MAX"
          />
          <p
            v-if="editingTooLong"
            class="mt-1 text-[11px] text-rose-400"
          >
            Izoh {{ NOTE_BODY_MAX }} belgidan oshmasin.
          </p>
          <div class="mt-2 flex flex-wrap gap-2">
            <BaseButton
              size="sm"
              :disabled="editingTrimmed.length === 0 || editingTooLong"
              :loading="updateMutation.isPending.value"
              @click="saveEdit(note.id)"
            >
              Saqlash
            </BaseButton>
            <BaseButton
              size="sm"
              variant="secondary"
              @click="cancelEdit"
            >
              Bekor qilish
            </BaseButton>
          </div>
        </template>

        <p
          v-else
          class="mt-1 whitespace-pre-line break-words text-sm leading-relaxed text-slate-100"
          v-text="note.body"
        />
      </li>
    </ul>
  </BaseCard>
</template>
