<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { toDateInput } from '@/entities/recording'
import { fetchLiveSessions } from '@/entities/session'
import SessionCard from '@/entities/session/ui/SessionCard.vue'
import { toUserMessage } from '@/shared/api'
import { formatTime, toDate } from '@/shared/lib/datetime'
import { AppIcon, BaseButton, DataStatus } from '@/shared/ui'

/**
 * Jonli darslar ro'yxati — o'quvchi, ustoz va o'quv bo'limi UCHALASI ham
 * shu ko'rinishni ishlatadi. Backend `GET /live-sessions` ni rolga qarab
 * o'zi cheklaydi, shuning uchun bitta widget yetarli.
 */
const props = withDefaults(defineProps<{
  /**
   * Qidiruv/filtr paneli (loyiha egasi, 2026-08-15: *"o'quv bo'limidagi
   * jonli darslar qismi uchun search filtr"*). Faqat O'QUV BO'LIMIDA
   * yoqilgan (`ManageSessionsPage`): u yerda darslar soni butun markaz
   * bo'yicha (yuzlab), ustoz/o'quvchi esa faqat O'Z darslarini ko'radi —
   * ularga filtr ortiqcha murakkablik bo'lardi.
   */
  searchable?: boolean
}>(), { searchable: false })

const router = useRouter()

/** Bir guruhda 69 tagacha dars bo'ladi — hammasini birdan chizish ortiqcha. */
const UPCOMING_CHUNK = 12
const upcomingLimit = ref(UPCOMING_CHUNK)

const sessionsQuery = useQuery({
  queryKey: ['live-sessions'],
  queryFn: ({ signal }) => fetchLiveSessions({ signal }),
})

/* ==========================================================================
   QIDIRUV / FILTR (faqat `searchable` da)
   ========================================================================== */
const searchText = ref('')
const dateFilter = ref('')
/** Vaqt filtri — erkin matn EMAS, pastdagi `availableTimes` dan tanlanadi. */
const timeFilter = ref('')

/**
 * Vaqt tanlagichning variantlari — guruh yaratilganda BELGILANGAN haqiqiy
 * dars soatlari (loyiha egasi: *"filtrdagi soatlar guruh yaratilgan
 * vaqtdagi belgilangan soatlardan olinishi kerak"*). Shuning uchun bu
 * ixtiyoriy vaqt EMAS — yuklangan darslarning O'ZIDA uchraydigan, ya'ni
 * haqiqatan rejalashtirilgan soatlar ro'yxati (masalan "09:00", "14:30").
 * TO'LIQ ro'yxatdan olinadi (joriy filtrlangan emas), aks holda filtr
 * qo'llangach variantlar g'oyib bo'lib qolardi.
 */
const availableTimes = computed(() => {
  const all = sessionsQuery.data.value ?? []
  const times = new Set(all.map((item) => formatTime(item.scheduledStart)))
  return [...times].sort()
})

const sessions = computed(() => {
  const all = sessionsQuery.data.value ?? []
  if (!props.searchable) return all

  return all.filter((item) => {
    if (dateFilter.value.length > 0 && toDateInput(toDate(item.scheduledStart)) !== dateFilter.value) {
      return false
    }
    if (timeFilter.value.length > 0 && formatTime(item.scheduledStart) !== timeFilter.value) {
      return false
    }
    const query = searchText.value.trim().toLowerCase()
    if (query.length > 0) {
      const haystack = `${item.groupName} ${item.title ?? ''} ${item.hostName ?? ''}`.toLowerCase()
      if (!haystack.includes(query)) return false
    }
    return true
  })
})
const liveSessions = computed(() => sessions.value.filter((item) => item.status === 'Live'))
const upcomingAll = computed(() => sessions.value.filter((item) => item.status === 'Scheduled'))
const upcomingSessions = computed(() => upcomingAll.value.slice(0, upcomingLimit.value))
const hasMoreUpcoming = computed(() => upcomingAll.value.length > upcomingLimit.value)

/** O'tgan darslar teskari tartibda — eng yaqini yuqorida foydaliroq. */
const pastSessions = computed(() =>
  sessions.value
    .filter((item) => item.status === 'Ended' || item.status === 'Cancelled')
    .slice()
    .reverse()
    .slice(0, 10),
)

