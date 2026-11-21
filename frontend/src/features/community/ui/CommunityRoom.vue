<script setup lang="ts">
import { useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'

import {
  COMMUNITY_BODY_MAX,
  COMMUNITY_KEYS,
  deleteCommunityMessage,
  fetchCommunityAccess,
  fetchCommunityMessages,
  markCommunityRead,
  muteCommunityMember,
  sendCommunityMessage,
  type CommunityMessageDto,
} from '@/entities/community'
import ChatDaySeparator from '@/features/group-chat/ui/ChatDaySeparator.vue'
import ChatEmojiPicker from '@/features/group-chat/ui/ChatEmojiPicker.vue'
import { toUserMessage } from '@/shared/api'
import { formatDateTimeNumeric, formatDayLabel, formatTime } from '@/shared/lib/datetime'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, DataStatus, EmptyState } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  HAMJAMIYAT XONASI (2026-11-21) — o'quvchi va xodim uchun BITTA komponent
 * ════════════════════════════════════════════════════════════════════════
 *
 * • Yangi xabarlar — har 6 soniyada `afterId` bilan (faqat sahifa ko'rinib
 *   turganda); eskilari — "Oldingi xabarlar" tugmasi (`beforeId`).
 * • Moderator (kurator, o'quv bo'limi): istalgan xabarni o'chiradi va
 *   o'quvchini 1 soat / 1 kun / 7 kun yozishdan to'xtatadi.
 * • Telefon raqami va havola — server rad etadi; bu yerda xabar ko'rsatiladi.
 */
const props = withDefaults(defineProps<{ heightClass?: string }>(), {
  heightClass: 'h-[calc(100dvh-12rem)] min-h-[380px]',
})

const queryClient = useQueryClient()

const accessQuery = useQuery({
  queryKey: COMMUNITY_KEYS.access,
  queryFn: ({ signal }) => fetchCommunityAccess({ signal }),
  refetchInterval: 60_000,
})
const access = computed(() => accessQuery.data.value ?? null)

const messages = ref<CommunityMessageDto[]>([])
const hasMore = ref(false)
const loading = ref(true)
const loadError = ref<string | null>(null)
const loadingOlder = ref(false)

const scroller = ref<HTMLElement | null>(null)

function nearBottom(): boolean {
  const el = scroller.value
  return el === null || el.scrollHeight - el.scrollTop - el.clientHeight < 120
}

async function scrollToBottom(): Promise<void> {
  await nextTick()
  const el = scroller.value
  if (el !== null) el.scrollTop = el.scrollHeight
}

const lastId = computed(() => messages.value.at(-1)?.id ?? 0)

async function markRead(): Promise<void> {
  if (lastId.value === 0) return
  try {
    await markCommunityRead(lastId.value)
    void queryClient.invalidateQueries({ queryKey: COMMUNITY_KEYS.access })
  } catch {
    // O'qildi belgisi muhim emas — keyingi so'rovda yana urinadi.
  }
}

async function loadInitial(): Promise<void> {
  loading.value = true
  loadError.value = null
  try {
    const page = await fetchCommunityMessages({ take: 50 })
    messages.value = page.items
    hasMore.value = page.hasMore
    await scrollToBottom()
    void markRead()
  } catch (err) {
    loadError.value = toUserMessage(err)
  } finally {
    loading.value = false
  }
}

async function loadOlder(): Promise<void> {
  const first = messages.value[0]
  if (first === undefined || loadingOlder.value) return
  loadingOlder.value = true
  const el = scroller.value
  const before = el?.scrollHeight ?? 0
  try {
    const page = await fetchCommunityMessages({ beforeId: first.id, take: 50 })
    messages.value = [...page.items, ...messages.value]
    hasMore.value = page.hasMore
    await nextTick()
    if (el !== null) el.scrollTop += el.scrollHeight - before
  } catch (err) {
    showToast(toUserMessage(err), 'error')
  } finally {
    loadingOlder.value = false
  }
}

