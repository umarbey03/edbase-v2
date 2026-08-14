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
  withDayLabels,
} from '@/entities/direct-message'
import { threadKey } from '@/entities/group-chat'
import {
  ChatDaySeparator,
  ChatNotice,
  GroupChatRoom,
  GroupChatThreadList,
} from '@/features/group-chat'
import ChatEmojiPicker from '@/features/group-chat/ui/ChatEmojiPicker.vue'
import { toUserMessage } from '@/shared/api'
import { formatTime } from '@/shared/lib/datetime'
import type { ConversationDto, GroupChatThreadDto } from '@/shared/types'
import { AppIcon, BaseAvatar, DataStatus, EmptyState } from '@/shared/ui'

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
 *
 * ============================================================================
 * ★ 2026-08-13 — DESKTOPDA (≥1024px) IKKI USTUN
 * ============================================================================
 *
 * Telefonda bu sahifa UCH HOLATLI BITTA ekran: ro'yxat YOKI guruh suhbati
 * YOKI shaxsiy suhbat — ochilgan suhbat ro'yxatning O'RNINI egallaydi.
 * 320px kenglikda bu yagona to'g'ri yechim. 1600px da esa AYNAN SHU narsa
 * xato bo'ladi: suhbat ochilishi bilan ekranning chap yarmi bo'shab qoladi
 * va foydalanuvchi ikkinchi suhbatga o'tish uchun har safar "Orqaga" bosishi
 * kerak — ya'ni katta ekran hech narsa yutmaydi.
 *
 * Shuning uchun desktopda RO'YXAT DOIM CHAPDA turadi, suhbat esa o'ngda
 * almashadi (`docs/MOSLASHUVCHANLIK.md`, 6.3: `lg:grid-cols-[340px_
 * minmax(0,1fr)]`). Bu ikkinchi ekran EMAS, ayni o'sha uch holatning
 * boshqacha JOYLASHUVI: holat modeli (`activePeer` / `activeThread`)
 * o'zgarmadi, faqat `showList` endi desktopda "chap ustun ko'rinadimi"
 * emas, "o'ng ustunda nima chiziladi" degan ma'noni oladi.
 *
 * ★ TELEFON YO'LI BIR BAYT HAM O'ZGARMADI va buni CSS kafolatlaydi: chap
 * ustun suhbat ochiq bo'lganda `hidden lg:flex`, o'ng ustun esa hech narsa
 * tanlanmaganda `hidden lg:flex`. Ya'ni <1024px da HAR DOIM ikkalasidan
 * FAQAT BITTASI ko'rinadi — bu bugungi xatti-harakatning o'zi. Chegaraning
 * yagona hakami CSS `lg:` (karkas qoidasi — `StudentShell` izohi); bu yerda
 * `useBreakpoint()` ATAYLAB ishlatilmadi.
 *
 * ★ YAGONA HAQIQIY FARQ (ko'rinmaydigan): endi suhbat ochilganda ro'yxat
 * DOM'dan chiqib ketmaydi, faqat `display:none` bo'ladi — ya'ni
 * `GroupChatThreadList` ning 30 sekundlik so'rovi telefonda ham davom
 * etadi. Bu ataylab qabul qilindi: (1) kurator suhbatlari ro'yxati
 * (`conversationsQuery`) ALLAQACHON sahifa darajasida, holatdan qat'i nazar
 * so'rab turadi — ya'ni naqsh yangi emas; (2) `v-if` bilan yechish uchun
 * JS chegara tekshiruvi kerak bo'lardi, u esa yuqoridagi "bitta hakam"
 * qoidasini buzardi; (3) "Orqaga" bosilganda ro'yxat endi yangilangan va
 * skroll joyi saqlangan holda qaytadi — telefonda ham foyda.
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

/**
 * KUN AJRATGICHLARI (2026-08-13, R28). Ilgari shaxsiy chatda ular UMUMAN
 * yo'q edi: bir necha haftalik yozishmada har xabar ostida faqat SOAT
 * turardi va "bu qachon yozilgan" degan savol javobsiz qolardi. Guruh
 * chatida esa AYNI o'quvchi ajratgichni allaqachon ko'rib turibdi — ya'ni
 * ikki ekran bir xil ma'lumotni ikki xil ko'rsatardi.
 *
 * Qoida entity qatlamida (`withDayLabels`), chunki uni ustozning "Savollar"
 * yozishmasi ham o'qiydi.
 */
const grouped = computed(() => withDayLabels(messages.value))

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

/**
 * Yozish maydonining o'zi — emoji tanlagichga KURSOR JOYI uchun kerak:
 * belgi matn oxiriga emas, kursor turgan joyga qo'yiladi
 * (`ChatEmojiPicker` izohiga qarang).
 */
const input = ref<HTMLTextAreaElement | null>(null)

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

/**
 * Belgilar sanog'i FAQAT chegaraga yaqinlashganda ko'rinadi — guruh
 * chatidagi qoida bilan AYNAN bir xil (`GroupChatRoom` izohi).
 */
const showCounter = computed(() => draft.value.length > DM_BODY_MAX - 200)

function submit(): void {
  const peer = activePeer.value
  if (peer === null || !canSend.value) return
  sendMutation.mutate({ peerId: peer.peerId, body: draft.value.trim() })
}

/**
 * ★ "ALLAQACHON OCHIQ" TEKSHIRUVI — DESKTOP TUG'DIRGAN SHART.
 *
 * Telefonda bu holat MUMKIN EMAS edi: suhbat ochilishi bilan ro'yxat
 * ekrandan ketardi, ya'ni ochiq suhbatning qatorini qayta bosib bo'lmasdi.
 * Desktopda ro'yxat doim ko'rinib turadi va o'sha qatorni ikkinchi marta
 * bosish oson — shartsiz `draft.value = ''` esa YOZILAYOTGAN, hali
 * yuborilmagan xabarni jimgina o'chirib yuborardi.
 *
 * Guruh suhbatida ham xuddi shu: `activeThread` ga YANGI obyekt yozilsa
 * `:key` o'zgarmagani uchun `GroupChatRoom` qayta yaratilmaydi, lekin
 * ortiqcha yozuvni umuman qilmaslik aniqroq.
 */
function openConversation(conversation: ConversationDto): void {
  if (activePeer.value?.peerId === conversation.peerId) return
  sendError.value = null
  draft.value = ''
  activeThread.value = null
  activePeer.value = conversation
}

function openGroupThread(thread: GroupChatThreadDto): void {
  const open = activeThread.value
  if (open !== null && open.groupId === thread.groupId && open.channel === thread.channel) return
  activePeer.value = null
  activeThread.value = thread
}

function backToList(): void {
  activePeer.value = null
  activeThread.value = null
}

/**
 * Telefonda: "ekranda ro'yxat turibdimi". Desktopda: "hech narsa tanlanmagan"
 * — o'ng ustunda bo'sh holat chiziladi, chap ustun esa har ikki holatda ham
 * o'z joyida qoladi (shablondagi `hidden lg:flex` juftligiga qarang).
 */
const showList = computed(() => activePeer.value === null && activeThread.value === null)

/** Desktopda tanlangan qator KO'RINIB turishi shart (`MOSLASHUVCHANLIK` 6.5). */
function isPeerActive(conversation: ConversationDto): boolean {
  return activePeer.value?.peerId === conversation.peerId
}

/**
 * ★ SUHBAT USTUNINING BALANDLIGI — bitta joyda, ikkala suhbat uchun.
 *
 * Ilgari balandlik xabarlar ro'yxatining O'ZIDA turardi
 * (`h-[calc(100dvh-340px)]`, shaxsiy chatda `max-h-[58dvh]`) va o'sha 340
 * raqami appbar + "Orqaga" qatori + yozish paneli + tab panelini BIRGA
 * kodlab qo'ygan edi. Ikki oqibati bor edi: (1) yozish maydoni ikkinchi
 * qatorga o'sishi bilanoq (u `resize-y`, `max-h-32`) hisob buzilib, panel
 * tab paneli ostiga surilardi; (2) sanalgan to'rt elementdan birortasi
 * o'zgarsa chat JIMGINA buzilardi — hech qayerda tekshirilmasdi.
 *
 * Endi ustun BALANDLIGI chegaralanadi, ichkarisini esa flex hisoblaydi:
 * xabarlar ro'yxati `flex-1` bilan qolgan bo'sh joyni oladi, qolgan hamma
 * narsa (orqaga qatori, kanal tab'lari, ogohlantirish, yozish paneli) o'z
 * balandligini o'zi belgilaydi.
 *
 * Ayirmada FAQAT KARKASning (`StudentShell`) o'zgarmas qismlari qoldi:
 *   68px  — yopishqoq appbar (`StudentAppBar`: `pt-4` 16 + avatar 40 + `pb-3` 12);
 *    4px  — `main` ning yuqori bo'shlig'i (`pt-1`);
 *   24px  — `main` ning pastki bo'shlig'i (`pb-6`);
 *   80px  — tab paneliga ajratilgan joy (karkas ustunining `padding-bottom`)
 *           + `env(safe-area-inset-bottom)` (tirnoqli iPhone).
 *
 * ★ NEGA INLINE `style`, KLASS EMAS: ichida `env()` bor va uni Tailwind
 * arbitrary qiymatiga solib bo'lmaydi (`safe-area-inset-bottom` ichidagi
 * tirelar matematik amal deb o'qilishi mumkin). Ilovadagi boshqa hamma
 * safe-area hisoblari ham shu sababdan inline `:style` da
 * (`BaseModal`, `ConfirmDialog`, `SessionRecordingControl`).
 *
 * `min-height` — yotiq holatdagi past ekran uchun: u yerda sahifa
 * skrollanadi, lekin yozish paneli hech qachon nolga siqilmaydi.
 *
 * DESKTOP (≥1024px) — boshqa ayirma: u yerda tab paneli yo'q (karkas
 * `lg:pb-0!`), appbar balandroq (`lg:pt-6` — 24 + 40 + 12 = 76) va `main`
 * pastdan kengroq (`lg:pb-12` — 48), ya'ni 76 + 4 + 48 = 128px va safe-area
 * umuman qatnashmaydi.
 *
 * ★ 2026-08-13 QAYTA TEKSHIRILDI: karkas ustunining kengligi 960px dan
 * 1600px ga o'zgardi. Bu ayirmaga TEGMAYDI — 128 raqami faqat VERTIKAL
 * karkas qismlaridan yig'ilgan (appbar `pt-6`/avatar/`pb-3`, `main`
 * `pt-1`/`lg:pb-12`), ularning birortasi ham kenglikka bog'liq emas.
 * Appbar kengaygandan keyin ham bir qatorda qoladi (unda ikkitagina bola
 * bor: "keyingi dars" chipi va avatar), ya'ni o'ralib balandlashmaydi.
 *
 * ★ DESKTOP AYIRMASI ENDI BITTA JOYDA — ikki ustunli setka konteynerida
 * (`lg:h-[calc(100dvh-128px)]`, shablonda). Suhbat ustuni u yerdan
 * `lg:h-full!` bilan oziqlanadi: aks holda 128 raqami ikki (guruh va
 * shaxsiy suhbat) o'rniga uch nusxada yurardi va ular ajralib ketishi
 * mumkin edi. `!` SHART: inline `style` ni faqat `!important` yenga oladi
 * — karkasning o'zi ham aynan shu sababdan `lg:pb-0!` yozgan. Chegara JS
 * bilan tekshirilmaydi: karkas qoidasi bo'yicha desktop chegarasining
 * yagona hakami CSS `lg:` bo'lishi kerak.
 */
const CHAT_FILL_STYLE = 'height: calc(100dvh - 176px - env(safe-area-inset-bottom, 0px))'
</script>

<template>
  <!--
    ★ DESKTOP SETKASI (`docs/MOSLASHUVCHANLIK.md` 6.3): 340px lik ro'yxat +
    qolgan hamma joyni oladigan suhbat. Ro'yxat kengligi QOTIRILGAN: u yerda
    faqat avatar + ikki qator matn turadi, cho'zilsa bo'sh joy paydo bo'lardi
    — qo'shimcha kenglik SUHBATGA berilishi kerak.

    Balandlik ham shu yerda, BITTA marta chegaralanadi (hisob —
    `CHAT_FILL_STYLE` izohida): ikkala ustun ham shu qutining ichida qoladi,
    ya'ni SAHIFANING O'ZI skrollanmaydi, ustunlar esa MUSTAQIL skrollanadi.
    1024px dan past bu klasslarning birortasi ham qo'llanmaydi — telefonda
    avvalgi bitta ustun va sahifa skrolli.
  -->
  <div class="lg:grid lg:h-[calc(100dvh-128px)] lg:grid-cols-[340px_minmax(0,1fr)]">
    <!-- ========================= CHAP USTUN: RO'YXAT ======================= -->
    <!--
      `hidden` FAQAT telefonda ishlaydi: `lg:flex` media so'rovi ichida
      turgani uchun ≥1024px da undan kuchli. Ya'ni bitta shart ikki xulq
      beradi — telefonda "ro'yxat o'rnini suhbat egalladi", desktopda
      "ro'yxat joyida qoldi".
    -->
    <section
      class="min-w-0 lg:flex lg:h-full lg:min-h-0 lg:flex-col lg:border-r lg:border-line lg:pr-5"
      :class="{ hidden: !showList }"
      aria-label="Chatlar ro‘yxati"
    >
      <h2
        class="mb-3 ml-1 mt-2 flex items-center gap-[7px] text-xs font-bold uppercase tracking-[1.4px] text-brand-300 lg:shrink-0"
      >
        <AppIcon
          name="chat"
          :size="15"
        />
        Chatlar
      </h2>

      <!--
        ★ IKKINCHI SKROLL SOHASI. Telefonda bu oddiy `div` — sahifaning o'zi
        skrollanadi (bugungi xulq). Desktopda esa ro'yxat SUHBATDAN MUSTAQIL
        skrollanadi: uzun ro'yxatni ko'rish uchun ochiq suhbatni yo'qotish
        kerak emas. `min-h-0` shart — flex bola o'z kontenti balandligidan
        kichrayishi uchun.
      -->
      <div class="lg:min-h-0 lg:flex-1 lg:overflow-y-auto lg:pr-1 lg:scrollbar-slim">
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
              <!--
                Tint asosi `-500` (shkala shartnomasi: `style.css`).

                ★ TANLANGAN QATOR — kuchliroq chegara va to'yingroq fon.
                Ikki ustunli ko'rinishda bu SHART: suhbat endi ro'yxatning
                o'rnini egallamaydi, ya'ni "qaysi biri ochiq" degan savolga
                navigatsiyaning o'zi javob bermay qoldi. Telefonda esa bu
                klasslar HECH QACHON ko'rinmaydi — u yerda suhbat ochilishi
                bilan butun ro'yxat `hidden` bo'ladi, ya'ni parite buzilmaydi.

                `aria-current` — o'sha ma'lumot skrinrider uchun; rang yolg'iz
                o'zi hech qachon yagona belgi bo'lmasligi kerak.
              -->
              <button
                type="button"
                class="flex w-full items-center gap-3 rounded-[14px] border px-3.5 py-3 text-left transition-colors"
                :class="
                  isPeerActive(conversation)
                    ? 'border-sky-500/70 bg-sky-500/20'
                    : 'border-sky-500/30 bg-sky-500/[0.07] hover:bg-sky-500/[0.13]'
                "
                :aria-current="isPeerActive(conversation) ? 'true' : undefined"
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

          ★ TANLANGAN QATOR `selected-key` PROP'i orqali ajratiladi, tashqi
          CSS bilan EMAS. Qator markupi shu faylda emas — `GroupChatThreadList`
          ichida. Uni tashqaridan `:deep()` + `:nth-child` bilan bo'yash
          mumkin edi, lekin u qatorlar TARTIBIGA va bola komponentning DOM
          tuzilishiga jim bog'lanish bo'lardi. Prop esa ochiq shartnoma:
          `null` berilsa (ustoz "Chatlar" hubi shunday qiladi) ko'rinish bir
          zarra o'zgarmaydi.
        -->
        <GroupChatThreadList
          empty-title="Guruh chati yo‘q"
          empty-text="Guruhga qo‘shilganingizdan keyin guruh chatlari shu yerda ochiladi."
          :selected-key="
            activeThread === null ? null : threadKey(activeThread.groupId, activeThread.channel)
          "
          @open="openGroupThread"
        />
      </div>
    </section>

    <!-- ==================== O'NG USTUN: GURUH SUHBATI ====================== -->
    <!--
      Balandlik hisobi va sabablari — `CHAT_FILL_STYLE` izohida. Desktopda
      inline `style` ni `lg:h-full!` yengadi va ustun setka katagining
      balandligini oladi; `lg:min-h-0` esa past desktop oynasida ustun
      setkadan toshib ketmasligi uchun 320px lik pol'ni bekor qiladi (u pol
      TELEFON yotiq holati uchun qo'yilgan edi).
    -->
    <div
      v-if="activeThread !== null"
      class="flex min-h-[320px] flex-col lg:h-full! lg:min-h-0 lg:pl-5"
      :style="CHAT_FILL_STYLE"
    >
      <div class="mb-3 mt-2 flex shrink-0 items-center gap-3 lg:border-b lg:border-line lg:pb-3">
        <!--
          ★ "ORQAGA" DESKTOPDA YO'Q: u yerda ro'yxat chapda turibdi, ya'ni
          tugma hech qayerga qaytarmaydi — bosilgach ekranning yarmi bo'shab
          qolardi. Telefonda esa u YAGONA chiqish yo'li (marshrut o'zgarmagani
          uchun brauzerning "orqaga" tugmasi bu yerda ishlamaydi), shuning
          uchun `lg:hidden` — o'chirish emas.
        -->
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800 lg:hidden"
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
        Desktopda bu YANADA muhim: ro'yxat doim ko'rinib turgani uchun bir
        suhbatdan ikkinchisiga sakrash endi bir bosishlik ish.

        Balandlik endi PIKSEL BILAN BERILMAYDI: eski ilovadagi
        `.chat { height: calc(100vh - 220px) }` naqshi v2 da ham davom etib,
        `calc(100dvh - 340px)` ga aylangan edi — ya'ni to'rt xil element
        balandligi bitta raqamga qotib qolgandi. Endi suhbat ustuni qolgan
        joyni `flex-1` bilan oladi va ichkarisini komponentning o'zi
        taqsimlaydi.

        ★ `height-class` PROP'I ENDI YO'Q (2026-08-13): u xodim sahifalarida
        qat'iy balandlik berib turgan yagona sabab edi. Bugun qoida bitta —
        "ota-ona balandlikni chegaralaydi, suhbat uni to'ldiradi" — va bu
        satr allaqachon shunday ishlardi.
      -->
      <GroupChatRoom
        :key="`${activeThread.groupId}:${activeThread.channel}`"
        class="min-h-0 flex-1"
        :group-id="activeThread.groupId"
        :group-name="activeThread.groupName"
        :channel="activeThread.channel"
      />
    </div>

    <!-- =================== O'NG USTUN: SHAXSIY SUHBAT ====================== -->
    <!--
      Guruh suhbati bilan BIR XIL ustun: balandlik setka katagidan keladi,
      ichkarisini flex taqsimlaydi (hisob — `CHAT_FILL_STYLE` izohida).
      Shaxsiy chatda ilgari `max-h-[58dvh]` turardi: yotiq holatda u
      ro'yxatni ekranning yarmiga qisar, yozish maydoni ikki qatorga
      o'sganda esa ikkalasi birga tab paneli ostiga surilardi.
    -->
    <div
      v-else-if="activePeer !== null"
      class="flex min-h-[320px] flex-col lg:h-full! lg:min-h-0 lg:pl-5"
      :style="CHAT_FILL_STYLE"
    >
      <div class="mb-3 mt-2 flex shrink-0 items-center gap-3 lg:border-b lg:border-line lg:pb-3">
        <!-- Desktopda "Orqaga" yo'q — sababi guruh suhbatidagi izohda. -->
        <button
          type="button"
          class="tap-target flex items-center gap-1.5 rounded-xl border border-line-strong bg-ink-900 px-3 text-sm font-bold text-slate-100 transition-colors hover:bg-ink-800 lg:hidden"
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

      <!--
        ★ `contents` — TELEFONDA BU O'RAMA UMUMAN YO'Q (`display: contents`):
        `DataStatus` chizadigan tugun bevosita ustunning bolasi bo'lib qoladi,
        ya'ni bugungi joylashuv bir piksel ham o'zgarmaydi.

        Desktopda esa u bo'sh joyni egallaydigan flex ustunga aylanadi. Sabab:
        u yerda ustun balandligi QAT'IY (setka katagi), va xabarlar hali
        yo'qligida ("Hali savol yo‘q") yozish paneli ustunning O'RTASIDA
        osilib qolardi — telefonda bunday ko'rinmaydi, chunki u yerda ustun
        kontent bo'yicha o'sadi.
      -->
      <div class="contents lg:flex lg:min-h-0 lg:flex-1 lg:flex-col">
        <!--
          ★ `DataStatus` SKROLL SOHASINING ICHIDA (2026-08-13). Ilgari u
          tashqarida turardi: yuklanayotganda skroll sohasi butunlay
          ALMASHARDI (`DataStatus.vue:40-49` — skeleton qatorlari), ya'ni
          ustun tuzilishi holatga qarab o'zgarib, yozish paneli har ochilishda
          bir sakrardi. Endi tuzilish har uch holatda bir xil.
        -->
        <div
          ref="scroller"
          class="scrollbar-slim min-h-0 flex-1 space-y-2 overflow-y-auto pb-1"
        >
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
                    row.message.mine
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
                    <!-- "Ikki belgi" faqat MENING xabarim uchun ma'noli. -->
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
      </div>

      <!-- Yozish maydoni -->
      <form
        class="mt-3 flex shrink-0 items-end gap-2"
        novalidate
        @submit.prevent="submit"
      >
        <!-- Emoji — guruh chatidagi bilan AYNAN bir xil komponent. -->
        <ChatEmojiPicker
          v-model="draft"
          :target="input"
          :max-length="DM_BODY_MAX"
        />
        <div class="min-w-0 flex-1">
          <!--
            ★ `resize-y` o'rniga `field-sizing-content` (2026-08-13): burchakdan
            tortish yozish panelini ustun ichida yuqoriga surar, uzun matnda esa
            xabarlar ro'yxatini siqib qo'yardi. Endi maydon MATNGA qarab o'sadi
            va `max-h-32` da to'xtaydi — sabab `GroupChatRoom` izohida.
          -->
          <textarea
            ref="input"
            v-model="draft"
            class="zn-input min-h-11 max-h-32 w-full resize-none overflow-y-auto py-2.5 field-sizing-content"
            rows="1"
            :maxlength="DM_BODY_MAX"
            placeholder="Xabar yozing..."
          />
          <!-- Chegara SERVER bilan bir xil (2000) — guruh chatidagi qoida. -->
          <p
            v-if="showCounter"
            class="mt-1 pr-2 text-right text-[11px] tabular-nums text-dim"
          >
            {{ draft.length }} / {{ DM_BODY_MAX }}
          </p>
        </div>
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

      <!--
        Yuborilmagan xabar — guruh chatidagi bilan BITTA komponent
        (`ChatNotice`). Yozilgan matn maydonda qoladi (u faqat
        muvaffaqiyatda tozalanadi), shuning uchun ogohlantirishni YOPISH
        mumkin.
      -->
      <ChatNotice
        v-if="sendError !== null"
        class="mt-2"
        :text="sendError"
        @dismiss="sendError = null"
      />
    </div>

    <!-- ================= O'NG USTUN: HECH NARSA TANLANMAGAN ================= -->
    <!--
      ★ FAQAT DESKTOPDA MAVJUD HOLAT (`hidden lg:flex`). Telefonda "hech
      narsa tanlanmagan" degani RO'YXATNING O'ZI ko'rinib turgani demak —
      u yerda bo'sh o'ng ustun yo'q, ya'ni bu blok hech qachon chizilmaydi
      va parite shartnomasiga tegmaydi (yangi matn ham FAQAT desktopda
      ko'rinadi).

      Desktopda esa uning o'rnini bo'sh joy egallardi: ekranning kattaroq
      yarmi hech nima demasdi. `EmptyState` — ilovaning yagona bo'sh holat
      ko'rinishi (`DataStatus` ham aynan shuni chizadi), matn ohangi esa
      qo'shni sahifalardagidek: nima yo'qligini emas, NIMA QILISH kerakligini
      aytadi.
    -->
    <div
      v-else
      class="hidden lg:flex lg:h-full lg:min-h-0 lg:items-center lg:justify-center lg:pl-5"
    >
      <EmptyState
        class="w-full max-w-[420px]"
        icon="chat"
        title="Suhbat tanlanmagan"
        text="Chapdagi ro‘yxatdan kurator yoki guruh chatini tanlang — yozishma shu yerda ochiladi."
      />
    </div>
  </div>
</template>
