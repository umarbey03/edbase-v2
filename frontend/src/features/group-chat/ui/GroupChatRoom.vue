<script setup lang="ts">
import { computed, ref, toRef, watch } from 'vue'

import { channelLabel, channelTone, GROUP_CHAT_BODY_MAX } from '@/entities/group-chat'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { formatFileSize } from '@/shared/lib/text'
import type { GroupChatChannelName } from '@/shared/types'
import { AppIcon, BaseBadge, BaseModal, BaseSpinner, DataStatus } from '@/shared/ui'

import { CHAT_ATTACHMENT_MAX_FILES } from '../lib/send-chat-attachments'
import { useGroupChatRoom } from '../model/useGroupChatRoom'
import { useGroupChatRows } from '../model/useGroupChatRows'
import { useGroupChatScroll } from '../model/useGroupChatScroll'
import ChatDaySeparator from './ChatDaySeparator.vue'
import ChatEmojiPicker from './ChatEmojiPicker.vue'
import ChatNotice from './ChatNotice.vue'
import GroupChatMessageRow from './GroupChatMessageRow.vue'

/**
 * SUHBAT EKRANI — ustoz, kurator va o'quvchi uchun BITTA komponent.
 *
 * NEGA BITTA: ekranning o'zi uch rolda ham AYNAN bir xil ishlaydi (tarix,
 * yuborish, o'qildi), farq faqat RANGDA — u esa `[data-theme]` orqali
 * avtomatik keladi (`bg-brand-500` o'quvchida oltin, xodimda sariq).
 * Ikki nusxa qilsak, tuzatish har doim ikki joyda kerak bo'lardi.
 *
 * Tuzilish eski ilovadan ko'chirilgan:
 *  • sarlavha — `teacher.html` (`initTeacherChat`): kanal nishoni +
 *    "«guruh nomi» · o'quvchilar bilan";
 *  • xabarlar oqimi — `.tchat` / `.mrow` / `.mbub`;
 *  • yozish paneli — `.tchatbar` (yumaloq maydon + yumaloq yuborish tugmasi),
 *    to'ldiruvchi matn AYNAN "Xabar yozing...".
 *
 * ═══════════════════════════════════════════════════════════════════════════
 * ★ BALANDLIK — CHAQIRUVCHINIKI (2026-08-13, talab: *"chat writing part
 * should be stuck in its place"*).
 *
 * Ilgari bu yerda `heightClass` prop'i bor edi va uning SUKUT qiymati
 * `h-[calc(100dvh-420px)]` — qat'iy balandlikdagi xabarlar ro'yxati. Xodim
 * sahifalari qiymat bermagani uchun aynan o'sha 420 raqami ishlardi va
 * ro'yxat TEPASIDAGI har bir o'zgarish (sahifa sarlavhasi, tablar, banner)
 * yozish panelini pastga — ko'pincha ekran tashqarisiga — surardi.
 *
 * Endi shartnoma bitta: KOMPONENT O'Z OTA-ONASINI TO'LDIRADI. Ildiz —
 * ustun flex'i, xabarlar ro'yxati `flex-auto` bilan qolgan joyni oladi,
 * sarlavha/ogohlantirish/yozish paneli esa `shrink-0`. Chaqiruvchi
 * balandlikni BIR MARTA chegaralaydi (`ChatFillColumn` yoki o'quvchi
 * sahifasidagi setka katagi) — shunda yozish paneli DOIM eng pastda,
 * qimirlamay turadi.
 *
 * ★ Balandligi chegaralanmagan ota-onada ham ekran BUZILMAYDI: `flex-auto`
 * (`flex-1` EMAS) mazmun bo'yicha o'sadi, ya'ni ro'yxat nol balandlikka
 * tushib qolmaydi. `flex-1` da (asos 0) aynan shunday bo'lardi.
 * ═══════════════════════════════════════════════════════════════════════════
 */
const props = withDefaults(
  defineProps<{
    groupId: number
    /** Sarlavhada darhol ko'rsatish uchun (server javobi kelguncha). */
    groupName?: string
    /** `null` — serverning o'zi tanlasin (birinchi ruxsat etilgan kanal). */
    channel?: GroupChatChannelName | null
  }>(),
  {
    groupName: '',
    channel: null,
  },
)

const auth = useAuthStore()

