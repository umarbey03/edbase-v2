<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  DM_BODY_MAX,
  fetchThread,
  markConversationRead,
  sendDirectMessage,
} from '@/entities/direct-message'
import { toUserMessage } from '@/shared/api'
import { formatDayLabel, formatTime } from '@/shared/lib/datetime'
import type { ConversationDto } from '@/shared/types'
import { AppIcon, DataStatus } from '@/shared/ui'

/**
 * Ochilgan yozishma — eski `#dm-msgs` + `#dm-form`.
 *
 * Suhbatlar RO'YXATIdan ataylab ajratilgan: ro'yxat 30 sekundda, ochiq
 * yozishma esa 15 sekundda yangilanadi va bittasining qayta chizilishi
 * ikkinchisini qimirlatmasligi kerak.
 */
const props = defineProps<{ peer: ConversationDto }>()

const emit = defineEmits<{ close: [] }>()

const queryClient = useQueryClient()

const peerId = computed(() => props.peer.peerId)

const threadQuery = useQuery({
  queryKey: ['dm', 'thread', peerId],
  queryFn: ({ signal }) => fetchThread(peerId.value, {}, { signal }),
  refetchInterval: 15_000,
})

const messages = computed(() => threadQuery.data.value?.items ?? [])

const threadError = computed(() =>
  threadQuery.error.value !== null ? toUserMessage(threadQuery.error.value) : null,
)

/**
 * Kun ajratgichlari (eski `.datesep`): bir kunlik xabarlar bitta sarlavha
 * ostida turadi, aks holda uzun yozishmada "qachon yozilgan" yo'qoladi.
 */
const grouped = computed(() => {
  let previous = ''
  return messages.value.map((message) => {
    const label = formatDayLabel(message.sentAt)
    const showDay = label !== previous
    previous = label
    return { message, dayLabel: showDay ? label : null }
  })
})

const scroller = ref<HTMLElement | null>(null)

watch(
  () => [peerId.value, messages.value.length] as const,
  () => {
    void nextTick(() => {
      const element = scroller.value
      if (element !== null) element.scrollTop = element.scrollHeight
    })
  },
  { immediate: true },
)

/** O'qildi belgilash idempotent — takror chaqiruvda server 0 qaytaradi. */
const markReadMutation = useMutation({
  mutationFn: (id: number) => markConversationRead(id),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['dm', 'conversations'] })
  },
})

watch(
  () => [peerId.value, threadQuery.data.value?.unreadCount ?? 0] as const,
  ([id, unread]) => {
    if (unread > 0) markReadMutation.mutate(id)
  },
)

/* --------------------------------------------------------------- yuborish */

const draft = ref('')
const sendError = ref<string | null>(null)

// Boshqa o'quvchiga o'tilganda yozib qo'yilgan matn ketmasin.
watch(peerId, () => {
  draft.value = ''
  sendError.value = null
})

const sendMutation = useMutation({
  mutationFn: (input: { id: number; body: string }) =>
    sendDirectMessage(input.id, { body: input.body }),
  onSuccess: () => {
    draft.value = ''
    sendError.value = null
    void threadQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: ['dm', 'conversations'] })
  },
  onError: (error: Error) => {
    sendError.value = toUserMessage(error)
  },
})

const canSend = computed(
  () =>
    draft.value.trim().length > 0 &&
    draft.value.length <= DM_BODY_MAX &&
    !sendMutation.isPending.value,
)

function submit(): void {
  if (!canSend.value) return
  sendMutation.mutate({ id: peerId.value, body: draft.value.trim() })
}
</script>

<template>
  <section class="flex min-h-[60vh] flex-col rounded-xl border border-line bg-ink-900 p-3.5">
    <header class="mb-2.5 flex items-center gap-2.5 border-b border-line pb-2.5">
      <!-- Telefonda ro'yxatga qaytish (eski `#dm-back`). Desktopda kerak emas. -->
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 px-2 text-slate-100 transition-colors hover:bg-ink-750 md:hidden"
        aria-label="Ro‘yxatga qaytish"
        @click="emit('close')"
      >
        <AppIcon
          name="arrow-left"
          :size="16"
        />
      </button>
      <div class="min-w-0 flex-1">
        <p
          class="truncate text-sm font-bold text-slate-100"
          v-text="props.peer.peerName ?? '—'"
        />
        <p
          v-if="props.peer.groupName !== null"
          class="truncate text-[11px] text-dim"
          v-text="props.peer.groupName"
        />
      </div>
    </header>

    <DataStatus
      :pending="threadQuery.isPending.value"
      :error="threadError"
      :empty="messages.length === 0"
      :retrying="threadQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="chat"
      empty-title="Hali xabar yo‘q — birinchi bo‘lib yozing."
      @retry="threadQuery.refetch()"
    >
      <div
        ref="scroller"
        class="chat-scroll-container scrollbar-slim flex-1 space-y-2 overflow-y-auto pb-1"
      >
        <template
          v-for="row in grouped"
          :key="row.message.id"
        >
          <p
            v-if="row.dayLabel !== null"
            class="mx-auto w-fit rounded-[20px] border border-line bg-ink-950 px-3 py-0.5 text-[11px] text-slate-400"
            v-text="row.dayLabel"
          />
          <div
            class="flex"
            :class="row.message.mine ? 'justify-end' : 'justify-start'"
          >
            <div
              class="max-w-[82%] rounded-2xl px-3.5 py-2"
              :class="
                row.message.mine
                  ? 'bg-brand-500 text-on-brand'
                  : 'border border-line bg-ink-950 text-slate-100'
              "
            >
              <!-- Savol qaysi kurs darsidan yozilgani (eski `📖 …` qatori). -->
              <p
                v-if="row.message.moduleLessonName !== null"
                class="mb-1 text-[10px] font-bold uppercase tracking-[1px]"
                :class="row.message.mine ? 'text-on-brand/70' : 'text-brand-300'"
                v-text="row.message.moduleLessonName"
              />
              <p
                class="whitespace-pre-line break-words text-[13px] leading-relaxed"
                v-text="row.message.body"
              />
              <p
                class="mt-1 flex items-center justify-end gap-1 text-[10px] tabular-nums"
                :class="row.message.mine ? 'text-on-brand/75' : 'text-dim'"
              >
                {{ formatTime(row.message.sentAt) }}
                <AppIcon
                  v-if="row.message.mine"
                  :name="row.message.readByPeer ? 'check' : 'clock'"
                  :size="11"
                />
              </p>
            </div>
          </div>
        </template>
      </div>
    </DataStatus>

    <form
      class="mt-2.5 flex items-end gap-2"
      novalidate
      @submit.prevent="submit"
    >
      <textarea
        v-model="draft"
        class="zn-input max-h-32 min-h-11 flex-1 resize-y py-2.5"
        rows="1"
        :maxlength="DM_BODY_MAX"
        placeholder="Javob yozing..."
      />
      <button
        type="submit"
        class="tap-target flex shrink-0 items-center justify-center gap-2 rounded-xl bg-brand-500 px-4 text-sm font-bold text-on-brand transition-colors disabled:opacity-40"
        :disabled="!canSend"
      >
        <AppIcon
          name="send"
          :size="16"
        />
        <span class="hidden sm:inline">Yuborish</span>
      </button>
    </form>

    <p
      v-if="sendError !== null"
      class="mt-2 text-xs text-rose-400"
      role="alert"
      v-text="sendError"
    />
  </section>
</template>
