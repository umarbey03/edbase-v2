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
import { GroupChatRoom, GroupChatThreadList } from '@/features/group-chat'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import type { ConversationDto, GroupChatThreadDto } from '@/shared/types'
import { AppIcon, BaseAvatar, DataStatus } from '@/shared/ui'

/**
 * ============================================================================
 *  CHAT — eski `student.html` dagi `#chat` bo'limi
 * ============================================================================
 *
 * ★ GURUH CHATI VA KURATOR DM'I QANDAY BIRGA YASHAYDI:
 *
 * IKKALASI BITTA RO'YXATDA, lekin ATAYLAB AJRATILGAN ikki bo'limda — bu
 * eski ilovaning yechimi va u shundayligicha ko'chirildi
 * (`student.html`, `renderChatList()`):
 *
 *   1) TEPADA, "pin qilingan" — «📌 Kurator — shaxsiy chat».
 *      Faqat o'quvchi va kurator ko'radi. Eski markupda u firuza chegara va
 *      gradient bilan ajratilgan (`border: 1px solid rgba(34,211,238,.35)`),
 *      chunki "faqat menga" atalgan yozishmani ADASHIB guruhga yuborish eng
 *      qimmat xato bo'lardi.
 *
 *   2) PASTDA — GURUH chatlari, har guruh uchun IKKI qatorgacha:
 *      "Ustoz chati" va "Kurator chati" (server `/threads` da aynan shunday
 *      qaytaradi — jonli tekshirilgan). Bu yerda yozilgani guruhdagi
 *      HAMMAGA ko'rinadi.
 *
 * NEGA ALOHIDA TAB YOKI ALOHIDA EKRAN EMAS:
 *  • o'quvchi karkasidagi pastki 5 tab eski ilovadan AYNAN ko'chirilgan va
 *    ularning tartibi/nomi o'zgartirilmaydi
 *    (`entities/user/model/navigation.ts`) — oltinchi tab qo'shish shu
 *    qoidani buzardi;
 *  • o'quvchi uchun bu ikkisi bitta savolning ikki manzili: "buni hammaga
 *    yozaymi yoki faqat kuratorgami". Ikki xil ekranga bo'lib qo'ysak, u har
 *    safar qaysi ekranda ekanini eslab yurishi kerak bo'lardi;
 *  • eski ilovada AYNAN shunday edi — bugungi o'quvchilar shu ro'yxatni
 *    bilishadi va qayta o'rganishlari shart emas.
 *
 * XAVFSIZLIK JIHATI: ro'yxat bitta bo'lsa ham, YOZISH oqimlari hech qachon
 * aralashmaydi — har qator o'z ekranini ochadi va guruh chatida yuqorida
 * doim kanal nishoni turadi ("Ustoz chati" / "Kurator chati"), ya'ni o'quvchi
 * kimga yozayotganini ko'rib turadi.
 *
 * Ro'yxat va ochilgan suhbat ALOHIDA MARSHRUT emas, bitta tab ichidagi holat
 * — eski ilovadagidek (`chat-list-view` / `chat-room-view`): "Orqaga" tugmasi
 * brauzer tarixiga tegmasdan ro'yxatga qaytaradi.
 */
const queryClient = useQueryClient()

/* ====================== 1-BO'LIM: shaxsiy (kurator DM) ===================== */

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

/**
 * BIR VAQTDA FAQAT BITTASI ochiq. Ikki `ref` ataylab bir-birini inkor
 * qiladi: `openConversation` guruh suhbatini yopadi va aksincha — aks holda
 * "orqaga" bosilganda ekranda ikkinchi suhbat qolib ketardi.
 */
const activePeer = ref<ConversationDto | null>(null)
const activeThread = ref<GroupChatThreadDto | null>(null)

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
  activeThread.value = null
  activePeer.value = conversation
}

function openGroupThread(thread: GroupChatThreadDto): void {
  activePeer.value = null
  activeThread.value = thread
}

function backToList(): void {
  activePeer.value = null
  activeThread.value = null
}

const showList = computed(() => activePeer.value === null && activeThread.value === null)
</script>

