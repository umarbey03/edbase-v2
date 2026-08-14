<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, ref } from 'vue'

import { downloadTestResultsCsv, fetchTestResults, percentLabel, scoreLabel } from '@/entities/test'
import { toUserMessage } from '@/shared/api'
import { formatDateTime } from '@/shared/lib/datetime'
import { saveBlob } from '@/shared/lib/download'
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { AppIcon, BaseBadge, BaseButton, BaseCard, DataStatus } from '@/shared/ui'

/**
 * Test natijalari (o'quv bo'limi/admin).
 *
 * ★ BITTA URINISH = BITTA QATOR. Guruh nomlari serverda ICHKI so'rov bilan
 * yig'iladi va vergul bilan BIR ustunda keladi (`groupNames` — satr, ro'yxat
 * emas). Eski tizim guruh jadvaliga `outerjoin` qilardi va ikki guruhdagi
 * o'quvchi natijalar jadvalida IKKI MARTA chiqardi — reyting ham, CSV ham
 * buzilardi. Shuning uchun bu yerda HECH QANDAY qayta guruhlash yo'q:
 * server bergan qator o'zgarishsiz chiziladi.
 *
 * Tartib ham serverniki (ball bo'yicha kamayish tartibida) — qayta
 * saralanmaydi.
 */
const props = defineProps<{ testId: number }>()

/*
  Kartochka ↔ jadval: CSS emas, `v-if` — `hidden lg:block` IKKALA daraxtni
  ham quradi (telefonda ko'rinmas jadval ham mount bo'lib, ma'lumot olardi).
  ★ Chegara `lg` (1024px), `md` EMAS: yon menyu ham AYNI shu yerda ochiladi,
  ya'ni iPad tik holati (768px) kartochka bo'lib qoladi — `style.css` dagi
  "md va lg haqidagi asosiy qaror" izohiga qarang.
*/
const { isDesktop } = useBreakpoint()

const resultsQuery = useQuery({
  queryKey: ['tests', props.testId, 'results'],
  queryFn: ({ signal }) => fetchTestResults(props.testId, { signal }),
})

const rows = computed(() => resultsQuery.data.value ?? [])

const errorMessage = computed(() =>
  resultsQuery.error.value !== null ? toUserMessage(resultsQuery.error.value) : null,
)

const exportError = ref<string | null>(null)

const exportMutation = useMutation({
  mutationFn: () => downloadTestResultsCsv(props.testId),
  onSuccess: (file) => {
    exportError.value = null
    saveBlob(file.blob, file.fileName)
  },
  onError: (error: Error) => {
    exportError.value = toUserMessage(error)
  },
})
</script>

<template>
  <BaseCard
    title="Natijalar"
    :subtitle="`${rows.length} ta topshirilgan urinish`"
    flush
  >
    <template #actions>
      <BaseButton
        size="sm"
        variant="secondary"
        :loading="exportMutation.isPending.value"
        :disabled="rows.length === 0"
        @click="exportMutation.mutate()"
      >
        <template #icon>
          <AppIcon
            name="download"
            :size="14"
          />
        </template>
        CSV eksport
      </BaseButton>
    </template>

    <div class="p-3.5 sm:p-5">
      <p
        v-if="exportError !== null"
        class="mb-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
        role="alert"
        v-text="exportError"
      />

      <DataStatus
        :pending="resultsQuery.isPending.value"
        :error="errorMessage"
        :empty="rows.length === 0"
        :retrying="resultsQuery.isFetching.value"
        :skeleton-rows="3"
        empty-icon="award"
        empty-title="Natija yo‘q"
        empty-text="O‘quvchilar testni topshirgach natijalar shu yerda ko‘rinadi."
        @retry="resultsQuery.refetch()"
      >
        <!-- Telefon/planshet: kartochka -->
        <ul
          v-if="!isDesktop"
          class="space-y-2"
        >
          <li
            v-for="row in rows"
            :key="row.attemptId"
            class="rounded-lg border border-line bg-ink-950 p-3"
          >
            <div class="flex items-start justify-between gap-2">
              <p
                class="min-w-0 flex-1 truncate text-sm font-medium text-slate-100"
                v-text="row.studentName"
              />
              <span class="shrink-0 text-sm font-semibold tabular-nums text-slate-100">
                {{ scoreLabel(row.score, row.maxScore) }}
              </span>
            </div>
            <p
              class="mt-0.5 truncate text-xs text-slate-400"
              v-text="row.groupNames"
            />
            <p class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] tabular-nums text-dim">
              <span>{{ percentLabel(row.percent) }}</span>
              <span v-if="row.submittedAt !== null">{{ formatDateTime(row.submittedAt) }}</span>
              <BaseBadge
                v-if="row.closedByTimeout"
                tone="danger"
              >
                Vaqti tugagan
              </BaseBadge>
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
                <th>F.I.Sh.</th>
                <th>Guruh</th>
                <th>Ball</th>
                <th>Foiz</th>
                <th>Topshirilgan</th>
                <th>Holat</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in rows"
                :key="row.attemptId"
              >
                <td
                  class="font-medium text-slate-100"
                  v-text="row.studentName"
                />
                <td
                  class="max-w-64 truncate text-slate-400"
                  v-text="row.groupNames"
                />
                <td
                  class="tabular-nums text-slate-200"
                  v-text="scoreLabel(row.score, row.maxScore)"
                />
                <td
                  class="tabular-nums text-slate-400"
                  v-text="percentLabel(row.percent)"
                />
                <td class="tabular-nums text-slate-400">
                  {{ row.submittedAt === null ? '—' : formatDateTime(row.submittedAt) }}
                </td>
                <td>
                  <BaseBadge :tone="row.closedByTimeout ? 'danger' : 'success'">
                    {{ row.closedByTimeout ? 'Vaqti tugagan' : 'Topshirgan' }}
                  </BaseBadge>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </DataStatus>
    </div>
  </BaseCard>
</template>
