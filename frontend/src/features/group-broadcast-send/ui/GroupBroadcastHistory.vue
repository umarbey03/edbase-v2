<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { fetchGroupBroadcasts } from '@/entities/group-broadcast'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { BaseBadge, BaseCard, DataStatus, PaginationBar } from '@/shared/ui'

/**
 * Yuborilgan xabarlar tarixi — yangisidan eskisiga, sahifalangan.
 *
 * ★ RAQAM SAHIFA BO'YICHA GLOBAL — `ManageGroupsPage`/`TeacherGroupsPage`
 * dagi jadvallar bilan AYNI naqsh: `index + 1` emas, `(page-1) * PAGE_SIZE
 * + index + 1`, aks holda 2-sahifada raqamlash 1 dan qayta boshlanardi.
 */
const { isDesktop } = useBreakpoint()

const page = ref(1)
const PAGE_SIZE = 20

const broadcastsQuery = useQuery({
  queryKey: ['group-broadcasts', page],
  queryFn: ({ signal }) => fetchGroupBroadcasts({ page: page.value, pageSize: PAGE_SIZE }, { signal }),
})

const broadcasts = computed(() => broadcastsQuery.data.value?.items ?? [])
const total = computed(() => broadcastsQuery.data.value?.total ?? 0)
const totalPages = computed(() => broadcastsQuery.data.value?.totalPages ?? 1)

const errorMessage = computed(() =>
  broadcastsQuery.error.value !== null ? toUserMessage(broadcastsQuery.error.value) : null,
)

function refetch(): void {
  void broadcastsQuery.refetch()
}
</script>

<template>
  <BaseCard
    flush
    title="Tarix"
  >
    <div class="p-3.5 sm:p-5">
      <DataStatus
        :pending="broadcastsQuery.isPending.value"
        :error="errorMessage"
        :empty="broadcasts.length === 0"
        :retrying="broadcastsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="send"
        empty-title="Hali xabar yuborilmagan"
        empty-text="Yuqoridagi forma orqali birinchi xabarni yuboring."
        @retry="refetch"
      >
        <!-- Telefon/planshet: kartochka -->
        <ul
          v-if="!isDesktop"
          class="space-y-2"
        >
          <li
            v-for="broadcast in broadcasts"
            :key="broadcast.id"
            class="rounded-lg border border-line bg-ink-950 p-3"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="broadcast.targetGroupNames"
              />
              <span
                class="shrink-0 text-xs tabular-nums text-dim"
                v-text="formatDateTime(broadcast.createdAt)"
              />
            </div>
            <p
              class="mt-1.5 line-clamp-3 text-xs text-slate-400"
              v-text="broadcast.body"
            />
            <div class="mt-2 flex flex-wrap items-center gap-1.5">
              <BaseBadge tone="neutral">
                {{ broadcast.targetGroupCount }} guruh
              </BaseBadge>
              <BaseBadge
                v-if="broadcast.sentToTelegram"
                tone="success"
              >
                Telegram · {{ broadcast.telegramRecipientCount }}
              </BaseBadge>
              <BaseBadge
                v-if="broadcast.sentToPlatformChat"
                tone="success"
              >
                Platforma chati
              </BaseBadge>
            </div>
            <p class="mt-1.5 text-xs text-dim">
              {{ broadcast.authorName }}
              <template v-if="broadcast.templateName !== null">
                · shablon: {{ broadcast.templateName }}
              </template>
            </p>
          </li>
        </ul>

        <!-- Desktop (≥1024px): jadval -->
        <div
          v-else
          class="scroll-x-safe scrollbar-slim"
        >
          <table class="zn-table">
            <thead>
              <tr>
                <th class="w-10">
                  <span class="sr-only">№</span>
                </th>
                <th>Sana</th>
                <th>Yuboruvchi</th>
                <th>Guruhlar</th>
                <th>Matn</th>
                <th>Kanal</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="(broadcast, index) in broadcasts"
                :key="broadcast.id"
              >
                <td
                  class="tabular-nums text-dim"
                  v-text="(page - 1) * PAGE_SIZE + index + 1"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="formatDateTime(broadcast.createdAt)"
                />
                <td
                  class="text-slate-400"
                  v-text="broadcast.authorName"
                />
                <td class="max-w-[220px] text-slate-400">
                  <p
                    class="truncate"
                    :title="broadcast.targetGroupNames"
                    v-text="broadcast.targetGroupNames"
                  />
                  <p class="text-xs text-dim">
                    {{ broadcast.targetGroupCount }} ta
                  </p>
                </td>
                <td class="max-w-[320px]">
                  <p
                    class="truncate text-slate-300"
                    :title="broadcast.body"
                    v-text="broadcast.body"
                  />
                  <p
                    v-if="broadcast.templateName !== null"
                    class="text-xs text-dim"
                  >
                    Shablon: {{ broadcast.templateName }}
                  </p>
                </td>
                <td>
                  <div class="flex flex-wrap gap-1.5">
                    <BaseBadge
                      v-if="broadcast.sentToTelegram"
                      tone="success"
                    >
                      Telegram · {{ broadcast.telegramRecipientCount }}
                    </BaseBadge>
                    <BaseBadge
                      v-if="broadcast.sentToPlatformChat"
                      tone="success"
                    >
                      Chat
                    </BaseBadge>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <PaginationBar
          :page="page"
          :total-pages="totalPages"
          :total="total"
          @update:page="page = $event"
        />
      </DataStatus>
    </div>
  </BaseCard>
</template>
