<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  createMessageTemplate,
  deleteMessageTemplate,
  fetchMessageTemplates,
  updateMessageTemplate,
} from '@/entities/message-template'
import { toUserMessage } from '@/shared/api'
import { useConfirm } from '@/shared/lib/useConfirm'
import type { MessageTemplateDto } from '@/shared/types'
import {
  AppIcon,
  BaseBadge,
  BaseButton,
  BaseField,
  ConfirmDeleteDialog,
  DataStatus,
} from '@/shared/ui'

/**
 * ============================================================================
 *  XABAR SHABLONLARI BOSHQARUVI — "Xabarlar" panelining lug'ati (2026-08-16)
 * ============================================================================
 *
 * ★ NAQSH `GroupCategoryPanel` BILAN AYNI (o'sha komponent izohidagi barcha
 * qarorlar shu yerda ham qo'llanadi): ALWAYS-INLINE panel (drawer yo'q),
 * tahrirlash JOYIDA (alohida oyna emas), o'chirish `ConfirmDeleteDialog`
 * bilan. Farqi — bu yerda ikkinchi maydon (`Body`) matn emas, XABAR
 * MATNI, shuning uchun `textarea`.
 *
 * O'CHIRISH ESKI TARIXGA TA'SIR QILMAYDI: `GroupBroadcast.Body` snapshot
 * va `TemplateId` FK `SetNull` (`MessageTemplateService.DeleteAsync`
 * izohi) — shuning uchun bu yerda `groupCount` kabi ogohlantiruvchi son
 * kerak emas.
 */
const queryClient = useQueryClient()
const confirm = useConfirm()

/* -------------------------------------------------------------- ro'yxat */

const templatesQuery = useQuery({
  queryKey: ['message-templates', 'all'],
  queryFn: ({ signal }) => fetchMessageTemplates({}, { signal }),
})

const templates = computed<MessageTemplateDto[]>(() => templatesQuery.data.value ?? [])

const listError = computed(() =>
  templatesQuery.error.value !== null ? toUserMessage(templatesQuery.error.value) : null,
)

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: ['message-templates'] })
}

/* ------------------------------------------------------------ yaratish */

const newName = ref('')
const newBody = ref('')
const createError = ref<string | null>(null)

const createMutation = useMutation({
  mutationFn: (input: { name: string; body: string }) =>
    createMessageTemplate({ name: input.name, body: input.body, isActive: true }),
  onSuccess: () => {
    newName.value = ''
    newBody.value = ''
    createError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    createError.value = toUserMessage(error)
  },
})

function onCreate(): void {
  const name = newName.value.trim()
  const body = newBody.value.trim()
  createError.value = null

  if (name.length === 0) {
    createError.value = 'Shablon nomini kiriting.'
    return
  }
  if (body.length === 0) {
    createError.value = 'Xabar matnini kiriting.'
    return
  }

  createMutation.mutate({ name, body })
}

/* ---------------------------------------------------------- tahrirlash */

const editingId = ref<number | null>(null)
const editName = ref('')
const editBody = ref('')
const editActive = ref(true)
const editError = ref<string | null>(null)

function startEdit(template: MessageTemplateDto): void {
  editingId.value = template.id
  editName.value = template.name
  editBody.value = template.body
  editActive.value = template.isActive
  editError.value = null
}

function cancelEdit(): void {
  editingId.value = null
  editError.value = null
}

const updateMutation = useMutation({
  mutationFn: (input: { id: number; name: string; body: string; isActive: boolean }) =>
    updateMessageTemplate(input.id, { name: input.name, body: input.body, isActive: input.isActive }),
  onSuccess: () => {
    editingId.value = null
    editError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    editError.value = toUserMessage(error)
  },
})

async function onSaveEdit(template: MessageTemplateDto): Promise<void> {
  const name = editName.value.trim()
  const body = editBody.value.trim()
  editError.value = null

  if (name.length === 0) {
    editError.value = 'Shablon nomini kiriting.'
    return
  }
  if (body.length === 0) {
    editError.value = 'Xabar matnini kiriting.'
    return
  }

  const details: string[] = []
  if (name !== template.name) details.push(`Nomi: “${template.name}” → “${name}”`)
  if (body !== template.body) details.push('Xabar matni o‘zgartirildi')
  if (editActive.value !== template.isActive) {
    details.push(
      editActive.value
        ? 'Shablon qayta faollashtiriladi'
        : 'Arxivlanadi: yuborish tanlagichida endi ko‘rinmaydi',
    )
  }

  if (details.length === 0) {
    cancelEdit()
    return
  }

  const ok = await confirm({
    title: 'Shablonni saqlash',
    message: 'O‘zgarishlar saqlansinmi?',
    confirmLabel: 'Saqlash',
    tone: 'primary',
    details,
  })

  if (!ok) return

  updateMutation.mutate({ id: template.id, name, body, isActive: editActive.value })
}

