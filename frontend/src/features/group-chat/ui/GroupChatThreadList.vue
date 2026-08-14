<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import {
  channelLabel,
  channelTone,
  fetchGroupChatThreads,
  threadKey,
  threadSubtitle,
} from '@/entities/group-chat'
import { toUserMessage } from '@/shared/api'
import { formatDayLabel, formatTime } from '@/shared/lib/datetime'
import type { GroupChatThreadDto } from '@/shared/types'
import { AppIcon, BaseAvatar, BaseBadge, DataStatus } from '@/shared/ui'

/**
 * "CHATLAR" RO'YXATI — eski `teacher.html` dagi `#chats-hub` bo'limi
 * (`renderChatsHub()`) va `student.html` dagi `#chat-list-box`
 * (`renderChatList()`) ning umumiy ko'rinishi.
 *
 * ★ QATOR = (GURUH, KANAL) JUFTLIGI, guruh emas. Server `/threads` da aynan
 * shunday qaytaradi va bu eski ilovaning xatti-harakatiga ham to'g'ri
 * keladi: o'quvchida har guruh IKKI qator bo'lib ko'rinardi — "Ustoz chati"
 * va "Kurator chati" (`student.html`, `renderChatList`). Ustozda esa faqat
 * bitta kanal ochiq, ya'ni bitta qator.
 */
const props = withDefaults(
  defineProps<{
    /** Qidiruv maydoni ko'rsatilsinmi (eski ustoz hubida bor edi). */
    searchable?: boolean
    emptyTitle?: string
    emptyText?: string
    /**
     * Ochiq turgan suhbat kaliti — `threadKey(groupId, channel)`.
     *
     * NEGA KERAK (2026-08-13): o'quvchi chati desktopda IKKI PANELLI bo'ldi —
     * ro'yxat doim chapda turadi va o'ng tarafda suhbat ochiladi. Bunday
     * joylashuvda "qaysi qator ochiq" ni KO'RSATISH shart: telefonda buni
     * navigatsiyaning o'zi bildirardi (ro'yxat suhbat bilan almashardi),
     * desktopda esa ikkalasi bir vaqtda ko'rinadi.
     *
     * ★ `null` SUKUT — ustoz hubi (`TeacherGroupChatsPage`) bu prop'ni
     * umuman bermaydi va uning ko'rinishi bir zarra ham o'zgarmaydi.
     *
     * ★ NEGA PROP, nega chaqiruvchi CSS bilan bo'yamaydi: qatorlar SHU
     * komponent ichida chiziladi. Tashqaridan `:deep()` + `:nth-child` bilan
     * bo'yash qator TARTIBIGA va ichki markupga jimgina bog'lanib qolardi —
     * bu fayl izohlari aynan shunday bog'lanishlarni yo'q qilish uchun
     * yozilgan.
     */
    selectedKey?: string | null
  }>(),
  {
    searchable: false,
    emptyTitle: 'Guruh topilmadi',
    emptyText: '',
    selectedKey: null,
  },
)

const emit = defineEmits<{ open: [GroupChatThreadDto] }>()

const threadsQuery = useQuery({
  queryKey: ['group-chat', 'threads'],
  queryFn: ({ signal }) => fetchGroupChatThreads({ signal }),
  /*
    Ro'yxat o'zi yangilanib turadi: suhbat OCHIQ bo'lmaganda hub ulanmagan
    va yangi xabar haqida boshqa hech narsa xabar bermaydi. Eski ilova ham
    30 sekundlik oraliqda so'rardi (`loadDmThreads`).
  */
  refetchInterval: 30_000,
})

const threads = computed<GroupChatThreadDto[]>(() => threadsQuery.data.value ?? [])

const error = computed(() =>
  threadsQuery.error.value !== null ? toUserMessage(threadsQuery.error.value) : null,
)

const search = ref('')

const filtered = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (query.length === 0) return threads.value
  return threads.value.filter((thread) => thread.groupName.toLowerCase().includes(query))
})

/**
 * Vaqt ustuni: bugungi xabarda SOAT, eskirog'ida SANA.
 * Telegram va eski ilova ham shunday (`chatDayLabel`) — "14:05" bugungi
 * suhbatni, "12-mart" esa eskisini bir qarashda ajratadi.
 */
function threadTime(thread: GroupChatThreadDto): string {
  if (thread.lastMessageAt === null) return ''
  const label = formatDayLabel(thread.lastMessageAt)
  return label === 'Bugun' ? formatTime(thread.lastMessageAt) : label
}
</script>

