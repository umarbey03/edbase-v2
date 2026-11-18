<script setup lang="ts">
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { computed, ref, watch } from 'vue'

import {
  assignStudentCurator,
  CURATOR_ROLE_LABELS,
  fetchCuratorCandidates,
  fetchStudentCurator,
  SELF_STUDY_CURATOR_KEYS,
  unassignStudentCurator,
} from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { formatDate } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

/**
 * O'QUVCHI PROFILIDAGI "KURATOR" BO'LIMI (2026-11-18).
 *
 * Mustaqil o'quvchiga asosiy va zaxira kurator biriktiriladi: asosiy kurator
 * vazifani tekshiradi, yozishmada javob beradi va darsni «o'zlashtirdi» deb
 * belgilaydi; zaxira kurator u yo'qligida tekshiradi.
 *
 * ★ Faqat obunasi bor (Mustaqil) o'quvchida ko'rsatiladi — chaqiruvchi
 *   `has-subscription` bilan beradi. Guruh o'quvchisining kuratori guruh
 *   o'rindig'idan keladi, server ham bunga 409 qaytaradi.
 */
const props = defineProps<{ studentId: number, hasSubscription: boolean }>()

const queryClient = useQueryClient()
const confirm = useConfirm()

const infoQuery = useQuery({
  queryKey: computed(() => SELF_STUDY_CURATOR_KEYS.student(props.studentId)),
  queryFn: ({ signal }) => fetchStudentCurator(props.studentId, { signal }),
  enabled: computed(() => props.hasSubscription),
})

const formOpen = ref(false)
const candidatesQuery = useQuery({
  queryKey: SELF_STUDY_CURATOR_KEYS.candidates,
  queryFn: ({ signal }) => fetchCuratorCandidates({ signal }),
  enabled: formOpen,
})

const current = computed(() => infoQuery.data.value?.current ?? null)
const history = computed(() => (infoQuery.data.value?.history ?? []).filter((h) => h.endedAt !== null))
const candidates = computed(() => candidatesQuery.data.value ?? [])

const curatorId = ref<number | null>(null)
const backupId = ref<number | null>(null)

watch(formOpen, (open) => {
  if (!open) return
  curatorId.value = current.value?.curatorId ?? null
  backupId.value = current.value?.backupCuratorId ?? null
})

const backupOptions = computed(() => candidates.value.filter((c) => c.id !== curatorId.value))

watch(curatorId, (id) => {
  if (id !== null && backupId.value === id) backupId.value = null
})

function candidateLabel(c: { fullName: string, role: string, activeStudents: number }): string {
  return `${c.fullName} — ${CURATOR_ROLE_LABELS[c.role] ?? c.role}, ${c.activeStudents} o‘quvchi`
}

function refresh(): void {
  void queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.all })
}