async function poll(): Promise<void> {
  if (document.hidden || loading.value || loadError.value !== null) return
  try {
    const stick = nearBottom()
    const page = await fetchCommunityMessages({ afterId: lastId.value, take: 100 })
    if (page.items.length > 0) {
      const known = new Set(messages.value.map((m) => m.id))
      messages.value = [...messages.value, ...page.items.filter((m) => !known.has(m.id))]
      if (stick) {
        await scrollToBottom()
        void markRead()
      }
    }
  } catch {
    // Tarmoq uzilishi — keyingi urinishda.
  }
}

let timer: number | null = null
onMounted(() => {
  void loadInitial()
  timer = window.setInterval(() => void poll(), 6_000)
})
onBeforeUnmount(() => {
  if (timer !== null) window.clearInterval(timer)
})

// O'chirilgan xabarlar holatini ham vaqti-vaqti bilan yangilash (moderator o'chirsa).
watch(() => accessQuery.dataUpdatedAt.value, async () => {
  if (messages.value.length === 0) return
  try {
    const page = await fetchCommunityMessages({ afterId: Math.max(0, messages.value[0]!.id - 1), take: 100 })
    const byId = new Map(page.items.map((m) => [m.id, m]))
    messages.value = messages.value.map((m) => byId.get(m.id) ?? m)
  } catch {
    // jim
  }
})

/* ---------------------------------------------------------------- qatorlar */

const rows = computed(() => {
  let previous = ''
  return messages.value.map((message) => {
    const label = formatDayLabel(message.createdAt)
    const dayLabel = label !== previous ? label : null
    previous = label
    return { message, dayLabel }
  })
})

/* ---------------------------------------------------------------- yozish */

const draft = ref('')
const input = ref<HTMLTextAreaElement | null>(null)
const replyTo = ref<CommunityMessageDto | null>(null)
const sending = ref(false)
const sendError = ref<string | null>(null)

function startReply(message: CommunityMessageDto): void {
  replyTo.value = message
  void nextTick(() => input.value?.focus())
}

async function send(): Promise<void> {
  const body = draft.value.trim()
  if (body.length === 0 || sending.value) return
  sending.value = true
  sendError.value = null
  try {
    const sent = await sendCommunityMessage(body, replyTo.value?.id ?? null)
    if (!messages.value.some((m) => m.id === sent.id)) messages.value = [...messages.value, sent]
    draft.value = ''
    replyTo.value = null
    await scrollToBottom()
  } catch (err) {
    sendError.value = toUserMessage(err)
  } finally {
    sending.value = false
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key === 'Enter' && !event.shiftKey && window.matchMedia('(min-width: 1024px)').matches) {
    event.preventDefault()
    void send()
  }
}

/* ---------------------------------------------------------------- moderatsiya */

const menuFor = ref<number | null>(null)

async function remove(message: CommunityMessageDto): Promise<void> {
  menuFor.value = null
  try {
    await deleteCommunityMessage(message.id)
    messages.value = messages.value.map((m) => (m.id === message.id ? { ...m, deleted: true, body: '' } : m))
  } catch (err) {
    showToast(toUserMessage(err), 'error')
  }
}

async function mute(message: CommunityMessageDto, hours: number): Promise<void> {
  menuFor.value = null
  try {
    await muteCommunityMember(message.authorId, hours, null)
    showToast(`${message.authorName} ${hours >= 24 ? `${hours / 24} kun` : `${hours} soat`} yoza olmaydi`)
  } catch (err) {
    showToast(toUserMessage(err), 'error')
  }
}

const accessError = computed(() => (accessQuery.error.value ? toUserMessage(accessQuery.error.value) : null))
</script>

