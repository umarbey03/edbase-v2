<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchMySelfStudyStudents, SELF_STUDY_CURATOR_KEYS } from '@/entities/self-study-curator'
import type { MySelfStudyStudentDto } from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseCard, DataStatus } from '@/shared/ui'

/**
 * KURATORNING "MUSTAQIL O'QUVCHILARIM" RO'YXATI (2026-11-18).
 *
 * Avval asosiy o'quvchilar, keyin zaxirada turganlar; ichida — tekshiruv
 * kutayotgan ishi ko'p o'quvchi yuqorida (tartib serverda).
 */
const emit = defineEmits<{ open: [student: MySelfStudyStudentDto] }>()

const query = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.mine,
  queryFn: ({ signal }) => fetchMySelfStudyStudents({ signal }),
  refetchInterval: 60_000,
})

const rows = computed(() => query.data.value ?? [])
const primary = computed(() => rows.value.filter((r) => !r.isBackup))
const backup = computed(() => rows.value.filter((r) => r.isBackup))
const pendingTotal = computed(() => primary.value.reduce((sum, r) => sum + r.pendingSubmissions, 0))
</script>

<template>
  <div class="space-y-4">
    <DataStatus
      :pending="query.isPending.value"
      :error="query.isError.value ? toUserMessage(query.error.value) : null"
      :empty="query.isSuccess.value && rows.length === 0"
      :retrying="query.isFetching.value"
      empty-icon="users"
      empty-title="Mustaqil o‘quvchi yo‘q"
      empty-text="Sizga hali Mustaqil o‘quvchi biriktirilmagan."
      @retry="query.refetch()"
    />

    <div
      v-if="primary.length > 0"
      class="grid gap-3 sm:grid-cols-3"
    >
      <BaseCard class="p-4">
        <p class="text-xs text-slate-400">
          O‘quvchilarim
        </p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">
          {{ primary.length }}
        </p>
      </BaseCard>
      <BaseCard class="p-4">
        <p class="text-xs text-slate-400">
          Tekshiruv kutmoqda
        </p>
        <p
          class="mt-1 text-2xl font-semibold"
          :class="pendingTotal > 0 ? 'text-amber-300' : 'text-slate-100'"
        >
          {{ pendingTotal }}
        </p>
      </BaseCard>
      <BaseCard class="p-4">
        <p class="text-xs text-slate-400">
          Zaxirada
        </p>
        <p class="mt-1 text-2xl font-semibold text-slate-100">
          {{ backup.length }}
        </p>
      </BaseCard>
    </div>

    <template
      v-for="section in [
        { key: 'primary', title: 'Asosiy kurator sifatida', items: primary },
        { key: 'backup', title: 'Zaxira kurator sifatida', items: backup },
      ]"
      :key="section.key"
    >
      <section
        v-if="section.items.length > 0"
        class="space-y-2"
      >
        <h2 class="text-sm font-semibold text-slate-300">
          {{ section.title }}
        </h2>
        <ul class="space-y-2">
          <li
            v-for="row in section.items"
            :key="row.studentId"
          >
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-xl border border-line bg-ink-900 px-4 py-3 text-left hover:bg-ink-850"
              @click="emit('open', row)"
            >
              <span class="min-w-0 flex-1">
                <span class="block truncate text-sm font-semibold text-slate-100">{{ row.fullName }}</span>
                <span class="block text-xs text-slate-400">
                  <template v-if="row.subscriptionEndsAt !== null">
                    Obuna {{ formatDate(row.subscriptionEndsAt) }} gacha
                  </template>
                  <template v-else>Obuna yo‘q</template>
                </span>
              </span>
              <BaseBadge
                v-if="!row.subscriptionActive"
                tone="neutral"
                size="xs"
              >
                Obuna tugagan
              </BaseBadge>
              <BaseBadge
                v-if="row.pendingSubmissions > 0"
                tone="warning"
                size="xs"
              >
                {{ row.pendingSubmissions }} ta tekshiruv
              </BaseBadge>
              <AppIcon
                name="chevron-right"
                :size="16"
                class="text-slate-500"
              />
            </button>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>
