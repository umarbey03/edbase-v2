<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import { periodLabel } from '@/entities/payment'
import { fetchPenaltyReport, staffRoleLabel } from '@/entities/penalty'
import { toUserMessage } from '@/shared/api'
import { formatMoney } from '@/shared/lib/money'
import { showToast } from '@/shared/lib/useToast'
import type { PenaltyReportUserDto } from '@/shared/types'
import { AppIcon, BaseButton, BaseDrawer, DataStatus } from '@/shared/ui'

/**
 * ════════════════════════════════════════════════════════════════════════
 *  OYLIK JARIMA HISOBOTI (2026-08-18)
 * ════════════════════════════════════════════════════════════════════════
 *
 * Xodim kesimida, ichida tur kesimi — "kimdan qancha va NIMA UCHUN
 * ushlandi" degan savolga bitta ekranda javob beradi.
 *
 * ★ SERVERDAN ALOHIDA OLINADI, jadvaldan guruhlanmaydi: jadval 20 tadan
 * keladi va undan hisoblangan "jami" faqat birinchi sahifani qamrardi.
 *
 * ★ BEKOR QILINGANLAR KIRMAYDI (server qoidasi): ular pul EMAS. Aks
 * holda hisobotdagi "jami" oylikdagi ushlanmadan katta chiqib, xodim
 * bilan bahsga sabab bo'lardi.
 *
 * ★ NUSXALASH — "EKSPORT" O'RNIGA: hisobot amalda Telegramga tashlanadi
 * yoki xabarga qo'yiladi. Fayl yuklab olish qo'shimcha qadam bo'lardi.
 */
const props = defineProps<{ open: boolean; period: string }>()

const emit = defineEmits<{ close: [] }>()

const reportQuery = useQuery({
  queryKey: ['penalties', 'report', computed(() => props.period)],
  queryFn: ({ signal }) => fetchPenaltyReport(props.period, { signal }),
  enabled: computed(() => props.open && props.period.length > 0),
})

const report = computed(() => reportQuery.data.value ?? null)
const users = computed(() => report.value?.users ?? [])

const loadError = computed(() =>
  reportQuery.error.value !== null ? toUserMessage(reportQuery.error.value) : null,
)

/** Ochilgan xodim satrlari — hammasi birdan yoyilsa ro'yxat o'qilmas edi. */
const expanded = ref<number | null>(null)

watch(
  () => props.open,
  () => {
    expanded.value = null
  },
)

function toggle(userId: number): void {
  expanded.value = expanded.value === userId ? null : userId
}

/* ------------------------------------------------------------ nusxalash */

function userText(user: PenaltyReportUserDto): string {
  const lines = user.lines.map((line, index) => {
    const times = line.count > 1 ? ` (${line.count} marta)` : ''
    return `   ${index + 1}) ${line.label}${times} — ${formatMoney(line.amount)} so'm`
  })

  return [`${user.userName} — umumiy jarima: ${formatMoney(user.total)} so'm`, ...lines].join('\n')
}

function fullText(): string {
  const head = `${periodLabel(props.period).toUpperCase()} — JARIMALAR HISOBOTI`

  const body = users.value.map((user, index) => `${index + 1}. ${userText(user)}`).join('\n\n')

  return `${head}\n\n${body}\n\nJAMI: ${formatMoney(report.value?.total ?? 0)} so'm`
}

async function copy(text: string, label: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    showToast(`${label} nusxalandi`)
  } catch {
    // Brauzer ruxsat bermasa (eski brauzer yoki himoyalanmagan
    // ulanish) — jim qolmaymiz, sabab aytiladi.
    showToast('Nusxalab bo‘lmadi — matnni qo‘lda belgilang', 'error')
  }
}
</script>

<template>
  <BaseDrawer
    :open="props.open"
    title="Oylik jarima hisoboti"
    :subtitle="periodLabel(props.period)"
    @close="emit('close')"
  >
    <DataStatus
      :pending="reportQuery.isPending.value"
      :error="loadError"
      :empty="users.length === 0"
      :retrying="reportQuery.isFetching.value"
      :skeleton-rows="4"
      empty-icon="clipboard"
      empty-title="Jarima yo‘q"
      empty-text="Bu oyda hech kimga jarima yozilmagan."
      @retry="reportQuery.refetch()"
    >
      <div class="mb-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-line bg-ink-900 p-4">
        <div>
          <p class="text-xs text-slate-400">
            {{ users.length }} ta xodim
          </p>
          <p
            class="text-2xl font-bold tabular-nums text-rose-400"
            v-text="`${formatMoney(report?.total ?? 0)} so‘m`"
          />
        </div>

        <BaseButton
          variant="secondary"
          @click="copy(fullText(), 'Hisobot')"
        >
          <template #icon>
            <AppIcon
              name="clipboard"
              :size="15"
            />
          </template>
          Nusxalash
        </BaseButton>
      </div>

      <ol class="space-y-2">
        <li
          v-for="(user, index) in users"
          :key="user.userId"
          class="overflow-hidden rounded-xl border border-line bg-ink-900"
        >
          <button
            type="button"
            class="flex w-full items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-ink-800"
            :aria-expanded="expanded === user.userId"
            @click="toggle(user.userId)"
          >
            <span
              class="w-5 shrink-0 text-sm tabular-nums text-dim"
              v-text="`${index + 1}.`"
            />

            <span class="min-w-0 flex-1">
              <span
                class="block truncate font-medium text-slate-100"
                v-text="user.userName"
              />
              <span
                class="block text-xs text-dim"
                v-text="`${staffRoleLabel(user.userRole)} · ${user.lines.length} xil qoidabuzarlik`"
              />
            </span>

            <span
              class="shrink-0 font-bold tabular-nums text-rose-300"
              v-text="`${formatMoney(user.total)} so‘m`"
            />

            <!-- "chevron-up" to'plamda yo'q — burchak burilish bilan. -->
            <AppIcon
              name="chevron-down"
              :size="16"
              class="shrink-0 text-slate-500 transition-transform"
              :class="expanded === user.userId ? 'rotate-180' : ''"
            />
          </button>

          <div
            v-if="expanded === user.userId"
            class="border-t border-line px-4 py-3"
          >
            <ol class="space-y-1.5">
              <li
                v-for="(line, lineIndex) in user.lines"
                :key="line.label"
                class="flex items-baseline gap-2 text-sm"
              >
                <span
                  class="w-5 shrink-0 tabular-nums text-dim"
                  v-text="`${lineIndex + 1})`"
                />
                <span class="min-w-0 flex-1 text-slate-300">
                  {{ line.label }}
                  <!-- "1 marta" ma'lumot bermaydi — faqat takrorlanganda. -->
                  <span
                    v-if="line.count > 1"
                    class="text-xs text-dim"
                  >({{ line.count }} marta)</span>
                </span>
                <span
                  class="shrink-0 tabular-nums text-slate-200"
                  v-text="`${formatMoney(line.amount)} so‘m`"
                />
              </li>
            </ol>

            <BaseButton
              size="sm"
              variant="secondary"
              class="mt-3"
              @click="copy(userText(user), user.userName)"
            >
              Shu xodimni nusxalash
            </BaseButton>
          </div>
        </li>
      </ol>
    </DataStatus>
  </BaseDrawer>
</template>
