<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

import { fetchLiveSessions } from '@/entities/session'
import SessionCard from '@/entities/session/ui/SessionCard.vue'
import { toUserMessage } from '@/shared/api'
import { BaseButton, DataStatus } from '@/shared/ui'

/**
 * Jonli darslar ro'yxati — o'quvchi, ustoz va o'quv bo'limi UCHALASI ham
 * shu ko'rinishni ishlatadi. Backend `GET /live-sessions` ni rolga qarab
 * o'zi cheklaydi, shuning uchun bitta widget yetarli.
 */
const router = useRouter()

/** Bir guruhda 69 tagacha dars bo'ladi — hammasini birdan chizish ortiqcha. */
const UPCOMING_CHUNK = 12
const upcomingLimit = ref(UPCOMING_CHUNK)

const sessionsQuery = useQuery({
  queryKey: ['live-sessions'],
  queryFn: ({ signal }) => fetchLiveSessions({ signal }),
})

const sessions = computed(() => sessionsQuery.data.value ?? [])
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

function openSession(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}
</script>

<template>
  <DataStatus
    :pending="sessionsQuery.isPending.value"
    :error="errorMessage"
    :empty="sessions.length === 0"
    :retrying="sessionsQuery.isFetching.value"
    empty-title="Darslar topilmadi"
    empty-text="Yangi dars rejalashtirilganda shu yerda ko‘rinadi."
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
</template>
