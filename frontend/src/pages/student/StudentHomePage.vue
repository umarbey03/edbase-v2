<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { fetchLiveSessions } from '@/entities/session'
import SessionCard from '@/entities/session/ui/SessionCard.vue'
import { roleLabel, roleTone } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseAvatar, BaseBadge, BaseButton, BaseSpinner } from '@/shared/ui'

const router = useRouter()
const auth = useAuthStore()

const sessionsQuery = useQuery({
  queryKey: ['live-sessions'],
  queryFn: ({ signal }) => fetchLiveSessions({ signal }),
})

const sessions = computed(() => sessionsQuery.data.value ?? [])
const liveSessions = computed(() => sessions.value.filter((item) => item.status === 'Live'))
const upcomingSessions = computed(() =>
  sessions.value.filter((item) => item.status === 'Scheduled'),
)
const pastSessions = computed(() =>
  sessions.value.filter((item) => item.status === 'Ended' || item.status === 'Cancelled'),
)

const errorMessage = computed(() =>
  sessionsQuery.error.value !== null ? toUserMessage(sessionsQuery.error.value) : null,
)

function openSession(sessionId: number): void {
  void router.push({ name: 'live-room', params: { sessionId: String(sessionId) } })
}

async function handleLogout(): Promise<void> {
  await auth.logout()
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="min-h-dvh bg-ink-950">
    <header class="border-b border-line bg-ink-900/70 backdrop-blur">
      <div class="mx-auto flex max-w-4xl items-center gap-3 px-4 py-3.5">
        <div class="flex size-9 items-center justify-center rounded-xl bg-brand-600 text-sm font-bold text-white">
          Z
        </div>
        <div class="min-w-0 flex-1">
          <p class="truncate text-sm font-semibold text-slate-100" v-text="auth.displayName" />
          <BaseBadge v-if="auth.role !== null" :tone="roleTone(auth.role)">
            {{ roleLabel(auth.role) }}
          </BaseBadge>
        </div>
        <BaseAvatar :name="auth.displayName" size="md" />
        <button
          type="button"
          class="rounded-lg p-2 text-slate-400 transition-colors hover:bg-white/5 hover:text-slate-100"
          title="Chiqish"
          @click="handleLogout"
        >
          <AppIcon name="logout" :size="18" />
        </button>
      </div>
    </header>

    <main class="mx-auto max-w-4xl px-4 py-6">
      <h1 class="text-xl font-semibold tracking-tight text-slate-50">Darslarim</h1>
      <p class="mt-1 text-sm text-slate-500">Jonli va rejalashtirilgan darslaringiz</p>

      <!-- Yuklanmoqda -->
      <div v-if="sessionsQuery.isPending.value" class="mt-6 space-y-3">
        <div
          v-for="index in 3"
          :key="index"
          class="h-24 animate-pulse rounded-2xl bg-ink-900 ring-1 ring-inset ring-line"
        />
      </div>

      <!-- Xatolik -->
      <div
        v-else-if="errorMessage !== null"
        class="mt-6 rounded-2xl bg-rose-500/10 p-5 text-center ring-1 ring-inset ring-rose-500/25"
      >
        <p class="text-sm text-rose-200" v-text="errorMessage" />
        <BaseButton
          class="mt-4"
          size="sm"
          variant="secondary"
          :loading="sessionsQuery.isFetching.value"
          @click="sessionsQuery.refetch()"
        >
          <template #icon><AppIcon name="refresh" :size="14" /></template>
          Qayta urinish
        </BaseButton>
      </div>

      <!-- Bo'sh -->
      <div
        v-else-if="sessions.length === 0"
        class="mt-6 rounded-2xl bg-ink-900 p-10 text-center ring-1 ring-inset ring-line"
      >
        <div class="mx-auto flex size-12 items-center justify-center rounded-2xl bg-ink-800 text-slate-600">
          <AppIcon name="calendar" :size="24" />
        </div>
        <p class="mt-4 text-sm font-medium text-slate-300">Darslar topilmadi</p>
        <p class="mt-1 text-xs text-slate-500">Yangi dars rejalashtirilganda shu yerda ko‘rinadi.</p>
      </div>

      <template v-else>
        <section v-if="liveSessions.length > 0" class="mt-6">
          <h2 class="mb-2.5 flex items-center gap-2 text-xs font-semibold uppercase tracking-wide text-rose-300">
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

        <section v-if="upcomingSessions.length > 0" class="mt-7">
          <h2 class="mb-2.5 text-xs font-semibold uppercase tracking-wide text-slate-500">
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
        </section>

        <section v-if="pastSessions.length > 0" class="mt-7">
          <h2 class="mb-2.5 text-xs font-semibold uppercase tracking-wide text-slate-500">
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
      </template>

      <div v-if="sessionsQuery.isFetching.value && !sessionsQuery.isPending.value" class="mt-6 flex justify-center">
        <BaseSpinner size="sm" class="text-slate-600" />
      </div>
    </main>
  </div>
</template>
