<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  conversationSubtitle,
  DM_BODY_MAX,
  fetchConversations,
  fetchThread,
  markConversationRead,
  peerRoleLabel,
  sendDirectMessage,
} from '@/entities/direct-message'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import type { ConversationDto } from '@/shared/types'
import { AppIcon, BaseAvatar, DataStatus } from '@/shared/ui'

/**
 * CHAT — eski `#chat` bo'limi ("Chatlar").
 *
 * Eski ilovadagi IKKI KO'RINISH saqlangan: suhbatlar ro'yxati va ochilgan
 * suhbat. Ular alohida MARSHRUT emas, bitta tab ichidagi holat — eski
 * ilovada ham shunday edi (`chat-list-view` / `chat-room-view`) va "Orqaga"
 * tugmasi brauzer tarixiga tegmasdan ro'yxatga qaytaradi.
 */
const queryClient = useQueryClient()

const conversationsQuery = useQuery({
  queryKey: ['dm', 'conversations'],
  queryFn: ({ signal }) => fetchConversations({ signal }),
  // Suhbat ro'yxati o'zi yangilanib turadi: kurator javob yozsa o'quvchi
  // sahifani qayta ochmasdan ko'rsin.
  refetchInterval: 30_000,
})

const conversations = computed(() => conversationsQuery.data.value ?? [])

const conversationsError = computed(() =>
  conversationsQuery.error.value !== null ? toUserMessage(conversationsQuery.error.value) : null,
)

/* ------------------------------------------------------------ ochiq suhbat */

const activePeer = ref<ConversationDto | null>(null)

const threadQuery = useQuery({
  queryKey: ['dm', 'thread', computed(() => activePeer.value?.peerId ?? null)],
  queryFn: ({ signal }) => fetchThread(activePeer.value?.peerId as number, {}, { signal }),
  enabled: computed(() => activePeer.value !== null),
  refetchInterval: 15_000,
})

const messages = computed(() => threadQuery.data.value?.items ?? [])

const threadError = computed(() =>
  threadQuery.error.value !== null ? toUserMessage(threadQuery.error.value) : null,
)

const scroller = ref<HTMLElement | null>(null)

/** Yangi xabar kelganda oxiriga tushamiz (chat odatiy xatti-harakati). */
watch(
  () => messages.value.length,
  () => {
    void nextTick(() => {
      const element = scroller.value
      if (element !== null) element.scrollTop = element.scrollHeight
    })
  },
)

/**
 * O'qildi belgilash — suhbat ochilganda va yangi xabar kelganda.
 * Idempotent (server takrorda 0 qaytaradi), shuning uchun ortiqcha shart yo'q.
 */
const markReadMutation = useMutation({
  mutationFn: (peerId: number) => markConversationRead(peerId),
  onSuccess: () => {
    void queryClient.invalidateQueries({ queryKey: ['dm', 'conversations'] })
  },
})

watch(
  () => [activePeer.value?.peerId ?? null, threadQuery.data.value?.unreadCount ?? 0] as const,
  ([peerId, unread]) => {
    if (peerId !== null && unread > 0) markReadMutation.mutate(peerId)
  },
)

/* --------------------------------------------------------------- yuborish */

const draft = ref('')
const sendError = ref<string | null>(null)