/* ------------------------------------------------------------ o'chirish */

const deleting = ref<MessageTemplateDto | null>(null)
const deleteError = ref<string | null>(null)

const deleteMutation = useMutation({
  mutationFn: (id: number) => deleteMessageTemplate(id),
  onSuccess: () => {
    deleting.value = null
    deleteError.value = null
    refresh()
  },
  onError: (error: unknown) => {
    deleteError.value = toUserMessage(error)
  },
})

function askDelete(template: MessageTemplateDto): void {
  deleting.value = template
  deleteError.value = null
}
</script>

<template>
  <div>
    <p class="mb-4 text-xs text-slate-400">
      "Xabarlar" panelining shablon tanlagichini to‘ldiradigan tayyor matnlar.
    </p>

    <!-- ─────────────────────── YANGI SHABLON ─────────────────────── -->
    <div class="mb-5 space-y-2.5 rounded-xl border border-line bg-ink-900 p-3.5">
      <BaseField
        label="Shablon nomi"
        hint="Masalan: To‘lov eslatmasi"
      >
        <input
          v-model="newName"
          class="zn-input"
          maxlength="100"
          placeholder="Shablon nomi"
        >
      </BaseField>
      <BaseField
        label="Xabar matni"
        :error="createError"
      >
        <textarea
          v-model="newBody"
          class="zn-input"
          rows="3"
          placeholder="Xabar matnini kiriting..."
        />
      </BaseField>
      <div class="flex justify-end">
        <BaseButton
          :loading="createMutation.isPending.value"
          @click="onCreate"
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
    </div>

    <!-- ───────────────────────── RO'YXAT ───────────────────────── -->
    <DataStatus
      :pending="templatesQuery.isPending.value"
      :error="listError"
      :empty="templates.length === 0"
      :retrying="templatesQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="send"
      empty-title="Shablon qo‘shilmagan"
      empty-text="Birinchi shablonni yuqoridagi maydondan qo‘shing."
      @retry="templatesQuery.refetch()"
    >
      <ul class="divide-y divide-line rounded-xl border border-line">
        <li
          v-for="template in templates"
          :key="template.id"
          class="p-3.5"
        >
          <!-- Tahrirlash rejimi -->
          <div
            v-if="editingId === template.id"
            class="space-y-2.5"
          >
            <BaseField label="Nomi">
              <input
                v-model="editName"
                class="zn-input"
                maxlength="100"
              >
            </BaseField>
            <BaseField
              label="Xabar matni"
              :error="editError"
            >
              <textarea
                v-model="editBody"
                class="zn-input"
                rows="3"
              />
            </BaseField>
            <label class="flex min-h-11 items-center gap-2.5 text-sm text-slate-300">
              <input
                v-model="editActive"
                type="checkbox"
                class="size-4 accent-brand-500"
              >
              Faol (yuborish paytida tanlagichda ko‘rinadi)
            </label>
            <div class="flex justify-end gap-2">
              <BaseButton
                size="sm"
                variant="secondary"
                @click="cancelEdit"
              >
                Bekor qilish
              </BaseButton>
              <BaseButton
                size="sm"
                :loading="updateMutation.isPending.value"
                @click="onSaveEdit(template)"
              >
                Saqlash
              </BaseButton>
            </div>
          </div>

          <!-- Ko'rish rejimi -->
          <div v-else>
            <div class="flex flex-wrap items-center gap-2">
              <span
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="template.name"
              />
              <BaseBadge :tone="template.isActive ? 'success' : 'neutral'">
                {{ template.isActive ? 'Faol' : 'Arxiv' }}
              </BaseBadge>
              <BaseButton
                size="sm"
                variant="secondary"
                @click="startEdit(template)"
              >
                <template #icon>
                  <AppIcon
                    name="edit"
                    :size="13"
                  />
                </template>
                Tahrirlash
              </BaseButton>
              <BaseButton
                size="sm"
                variant="danger"
                @click="askDelete(template)"
              >
                O‘chirish
              </BaseButton>
            </div>
            <p
              class="mt-1.5 line-clamp-2 text-xs text-slate-400"
              v-text="template.body"
            />
          </div>
        </li>
      </ul>
    </DataStatus>

    <ConfirmDeleteDialog
      :open="deleting !== null"
      title="Shablonni o‘chirish"
      :message="`“${deleting?.name ?? '—'}” shabloni o‘chiriladi. Bu amalni qaytarib bo‘lmaydi.`"
      :pending="deleteMutation.isPending.value"
      :error="deleteError"
      @close="deleting = null"
      @confirm="deleting !== null && deleteMutation.mutate(deleting.id)"
    />
  </div>
</template>