<template>
  <div>
    <!-- Qidiruv — eski `#chats-search` ("🔍 Guruh nomi bo'yicha..."). -->
    <div
      v-if="props.searchable"
      class="relative mb-3.5 max-w-[320px]"
    >
      <label
        class="sr-only"
        for="group-chat-search"
      >
        Guruh nomi bo‘yicha qidirish
      </label>
      <AppIcon
        class="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-dim"
        name="search"
        :size="15"
      />
      <input
        id="group-chat-search"
        v-model="search"
        class="zn-input pl-9"
        type="search"
        placeholder="Guruh nomi bo‘yicha..."
      >
    </div>

    <DataStatus
      :pending="threadsQuery.isPending.value"
      :error="error"
      :empty="filtered.length === 0"
      :retrying="threadsQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="chat"
      :empty-title="props.emptyTitle"
      :empty-text="props.emptyText"
      @retry="threadsQuery.refetch()"
    >
      <ul class="flex flex-col gap-2.5">
        <li
          v-for="thread in filtered"
          :key="threadKey(thread.groupId, thread.channel)"
        >
          <!--
            ★ TANLANGAN QATOR o'qilmagan qatordan USTUN turadi: ikkalasi ham
            chegara rangini belgilaydi, lekin "hozir ochiq" — foydalanuvchi
            AYNAN shu daqiqada qayerdaligi, "o'qilmagan" esa eslatma. Shuning
            uchun shart uch tarmoqli, ikki alohida `:class` emas.

            `aria-current="true"` — ko'rish qobiliyati cheklangan foydalanuvchi
            ham qaysi suhbat ochiqligini biladi (rang yolg'iz yetarli emas).
          -->
          <button
            type="button"
            class="flex w-full items-center gap-3 rounded-[14px] border bg-ink-900 px-3.5 py-3 text-left transition-colors hover:bg-ink-800"
            :class="
              threadKey(thread.groupId, thread.channel) === props.selectedKey
                ? 'border-brand-500/70 bg-brand-500/15'
                : thread.unreadCount > 0
                  ? 'border-brand-500/40'
                  : 'border-line'
            "
            :aria-current="
              threadKey(thread.groupId, thread.channel) === props.selectedKey ? 'true' : undefined
            "
            @click="emit('open', thread)"
          >
            <BaseAvatar
              :name="thread.groupName"
              size="md"
            />

            <span class="min-w-0 flex-1">
              <!-- Sarlavha qatori: 👥 guruh nomi + kanal nishoni + vaqt.
                   Emoji eski ilovadan (`👥 ${g.name}`) — ustoz ro'yxatda
                   guruhni aynan shu belgi bilan ajratardi. -->
              <span class="flex items-center gap-2">
                <span
                  class="min-w-0 flex-1 truncate text-[15px] font-bold text-slate-100"
                  v-text="`👥 ${thread.groupName}`"
                />
                <span
                  v-if="threadTime(thread).length > 0"
                  class="shrink-0 text-[11px] tabular-nums text-dim"
                  v-text="threadTime(thread)"
                />
              </span>

              <!--
                ★ KANAL NISHONI — o'quvchida bitta guruh IKKI qator bo'lib
                turadi va ularni FAQAT shu nishon ajratadi. Rang eski
                ilovadagidek: ustoz oqimi oltin, kurator oqimi moviy.
              -->
              <span class="mt-1 flex items-center gap-2">
                <BaseBadge
                  :tone="channelTone(thread.channel)"
                  size="xs"
                  dot
                >
                  {{ channelLabel(thread.channel) }}
                </BaseBadge>
                <span
                  v-if="thread.unreadCount > 0"
                  class="ml-auto shrink-0 rounded-full bg-brand-500 px-1.5 py-0.5 text-[10px] font-extrabold text-on-brand"
                  v-text="thread.unreadCount"
                />
              </span>

              <!-- Oxirgi xabar: "Kim: matn" (eski `PREVIEW[...]` qatori). -->
              <span
                class="mt-1 block truncate text-xs text-slate-400"
                v-text="threadSubtitle(thread)"
              />
            </span>

            <AppIcon
              class="shrink-0 text-dim"
              name="chat"
              :size="18"
            />
          </button>
        </li>
      </ul>
    </DataStatus>
  </div>
</template>
