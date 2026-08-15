<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  DM_BODY_MAX,
  fetchConversations,
  fetchThread,
  sendDirectMessage,
  withDayLabels,
} from '@/entities/direct-message'
import { ChatDaySeparator } from '@/features/group-chat'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import { AppIcon, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  DARS DASHBOARD — MINI-CHAT ("Savol-javob")
 * ════════════════════════════════════════════════════════════════════════
 *
 * `StudentChatPage`dagi kurator DM'ining KICHRAYTIRILGAN, SHU DARSGA
 * FILTRLANGAN ko'rinishi — alohida Suhbat tabiga o'tmasdan, video ostida.
 *
 * ★ NEGA YANGI CHAT ENTITY EMAS: mavjud `DirectMessage.moduleLessonId`
 * mexanizmi (R40) ANIQ shu vazifa uchun qurilgan — faqat u paytda hech bir
 * ekran uni FILTR sifatida ishlatmasdi (faqat YOZISHDA belgilardi). Bu
 * panel `fetchThread`ga `moduleLessonId` beradi (backend qo'shimchasi —
 * `DirectMessageService.GetThreadAsync`) va shu bilan "shu darsning
 * savol-javoblari" haqiqiy filtrga aylanadi.
 *
 * ★ SUHBATDOSH AVTOMATIK: server MAS'ULIYAT tartibida qaytaradi
 * (`Group.questionResponderRole`) — birinchi suhbat har doim javob
 * beradigan xodim (`StudentChatPage`dagi AYNI qoida).
 */
const props = defineProps<{ lessonId: number }>()

const queryClient = useQueryClient()

const conversationsQuery = useQuery({
  queryKey: ['dm', 'conversations'],
  queryFn: ({ signal }) => fetchConversations({ signal }),
})

const curator = computed(() => conversationsQuery.data.value?.[0] ?? null)

const threadQuery = useQuery({
  queryKey: ['dm', 'thread', computed(() => curator.value?.peerId ?? null), 'lesson', props.lessonId],
  queryFn: ({ signal }) =>
    fetchThread(
      curator.value?.peerId as number,
      { moduleLessonId: props.lessonId },
      { signal },
    ),
  enabled: computed(() => curator.value !== null),
})

const messages = computed(() => threadQuery.data.value?.items ?? [])
const grouped = computed(() => withDayLabels(messages.value))

const threadError = computed(() =>
  threadQuery.error.value !== null ? toUserMessage(threadQuery.error.value) : null,
)

const scroller = ref<HTMLElement | null>(null)

watch(
  () => messages.value.length,
  () => {
    void nextTick(() => {
      const element = scroller.value
      if (element !== null) element.scrollTop = element.scrollHeight
    })
  },
)

const draft = ref('')
const sendError = ref<string | null>(null)

const sendMutation = useMutation({
  mutationFn: (input: { peerId: number; body: string }) =>
    sendDirectMessage(input.peerId, { body: input.body, moduleLessonId: props.lessonId }),
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
    curator.value !== null &&
    draft.value.trim().length > 0 &&
    draft.value.length <= DM_BODY_MAX &&
    !sendMutation.isPending.value,
)

function submit(): void {
  const peer = curator.value
  if (peer === null || !canSend.value) return
  sendMutation.mutate({ peerId: peer.peerId, body: draft.value.trim() })
}
</script>

<template>
  <div>
    <p
      v-if="!conversationsQuery.isPending.value && curator === null"
      class="rounded-xl border border-line bg-ink-900 px-3.5 py-3 text-xs text-slate-400"
    >
      Sizga hali kurator biriktirilmagan.
    </p>

    <template v-else>
      <div
        ref="scroller"
        class="scrollbar-slim max-h-64 space-y-2 overflow-y-auto rounded-xl border border-line bg-ink-950 p-3"
      >
        <DataStatus
          :pending="conversationsQuery.isPending.value || threadQuery.isPending.value"
          :error="threadError"
          :empty="messages.length === 0"
          :retrying="threadQuery.isFetching.value"
          :skeleton-rows="2"
          empty-icon="chat"
          empty-title="Hali savol yo‘q"
          empty-text="Shu dars bo‘yicha birinchi savolingizni yozing."
          @retry="threadQuery.refetch()"
        >
          <template
            v-for="row in grouped"
            :key="row.message.id"
          >
            <ChatDaySeparator
              v-if="row.dayLabel !== null"
              :label="row.dayLabel"
            />
            <div
              class="flex"
              :class="row.message.mine ? 'justify-end' : 'justify-start'"
            >
              <div
                class="max-w-[85%] rounded-2xl px-3 py-1.5"
                :class="
                  row.message.mine
                    ? 'bg-brand-500 text-on-brand'
                    : 'border border-line bg-ink-900 text-slate-100'
                "
              >
                <p
                  class="whitespace-pre-line break-words text-[13px] leading-relaxed"
                  v-text="row.message.body"
                />
                <p
                  class="mt-0.5 text-right text-[10px] tabular-nums"
                  :class="row.message.mine ? 'text-on-brand/75' : 'text-dim'"
                  v-text="formatTime(row.message.sentAt)"
                />
              </div>
            </div>
          </template>
        </DataStatus>
      </div>

      <form
        class="mt-2 flex items-end gap-2"
        novalidate
        @submit.prevent="submit"
      >
        <textarea
          v-model="draft"
          class="zn-input max-h-24 min-h-11 flex-1 resize-none py-2 text-sm"
          rows="1"
          :maxlength="DM_BODY_MAX"
          placeholder="Shu dars bo‘yicha savolingizni yozing..."
        />
        <button
          type="submit"
          class="flex size-11 shrink-0 items-center justify-center rounded-full bg-brand-500 font-bold text-on-brand shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-40"
          :disabled="!canSend"
          aria-label="Yuborish"
        >
          <AppIcon
            name="send"
            :size="18"
          />
        </button>
      </form>

      <p
        v-if="sendError !== null"
        class="mt-2 text-xs text-rose-400"
        role="alert"
        v-text="sendError"
      />
    </template>
  </div>
</template>
