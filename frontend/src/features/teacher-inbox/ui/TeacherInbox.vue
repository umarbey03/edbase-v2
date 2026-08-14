<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  conversationSubtitle,
  fetchConversations,
  fetchLessonQuestions,
  waitLabel,
  waitTone,
} from '@/entities/direct-message'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { useNow } from '@/shared/lib/use-now'
import type { ConversationDto } from '@/shared/types'
import { AppIcon, BaseBadge, DataStatus, EmptyState } from '@/shared/ui'

import type { InboxFilter, InboxRow } from '../model/inbox'
import { filterRows, groupOptions, INBOX_FILTERS, toRows } from '../model/inbox'
import InboxThread from './InboxThread.vue'

/**
 * "Savollar" — eski `teacher.html` dagi `#dm-hub` bo'limi.
 *
 * IKKI USTUN eski ilovadagidek: chapda suhbatlar (320px), o'ngda ochiq
 * yozishma. Telefonda (eski `@media(max-width:720px)`) ular ALMASHADI —
 * suhbat ochilsa ro'yxat yashiriladi, "←" tugmasi qaytaradi. Bu alohida
 * MARSHRUT emas: brauzer tarixi savollar bilan to'lib ketmasin.
 *
 * 🔴 IKKI USTUN `lg:` (1024px) DAN BOSHLANADI, `md:` DAN EMAS: 768px da
 * 320px ustun ayrilgach yozishmaga ~420px qolardi — xabar pufakchalari va
 * yozish maydoni uchun juda tor. Endi iPad tik holatida ham "ro'yxat →
 * yozishma" almashuvi ishlaydi, ya'ni butun ekran bitta ishga tegishli.
 *
 * ★ QAYTISH TUGMASI HAM SHU CHEGARADA: `InboxThread` dagi "←" `lg:hidden`.
 * Agar u `md:hidden` bo'lib qolsa, 768–1023px oralig'ida ro'yxat YASHIRINIB,
 * qaytish tugmasi ham YO'QOLIB, foydalanuvchi yozishmada qamalib qolardi.
 */
const props = withDefaults(defineProps<{ emptyHint?: string }>(), { emptyHint: '' })

const now = useNow()

const conversationsQuery = useQuery({
  queryKey: ['dm', 'conversations'],
  queryFn: ({ signal }) => fetchConversations({ signal }),
  // Eski ilova 30 sekundda bir `loadDmThreads()` chaqirardi — o'sha oraliq.
  refetchInterval: 30_000,
})

const conversations = computed<ConversationDto[]>(() => conversationsQuery.data.value ?? [])

const conversationsError = computed(() =>
  conversationsQuery.error.value !== null ? toUserMessage(conversationsQuery.error.value) : null,
)

/* ---------------------------------------------------------------- filtrlar */

const search = ref('')
const groupFilter = ref('')
const filter = ref<InboxFilter>('all')

const groups = computed(() => groupOptions(conversations.value))

// Guruh ro'yxatdan yo'qolsa (o'quvchi boshqa guruhga o'tsa) filtr osilib
// qolmasin — aks holda ekran hech qachon bo'shamaydigan "topilmadi" ko'rsatardi.
watch(groups, (list) => {
  if (groupFilter.value.length > 0 && !list.includes(groupFilter.value)) groupFilter.value = ''
})

const rows = computed(() => toRows(conversations.value, now.value))

const filtered = computed(() =>
  filterRows(rows.value, {
    search: search.value,
    groupName: groupFilter.value,
    filter: filter.value,
  }),
)

interface InboxSection {
  title: string
  urgent: boolean
  items: InboxRow[]
}

/**
 * "Hammasi" chipida ro'yxat IKKI bo'limga bo'linadi (eski `sec()`):
 * javob kutayotganlar tepada, qolgani pastda. Boshqa chiplarda bo'lim
 * sarlavhasi ortiqcha — ro'yxatning o'zi allaqachon filtrlangan.
 */
const sections = computed<InboxSection[]>(() => {
  if (filter.value !== 'all') return [{ title: '', urgent: false, items: filtered.value }]
  return [
    {
      title: 'Javob kutmoqda',
      urgent: true,
      items: filtered.value.filter((row) => row.waitingHours !== null),
    },
    {
      title: 'Boshqalar',
      urgent: false,
      items: filtered.value.filter((row) => row.waitingHours === null),
    },
  ]
})