const sendMutation = useMutation({
  mutationFn: (input: { peerId: number; body: string }) =>
    sendDirectMessage(input.peerId, { body: input.body }),
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
    activePeer.value !== null &&
    draft.value.trim().length > 0 &&
    draft.value.length <= DM_BODY_MAX &&
    !sendMutation.isPending.value,
)

function submit(): void {
  const peer = activePeer.value
  if (peer === null || !canSend.value) return
  sendMutation.mutate({ peerId: peer.peerId, body: draft.value.trim() })
}

function openConversation(conversation: ConversationDto): void {
  sendError.value = null
  draft.value = ''
  activePeer.value = conversation
}
</script>

<template>
  <div>
    <!-- ============================ Ro'yxat ============================= -->
    <template v-if="activePeer === null">
      <h2
        class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-brand-300"
      >
        <AppIcon
          name="chat"
          :size="15"
        />
        Chatlar
      </h2>

      <DataStatus
        :pending="conversationsQuery.isPending.value"
        :error="conversationsError"
        :empty="conversations.length === 0"
        :retrying="conversationsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="chat"
        empty-title="Guruh yo‘q"
        empty-text="Guruhga qo‘shilganingizdan keyin kuratoringiz bilan yozishish shu yerda ochiladi."
        @retry="conversationsQuery.refetch()"
      >
        <ul class="space-y-2">
          <li
            v-for="conversation in conversations"
            :key="conversation.peerId"
          >
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-[14px] border border-line bg-ink-900 px-3.5 py-3 text-left transition-colors hover:bg-ink-800"
              @click="openConversation(conversation)"
            >
              <BaseAvatar
                :name="conversation.peerName ?? '?'"
                size="md"
              />
              <span class="min-w-0 flex-1">
                <span class="flex items-center gap-2">
                  <span
                    class="min-w-0 flex-1 truncate text-sm font-semibold text-slate-100"
                    v-text="conversation.peerName ?? '—'"
                  />
                  <span
                    v-if="conversation.lastMessageAt !== null"
                    class="shrink-0 text-[11px] tabular-nums text-dim"
                    v-text="formatTime(conversation.lastMessageAt)"
                  />
                </span>
                <span class="mt-0.5 flex items-center gap-2">
                  <span
                    class="min-w-0 flex-1 truncate text-xs text-slate-400"
                    v-text="conversationSubtitle(conversation)"
                  />
                  <span
                    v-if="conversation.unreadCount > 0"
                    class="shrink-0 rounded-full bg-brand-500 px-1.5 py-0.5 text-[10px] font-extrabold text-on-brand"
                    v-text="conversation.unreadCount"
                  />
                </span>
                <span
                  class="mt-0.5 block text-[10px] font-bold uppercase tracking-[1px] text-dim"
                  v-text="peerRoleLabel(conversation.peerRole)"
                />
              </span>
            </button>
          </li>
        </ul>
      </DataStatus>
    </template>

    <!-- ========================== Ochiq suhbat ========================== -->
    <template v-else>
      <div class="mb-3 mt-2 flex items-center gap-3">
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-[10px] border border-line bg-white/[0.06] px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-white/[0.12]"
          @click="activePeer = null"
        >
          <AppIcon
            name="arrow-left"
            :size="15"
          />
          Orqaga
        </button>
        <span class="min-w-0 flex-1">
          <span
            class="block truncate text-sm font-bold text-slate-100"
            v-text="activePeer.peerName ?? '—'"
          />
          <span
            class="text-[11px] text-dim"
            v-text="peerRoleLabel(activePeer.peerRole)"
          />
        </span>
      </div>

      <DataStatus
        :pending="threadQuery.isPending.value"
        :error="threadError"
        :empty="messages.length === 0"
        :retrying="threadQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="chat"
        empty-title="Hali xabar yo‘q"
        empty-text="Birinchi xabarni siz yozishingiz mumkin."
        @retry="threadQuery.refetch()"
      >
        <div
          ref="scroller"
          class="scrollbar-slim max-h-[58vh] space-y-2 overflow-y-auto pb-1"
        >
          <div
            v-for="message in messages"
            :key="message.id"
            class="flex"
            :class="message.mine ? 'justify-end' : 'justify-start'"
          >
            <!--
              MENING xabarim — TO'LIQ brend fonida, eski ilovadagidek
              (`.mrow.mine .mbub { background: var(--accent); color: #071e2c }`).

              `bg-brand-500/16` ATAYLAB ishlatilmadi: o'quvchi temasida brend
              oltin (#f5b731) va uni to'q ko'k fon ustiga 16% shaffoflik bilan
              qo'yganda loyqa zaytun rang chiqadi — brauzerda ko'rildi. To'liq
              fon "kim yozgani" ni bir qarashda ajratadi, matn rangi esa
              `text-on-brand` orqali temaga moslashadi.
            -->
            <div
              class="max-w-[82%] rounded-2xl px-3.5 py-2"
              :class="
                message.mine
                  ? 'bg-brand-500 text-on-brand'
                  : 'border border-line bg-ink-900 text-slate-100'
              "
            >
              <!--
                Ikkilamchi matnlar (dars konteksti, vaqt) O'Z xabarimda
                brend fonida turadi — u yerda `text-brand-300`/`text-dim`
                o'qilmaydi. Eski ilova ham shuni qilardi:
                `.mrow.mine .mtime { color: rgba(7,30,44,.8) }`.
              -->
              <p
                v-if="message.moduleLessonName !== null"
                class="mb-1 text-[10px] font-bold uppercase tracking-[1px]"
                :class="message.mine ? 'text-on-brand/70' : 'text-brand-300'"
                v-text="message.moduleLessonName"
              />
              <p
                class="whitespace-pre-line break-words text-[13px] leading-relaxed"
                v-text="message.body"
              />
              <p
                class="mt-1 flex items-center justify-end gap-1 text-[10px] tabular-nums"
                :class="message.mine ? 'text-on-brand/75' : 'text-dim'"
              >
                {{ formatTime(message.sentAt) }}
                <!-- "Ikki belgi" faqat MENING xabarim uchun ma'noli. -->
                <AppIcon
                  v-if="message.mine"
                  :name="message.readByPeer ? 'check' : 'clock'"
                  :size="11"
                />
              </p>
            </div>
          </div>
        </div>
      </DataStatus>

      <!-- Yozish maydoni -->
      <form
        class="mt-3 flex items-end gap-2"
        novalidate
        @submit.prevent="submit"
      >
        <textarea
          v-model="draft"
          class="zn-input min-h-11 max-h-32 flex-1 resize-y py-2.5"
          rows="1"
          :maxlength="DM_BODY_MAX"
          placeholder="Xabar yozing..."
        />
        <button
          type="submit"
          class="tap-target flex shrink-0 items-center justify-center rounded-xl bg-brand-500 px-4 font-bold text-on-brand transition-colors disabled:opacity-40"
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