/**
 * Tanlangan kanal — ro'yxatdan qaysi qator bosilgani (`channel` prop'i).
 * MAHALLIY ref'da saqlanadi, chunki `useGroupChatRoom` unga reaktiv ulanadi
 * va prop o'zgarganda (boshqa qator tanlanganda) suhbat qayta ulanishi kerak.
 *
 * ★ `null` — SERVER tanlaydi. `TeacherGroupPage` ataylab kanal bermaydi:
 * ustozga `Teacher`, kuratorga `Curator` oqimini serverning o'zi beradi va
 * bu qoida klientda TAKRORLANMAYDI.
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

/*
  ★ KANAL TAB'LARI OLIB TASHLANDI (2026-08-13, talab: *"chat qismda o'zi 2 ta
  ustoz va curator chatiga ajratishi yetarli, yana ichiga kirganda 2 ga
  ajratishi kerakmas"*).

  Bo'linish BIR MARTA — suhbatlar RO'YXATIDA bo'ladi: server `/threads` da
  har guruh uchun "Ustoz chati" va "Kurator chati" qatorlarini alohida
  qaytaradi, ya'ni foydalanuvchi qaysi oqimga kirayotganini ro'yxatda
  tanlaydi. Suhbat ichida ikkinchi marta tanlash o'sha tanlovni takrorlardi.

  ★ HECH KIM KIRISHNI YO'QOTMADI: tab'lar `availableChannels.length > 1`
  bo'lgandagina chizilardi (o'quvchi, admin, o'quv bo'limi) — ular ikkala
  qatorni ham ro'yxatdan ochadi. Ustoz va kuratorda tab umuman ko'rinmasdi.

  🔴 `availableChannels` DTO'dan OLIB TASHLANMAYDI: u ruxsat ma'lumoti
  (`useGroupChatHub.ts:105-126` o'sha ro'yxatga suyanadi) va backend
  integratsion testlari uni tekshiradi.

  Qaysi oqimda ekanini yuqoridagi nishon (`BaseBadge`) aytib turadi — u
  ATAYLAB qoldirilgan: "kimga yozayapman" savoli hech qachon javobsiz
  qolmasligi kerak.
*/

/* --------------------------------- yuborish -------------------------------- */

const draft = ref('')

/**
 * Yozish maydonining o'zi — emoji tanlagichga kursor joyi uchun kerak
 * (`ChatEmojiPicker` matnni AYNAN kursor turgan joyga qo'yadi).
 */
const input = ref<HTMLTextAreaElement | null>(null)

const trimmed = computed(() => draft.value.trim())

/* ------------------------------- biriktirmalar ----------------------------- */

/**
 * ★ R16b · TANLANGAN, LEKIN HALI YUBORILMAGAN FAYLLAR.
 *
 * 🔴 FAYLLAR SERVERGA FAQAT "YUBORISH" BOSILGANDA KETADI — tanlanganda
 * EMAS. Sabab server tomonida: xabar va biriktirmalar BITTA tranzaksiyada
 * yoziladi, ya'ni "yukladim, keyin fikrimdan qaytdim" degan holat ombordа
 * pul turadigan YETIM obyekt qoldirmaydi (batafsil: backend
 * `GroupChatAttachment` izohi).
 *
 * ⚠️ NARXI OCHIQ: progress FAYL BOSHIGA emas, butun so'rov bo'yicha
 * ko'rinadi va "yozayotganda fonda yuklab turish" yo'q. Telegram'dan
 * farqi shu.
 */
const pendingFiles = ref<File[]>([])
const fileInput = ref<HTMLInputElement | null>(null)

const attachmentError = ref<string | null>(null)

function pickFiles(): void {
  fileInput.value?.click()
}

function onFilesChosen(event: Event): void {
  const input = event.target as HTMLInputElement
  const chosen = Array.from(input.files ?? [])

  /*
    Bir xil faylni ikki marta tanlash mumkin bo'lsin uchun `value` DARHOL
    tozalanadi: aks holda brauzer o'sha faylni qayta tanlaganda `change`
    hodisasi umuman chiqmasdi.
  */
  input.value = ''

  if (chosen.length === 0) return

  const room = pendingFiles.value.length + chosen.length
  if (room > CHAT_ATTACHMENT_MAX_FILES) {
    attachmentError.value = `Bitta xabarga ko‘pi bilan ${CHAT_ATTACHMENT_MAX_FILES} ta fayl.`
    return
  }

  attachmentError.value = null
  pendingFiles.value = [...pendingFiles.value, ...chosen]
}

function removeFile(index: number): void {
  pendingFiles.value = pendingFiles.value.filter((_, position) => position !== index)
  attachmentError.value = null
}

const hasFiles = computed(() => pendingFiles.value.length > 0)

/**
 * Yuborish mumkinmi.
 *
 * ★ IKKI HOLAT: matn bilan YOKI fayl bilan. Fayl bo'lsa MATN SHART EMAS —
 * bu R16b ning aynan mohiyati (izohsiz surat, Telegram'dagi kabi) va
 * server ham shu invariantni saqlaydi ("matn bo'sh bo'lsa kamida bitta
 * biriktirma").
 */