const save = useMutation({
  mutationFn: () => assignStudentCurator(props.studentId, {
    curatorId: curatorId.value as number,
    backupCuratorId: backupId.value,
  }),
  onSuccess: () => {
    showToast('Kurator saqlandi', 'success')
    formOpen.value = false
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

const remove = useMutation({
  mutationFn: () => unassignStudentCurator(props.studentId),
  onSuccess: () => {
    showToast('Kurator olib qo‘yildi', 'success')
    refresh()
  },
  onError: (error) => showToast(toUserMessage(error), 'error'),
})

async function askRemove(): Promise<void> {
  const ok = await confirm({
    title: 'Kuratorni olib qo‘yasizmi?',
    message: 'O‘quvchi kuratorsiz qoladi: vazifalarini hech kim tekshirmaydi va keyingi dars ochilmaydi.',
    confirmLabel: 'Olib qo‘yish',
    tone: 'danger',
  })
  if (ok) remove.mutate()
}
</script>

<template>
  <section
    v-if="props.hasSubscription"
    class="rounded-2xl border border-line bg-ink-800 p-3.5"
  >
    <header class="mb-3 flex items-center gap-2">
      <AppIcon
        name="user-check"
        :size="16"
        class="text-slate-400"
      />
      <h3 class="flex-1 text-sm font-semibold text-slate-100">
        Kurator
      </h3>
      <BaseButton
        v-if="!formOpen && !infoQuery.isPending.value"
        size="sm"
        variant="secondary"
        @click="formOpen = true"
      >
        {{ current === null ? 'Biriktirish' : 'Almashtirish' }}
      </BaseButton>
    </header>

    <p
      v-if="infoQuery.isError.value"
      class="text-xs text-rose-300"
    >
      {{ toUserMessage(infoQuery.error.value) }}
    </p>

    <form
      v-if="formOpen"
      class="mb-3 space-y-2.5 rounded-xl border border-line bg-ink-900 p-3"
      @submit.prevent="save.mutate()"
    >
      <label class="block space-y-1">
        <span class="text-xs text-slate-400">Asosiy kurator — tekshiradi va 24/7 javob beradi</span>
        <select
          v-model="curatorId"
          required
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
          <option
            :value="null"
            disabled
          >
            Tanlang
          </option>
          <option
            v-for="c in candidates"
            :key="c.id"
            :value="c.id"
          >
            {{ candidateLabel(c) }}
          </option>
        </select>
      </label>
      <label class="block space-y-1">
        <span class="text-xs text-slate-400">Zaxira kurator (ixtiyoriy) — asosiy yo‘qligida</span>
        <select
          v-model="backupId"
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
          <option :value="null">
            Yo‘q
          </option>
          <option
            v-for="c in backupOptions"
            :key="c.id"
            :value="c.id"
          >
            {{ candidateLabel(c) }}
          </option>
        </select>
      </label>
      <p
        v-if="candidatesQuery.isSuccess.value && candidates.length === 0"
        class="text-xs text-amber-300"
      >
        Faol kurator yoki ustoz yo‘q. Avval «Foydalanuvchilar» bo‘limida kurator qo‘shing.
      </p>
      <div class="flex justify-end gap-2">
        <BaseButton
          size="sm"
          variant="ghost"
          type="button"
          @click="formOpen = false"
        >
          Bekor
        </BaseButton>
        <BaseButton
          size="sm"
          type="submit"
          :loading="save.isPending.value"
          :disabled="curatorId === null"
        >
          Saqlash
        </BaseButton>
      </div>
    </form>

    <div
      v-if="!formOpen && infoQuery.isSuccess.value"
      class="space-y-2"
    >
      <div
        v-if="current !== null"
        class="flex items-start gap-2 rounded-lg border border-line bg-ink-900 px-3 py-2"
      >
        <span class="min-w-0 flex-1">
          <span class="block truncate text-[13px] font-semibold text-slate-100">{{ current.curatorName }}</span>
          <span class="block text-[11px] text-dim">
            Zaxira: {{ current.backupCuratorName ?? 'yo‘q' }} · {{ formatDate(current.assignedAt) }} dan
            <template v-if="current.assignedByName !== null"> · {{ current.assignedByName }}</template>
          </span>
        </span>
        <BaseBadge
          tone="success"
          size="xs"
        >
          Faol
        </BaseBadge>
        <button
          type="button"
          class="rounded-md p-1 text-slate-400 hover:bg-ink-800 hover:text-rose-300"
          aria-label="Kuratorni olib qo‘yish"
          :disabled="remove.isPending.value"
          @click="askRemove()"
        >
          <AppIcon
            name="close"
            :size="14"
          />
        </button>
      </div>
      <p
        v-else
        class="flex items-center gap-2 rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-xs text-amber-200"
      >
        <AppIcon
          name="alert"
          :size="14"
        />
        Kurator biriktirilmagan — vazifalarni hech kim tekshirmaydi.
      </p>

      <details
        v-if="history.length > 0"
        class="text-[11px] text-dim"
      >
        <summary class="cursor-pointer select-none">
          Avvalgi kuratorlar ({{ history.length }})
        </summary>
        <ul class="mt-1.5 space-y-1">
          <li
            v-for="h in history"
            :key="h.id"
          >
            {{ h.curatorName }}<template v-if="h.backupCuratorName !== null">
              (zaxira: {{ h.backupCuratorName }})
            </template>
            · {{ formatDate(h.assignedAt) }} — {{ formatDate(h.endedAt as string) }}
          </li>
        </ul>
      </details>
    </div>
  </section>
</template>
