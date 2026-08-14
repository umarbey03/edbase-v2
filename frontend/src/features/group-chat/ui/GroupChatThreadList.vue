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
import { fetchGroupCategories } from '@/entities/group-category'
import { toUserMessage } from '@/shared/api'
import { formatDayLabel, formatTime } from '@/shared/lib/datetime'
import type { GroupChatThreadDto, GroupTypeName } from '@/shared/types'
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
    /**
     * ════════════════════════════════════════════════════════════════════
     * R38 — GURUH TURI VA YO'NALISHI BO'YICHA FILTR
     * ════════════════════════════════════════════════════════════════════
     *
     * Talab: *"chatlar qismga ham filter qo'shilishi kerak, guruh tur va
     * kategoriyalar bo'yicha"*.
     *
     * ★ `searchable` KABI IXTIYORIY VA SUKUT BO'YICHA O'CHIQ. Yoqilgan
     * yagona joy — ustoz/kurator "Chatlar" hubi (`TeacherGroupChatsPage`),
     * ya'ni AYNAN qidiruv yoqilgan joy va AYNI sabab bilan: filtr o'nlab
     * guruhi bor xodim uchun ma'noli.
     *
     * 🔴 O'QUVCHI SAHIFASIDA ATAYLAB O'CHIQ (`StudentChatPage`): u 1–3
     * guruhda bo'ladi, ya'ni ro'yxatda 2–6 qator turadi va ikkita tanlagich
     * ularning yarmidan ko'p joyni egallardi. Chap ustun u yerda 340px va
     * unda kurator DM'ining "pin qilingan" bo'limi ham bor. Server
     * `/group-categories` yo'lini o'quvchiga umuman ochmagan (403), ya'ni
     * bu qaror BACKEND bilan ham izchil — birini yoqish ikkinchisini ham
     * o'zgartirishni talab qiladi.
     */
    filterable?: boolean
  }>(),
  {
    searchable: false,
    emptyTitle: 'Guruh topilmadi',
    emptyText: '',
    selectedKey: null,
    filterable: false,
  },
)

const emit = defineEmits<{ open: [GroupChatThreadDto] }>()

/*
  ══════════════════════════════════════════════════════════════════════
   R38 · FILTR HOLATI — SERVERGA YUBORILADI
  ══════════════════════════════════════════════════════════════════════

  🔴 NEGA `threads.value.filter(...)` EMAS: server ro'yxatni saralagandan
  KEYIN 200 qatorda kesadi (`GroupChatService.MaxThreads`). Mijozdagi filtr
  faqat SHU 200 qatorni ko'rardi — 201-o'rindagi guruh filtrga to'liq mos
  kelsa ham natijada UMUMAN chiqmasdi va foydalanuvchi "bunday guruh yo'q"
  degan YOLG'ON javobni olardi. Bu UX nuqsoni emas, MA'LUMOT YO'QOLISHI.

  Server tomonda filtr `WHERE` ga tushadi, ya'ni kesish FILTRLANGAN
  to'plamdan boshlanadi — kutilgan xulq aynan shu.

  ⚠️ TUR TANLAGICHIDA `Curator` YO'Q va bo'lishi ham MUMKIN EMAS: kurator
  turidagi guruhning alohida chati yo'q va server bunday so'rovga 400
  qaytaradi (u ro'yxatda hech qachon ko'rinmagan).
*/
const typeFilter = ref<'' | Exclude<GroupTypeName, 'Curator'>>('')
const categoryFilter = ref('')

const threadsQuery = useQuery({
  // ★ Filtrlar KALITGA kiradi: aks holda tanlov o'zgarganda so'rov qayta
  //   yuborilmasdi va ro'yxat eski javobda qotib qolardi.
  queryKey: ['group-chat', 'threads', typeFilter, categoryFilter],
  queryFn: ({ signal }) =>
    fetchGroupChatThreads(
      {
        type: typeFilter.value === '' ? undefined : typeFilter.value,
        categoryId: categoryFilter.value === '' ? undefined : Number(categoryFilter.value),
      },
      { signal },
    ),
  /*
    Ro'yxat o'zi yangilanib turadi: suhbat OCHIQ bo'lmaganda hub ulanmagan
    va yangi xabar haqida boshqa hech narsa xabar bermaydi. Eski ilova ham
    30 sekundlik oraliqda so'rardi (`loadDmThreads`).
  */
  refetchInterval: 30_000,
})

