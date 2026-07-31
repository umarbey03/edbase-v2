<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'

import { channelLabel, channelTone, GROUP_CHAT_BODY_MAX } from '@/entities/group-chat'
import { useAuthStore } from '@/features/auth/model/auth.store'
import type { GroupChatChannelName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseSpinner, DataStatus } from '@/shared/ui'

import { useGroupChatRoom } from '../model/useGroupChatRoom'
import { useGroupChatRows } from '../model/useGroupChatRows'
import { useGroupChatScroll } from '../model/useGroupChatScroll'
import GroupChatMessageRow from './GroupChatMessageRow.vue'

/**
 * SUHBAT EKRANI — ustoz, kurator va o'quvchi uchun BITTA komponent.
 *
 * NEGA BITTA: ekranning o'zi uch rolda ham AYNAN bir xil ishlaydi (tarix,
 * yuborish, kanal tab'lari), farq faqat RANGDA — u esa `[data-theme]` orqali
 * avtomatik keladi (`bg-brand-500` o'quvchida oltin, xodimda sariq).
 * Ikki nusxa qilsak, tuzatish har doim ikki joyda kerak bo'lardi.
 *
 * Tuzilish eski ilovadan ko'chirilgan:
 *  • sarlavha — `teacher.html` (`initTeacherChat`): kanal nishoni +
 *    "«guruh nomi» · o'quvchilar bilan";
 *  • xabarlar oqimi — `.tchat` / `.mrow` / `.mbub`;
 *  • yozish paneli — `.tchatbar` (yumaloq maydon + yumaloq yuborish tugmasi),
 *    to'ldiruvchi matn AYNAN "Xabar yozing...".
 */
const props = withDefaults(
  defineProps<{
    groupId: number
    /** Sarlavhada darhol ko'rsatish uchun (server javobi kelguncha). */
    groupName?: string
    /** `null` — serverning o'zi tanlasin (birinchi ruxsat etilgan kanal). */
    channel?: GroupChatChannelName | null
    /**
     * Xabarlar oynasining balandligi. Eski ilovada ikki panelda ikki xil edi:
     * ustozda `calc(100vh - 420px)`, o'quvchida `calc(100vh - 220px)`.
     */
    heightClass?: string
  }>(),
  {
    groupName: '',
    channel: null,
    heightClass: 'h-[calc(100vh-420px)] min-h-[300px]',
  },
)

const emit = defineEmits<{ 'update:channel': [GroupChatChannelName] }>()

const auth = useAuthStore()

/**
 * Tanlangan kanal MAHALLIY holat: foydalanuvchi tab bosganda darhol
 * o'zgaradi va `useGroupChatRoom` yangi suhbatga ulanadi. Boshlang'ich
 * qiymat prop'dan keladi (ro'yxatdan qaysi qator bosilgani).
 */
const selectedChannel = ref<GroupChatChannelName | null>(props.channel)

watch(
  () => props.channel,
  (value) => {
    selectedChannel.value = value
  },
)

const scroller = ref<HTMLElement | null>(null)

/*
  ★ AYLANMA BOG'LANISH ATAYLAB UZILGAN:
  `useGroupChatRoom` xabarlarni qirqish uchun "foydalanuvchi pastdami?" ni
  bilishi kerak, `useGroupChatScroll` esa xabarlar ro'yxatini. Ikkalasini
  bir-biriga to'g'ridan-to'g'ri bersak, aylanma import chiqardi. Shuning
  uchun holat SHU YERDA — `isAtBottom` ref'i orqali — ulanadi va qirqish
  funksiyaga (`canTrim`) o'ralib beriladi: u chaqirilgan PAYTDA o'qiladi.
*/
const isAtBottomRef = ref(true)

const room = useGroupChatRoom({
  groupId: toRef(props, 'groupId'),
  channel: selectedChannel,
  canTrim: () => isAtBottomRef.value,
})

const scroll = useGroupChatScroll({
  scroller,
  messages: room.messages,
  loadOlder: room.loadOlder,
  hasMore: room.hasMore,
})

// Skroll composable'i hisoblagan holatni yuqoridagi ref'ga ko'chiramiz.
watch(scroll.isAtBottom, (value) => {
  isAtBottomRef.value = value
})

const rows = useGroupChatRows(room.messages, computed(() => auth.userId))

const title = computed(() =>
  room.groupName.value.length > 0 ? room.groupName.value : props.groupName,
)

/** Server aytgan kanal (u tanlovni o'zi qilishi mumkin). */
const shownChannel = computed(() => room.activeChannel.value ?? selectedChannel.value ?? 'Teacher')

/**
 * Kanal tab'lari FAQAT bittadan ko'p bo'lganda chiziladi.
 * Ustozda `availableChannels` doim `["Teacher"]` — unga tab ko'rsatish
 * bosib bo'lmaydigan yagona tugma bo'lardi (jonli tekshirildi).
 */
