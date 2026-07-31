<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'

import type { ChatMessage } from '@/entities/message'
import ParticipantList from '@/features/presence/ui/ParticipantList.vue'
import type { HubStatus, PresenceEntry, UserRoleName } from '@/shared/types'
import { AppIcon, BaseSpinner } from '@/shared/ui'

import { useChatScroll } from '../model/useChatScroll'
import { useMessageRows } from '../model/useMessageRows'
import { useOptimisticChat } from '../model/useOptimisticChat'
import ChatComposer from './ChatComposer.vue'
import ChatMessageRow from './ChatMessageRow.vue'

const props = defineProps<{
  messages: readonly ChatMessage[]
  currentUserId: number | null
  /** userId -> rol. Xabar rozetkalari uchun O(1) qidiruv. */
  roleByUserId: ReadonlyMap<number, UserRoleName>
  participants: readonly PresenceEntry[]
  participantCount: number
  raisedHands: readonly PresenceEntry[]
  status: HubStatus
  canSend: boolean
  isSending: boolean
  cooldownRemainingMs: number
  notice: string | null
  sessionEnded: boolean
  /**
   * Yuborish funksiyasi.
   *
   * `clientId` — optimistik nusxa bilan server broadcast'ini bog'lovchi
   * BARQAROR kalit (batafsil: `useOptimisticChat`). Chaqiruvchi uni
   * o'zgarishsiz hub'ga uzatishi shart.
   */
  send: (body: string, clientId: string) => Promise<boolean>
}>()

const emit = defineEmits<{
  retry: []
  'dismiss-notice': []
  close: []
  'unread-change': [count: number]
}>()

type Tab = 'chat' | 'people'
const tab = ref<Tab>('chat')

const scrollerEl = ref<HTMLElement | null>(null)
const messagesRef = toRef(props, 'messages')
const currentUserIdRef = toRef(props, 'currentUserId')

/**
 * O'z ismimiz ishtirokchilar ro'yxatidan olinadi — optimistik nusxa to'liq
 * bo'lishi uchun. Alohida prop qo'shilmadi: ma'lumot allaqachon shu yerda.
 * (O'z xabarida ism baribir chizilmaydi, lekin DTO chala qolmasin.)
 */
const currentUserName = computed(
  () =>
    props.participants.find((entry) => entry.userId === props.currentUserId)?.displayName ?? '',
)

// Optimistik ko'rsatish: xabar serverni kutmasdan ekranga chiqadi va
// broadcast qaytganda kalit bo'yicha dedupe qilinadi.
const { merged, pendingKeys, submit } = useOptimisticChat(
  messagesRef,
  currentUserIdRef,
  currentUserName,
  props.send,
)

const rows = useMessageRows(merged, currentUserIdRef, pendingKeys)
const { isPinnedToBottom, unreadCount, jumpToBottom } = useChatScroll(scrollerEl, merged)

// Mobil boshqaruv panelidagi nishoncha uchun.
watch(unreadCount, (count) => emit('unread-change', count))

const STATUS_LABEL: Record<HubStatus, string> = {
  idle: 'Kutilmoqda',
  connecting: 'Ulanmoqda…',
  connected: 'Ulangan',
  reconnecting: 'Qayta ulanmoqda…',
  disconnected: 'Aloqa uzildi',
}

const STATUS_DOT: Record<HubStatus, string> = {
  idle: 'bg-slate-500',
  connecting: 'bg-amber-400 animate-pulse',
  connected: 'bg-emerald-400',
  reconnecting: 'bg-amber-400 animate-pulse',
  disconnected: 'bg-rose-500',
}

const composerDisabled = computed(() => props.sessionEnded || props.status !== 'connected')
const composerHint = computed(() =>
  props.sessionEnded ? 'Dars yakunlandi' : 'Aloqa tiklanmoqda — biroz kuting',
)

function roleOf(senderId: number): string {
  return props.roleByUserId.get(senderId) ?? ''
}
</script>

