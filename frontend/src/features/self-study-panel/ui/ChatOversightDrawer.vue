<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  SELF_STUDY_PANEL_KEYS,
  fetchSelfStudyChatAccessLog,
  fetchSelfStudyChatMessages,
  type SelfStudyChatRowDto,
} from '@/entities/self-study-panel'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric } from '@/shared/lib/datetime'
import type { DirectMessageDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseDrawer, DataStatus } from '@/shared/ui'

import ChatAttachmentView from './ChatAttachmentView.vue'

/**
 * KURATOR ↔ O'QUVCHI YOZISHMASI — FAQAT O'QISH (2026-11-19, loyiha egasining qarori).
 *
 *   • yozish maydoni YO'Q — nazoratchi suhbatga aralashmaydi;
 *   • ochish server tomonida auditga yoziladi va shu yerda ko'rsatiladi
 *     ("kim, qachon ochgan") — kurator ham, o'quv bo'limi ham buni biladi;
 *   • "o'qildi" bayroqlari o'zgarmaydi: kurator uchun javobsiz xabar
 *     nazoratchi ochgani uchun "ko'rilgan" bo'lib qolmaydi.
 */
const props = defineProps<{
  open: boolean
  chat: SelfStudyChatRowDto | null
}>()

const emit = defineEmits<{ close: [] }>()

const older = ref<DirectMessageDto[]>([])
const nextBeforeId = ref<number | null>(null)
const hasMoreOlder = ref(false)
const loadingOlder = ref(false)
const olderError = ref<string | null>(null)

const enabled = computed(() => props.open && props.chat !== null)

const firstPage = useQuery({
  queryKey: computed(() =>
    SELF_STUDY_PANEL_KEYS.chat(props.chat?.studentId ?? 0, props.chat?.staffId ?? 0)),
  queryFn: ({ signal }) =>
    fetchSelfStudyChatMessages(props.chat!.studentId, props.chat!.staffId, null, { signal }),
  enabled,
  // Har ochilish — yangi audit yozuvi; keshdan "ko'rsatib qo'yish" ochishni yashirardi.
  gcTime: 0,
  staleTime: 0,
})

const accessLog = useQuery({
  queryKey: computed(() =>
    SELF_STUDY_PANEL_KEYS.chatAccess(props.chat?.studentId ?? 0, props.chat?.staffId ?? 0)),
  queryFn: ({ signal }) =>
    fetchSelfStudyChatAccessLog(props.chat!.studentId, props.chat!.staffId, { signal }),
  enabled: computed(() => enabled.value && firstPage.isSuccess.value),
})

watch(
  () => firstPage.data.value,
  (page) => {
    older.value = []
    nextBeforeId.value = page?.nextBeforeId ?? null
    hasMoreOlder.value = page?.hasMore ?? false
  },
)

const messages = computed(() => [...older.value, ...(firstPage.data.value?.items ?? [])])

async function loadOlder(): Promise<void> {
  if (props.chat === null || nextBeforeId.value === null) return
  loadingOlder.value = true
  olderError.value = null
  try {
    const page = await fetchSelfStudyChatMessages(props.chat.studentId, props.chat.staffId, nextBeforeId.value)
    older.value = [...(page.items ?? []), ...older.value]
    nextBeforeId.value = page.nextBeforeId
    hasMoreOlder.value = page.hasMore
  } catch (err) {
    olderError.value = toUserMessage(err)
  } finally {
    loadingOlder.value = false
  }
}

function fromStudent(message: DirectMessageDto): boolean {
  return message.senderId === props.chat?.studentId
}

const error = computed(() => (firstPage.error.value ? toUserMessage(firstPage.error.value) : null))
</script>

<template>
  <BaseDrawer
    :open="open"
    :title="chat !== null ? `${chat.studentName} ↔ ${chat.staffName}` : 'Yozishma'"
    subtitle="Faqat o‘qish — xabar yozib bo‘lmaydi"
    @close="emit('close')"
  >
    <div
      class="mb-3 flex items-start gap-2 rounded-lg border border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-100"
      role="note"
    >
      <AppIcon
        name="eye"
        :size="14"
        class="mt-0.5 shrink-0"
      />
      <span>
        Bu yozishmani ochganingiz qayd etiladi. Siz ochganingiz kurator uchun xabarlarni «o‘qilgan» qilmaydi.
      </span>
    </div>

    <DataStatus
      :pending="firstPage.isPending.value"
      :error="error"
      :empty="messages.length === 0"
      :retrying="firstPage.isFetching.value"
      empty-icon="chat"
      empty-title="Xabar yo‘q"
      empty-text="Bu juftlikda hali yozishma bo‘lmagan."
      @retry="firstPage.refetch()"
    >
      <div class="space-y-2">
        <div
          v-if="hasMoreOlder"
          class="text-center"
        >
          <BaseButton
            size="sm"
            variant="secondary"
            :loading="loadingOlder"
            @click="loadOlder"
          >
            Eskiroq xabarlar
          </BaseButton>
          <p
            v-if="olderError !== null"
            class="mt-1 text-[11px] text-rose-400"
            v-text="olderError"
          />
        </div>

        <div
          v-for="message in messages"
          :key="message.id"
          class="flex"
          :class="fromStudent(message) ? 'justify-start' : 'justify-end'"
        >
          <div
            class="max-w-[85%] rounded-2xl border px-3 py-2 text-sm"
            :class="
              fromStudent(message)
                ? 'border-line bg-ink-900 text-slate-100'
                : 'border-brand-500/30 bg-brand-500/10 text-slate-100'
            "
          >
            <p class="mb-0.5 flex items-center gap-2 text-[11px] text-dim">
              <span
                class="font-semibold text-slate-300"
                v-text="message.senderName"
              />
              <span v-text="formatDateTimeNumeric(message.sentAt)" />
              <span
                v-if="!message.readByPeer"
                class="text-amber-400"
              >o‘qilmagan</span>
            </p>
            <p
              v-if="message.moduleLessonName !== null"
              class="mb-1 text-[11px] text-brand-300"
              v-text="`📘 ${message.moduleLessonName}`"
            />
            <p
              v-if="message.body.length > 0"
              class="whitespace-pre-wrap break-words"
              v-text="message.body"
            />
            <ChatAttachmentView
              v-for="attachment in message.attachments ?? []"
              :key="attachment.id"
              :attachment="attachment"
            />
          </div>
        </div>
      </div>
    </DataStatus>

    <template #footer>
      <details class="w-full text-xs text-slate-400">
        <summary class="cursor-pointer select-none font-semibold text-slate-300">
          Kim ochgan ({{ accessLog.data.value?.length ?? 0 }})
        </summary>
        <ul class="mt-2 max-h-40 space-y-1 overflow-y-auto scrollbar-slim">
          <li
            v-for="(entry, index) in accessLog.data.value ?? []"
            :key="index"
            class="flex justify-between gap-2"
          >
            <span>
              {{ entry.viewerName }}
              <span class="text-dim">· {{ entry.kind === 'Thread' ? 'yozishma' : 'fayl' }}</span>
            </span>
            <span
              class="tabular-nums text-dim"
              v-text="formatDateTimeNumeric(entry.at)"
            />
          </li>
        </ul>
      </details>
    </template>
  </BaseDrawer>
</template>