/* ==========================================================================
   R40 · DARS SAVOLLARI NAVBATI
   ==========================================================================

   Loyiha egasi: *"savollar qismida darslarda video darslardan kelgan
   savollar bo'ladi, ularga javob berish mumkin bo'ladi, bunda ham
   ketma-ketlik bo'yicha bo'lsin"*.

   ★ ALOHIDA SAHIFA EMAS, SHU EKRANNING IKKINCHI KO'RINISHI: qator
     bosilganda AYNI yozishma o'ngda ochiladi. Ikkinchi chat ekrani
     qurilsa "javob yozish" oqimi (tarix, o'qildi, emoji) ikki nusxada
     bo'lardi.

   ★ TARTIBNI SERVER BELGILAYDI (javobsizlar tepada, ular ichida eng uzoq
     kutgani birinchi) — bu yerda qayta saralanmaydi, aks holda "navbatda
     kim birinchi" degan qaror ikki joyda bo'lardi. */

type InboxView = 'people' | 'lessons'

const view = ref<InboxView>('people')

const lessonQuestionsQuery = useQuery({
  queryKey: ['dm', 'lesson-questions'],
  queryFn: ({ signal }) => fetchLessonQuestions({}, { signal }),

  // ★ FAQAT KERAK BO'LGANDA so'raladi: ustozlarning aksariyati bu
  //   ko'rinishni umuman ochmaydi (savollar standart holda kuratorga
  //   ketadi), ya'ni shartsiz so'rov har 30 sekundda bekorga ketardi.
  enabled: computed(() => view.value === 'lessons'),
  refetchInterval: 30_000,
})

const lessonQuestions = computed(() => lessonQuestionsQuery.data.value ?? [])

const lessonQuestionsError = computed(() =>
  lessonQuestionsQuery.error.value !== null
    ? toUserMessage(lessonQuestionsQuery.error.value)
    : null,
)

/* ------------------------------------------------------------ ochiq suhbat */

const activePeerId = ref<number | null>(null)

const activePeer = computed<ConversationDto | null>(
  () => conversations.value.find((item) => item.peerId === activePeerId.value) ?? null,
)

function rowBadgeTone(row: InboxRow): 'danger' | 'accent' | 'neutral' {
  return row.waitingHours === null ? 'neutral' : waitTone(row.waitingHours)
}

function rowBorder(row: InboxRow): string {
  if (row.waitingHours !== null && row.waitingHours >= 24) return 'border-rose-500/35'
  if (row.conversation.unreadCount > 0) return 'border-brand-500/30'
  return 'border-transparent'
}

/**
 * QATOR KO'RINISHI — uch tarmoqli shart, `GroupChatThreadList` dagi bilan
 * bir xil qoida (2026-08-13, R28).
 *
 * ★ "HOZIR OCHIQ" boshqa hamma belgidan USTUN turadi: shoshilinchlik
 * (24 soatdan oshgan kutish) va o'qilmaganlik — ESLATMA, ochiqlik esa
 * foydalanuvchi AYNAN shu daqiqada qayerdaligi. Ilgari tanlangan qator
 * faqat `bg-ink-800` bilan ajratilardi va u hover fonidan deyarli
 * farqlanmasdi — ikki panelli ekranda "qaysi biri ochiq" degan savol
 * javobsiz qolardi. Kutish muddati baribir ko'rinadi: uni nishon
 * (`BaseBadge`) aytib turadi.
 */
function rowClass(row: InboxRow): string {
  if (row.conversation.peerId === activePeerId.value) return 'border-brand-500/70 bg-brand-500/15'
  return `hover:bg-ink-850 ${rowBorder(row)}`
}
</script>