const showChannelTabs = computed(() => room.availableChannels.value.length > 1)

function selectChannel(channel: GroupChatChannelName): void {
  if (channel === shownChannel.value) return
  selectedChannel.value = channel
  emit('update:channel', channel)
}

/* --------------------------------- yuborish -------------------------------- */

const draft = ref('')

const trimmed = computed(() => draft.value.trim())

const canSubmit = computed(
  () => trimmed.value.length > 0 && trimmed.value.length <= GROUP_CHAT_BODY_MAX && room.canSend.value,
)

/**
 * Belgilar sanog'i FAQAT chegaraga yaqinlashganda ko'rinadi.
 * Doim turgan "0/2000" hisoblagichi oddiy bir jumlalik xabar yozayotgan
 * o'quvchi uchun shovqin — u chegarani hech qachon ko'rmaydi.
 */
const showCounter = computed(() => draft.value.length > GROUP_CHAT_BODY_MAX - 200)

async function submit(): Promise<void> {
  if (!canSubmit.value) return
  const body = trimmed.value
  // Maydonni DARHOL bo'shatamiz: yuborish davomida foydalanuvchi keyingisini
  // yozishi mumkin. Xato bo'lsa matn `notice` da ko'rinadi.
  draft.value = ''
  const ok = await room.send(body)
  if (!ok) {
    // Yuborilmagan matnni QAYTARAMIZ — aks holda yozilgani yo'qolardi.
    draft.value = body
    return
  }
  scroll.jumpToBottom()
}

/**
 * `Enter` — yuborish, `Shift+Enter` — yangi qator.
 * Telefonda `Enter` odatda yangi qator bo'ladi, shuning uchun bu qoida
 * faqat klaviaturada ma'noli; yuborish tugmasi hamma joyda ishlaydi.
 */
function handleKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Enter' || event.shiftKey) return
  event.preventDefault()
  void submit()
}

/* --------------------------------- o'qildi --------------------------------- */

/*
  Suhbat ko'rilganini serverga aytamiz: ochilganda va foydalanuvchi PASTDA
  turganida yangi xabar kelganda. Yuqorida eski xabarlarni o'qiyotgan bo'lsa
  belgilanmaydi — u yangi xabarni ko'rmagan.
*/
watch(
  () => [room.messages.value.length, scroll.isAtBottom.value] as const,
  ([count, atBottom]) => {
    if (count > 0 && atBottom) room.markRead()
  },
  { immediate: true },
)
</script>

