<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, nextTick, ref, watch } from 'vue'

import {
  DM_BODY_MAX,
  fetchThread,
  markConversationRead,
  sendDirectMessage,
  withDayLabels,
} from '@/entities/direct-message'
import {
  DirectAttachment,
  DM_ATTACHMENT_MAX_FILES,
  sendDirectMessageAttachments,
  VoiceMessageButton,
} from '@/features/direct-message'
import { ChatDaySeparator, ChatNotice } from '@/features/group-chat'
import ChatEmojiPicker from '@/features/group-chat/ui/ChatEmojiPicker.vue'
import { ImageAnnotator } from '@/features/image-annotator'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import { formatFileSize } from '@/shared/lib/text'
import type { ConversationDto } from '@/shared/types'
import { AppIcon, DataStatus, ImageLightbox } from '@/shared/ui'

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
 * Kun ajratgichlari — qoida ENDI entity qatlamida (`withDayLabels`), chunki
 * o'quvchining kurator chati ham AYNAN shu ajratgichni chizadi (R28).
 */
const grouped = computed(() => withDayLabels(messages.value))

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

/** Emoji tanlagichga kursor joyi kerak (`ChatEmojiPicker` izohi). */
const input = ref<HTMLTextAreaElement | null>(null)

// Boshqa o'quvchiga o'tilganda yozib qo'yilgan matn ketmasin.
watch(peerId, () => {
  draft.value = ''
  sendError.value = null
  pendingFiles.value = []
  attachmentError.value = null
})

/*
  ════════════════════════════════════════════════════════════════════════
   BIRIKTIRMALAR, OVOZ VA RASM TAHRIRI (24/7 chat, 2026-11-18)
  ════════════════════════════════════════════════════════════════════════
  🔴 ILGARI BU OYNA FAQAT MATN EDI: o'quvchi yuborgan rasm va ovozli xabar
     kuratorga UMUMAN ko'rinmasdi (biriktirmalar chizilmasdi), u esa faqat
     matn bilan javob bera olardi. Endi o'quvchi chatidagi AYNI imkoniyatlar:
     fayl, ovozli xabar va rasmni belgilab qayta yuborish.
*/
const fileInput = ref<HTMLInputElement | null>(null)
const pendingFiles = ref<File[]>([])
const attachmentError = ref<string | null>(null)
const uploadPercent = ref<number | null>(null)
const zoomUrl = ref<string | null>(null)

function pickFiles(): void {
  fileInput.value?.click()
}

function onFilesChosen(event: Event): void {
  const target = event.target as HTMLInputElement
  const chosen = Array.from(target.files ?? [])
  target.value = ''
  if (chosen.length === 0) return
  if (pendingFiles.value.length + chosen.length > DM_ATTACHMENT_MAX_FILES) {
    attachmentError.value = `Bitta xabarga ko‘pi bilan ${DM_ATTACHMENT_MAX_FILES} ta fayl.`
    return
  }
  attachmentError.value = null
  pendingFiles.value = [...pendingFiles.value, ...chosen]
}

function removeFile(index: number): void {
  pendingFiles.value = pendingFiles.value.filter((_, position) => position !== index)
  attachmentError.value = null
}

const attachMutation = useMutation({
  mutationFn: (input: { files: File[], body: string, durationSec?: number }) =>
    sendDirectMessageAttachments({
      peerId: peerId.value,
      files: input.files,
      body: input.body,
      moduleLessonId: null,
      durationSec: input.durationSec ?? null,
      onProgress: (progress) => {
        uploadPercent.value = progress.percent
      },
    }),
  onSuccess: (_data, input) => {
    if (input.body.length > 0) draft.value = ''
    if (input.files === pendingFiles.value) pendingFiles.value = []
    attachmentError.value = null
    sendError.value = null
    uploadPercent.value = null
    void threadQuery.refetch()
    void queryClient.invalidateQueries({ queryKey: ['dm', 'conversations'] })
  },
  onError: (error: Error) => {
    uploadPercent.value = null
    sendError.value = toUserMessage(error)
  },
})

const editSource = ref<Blob | null>(null)
const editName = ref<string | null>(null)
const editingIndex = ref<number | null>(null)

function editAttachment(blob: Blob, name: string): void {
  editingIndex.value = null
  editName.value = name
  editSource.value = blob
}

function editPending(index: number): void {
  const file = pendingFiles.value[index]
  if (file === undefined) return
  editingIndex.value = index
  editName.value = file.name
  editSource.value = file
}

function closeEditor(): void {
  editSource.value = null
  editingIndex.value = null
}

function onEdited(file: File): void {
  const index = editingIndex.value
  closeEditor()
  if (index !== null) {
    pendingFiles.value = pendingFiles.value.map((f, i) => (i === index ? file : f))
    return
  }
  attachMutation.mutate({ files: [file], body: '' })
}