const canSubmit = computed(
  () =>
    (trimmed.value.length > 0 || hasFiles.value)
    && trimmed.value.length <= GROUP_CHAT_BODY_MAX
    && room.canSend.value,
)

/* --------------------------------- lightbox -------------------------------- */

/**
 * Kattalashtirilgan rasm.
 *
 * ★ ICHMA-ICH `BaseModal` XAVFSIZ (2026-08-11 refaktoridan keyin):
 * `useModalHost` ESC uchun QATLAM STEKINI yuritadi va faqat eng tepadagi
 * qatlamni yopadi. Ilgari har oyna `document` ga o'z tinglovchisini
 * qo'yardi va ESC ikkala qatlamni birga yopardi — aynan shu sabab
 * `GradeDialog` da kattalashtirish O'CHIRIB qo'yilgan edi.
 */
const zoomUrl = ref<string | null>(null)

/**
 * Belgilar sanog'i FAQAT chegaraga yaqinlashganda ko'rinadi.
 * Doim turgan "0/2000" hisoblagichi oddiy bir jumlalik xabar yozayotgan
 * o'quvchi uchun shovqin — u chegarani hech qachon ko'rmaydi.
 */
const showCounter = computed(() => draft.value.length > GROUP_CHAT_BODY_MAX - 200)