<template>
  <div>
    <!-- ============================== Sarlavha ============================== -->
    <div class="mb-2.5 flex flex-wrap items-center gap-2.5">
      <BaseBadge
        :tone="channelTone(shownChannel)"
        size="sm"
        dot
      >
        {{ channelLabel(shownChannel) }}
      </BaseBadge>
      <span
        v-if="title.length > 0"
        class="min-w-0 truncate text-xs text-slate-400"
      >
        <span
          class="font-semibold text-slate-300"
          v-text="title"
        />
        · o‘quvchilar bilan
      </span>

      <!-- Aloqa holati. "Ulangan" ATAYLAB ko'rsatilmaydi: hammasi joyida
           bo'lgani odatiy holat va uni e'lon qilish ekranni band qilardi. -->
      <span
        v-if="room.status.value === 'reconnecting' || room.status.value === 'disconnected'"
        class="ml-auto flex items-center gap-1.5 text-[11px] font-semibold text-amber-400"
      >
        <AppIcon
          name="wifi-off"
          :size="13"
        />
        {{ room.status.value === 'reconnecting' ? 'Qayta ulanmoqda…' : 'Aloqa yo‘q' }}
      </span>
    </div>

    <!-- =========================== Kanal tab'lari =========================== -->
    <!--
      ★ IKKI OQIM. O'quvchi ustozga va kuratorga ALOHIDA yozadi; ustoz
      kurator oqimini KO'RMAYDI va aksincha. Ro'yxat SERVERDAN keladi
      (`availableChannels`) — klient uni o'zi to'qimaydi, chunki ruxsat
      qoidasi serverniki va ruxsatsiz kanal so'ralsa 403 qaytadi.
    -->
    <div
      v-if="showChannelTabs"
      class="mb-2.5 flex gap-1.5"
      role="tablist"
    >
      <button
        v-for="option in room.availableChannels.value"
        :key="option"
        type="button"
        role="tab"
        :aria-selected="option === shownChannel"
        class="rounded-full border px-3 py-1.5 text-xs font-bold transition-colors"
        :class="
          option === shownChannel
            ? 'border-transparent bg-brand-500 text-on-brand'
            : 'border-line bg-ink-900 text-slate-300 hover:bg-ink-800'
        "
        @click="selectChannel(option)"
      >
        {{ channelLabel(option) }}
      </button>
    </div>

    <DataStatus
      :pending="room.isPending.value"
      :error="room.loadError.value"
      :empty="false"
      :retrying="false"
      :skeleton-rows="3"
      @retry="room.retry()"
    >
      <!-- ============================ Xabarlar ============================= -->
      <div
        ref="scroller"
        class="scrollbar-slim flex flex-col overflow-y-auto overflow-x-hidden"
        :class="props.heightClass"
      >
        <!-- Eskiroq sahifa yuklanayotgani (yuqoriga skroll qilinganda). -->
        <div
          v-if="room.isLoadingOlder.value"
          class="flex justify-center py-2"
        >
          <BaseSpinner size="sm" />
        </div>
        <p
          v-else-if="!room.hasMore.value && rows.length > 0"
          class="py-2 text-center text-[11px] text-dim"
        >
          Suhbat boshlanishi
        </p>

        <!--
          Bo'sh holat — eski ilovadagi matn AYNAN:
          `student.html`: "Hali xabar yo'q. Birinchi bo'lib yozing!"
        -->
        <p
          v-if="rows.length === 0"
          class="m-auto px-4 text-center text-sm text-slate-400"
        >
          Hali xabar yo‘q. Birinchi bo‘lib yozing!
        </p>

        <template
          v-for="row in rows"
          :key="row.id"
        >
          <!-- Kun ajratgichi — eski `.datesep`. -->
          <div
            v-if="row.dayLabel !== null"
            class="my-2 self-center rounded-full border border-line bg-ink-950 px-3 py-0.5 text-[11px] text-slate-400"
            v-text="row.dayLabel"
          />
          <GroupChatMessageRow
            :sender-name="row.senderName"
            :body="row.body"
            :time="row.time"
            :is-own="row.isOwn"
            :show-header="row.showHeader"
            :role="row.senderRole"
          />
        </template>
      </div>
    </DataStatus>

    <!-- ============================ Ogohlantirish =========================== -->
    <!-- 429, ruxsat xatosi va boshqalar. Yopish tugmasi bilan — xabar
         ekranda abadiy osilib qolmasin. -->
    <div
      v-if="room.notice.value !== null"
      class="mt-2 flex items-start gap-2 rounded-xl border border-amber-500/30 bg-amber-500/10 px-3 py-2"
      role="alert"
    >
      <p
        class="min-w-0 flex-1 text-xs leading-relaxed text-amber-200"
        v-text="room.notice.value"
      />
      <button
        type="button"
        class="shrink-0 text-amber-300/70 transition-colors hover:text-amber-200"
        aria-label="Yopish"
        @click="room.dismissNotice()"
      >
        <AppIcon
          name="close"
          :size="14"
        />
      </button>
    </div>

    <!-- ============================ Yozish paneli =========================== -->
    <form
      class="mt-2.5 flex items-end gap-2"
      novalidate
      @submit.prevent="submit"
    >
      <div class="min-w-0 flex-1">
        <label
          class="sr-only"
          for="group-chat-input"
        >
          Xabar matni
        </label>
        <textarea
          id="group-chat-input"
          v-model="draft"
          class="zn-input max-h-32 min-h-11 w-full resize-y rounded-3xl py-2.5"
          rows="1"
          :maxlength="GROUP_CHAT_BODY_MAX"
          placeholder="Xabar yozing..."
          @keydown="handleKeydown"
        />
        <!--
          Chegara SERVER bilan bir xil (2000). Server uzunini kesib tashlaydi,
          shuning uchun `maxlength` bilan oldini olamiz: jimgina kesilgan
          xabar foydalanuvchi uchun ma'lumot yo'qolishi bo'lardi.
        -->
        <p
          v-if="showCounter"
          class="mt-1 pr-2 text-right text-[11px] tabular-nums text-dim"
        >
          {{ draft.length }} / {{ GROUP_CHAT_BODY_MAX }}
        </p>
      </div>

      <button
        type="submit"
        class="tap-target flex size-11 shrink-0 items-center justify-center rounded-full bg-brand-500 font-bold text-on-brand transition-colors disabled:opacity-40"
        :disabled="!canSubmit"
        :aria-label="
          room.cooldownSeconds.value > 0
            ? `${room.cooldownSeconds.value} soniyadan so‘ng yuborish mumkin`
            : 'Yuborish'
        "
      >
        <!-- 429 dan keyin tugmada orqaga sanoq turadi: foydalanuvchi QANCHA
             kutishini ko'rib tursa, qayta-qayta bosib oynani uzaytirmaydi. -->
        <span
          v-if="room.cooldownSeconds.value > 0"
          class="text-xs tabular-nums"
          v-text="room.cooldownSeconds.value"
        />
        <BaseSpinner
          v-else-if="room.isSending.value"
          size="sm"
        />
        <AppIcon
          v-else
          name="send"
          :size="18"
        />
      </button>
    </form>
  </div>
</template>