function onVoiceRecorded(file: File, durationSec: number): void {
  attachMutation.mutate({ files: [file], body: '', durationSec })
}

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
    (draft.value.trim().length > 0 || pendingFiles.value.length > 0) &&
    draft.value.length <= DM_BODY_MAX &&
    !sendMutation.isPending.value &&
    !attachMutation.isPending.value,
)

/**
 * Belgilar sanog'i FAQAT chegaraga yaqinlashganda ko'rinadi — guruh
 * chatidagi qoida bilan AYNAN bir xil (`GroupChatRoom` izohi): doim turgan
 * "0/2000" bir jumlalik javob yozayotgan xodim uchun shovqin.
 */
const showCounter = computed(() => draft.value.length > DM_BODY_MAX - 200)

function submit(): void {
  if (!canSend.value) return
  if (pendingFiles.value.length > 0) {
    attachMutation.mutate({ files: pendingFiles.value, body: draft.value.trim() })
    return
  }
  sendMutation.mutate({ id: peerId.value, body: draft.value.trim() })
}
</script>

<template>
  <!--
    ★ `min-h-[60dvh]` → `h-[60dvh]` (2026-08-13, talab: *"chat writing part
    should be stuck in its place"*). `min-h` — POL edi, ya'ni kartochka
    xabarlar ko'paygan sari CHEKSIZ o'sardi va yozish paneli u bilan birga
    pastga, ekran tashqarisiga ketardi; ichkaridagi `flex-1` skroll sohasi
    esa hech qachon ishga tushmasdi (cho'zilishga chegara yo'q edi).
    Endi balandlik CHEGARA: ro'yxat skrollanadi, panel joyida qoladi.

    `min-h-[320px]` — telefon yotiq holati uchun pol (60dvh o'sha yerda
    ~200px bo'lib qolardi).
  -->
  <section class="flex h-[60dvh] min-h-[320px] flex-col rounded-xl border border-line bg-ink-900 p-3.5">
    <header class="mb-2.5 flex shrink-0 items-center gap-2.5 border-b border-line pb-2.5">
      <!--
        Ro'yxatga qaytish (eski `#dm-back`). Ikki ustunli desktopda kerak emas.

        🔴 `lg:hidden`, `md:hidden` EMAS — `TeacherInbox` dagi ikki ustunli
        setka bilan BIR XIL chegara. Ular ajralib qolsa 768–1023px oralig'ida
        ro'yxat yashirinib, qaytish tugmasi ham yo'q bo'lib, foydalanuvchi
        yozishmadan chiqa olmasdi.
      -->
      <button
        type="button"
        class="tap-target flex items-center justify-center rounded-lg border border-line bg-ink-800 px-2 text-slate-100 transition-colors hover:bg-ink-750 lg:hidden"
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

    <!--
      ★ `DataStatus` SKROLL SOHASINING ICHIDA (2026-08-13): tashqarida
      turganda yuklanish holati butun sohani almashtirardi
      (`DataStatus.vue:40-49`) va yozishma har ochilganda yozish paneli bir
      sakrab, keyin joyiga tushardi. Endi kartochka tuzilishi har uch holatda
      bir xil: sarlavha → skroll sohasi → yozish paneli.
    -->
    <div
      ref="scroller"
      class="chat-scroll-container scrollbar-slim min-h-0 flex-1 space-y-2 overflow-y-auto pb-1"
    >
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
        <template
          v-for="row in grouped"
          :key="row.message.id"
        >
          <!-- Kun ajratgichi — uch chat ekrani uchun BITTA komponent. -->
          <ChatDaySeparator
            v-if="row.dayLabel !== null"
            :label="row.dayLabel"
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
                v-if="row.message.body.length > 0"
                class="whitespace-pre-line break-words text-[13px] leading-relaxed"
                v-text="row.message.body"
              />
              <div
                v-if="(row.message.attachments ?? []).length > 0"
                :class="row.message.body.length > 0 ? 'mt-1.5' : ''"
              >
                <DirectAttachment
                  v-for="item in row.message.attachments ?? []"
                  :key="item.id"
                  :attachment="item"
                  editable
                  @zoom="(url) => (zoomUrl = url)"
                  @edit="editAttachment"
                />
              </div>
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
      </DataStatus>
    </div>

    <!-- `shrink-0`: kartochka balandligi chegaralangan — siqiladigan yagona
         element xabarlar sohasi bo'lsin, yozish paneli emas. -->
    <div
      v-if="pendingFiles.length > 0 || attachmentError !== null"
      class="mt-2 shrink-0"
    >
      <p
        v-if="attachmentError !== null"
        class="mb-1.5 text-[11px] text-rose-400"
        role="alert"
        v-text="attachmentError"
      />
      <ul class="flex flex-wrap gap-1.5">
        <li
          v-for="(file, index) in pendingFiles"
          :key="`${file.name}:${index}`"
          class="flex max-w-full items-center gap-1.5 rounded-full border border-line bg-ink-950 py-1 pl-2.5 pr-1"
        >
          <AppIcon
            name="paperclip"
            :size="12"
          />
          <span
            class="min-w-0 max-w-40 truncate text-[11.5px] text-slate-300"
            v-text="file.name"
          />
          <span
            class="shrink-0 text-[10.5px] tabular-nums text-dim"
            v-text="formatFileSize(file.size)"
          />
          <button
            v-if="file.type.startsWith('image/')"
            type="button"
            class="flex size-5 shrink-0 items-center justify-center rounded-full text-slate-400 hover:bg-ink-800 hover:text-slate-100"
            aria-label="Rasmni tahrirlash"
            @click="editPending(index)"
          >
            <AppIcon
              name="edit"
              :size="12"
            />
          </button>
          <button
            type="button"
            class="flex size-5 shrink-0 items-center justify-center rounded-full text-slate-400 hover:bg-ink-800 hover:text-slate-100"
            aria-label="Faylni olib tashlash"
            @click="removeFile(index)"
          >
            <AppIcon
              name="close"
              :size="12"
            />
          </button>
        </li>
      </ul>
      <div
        v-if="uploadPercent !== null"
        class="mt-1.5 h-1 overflow-hidden rounded-full bg-ink-800"
      >
        <div
          class="h-full rounded-full bg-brand-500 transition-[width]"
          :style="{ width: `${uploadPercent}%` }"
        />
      </div>
    </div>

    <form
      class="mt-2.5 flex shrink-0 items-end gap-2"
      novalidate
      @submit.prevent="submit"
    >
      <input
        ref="fileInput"
        class="hidden"
        type="file"
        multiple
        accept="image/*,audio/*,application/pdf"
        @change="onFilesChosen"
      >
      <!-- Emoji — guruh chati va o'quvchi chatidagi bilan AYNAN bir xil. -->
      <ChatEmojiPicker
        v-model="draft"
        :target="input"
        :max-length="DM_BODY_MAX"
      />

      <div class="min-w-0 flex-1">
        <!--
          ★ `resize-y` o'rniga `field-sizing-content`: qo'lda cho'zish
          kartochkaning qat'iy balandligida yozish panelini pastga surardi.
          Sabab `GroupChatRoom` izohida.
        -->
        <textarea
          ref="input"
          v-model="draft"
          class="zn-input max-h-32 min-h-11 w-full resize-none overflow-y-auto py-2.5 field-sizing-content"
          rows="1"
          :maxlength="DM_BODY_MAX"
          placeholder="Javob yozing..."
        />
        <!-- Chegara SERVER bilan bir xil (2000) — guruh chatidagi qoida. -->
        <p
          v-if="showCounter"
          class="mt-1 pr-2 text-right text-[11px] tabular-nums text-dim"
        >
          {{ draft.length }} / {{ DM_BODY_MAX }}
        </p>
      </div>

      <!--
        ★ TUGMADA YOZUV YO'Q (2026-08-13, R28): o'quvchi chatida va guruh
        chatida bu tugma faqat ikonka, bu yerda esa `sm:` dan boshlab
        "Yuborish" yozuvi chiqardi — ya'ni bir xil amal uch ekranda ikki xil
        kenglikda turardi. Ma'no `aria-label` da qoladi.
      -->
      <button
        type="button"
        class="tap-target flex shrink-0 items-center justify-center rounded-xl text-slate-400 hover:bg-ink-800 hover:text-slate-200"
        aria-label="Fayl biriktirish"
        title="Fayl biriktirish"
        @click="pickFiles"
      >
        <AppIcon
          name="paperclip"
          :size="18"
        />
      </button>

      <VoiceMessageButton
        :disabled="attachMutation.isPending.value"
        @recorded="onVoiceRecorded"
      />

      <button
        type="submit"
        class="tap-target flex shrink-0 items-center justify-center rounded-xl bg-brand-500 px-4 text-sm font-bold text-on-brand transition-colors disabled:opacity-40"
        :disabled="!canSend"
        aria-label="Yuborish"
      >
        <AppIcon
          name="send"
          :size="16"
        />
      </button>
    </form>

    <!--
      Yuborilmagan xabar — guruh chatidagi bilan BITTA komponent
      (`ChatNotice`). Yozilgan matn maydonda qoladi, shuning uchun
      ogohlantirishni YOPISH mumkin.
    -->
    <ChatNotice
      v-if="sendError !== null"
      class="mt-2"
      :text="sendError"
      @dismiss="sendError = null"
    />

    <ImageLightbox
      :src="zoomUrl"
      @close="zoomUrl = null"
    />
    <ImageAnnotator
      :source="editSource"
      :file-name="editName"
      @close="closeEditor"
      @done="onEdited"
    />
  </section>
</template>
