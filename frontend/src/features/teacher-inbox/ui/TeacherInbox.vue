<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  conversationSubtitle,
  fetchConversations,
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
</script>

<template>
  <div class="grid items-start gap-3.5 md:grid-cols-[320px_minmax(0,1fr)]">
    <!-- ========================= Suhbatlar ro'yxati ========================= -->
    <section
      class="rounded-xl border border-line bg-ink-900 p-2.5"
      :class="activePeer !== null ? 'hidden md:block' : ''"
    >
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
          class="scrollbar-slim max-h-[74vh] overflow-y-auto"
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

            <button
              v-for="row in section.items"
              :key="row.conversation.peerId"
              type="button"
              class="mb-1 block w-full rounded-[10px] border p-2.5 text-left transition-colors"
              :class="[
                row.conversation.peerId === activePeerId ? 'bg-ink-800' : 'hover:bg-ink-850',
                rowBorder(row),
              ]"
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
    </section>

    <!-- =========================== Ochiq yozishma =========================== -->
    <InboxThread
      v-if="activePeer !== null"
      :peer="activePeer"
      @close="activePeerId = null"
    />
    <EmptyState
      v-else
      class="hidden md:block"
      icon="chat"
      title="O‘quvchini tanlang"
      text="Chapdagi ro‘yxatdan o‘quvchini tanlang — yozishma shu yerda ochiladi."
    />
  </div>
</template>