<template>
  <aside class="flex h-full min-h-0 flex-col bg-ink-900">
    <!-- Sarlavha + tablar -->
    <header class="flex shrink-0 items-center gap-1 border-b border-line px-2 py-2">
      <button
        type="button"
        class="rounded-lg px-3 py-1.5 text-sm font-medium transition-colors"
        :class="tab === 'chat' ? 'bg-ink-750 text-slate-100' : 'text-slate-400 hover:text-slate-200'"
        @click="tab = 'chat'"
      >
        Suhbat
      </button>
      <button
        type="button"
        class="flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-sm font-medium transition-colors"
        :class="tab === 'people' ? 'bg-ink-750 text-slate-100' : 'text-slate-400 hover:text-slate-200'"
        @click="tab = 'people'"
      >
        <AppIcon
          name="users"
          :size="15"
        />
        <span class="tabular-nums">{{ props.participantCount }}</span>
      </button>

      <div class="ml-auto flex items-center gap-2 pr-1">
        <span
          class="size-2 rounded-full"
          :class="STATUS_DOT[props.status]"
          :title="STATUS_LABEL[props.status]"
          aria-hidden="true"
        />
        <button
          type="button"
          class="rounded-lg p-1.5 text-slate-400 transition-colors hover:bg-white/5 hover:text-slate-200 lg:hidden"
          title="Yopish"
          @click="emit('close')"
        >
          <AppIcon
            name="close"
            :size="18"
          />
        </button>
      </div>
    </header>

    <!-- Ko'tarilgan qo'llar -->
    <div
      v-if="props.raisedHands.length > 0"
      class="shrink-0 border-b border-line bg-amber-500/10 px-3 py-2"
    >
      <div class="flex items-center gap-2 text-amber-300">
        <AppIcon
          name="hand"
          :size="15"
        />
        <span class="text-xs font-semibold">Qo‘l ko‘targanlar ({{ props.raisedHands.length }})</span>
      </div>
      <p class="mt-1 line-clamp-2 text-xs text-amber-200/80">
        {{ props.raisedHands.map((entry) => entry.displayName).join(', ') }}
      </p>
    </div>

    <!-- Ishtirokchilar -->
    <ParticipantList
      v-if="tab === 'people'"
      :participants="props.participants"
      :total-count="props.participantCount"
      :current-user-id="props.currentUserId"
    />

    <!-- Suhbat -->
    <template v-else>
      <div class="relative min-h-0 flex-1">
        <div
          ref="scrollerEl"
          class="scrollbar-slim chat-scroll-container h-full overflow-y-auto pb-3 pt-1"
        >
          <div
            v-if="rows.length === 0"
            class="flex h-full flex-col items-center justify-center gap-2 px-6 text-center"
          >
            <BaseSpinner
              v-if="props.status === 'connecting'"
              class="text-slate-600"
            />
            <template v-else>
              <div class="flex size-11 items-center justify-center rounded-2xl bg-ink-800 text-slate-600">
                <AppIcon
                  name="chat"
                  :size="22"
                />
              </div>
              <p class="text-sm text-slate-400">
                Hozircha xabarlar yo‘q
              </p>
              <p class="text-xs text-slate-600">
                Birinchi bo‘lib yozing
              </p>
            </template>
          </div>

          <!--
            `:key="row.key"` — barqaror kalit. Vue eski qatorlarni qayta ishlatadi,
            faqat yangilari DOM'ga qo'shiladi. Xabarlar 200 tadan oshsa eng
            eskilari ro'yxatdan (va DOM'dan) chiqib ketadi.

            ★ ILGARI `row.id` EDI: real vaqtdagi xabarlarda `id` doim 0 bo'lgani
            uchun hamma yangi qator BITTA kalitni bo'lishardi.
          -->
          <template
            v-for="row in rows"
            :key="row.key"
          >
            <div
              v-if="row.dayLabel !== null"
              class="my-3 flex items-center gap-3 px-3"
            >
              <span
                class="h-px flex-1 bg-line"
                aria-hidden="true"
              />
              <span
                class="rounded-full bg-ink-800 px-2.5 py-0.5 text-[11px] font-medium text-slate-400"
                v-text="row.dayLabel"
              />
              <span
                class="h-px flex-1 bg-line"
                aria-hidden="true"
              />
            </div>

            <ChatMessageRow
              :sender-name="row.senderName"
              :body="row.body"
              :time="row.time"
              :is-own="row.isOwn"
              :show-header="row.showHeader"
              :role="roleOf(row.senderId)"
              :is-pending="row.isPending"
            />
          </template>
        </div>

        <!-- "Yangi xabarlar" tugmasi: foydalanuvchi tepada o'qiyotgan bo'lsa
             avtoskroll qilinmaydi, o'rniga shu tugma chiqadi. -->
        <button
          v-if="!isPinnedToBottom && unreadCount > 0"
          type="button"
          class="absolute bottom-3 left-1/2 z-10 flex -translate-x-1/2 animate-fade-up items-center gap-1.5 rounded-full bg-brand-600 py-1.5 pl-3 pr-2.5 text-xs font-semibold text-white shadow-lg shadow-brand-900/40 transition-colors hover:bg-brand-500"
          @click="jumpToBottom"
        >
          {{ unreadCount }} ta yangi xabar
          <AppIcon
            name="arrow-down"
            :size="14"
          />
        </button>
      </div>

      <!-- Ogohlantirish -->
      <div
        v-if="props.notice !== null"
        class="flex shrink-0 items-center gap-2 border-t border-amber-500/25 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      >
        <span
          class="flex-1"
          v-text="props.notice"
        />
        <button
          type="button"
          class="rounded p-0.5 text-amber-300/70 hover:text-amber-200"
          title="Yopish"
          @click="emit('dismiss-notice')"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </div>

      <!-- Aloqa uzilgan -->
      <div
        v-if="props.status === 'disconnected' && !props.sessionEnded"
        class="flex shrink-0 items-center gap-2 border-t border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      >
        <AppIcon
          name="wifi-off"
          :size="14"
        />
        <span class="flex-1">Suhbat aloqasi uzildi</span>
        <button
          type="button"
          class="rounded-md bg-rose-500/20 px-2 py-1 font-semibold text-rose-100 hover:bg-rose-500/30"
          @click="emit('retry')"
        >
          Qayta ulanish
        </button>
      </div>

      <ChatComposer
        :send="submit"
        :can-send="props.canSend"
        :is-sending="props.isSending"
        :cooldown-remaining-ms="props.cooldownRemainingMs"
        :disabled="composerDisabled"
        :disabled-hint="composerHint"
      />
    </template>
  </aside>
</template>