/*
  Yo'nalishlar lug'ati — FAQAT filtr yoqilgan sahifada so'raladi.

  🔴 `enabled` MAJBURIY: o'quvchi sahifasi ham shu komponentni ishlatadi va
  `/api/v1/group-categories` unga 403 qaytaradi (server bu yo'lni faqat
  xodimga ochgan). Shartsiz so'ralsa har o'quvchida ekranda hech narsa
  o'zgarmasdan, konsolda va monitoringda muntazam 403 oqimi paydo bo'lardi.
*/
const categoriesQuery = useQuery({
  queryKey: ['group-categories', 'active'],
  queryFn: ({ signal }) => fetchGroupCategories({ isActive: true }, { signal }),
  enabled: computed(() => props.filterable),
})

const categories = computed(() => categoriesQuery.data.value ?? [])

const threads = computed<GroupChatThreadDto[]>(() => threadsQuery.data.value ?? [])

const error = computed(() =>
  threadsQuery.error.value !== null ? toUserMessage(threadsQuery.error.value) : null,
)

const search = ref('')

/*
  ⚠️ QIDIRUV HAMON MIJOZDA — va u AYNI 200 qatorlik cheklovga tushadi.
  Bu R38 doirasida O'ZGARTIRILMADI (talab tur va kategoriya haqida), lekin
  cheklov shu yerda ochiq yozilgan: qidiruvni ham serverga ko'chirish
  kerak bo'lsa, yo'l tayyor — `fetchGroupChatThreads` ga `search`
  parametrini qo'shish va bu `computed` ni olib tashlash yetadi.
*/
const filtered = computed(() => {
  const query = search.value.trim().toLowerCase()
  if (query.length === 0) return threads.value
  return threads.value.filter((thread) => thread.groupName.toLowerCase().includes(query))
})

/** Filtr tanlangan holatda bo'sh natija — "guruh yo'q" degani EMAS. */
const hasActiveFilter = computed(() => typeFilter.value !== '' || categoryFilter.value !== '')

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

    <!--
      ══════════════════════════════════════════════════════════════════
       R38 · TUR VA YO'NALISH FILTRLARI
      ══════════════════════════════════════════════════════════════════

      🔴 Ikkalasi ham SERVERGA yuboriladi (sabab skriptdagi izohda:
      ro'yxat 200 qatorda kesiladi va mijozdagi filtr undan keyingi
      guruhlarni umuman ko'rmasdi).

      ★ Ustun 340px, shuning uchun ikki tanlagich `grid-cols-2` bilan
      yonma-yon: qator ostiga tushirilsa ro'yxatning ko'rinadigan qismi
      yana bir qator kamayardi.
    -->
    <div
      v-if="props.filterable"
      class="mb-3.5 grid max-w-[320px] grid-cols-2 gap-2"
    >
      <select
        v-model="typeFilter"
        class="zn-input text-[13px]"
        aria-label="Guruh turi bo‘yicha filtr"
      >
        <option value="">
          Barcha turlar
        </option>
        <!--
          ⚠️ "Kurator guruhi" bandi YO'Q va bo'lishi MUMKIN EMAS: kurator
          turidagi guruhning alohida chati yo'q, u bu ro'yxatga umuman
          tushmaydi va server bunday so'rovni 400 bilan rad etadi.
        -->
        <option value="Group">
          Guruh
        </option>
        <option value="Individual">
          Individual
        </option>
      </select>
      <select
        v-model="categoryFilter"
        class="zn-input text-[13px]"
        aria-label="Yo‘nalish bo‘yicha filtr"
      >
        <option value="">
          Barcha yo‘nalishlar
        </option>
        <option
          v-for="category in categories"
          :key="category.id"
          :value="String(category.id)"
        >
          {{ category.name }}
        </option>
      </select>
    </div>

    <DataStatus
      :pending="threadsQuery.isPending.value"
      :error="error"
      :empty="filtered.length === 0"
      :retrying="threadsQuery.isFetching.value"
      :skeleton-rows="3"
      empty-icon="chat"
      :empty-title="hasActiveFilter ? 'Filtrga mos chat topilmadi' : props.emptyTitle"
      :empty-text="
        hasActiveFilter ? 'Filtrni tozalab ko‘ring — boshqa guruhlar chati saqlanib turibdi.' : props.emptyText
      "
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
                <!--
                  R38 · YO'NALISH nishoni. Filtrlangan ro'yxatda "nima
                  bo'yicha filtrladim" savoliga javob qatorning O'ZIDA
                  ko'rinishi kerak — aks holda tanlagichga qayta qarash
                  kerak bo'lardi.

                  ★ `neutral` ohang ATAYLAB: kanal nishoni (oltin/moviy)
                  ASOSIY ajratgich bo'lib qolishi shart — o'quvchida bitta
                  guruh IKKI qator beradi va ularni FAQAT o'sha nishon
                  farqlaydi. Ikkinchi rangli nishon uni ko'zdan yashirardi.
                -->
                <BaseBadge
                  v-if="thread.categoryName !== null"
                  tone="neutral"
                  size="xs"
                >
                  {{ thread.categoryName }}
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