<template>
  <div>
    <!-- ============================== RO'YXAT ============================== -->
    <template v-if="showList">
      <h2
        class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-brand-300"
      >
        <AppIcon
          name="chat"
          :size="15"
        />
        Chatlar
      </h2>

      <!--
        ★ 1-BO'LIM — SHAXSIY (pin qilingan, eng tepada).
        Eski ilovadagi firuza ajratma saqlangan: bu yozishmani guruh
        chatlaridan KO'Z BILAN farqlash mumkin bo'lishi kerak.
      -->
      <DataStatus
        :pending="conversationsQuery.isPending.value"
        :error="conversationsError"
        :empty="false"
        :retrying="conversationsQuery.isFetching.value"
        :skeleton-rows="1"
        @retry="conversationsQuery.refetch()"
      >
        <ul
          v-if="conversations.length > 0"
          class="mb-4 space-y-2"
        >
          <li
            v-for="conversation in conversations"
            :key="conversation.peerId"
          >
            <!-- Tint asosi `-500` (shkala shartnomasi: `style.css`). -->
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-[14px] border border-sky-500/30 bg-sky-500/[0.07] px-3.5 py-3 text-left transition-colors hover:bg-sky-500/[0.13]"
              @click="openConversation(conversation)"
            >
              <BaseAvatar
                :name="conversation.peerName ?? '?'"
                size="md"
              />
              <span class="min-w-0 flex-1">
                <span class="flex items-center gap-2">
                  <!-- Matn eski ilovadan: "📌 Kurator — shaxsiy chat". -->
                  <span class="min-w-0 flex-1 truncate text-sm font-semibold text-sky-200">
                    📌 {{ peerRoleLabel(conversation.peerRole) }} — shaxsiy chat
                  </span>
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
                  class="mt-0.5 block truncate text-[11px] text-dim"
                  v-text="conversation.peerName ?? '—'"
                />
              </span>
            </button>
          </li>
        </ul>

        <!-- Kurator biriktirilmagan holat — eski ilovadagi matn. -->
        <p
          v-else
          class="mb-4 rounded-[14px] border border-line bg-ink-900 px-3.5 py-3 text-xs text-slate-400"
        >
          Sizga hali kurator biriktirilmagan.
        </p>
      </DataStatus>

      <!--
        ★ 2-BO'LIM — GURUH chatlari. Har guruh uchun ikki qator bo'lishi
        MUMKIN ("Ustoz chati" / "Kurator chati") — bu server qaroriga bog'liq
        (`availableChannels`), klient uni o'zi to'qimaydi.
      -->
      <GroupChatThreadList
        empty-title="Guruh chati yo‘q"
        empty-text="Guruhga qo‘shilganingizdan keyin guruh chatlari shu yerda ochiladi."
        @open="openGroupThread"
      />
    </template>

    <!-- ========================== GURUH SUHBATI ============================ -->
    <template v-else-if="activeThread !== null">
      <div class="mb-3 mt-2 flex items-center gap-3">
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800"
          @click="backToList"
        >
          <AppIcon
            name="arrow-left"
            :size="15"
          />
          Orqaga
        </button>
        <h3
          class="min-w-0 flex-1 truncate text-base font-extrabold text-slate-100"
          v-text="activeThread.groupName"
        />
      </div>

      <!--
        ★ `:key` guruh + kanal bo'yicha — boshqa suhbatga o'tilganda komponent
        qaytadan yaratiladi (eski skroll joyi va yozilgan matn qolib ketmasin).

        Balandlik o'quvchi karkasiga moslangan: eski ilovada
        `.chat { height: calc(100vh - 220px); min-height: 280px }` edi, lekin
        u yerda kanal tab'lari yo'q edi — v2 da ular qo'shilgani uchun ayirma
        kattaroq. `dvh` (`vh` EMAS): telefon brauzerining manzil paneli
        yig'ilganda `vh` "sakrab" ketadi.
      -->
      <GroupChatRoom
        :key="`${activeThread.groupId}:${activeThread.channel}`"
        :group-id="activeThread.groupId"
        :group-name="activeThread.groupName"
        :channel="activeThread.channel"
        height-class="h-[calc(100dvh-340px)] min-h-[260px]"
      />
    </template>

    <!-- ========================= SHAXSIY SUHBAT ============================ -->
    <template v-else-if="activePeer !== null">
      <div class="mb-3 mt-2 flex items-center gap-3">
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800"
          @click="backToList"
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
            v-text="`${peerRoleLabel(activePeer.peerRole)} — shaxsiy chat`"
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
        empty-title="Hali savol yo‘q"
        empty-text="Birinchi savolingizni yozing!"
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