async function submit(): Promise<void> {
  if (!canSubmit.value) return

  const body = trimmed.value
  const files = pendingFiles.value

  /*
    Maydonlarni DARHOL bo'shatamiz: yuborish davomida foydalanuvchi
    keyingisini yozishi mumkin. Xato bo'lsa hammasi QAYTARILADI — aks holda
    yozilgani ham, tanlangan fayl ham yo'qolardi.
  */
  draft.value = ''
  pendingFiles.value = []
  attachmentError.value = null

  /*
    ★ YO'L TANLASH — YAGONA JOY: biriktirma bor -> REST (`sendWithFiles`),
    yo'q -> hub afzal ko'riladigan `send`. Sabab `sendWithFiles` izohida
    (SignalR fayl tashiy olmaydi).
  */
  const ok = files.length > 0 ? await room.sendWithFiles(files, body) : await room.send(body)

  if (!ok) {
    draft.value = body
    pendingFiles.value = files
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
  <!--
    ★ ILDIZ — USTUN FLEX'i. Xabarlar ro'yxati qolgan joyni o'zi egallaydi,
    sarlavha, ogohlantirish va yozish paneli esa o'z balandligini o'zi oladi
    va ularni hech kim piksel bilan sanamaydi (sabab — yuqoridagi
    "BALANDLIK — CHAQIRUVCHINIKI" izohi).
  -->
  <div class="flex min-h-0 flex-col">
    <!-- ============================== Sarlavha ============================== -->
    <!-- `shrink-0`: balandlik chegaralanganda siqiladigan yagona element
         xabarlar ro'yxati bo'lsin — sarlavha va yozish paneli emas. -->
    <div class="mb-2.5 flex shrink-0 flex-wrap items-center gap-2.5">
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

    <!-- ============================== Xabarlar ============================== -->
    <!--
      ★ `DataStatus` SKROLL SOHASINING ICHIDA (2026-08-13). Ilgari u tashqarida
      turardi va yuklanayotganda BUTUN ro'yxat o'rniga skeleton chizardi
      (`DataStatus.vue:40-49` — `skeletonRows × h-20`). Skeleton balandligi
      ro'yxat balandligiga teng emas, ya'ni suhbat har ochilganda yozish
      paneli bir sakrab, keyin joyiga qaytardi. Endi holat qanday bo'lishidan
      qat'i nazar ustun tuzilishi bir xil: sarlavha → skroll sohasi → panel.

      `flex-auto` (`flex-1` EMAS) — sabab yuqoridagi bosh izohda.
    -->
    <div
      ref="scroller"
      class="scrollbar-slim flex min-h-0 flex-auto flex-col overflow-y-auto overflow-x-hidden"
    >
      <DataStatus
        :pending="room.isPending.value"
        :error="room.loadError.value"
        :empty="false"
        :retrying="false"
        :skeleton-rows="3"
        @retry="room.retry()"
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
          <!-- Kun ajratgichi — uch chat ekrani uchun BITTA komponent. -->
          <ChatDaySeparator
            v-if="row.dayLabel !== null"
            :label="row.dayLabel"
          />
          <GroupChatMessageRow
            :sender-name="row.senderName"
            :body="row.body"
            :time="row.time"
            :is-own="row.isOwn"
            :show-header="row.showHeader"
            :role="row.senderRole"
            :attachments="row.attachments"
            @zoom="(url) => (zoomUrl = url)"
          />
        </template>
      </DataStatus>
    </div>

    <!-- ============================ Ogohlantirish =========================== -->
    <!-- 429, ruxsat xatosi va boshqalar. Ko'rinishi shaxsiy chatlar bilan
         BITTA komponentdan keladi (`ChatNotice` izohi). -->
    <ChatNotice
      v-if="room.notice.value !== null"
      class="mt-2"
      :text="room.notice.value"
      @dismiss="room.dismissNotice()"
    />

    <!-- ====================== Tanlangan fayllar (R16b) ====================== -->
    <!--
      Fayllar YUBORILGUNCHA shu yerda turadi. Ular hali serverga ketmagan —
      "Yuborish" bosilganda xabar bilan BITTA so'rovda ketadi (sabab
      `pendingFiles` izohida).
    -->
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
          class="flex max-w-full items-center gap-1.5 rounded-full border border-line bg-ink-900 py-1 pl-2.5 pr-1"
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
            type="button"
            class="flex size-5 shrink-0 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            title="Olib tashlash"
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

      <!-- Yuklash progressi — BUTUN so'rov bo'yicha (izohi `pendingFiles` da). -->
      <div
        v-if="room.uploadPercent.value !== null"
        class="mt-1.5 h-1 overflow-hidden rounded-full bg-ink-800"
      >
        <div
          class="h-full rounded-full bg-brand-500 transition-[width]"
          :style="{ width: `${room.uploadPercent.value}%` }"
        />
      </div>
    </div>

    <!-- ============================ Yozish paneli =========================== -->
    <form
      class="mt-2.5 flex shrink-0 items-end gap-2"
      novalidate
      @submit.prevent="submit"
    >
      <!--
        Emoji CHAPDA: o'ng tomon yuborish tugmasiniki va u eski ilovadan
        ko'chirilgan joyda qolishi kerak (barmoq o'sha burchakni "biladi").
      -->
      <ChatEmojiPicker
        v-model="draft"
        :target="input"
        :max-length="GROUP_CHAT_BODY_MAX"
      />

      <!--
        ★ R16b · FAYL BIRIKTIRISH. Emojidan KEYIN va matn maydonidan OLDIN —
        Telegram'dagi tartib (qo'shimchalar chapda, yuborish o'ngda).

        `accept` — TAVSIYA, tekshiruv EMAS: haqiqiy tur serverda SEHRLI
        BAYTLARDAN aniqlanadi (`.jpg` deb nomlangan EXE 400 oladi). Bu yerda
        u faqat telefon galereyasini to'g'ri ochish uchun.
      -->
      <input
        ref="fileInput"
        class="hidden"
        type="file"
        multiple
        accept="image/*,audio/*,application/pdf"
        @change="onFilesChosen"
      >
      <button
        type="button"
        class="tap-target flex size-11 shrink-0 items-center justify-center rounded-full text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
        title="Fayl biriktirish"
        aria-label="Fayl biriktirish"
        @click="pickFiles"
      >
        <AppIcon
          name="paperclip"
          :size="18"
        />
      </button>

      <div class="min-w-0 flex-1">
        <label
          class="sr-only"
          for="group-chat-input"
        >
          Xabar matni
        </label>
        <!--
          ★ `resize-y` OLIB TASHLANDI (2026-08-13): burchakdan tortib
          maydonni `max-h-32` gacha cho'zish mumkin edi va u xabarlar
          ro'yxatini emas, PANELNI surardi — foydalanuvchi o'z qo'li bilan
          yozish maydonini ekrandan chiqarib yuborardi.

          O'rniga `field-sizing-content`: maydon YOZILGAN MATNGA qarab
          o'sadi (Telegram'dagidek) va `max-h-32` da to'xtaydi. Qo'llab-
          quvvatlamaydigan brauzerda bugungi bir qatorli ko'rinish qoladi —
          ya'ni chekinish yo'q, faqat yaxshilanish bor.
        -->
        <textarea
          id="group-chat-input"
          ref="input"
          v-model="draft"
          class="zn-input max-h-32 min-h-11 w-full resize-none overflow-y-auto rounded-3xl py-2.5 field-sizing-content"
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

    <!--
      Kattalashtirilgan rasm. `GradingQueueOverlay` dagi AYNI naqsh:
      `BaseModal wide` + `max-h-[75dvh] object-contain`.

      ★ ICHMA-ICH OYNA XAVFSIZ: `useModalHost` ESC steki faqat eng
      tepadagi qatlamni yopadi (izohi `zoomUrl` ustida).
    -->
    <BaseModal
      :open="zoomUrl !== null"
      title="Rasm"
      wide
      @close="zoomUrl = null"
    >
      <img
        v-if="zoomUrl !== null"
        :src="zoomUrl"
        alt="Kattalashtirilgan rasm"
        class="mx-auto max-h-[75dvh] w-auto rounded-lg object-contain"
      >
    </BaseModal>
  </div>
</template>