<template>
  <DataStatus
    :pending="accessQuery.isPending.value"
    :error="accessError"
    :empty="false"
    :skeleton-rows="3"
    @retry="accessQuery.refetch()"
  >
    <EmptyState
      v-if="access !== null && !access.canRead"
      icon="users"
      title="Hamjamiyat tez orada ochiladi"
      text="Bu yerda barcha Mustaqil o‘quvchilar bilan fikr almashasiz. Hozircha savollaringizni kuratoringizga yozing."
    />

    <section
      v-else-if="access !== null"
      class="flex flex-col rounded-xl border border-line bg-ink-900 p-2.5 sm:p-3.5"
      :class="props.heightClass"
    >
      <header class="mb-2 flex shrink-0 items-center gap-2 border-b border-line pb-2">
        <span class="flex size-9 items-center justify-center rounded-xl bg-emerald-500/15 text-emerald-300">
          <AppIcon
            name="users"
            :size="18"
          />
        </span>
        <div class="min-w-0 flex-1">
          <p class="text-sm font-bold text-slate-100">
            Hamjamiyat
          </p>
          <p class="text-[11px] text-dim">
            {{ access.memberCount }} ta Mustaqil o‘quvchi · kuratorlar kuzatib turadi
          </p>
        </div>
        <span
          v-if="!access.enabled && access.isModerator"
          class="rounded-md bg-amber-500/15 px-2 py-0.5 text-[11px] font-semibold text-amber-300"
          title="Tizim sozlamalari → Chat → «Mustaqil o‘quvchilar hamjamiyati»"
        >O‘quvchilarga yopiq</span>
      </header>

      <div
        ref="scroller"
        class="scrollbar-slim min-h-0 flex-1 space-y-1.5 overflow-y-auto px-0.5"
      >
        <div
          v-if="hasMore"
          class="flex justify-center py-1"
        >
          <button
            type="button"
            class="rounded-full border border-line px-3 py-1 text-xs text-slate-400 hover:bg-ink-800"
            :disabled="loadingOlder"
            @click="loadOlder"
          >
            {{ loadingOlder ? 'Yuklanmoqda…' : 'Oldingi xabarlar' }}
          </button>
        </div>

        <p
          v-if="loadError !== null"
          class="py-6 text-center text-sm text-rose-300"
          v-text="loadError"
        />
        <p
          v-else-if="!loading && messages.length === 0"
          class="py-10 text-center text-sm text-slate-500"
        >
          Hali xabar yo‘q — birinchi bo‘lib salom bering 👋
        </p>

        <template
          v-for="row in rows"
          :key="row.message.id"
        >
          <ChatDaySeparator
            v-if="row.dayLabel !== null"
            :label="row.dayLabel"
          />
          <div
            class="group flex"
            :class="row.message.mine ? 'justify-end' : 'justify-start'"
          >
            <div
              class="relative max-w-[85%] rounded-2xl px-3 py-2 text-sm sm:max-w-[70%]"
              :class="row.message.mine
                ? 'rounded-br-md bg-brand-500 text-on-brand'
                : row.message.authorKind === 'Staff'
                  ? 'rounded-bl-md border border-emerald-500/30 bg-emerald-500/10 text-slate-100'
                  : 'rounded-bl-md bg-ink-800 text-slate-100'"
            >
              <p
                v-if="!row.message.mine"
                class="mb-0.5 flex items-center gap-1 text-[11px] font-bold"
                :class="row.message.authorKind === 'Staff' ? 'text-emerald-300' : 'text-brand-300'"
              >
                {{ row.message.authorName }}
                <span
                  v-if="row.message.authorKind === 'Staff'"
                  class="rounded bg-emerald-500/20 px-1 text-[9px] uppercase tracking-wide"
                >kurator</span>
              </p>

              <!-- Javob berilgan xabar -->
              <p
                v-if="row.message.replyTo !== null"
                class="mb-1 border-l-2 pl-2 text-[11px]"
                :class="row.message.mine ? 'border-on-brand/50 text-on-brand/80' : 'border-brand-400/60 text-slate-400'"
              >
                <b>{{ row.message.replyTo.authorName }}:</b>
                {{ row.message.replyTo.deleted ? 'xabar o‘chirilgan' : row.message.replyTo.snippet }}
              </p>

              <p
                v-if="row.message.deleted"
                class="italic opacity-60"
              >
                Xabar o‘chirildi
              </p>
              <p
                v-else
                class="whitespace-pre-line break-words"
                v-text="row.message.body"
              />

              <p
                class="mt-0.5 text-right text-[10px] tabular-nums"
                :class="row.message.mine ? 'text-on-brand/70' : 'text-dim'"
                :title="formatDateTimeNumeric(row.message.createdAt)"
              >
                {{ formatTime(row.message.createdAt) }}
              </p>

              <!-- Amallar: javob, o'chirish, moderatsiya -->
              <div
                v-if="!row.message.deleted"
                class="absolute -top-3 flex gap-1 opacity-100 transition-opacity lg:opacity-0 lg:group-hover:opacity-100"
                :class="row.message.mine ? 'left-1' : 'right-1'"
              >
                <button
                  v-if="access.canWrite"
                  type="button"
                  class="rounded-full border border-line bg-ink-900 px-1.5 py-0.5 text-[10px] text-slate-300 shadow"
                  aria-label="Javob berish"
                  @click="startReply(row.message)"
                >
                  ↩
                </button>
                <button
                  v-if="row.message.mine || access.isModerator"
                  type="button"
                  class="rounded-full border border-line bg-ink-900 px-1.5 py-0.5 text-[10px] text-slate-300 shadow"
                  aria-label="Ko‘proq"
                  @click="menuFor = menuFor === row.message.id ? null : row.message.id"
                >
                  ⋯
                </button>
              </div>
              <div
                v-if="menuFor === row.message.id"
                class="absolute top-4 z-10 w-44 overflow-hidden rounded-lg border border-line bg-ink-900 text-left text-xs text-slate-200 shadow-xl"
                :class="row.message.mine ? 'left-0' : 'right-0'"
              >
                <button
                  type="button"
                  class="block w-full px-3 py-2 text-left text-rose-300 hover:bg-ink-800"
                  @click="remove(row.message)"
                >
                  🗑 O‘chirish
                </button>
                <template v-if="access.isModerator && !row.message.mine && row.message.authorKind === 'Student'">
                  <button
                    v-for="hours in [1, 24, 168]"
                    :key="hours"
                    type="button"
                    class="block w-full px-3 py-2 text-left hover:bg-ink-800"
                    @click="mute(row.message, hours)"
                  >
                    🔇 {{ hours === 1 ? '1 soat' : hours === 24 ? '1 kun' : '7 kun' }} yozmasin
                  </button>
                </template>
              </div>
            </div>
          </div>
        </template>
      </div>

      <!-- Yozish -->
      <p
        v-if="!access.canWrite"
        class="mt-2 shrink-0 rounded-lg border border-line bg-ink-950 px-3 py-2 text-xs text-slate-400"
      >
        🔇 Siz vaqtincha yoza olmaysiz{{ access.mutedUntil ? ` (${formatDateTimeNumeric(access.mutedUntil)} gacha)` : '' }}.
        Xabarlarni o‘qishingiz mumkin.
      </p>
      <template v-else>
        <div
          v-if="replyTo !== null"
          class="mt-2 flex shrink-0 items-center gap-2 rounded-lg border border-brand-500/30 bg-brand-500/10 px-3 py-1.5 text-xs text-brand-200"
        >
          <span class="min-w-0 flex-1 truncate">↩ <b>{{ replyTo.authorName }}</b>: {{ replyTo.body }}</span>
          <button
            type="button"
            class="text-slate-400 hover:text-slate-100"
            aria-label="Bekor qilish"
            @click="replyTo = null"
          >
            <AppIcon
              name="close"
              :size="13"
            />
          </button>
        </div>
        <p
          v-if="sendError !== null"
          class="mt-2 shrink-0 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-1.5 text-xs text-rose-200"
          role="alert"
          v-text="sendError"
        />
        <form
          class="mt-2 flex shrink-0 items-end gap-2"
          novalidate
          @submit.prevent="send"
        >
          <ChatEmojiPicker
            v-model="draft"
            :target="input"
            :max-length="COMMUNITY_BODY_MAX"
          />
          <textarea
            ref="input"
            v-model="draft"
            rows="1"
            class="zn-input max-h-32 min-h-11 flex-1 resize-none py-2.5 text-sm"
            :maxlength="COMMUNITY_BODY_MAX"
            placeholder="Hamjamiyatga yozing…"
            aria-label="Xabar"
            @keydown="onKeydown"
          />
          <button
            type="submit"
            class="flex size-11 shrink-0 items-center justify-center rounded-xl bg-brand-500 text-on-brand transition-opacity disabled:opacity-50"
            :disabled="sending || draft.trim().length === 0"
            aria-label="Yuborish"
          >
            <AppIcon
              name="send"
              :size="18"
            />
          </button>
        </form>
      </template>
    </section>
  </DataStatus>
</template>