const errorMessage = computed(() =>
  sessionsQuery.error.value !== null ? toUserMessage(sessionsQuery.error.value) : null,
)

const filtersActive = computed(
  () => dateFilter.value.length > 0 || timeFilter.value.length > 0 || searchText.value.trim().length > 0,
)

function clearFilters(): void {
  searchText.value = ''
  dateFilter.value = ''
  timeFilter.value = ''
}

function openSession(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}
</script>

<template>
  <div>
    <!-- ------------------------------------------------------ qidiruv/filtr -->
    <div
      v-if="props.searchable"
      class="mb-4 flex flex-wrap items-end gap-2.5"
    >
      <label class="min-w-[180px] flex-1">
        <span class="mb-1 block text-xs font-medium text-slate-400">Qidiruv</span>
        <input
          v-model="searchText"
          type="text"
          class="zn-input"
          placeholder="Guruh, dars yoki ustoz nomi..."
        >
      </label>
      <label class="w-[150px]">
        <span class="mb-1 block text-xs font-medium text-slate-400">Sana</span>
        <input
          v-model="dateFilter"
          type="date"
          class="zn-input"
        >
      </label>
      <label class="w-[130px]">
        <span class="mb-1 block text-xs font-medium text-slate-400">Vaqt</span>
        <select
          v-model="timeFilter"
          class="zn-input"
        >
          <option value="">
            Barchasi
          </option>
          <option
            v-for="time in availableTimes"
            :key="time"
            :value="time"
          >
            {{ time }}
          </option>
        </select>
      </label>
      <button
        v-if="filtersActive"
        type="button"
        class="tap-target flex items-center gap-1 rounded-lg px-2 text-xs font-semibold text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
        @click="clearFilters"
      >
        <AppIcon
          name="close"
          :size="13"
        />
        Tozalash
      </button>
    </div>

    <DataStatus
      :pending="sessionsQuery.isPending.value"
      :error="errorMessage"
      :empty="sessions.length === 0"
      :retrying="sessionsQuery.isFetching.value"
      :empty-title="filtersActive ? 'Hech narsa topilmadi' : 'Darslar topilmadi'"
      :empty-text="
        filtersActive
          ? 'Filtrga mos dars yo‘q — mezonlarni o‘zgartirib ko‘ring.'
          : 'Yangi dars rejalashtirilganda shu yerda ko‘rinadi.'
      "
      @retry="sessionsQuery.refetch()"
    >
      <section v-if="liveSessions.length > 0">
        <h2 class="mb-2.5 flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-rose-400">
          <span class="size-1.5 animate-pulse rounded-full bg-rose-400" />
          Hozir efirda
        </h2>
        <div class="space-y-3">
          <SessionCard
            v-for="item in liveSessions"
            :key="item.id"
            :session="item"
            @join="openSession"
          />
        </div>
      </section>

      <section
        v-if="upcomingSessions.length > 0"
        :class="liveSessions.length > 0 ? 'mt-7' : ''"
      >
        <h2 class="mb-2.5 text-xs font-semibold uppercase tracking-wide text-slate-400">
          Yaqinda bo‘ladi
        </h2>
        <div class="space-y-3">
          <SessionCard
            v-for="item in upcomingSessions"
            :key="item.id"
            :session="item"
            @join="openSession"
          />
        </div>
        <BaseButton
          v-if="hasMoreUpcoming"
          class="mt-3"
          size="sm"
          variant="secondary"
          block
          @click="upcomingLimit += UPCOMING_CHUNK"
        >
          Yana {{ Math.min(UPCOMING_CHUNK, upcomingAll.length - upcomingLimit) }} ta dars
        </BaseButton>
      </section>

      <section
        v-if="pastSessions.length > 0"
        class="mt-7"
      >
        <h2 class="mb-2.5 text-xs font-semibold uppercase tracking-wide text-slate-400">
          O‘tgan darslar
        </h2>
        <div class="space-y-3 opacity-60">
          <SessionCard
            v-for="item in pastSessions"
            :key="item.id"
            :session="item"
            @join="openSession"
          />
        </div>
      </section>
    </DataStatus>
  </div>
</template>