<template>
  <!--
    ★ RO'YXAT USTUNI 340px (2026-08-13, R28): o'quvchi chati va ustoz
    "Chatlar" hubi ham AYNAN shu kenglikda (`docs/MOSLASHUVCHANLIK.md` 6.3).
    Uchta chat ekrani bir kunda bir necha marta almashadi va 20px lik farq
    ro'yxat qatorlarini boshqa joyda sindirardi.
  -->
  <div class="grid items-start gap-3.5 lg:grid-cols-[340px_minmax(0,1fr)]">
    <!-- ========================= Suhbatlar ro'yxati ========================= -->
    <section
      class="rounded-xl border border-line bg-ink-900 p-2.5"
      :class="activePeer !== null ? 'hidden lg:block' : ''"
    >
      <!--
        R40 — IKKI KO'RINISH: "O'quvchilar" (suhbatlar) va "Dars savollari"
        (navbat). Segment tugmalar, tab EMAS: ikkalasi ham AYNI yozishmaga
        olib boradi, ya'ni bu boshqa BO'LIM emas, ayni ro'yxatning boshqa
        SARALANISHI.
      -->
      <div
        class="mb-2 flex gap-1.5"
        role="group"
        aria-label="Ro‘yxat ko‘rinishi"
      >
        <button
          type="button"
          class="min-h-9 flex-1 rounded-full border px-3 text-xs font-semibold transition-colors"
          :class="
            view === 'people'
              ? 'border-brand-500 bg-brand-500 text-on-brand'
              : 'border-line bg-ink-950 text-slate-400 hover:border-brand-500'
          "
          :aria-pressed="view === 'people'"
          @click="view = 'people'"
        >
          O‘quvchilar
        </button>
        <button
          type="button"
          class="min-h-9 flex-1 rounded-full border px-3 text-xs font-semibold transition-colors"
          :class="
            view === 'lessons'
              ? 'border-brand-500 bg-brand-500 text-on-brand'
              : 'border-line bg-ink-950 text-slate-400 hover:border-brand-500'
          "
          :aria-pressed="view === 'lessons'"
          @click="view = 'lessons'"
        >
          Dars savollari
        </button>
      </div>

      <!-- ==================== DARS SAVOLLARI NAVBATI ==================== -->
      <DataStatus
        v-if="view === 'lessons'"
        :pending="lessonQuestionsQuery.isPending.value"
        :error="lessonQuestionsError"
        :empty="lessonQuestions.length === 0"
        :retrying="lessonQuestionsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="book"
        empty-title="Dars savollari yo‘q"
        empty-text="O‘quvchi dars sahifasidan “Bu dars bo‘yicha savol berish” tugmasini bosganda savol shu yerga tushadi."
        @retry="lessonQuestionsQuery.refetch()"
      >
        <div class="scrollbar-slim max-h-[74dvh] overflow-y-auto">
          <button
            v-for="question in lessonQuestions"
            :key="question.messageId"
            type="button"
            class="mb-1 block w-full rounded-[10px] border p-2.5 text-left transition-colors"
            :class="
              question.peerId === activePeerId
                ? 'border-brand-500/70 bg-brand-500/15'
                : `hover:bg-ink-850 ${question.answered ? 'border-transparent' : 'border-brand-500/30'}`
            "
            :aria-current="question.peerId === activePeerId ? 'true' : undefined"
            @click="activePeerId = question.peerId"
          >
            <span class="flex items-center justify-between gap-2">
              <b
                class="min-w-0 flex-1 truncate text-[13.5px] text-slate-100"
                v-text="question.peerName ?? '—'"
              />
              <!--
                "Javob berilgan" belgisi — navbatning ma'nosi shu: qaysi
                savol hali javobsiz. Sanani ko'rsatish uni almashtira
                olmaydi (eski, lekin javob berilgan savol ham sanaga ega).
              -->
              <BaseBadge :tone="question.answered ? 'neutral' : 'danger'">
                {{ question.answered ? 'Javob berilgan' : 'Javob kutmoqda' }}
              </BaseBadge>
            </span>

            <span class="mt-0.5 flex items-center gap-1.5 text-[11px] text-brand-300">
              <AppIcon
                name="book"
                :size="12"
              />
              <span
                class="min-w-0 truncate font-bold"
                v-text="question.moduleLessonName ?? 'Dars'"
              />
              <span
                v-if="question.groupName !== null"
                class="shrink-0 text-dim"
              >· {{ question.groupName }}</span>
            </span>

            <span class="mt-[3px] flex items-center justify-between gap-2">
              <span
                class="min-w-0 flex-1 truncate text-xs text-slate-400"
                v-text="question.body"
              />
              <span
                class="shrink-0 text-[11px] tabular-nums text-dim"
                v-text="formatDate(question.sentAt)"
              />
            </span>
          </button>
        </div>
      </DataStatus>

      <template v-else>
        <label
          class="sr-only"
          for="dm-search"
        >
          O‘quvchi ismi bo‘yicha qidirish
        </label>
        <div class="relative mb-2">
          <AppIcon
            class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
            name="search"
            :size="15"
          />
          <input
            id="dm-search"
            v-model="search"
            class="zn-input pl-9"
            type="search"
            placeholder="O‘quvchi ismi bo‘yicha..."
          >
        </div>

        <label
          class="sr-only"
          for="dm-group"
        >
          Guruh
        </label>
        <select
          id="dm-group"
          v-model="groupFilter"
          class="zn-input mb-2 text-[13px]"
        >
          <option value="">
            Barcha guruhlar
          </option>
          <option
            v-for="name in groups"
            :key="name"
            :value="name"
            v-text="name"
          />
        </select>

        <div class="mb-2.5 flex flex-wrap gap-1.5">
          <button
            v-for="option in INBOX_FILTERS"
            :key="option.key"
            type="button"
            class="min-h-9 rounded-full border px-3 text-xs font-semibold transition-colors"
            :class="
              filter === option.key
                ? 'border-brand-500 bg-brand-500 text-on-brand'
                : 'border-line bg-ink-950 text-slate-400 hover:border-brand-500'
            "
            :aria-pressed="filter === option.key"
            @click="filter = option.key"
            v-text="option.label"
          />
        </div>

        <DataStatus
          :pending="conversationsQuery.isPending.value"
          :error="conversationsError"
          :empty="conversations.length === 0"
          :retrying="conversationsQuery.isFetching.value"
          :skeleton-rows="3"
          empty-icon="chat"
          empty-title="Sizga biriktirilgan o‘quvchi topilmadi."
          :empty-text="props.emptyHint"
          @retry="conversationsQuery.refetch()"
        >
          <p
            v-if="filtered.length === 0"
            class="px-1.5 py-5 text-center text-xs text-slate-400"
          >
            Mos o‘quvchi topilmadi.
          </p>

          <div
            v-else
            class="scrollbar-slim max-h-[74dvh] overflow-y-auto"
          >
            <template
              v-for="section in sections"
              :key="section.title"
            >
              <p
                v-if="section.title.length > 0 && section.items.length > 0"
                class="mb-1.5 ml-1 mt-2.5 text-[11px] font-bold uppercase tracking-[0.4px]"
                :class="section.urgent ? 'text-rose-400' : 'text-slate-400'"
              >
                {{ section.title }} ({{ section.items.length }})
              </p>

              <!--
              `aria-current="true"` — ko'rish qobiliyati cheklangan
              foydalanuvchi ham qaysi yozishma ochiqligini biladi (rang
              yolg'iz o'zi hech qachon yagona belgi bo'lmasligi kerak).
              O'quvchi chatidagi ro'yxat bilan bir xil qoida.
            -->
              <button
                v-for="row in section.items"
                :key="row.conversation.peerId"
                type="button"
                class="mb-1 block w-full rounded-[10px] border p-2.5 text-left transition-colors"
                :class="rowClass(row)"
                :aria-current="row.conversation.peerId === activePeerId ? 'true' : undefined"
                @click="activePeerId = row.conversation.peerId"
              >
                <span class="flex items-center justify-between gap-2">
                  <b
                    class="min-w-0 flex-1 truncate text-[13.5px] text-slate-100"
                    v-text="row.conversation.peerName ?? '—'"
                  />
                  <BaseBadge
                    v-if="row.waitingHours !== null"
                    :tone="rowBadgeTone(row)"
                  >
                    {{ waitLabel(row.waitingHours) }} kutmoqda
                  </BaseBadge>
                  <BaseBadge
                    v-else-if="row.conversation.unreadCount > 0"
                    tone="danger"
                  >
                    {{ row.conversation.unreadCount }}
                  </BaseBadge>
                  <span
                    v-else-if="row.conversation.lastMessageAt !== null"
                    class="shrink-0 text-[11px] tabular-nums text-dim"
                    v-text="formatDate(row.conversation.lastMessageAt)"
                  />
                </span>

                <span class="mt-0.5 block truncate text-[11px] text-slate-400">
                  {{ row.conversation.groupName ?? '' }}
                  <span
                    v-if="row.staleDays !== null"
                    class="text-dim"
                  >· {{ row.staleDays }} kun aloqa yo‘q</span>
                </span>

                <span
                  class="mt-[3px] block truncate text-xs text-slate-400"
                  v-text="conversationSubtitle(row.conversation)"
                />
              </button>
            </template>
          </div>
        </DataStatus>
      </template>
    </section>

    <!-- =========================== Ochiq yozishma =========================== -->
    <InboxThread
      v-if="activePeer !== null"
      :peer="activePeer"
      @close="activePeerId = null"
    />
    <!--
      "O'quvchini tanlang" ko'rsatkichi FAQAT ikki ustunli joylashuvda
      mazmunli — bitta ustunda ro'yxatning o'zi allaqachon ekranda turadi.

      ★ MARKAZDA (2026-08-13, R28): o'quvchi chatidagi bo'sh o'ng ustun
      bilan bir xil. `self-stretch` SHART — setka `items-start` bilan
      qurilgan (qatorlar tepadan tekislanadi), ya'ni usiz katak kontent
      balandligida qolib, markazlashtiradigan hech narsa bo'lmasdi.
    -->
    <div
      v-else
      class="hidden lg:flex lg:items-center lg:justify-center lg:self-stretch"
    >
      <EmptyState
        class="w-full max-w-[420px]"
        icon="chat"
        title="O‘quvchini tanlang"
        text="Chapdagi ro‘yxatdan o‘quvchini tanlang — yozishma shu yerda ochiladi."
      />
    </div>
  </div>
</template>
