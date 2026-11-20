<script setup lang="ts">
import { useMutation, useQueryClient } from '@tanstack/vue-query'
import { computed, reactive, ref } from 'vue'

import {
  DEFAULT_CURATOR_CAPACITY,
  SELF_STUDY_PANEL_KEYS,
  addCuratorToPool,
  removeCuratorFromPool,
  runCuratorPool,
  updateCuratorPoolMember,
  type CuratorPoolDto,
  type CuratorPoolMemberDto,
  type SelfStudyCuratorRowDto,
} from '@/entities/self-study-panel'
import { SELF_STUDY_CURATOR_KEYS } from '@/entities/self-study-curator'
import { toUserMessage } from '@/shared/api'
import { formatDateNumeric } from '@/shared/lib/datetime'
import { useConfirm } from '@/shared/lib/useConfirm'
import { showToast } from '@/shared/lib/useToast'
import { AppIcon, BaseBadge, BaseButton, BaseCard } from '@/shared/ui'

/**
 * MUSTAQIL KURATORLARI — AVTOMATIK BIRIKTIRISH (2026-11-20, loyiha egasi).
 *
 * Ro'yxatdagi kuratorlar YUQORIDAN PASTGA to'ldiriladi: birinchisining
 * sig'imi (standart 100) to'lgach yangi o'quvchi keyingisiga o'tadi. Hammasi
 * to'lsa o'quvchi navbatda qoladi va eslatma chiqadi; yangi kurator
 * qo'shilishi bilan navbat unga taqsimlanadi. Server har daqiqada ham
 * taqsimlaydi — tugma faqat "hozir" kutmaslik uchun.
 */
const props = defineProps<{
  pool: CuratorPoolDto | null
  /** Kurator bo'la oladigan xodimlar (qo'shish ro'yxati shundan). */
  candidates: SelfStudyCuratorRowDto[]
}>()

const queryClient = useQueryClient()
const confirm = useConfirm()

const error = ref<string | null>(null)
const newCuratorId = ref<number | ''>('')
const newCapacity = ref(DEFAULT_CURATOR_CAPACITY)
/** Tahrirlanayotgan sig'imlar — saqlanguncha shu yerda. */
const capacityDraft = reactive(new Map<number, number>())

const members = computed(() => props.pool?.members ?? [])
const inPool = computed(() => new Set(members.value.map((m) => m.curatorId)))
const addable = computed(() =>
  props.candidates
    .filter((c) => !inPool.value.has(c.id))
    .sort((a, b) => (a.role === b.role ? a.fullName.localeCompare(b.fullName) : a.role === 'Assistant' ? -1 : 1)))

function loadPercent(member: CuratorPoolMemberDto): number {
  return Math.min(100, Math.round((member.activeStudents / member.capacity) * 100))
}

function loadTone(member: CuratorPoolMemberDto): string {
  if (member.activeStudents >= member.capacity) return 'bg-rose-500'
  if (member.activeStudents >= member.capacity * 0.9) return 'bg-amber-500'
  return 'bg-emerald-500'
}

async function refresh(next?: CuratorPoolDto): Promise<void> {
  if (next !== undefined) queryClient.setQueryData(SELF_STUDY_PANEL_KEYS.pool, next)
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: SELF_STUDY_PANEL_KEYS.all }),
    queryClient.invalidateQueries({ queryKey: SELF_STUDY_CURATOR_KEYS.all }),
  ])
}

const add = useMutation({
  mutationFn: () => addCuratorToPool({ curatorId: Number(newCuratorId.value), capacity: newCapacity.value }),
  onSuccess: async (next) => {
    error.value = null
    newCuratorId.value = ''
    newCapacity.value = DEFAULT_CURATOR_CAPACITY
    await refresh(next)
    showToast('Kurator qo‘shildi — navbatdagi o‘quvchilar unga biriktirildi')
  },
  onError: (err) => { error.value = toUserMessage(err) },
})

const update = useMutation({
  mutationFn: (args: { member: CuratorPoolMemberDto, capacity: number, isAccepting: boolean }) =>
    updateCuratorPoolMember(args.member.curatorId, { capacity: args.capacity, isAccepting: args.isAccepting }),
  onSuccess: async (next, args) => {
    error.value = null
    capacityDraft.delete(args.member.curatorId)
    await refresh(next)
    showToast('Saqlandi')
  },
  onError: (err) => { error.value = toUserMessage(err) },
})

const remove = useMutation({
  mutationFn: (member: CuratorPoolMemberDto) => removeCuratorFromPool(member.curatorId),
  onSuccess: async (next) => {
    error.value = null
    await refresh(next)
    showToast('Ro‘yxatdan chiqarildi')
  },
  onError: (err) => { error.value = toUserMessage(err) },
})

const run = useMutation({
  mutationFn: runCuratorPool,
  onSuccess: async (result) => {
    error.value = null
    await refresh()
    showToast(
      result.assigned > 0
        ? `${result.assigned} ta o‘quvchi biriktirildi${result.stillWaiting > 0 ? `, ${result.stillWaiting} ta navbatda` : ''}`
        : result.stillWaiting > 0 ? `Bo‘sh joy yo‘q — ${result.stillWaiting} ta o‘quvchi navbatda` : 'Navbat bo‘sh',
      result.stillWaiting > 0 ? 'warning' : 'success',
    )
  },
  onError: (err) => { error.value = toUserMessage(err) },
})

async function askRemove(member: CuratorPoolMemberDto): Promise<void> {
  const ok = await confirm({
    title: 'Ro‘yxatdan chiqarish',
    message: `${member.fullName} ga yangi o‘quvchi biriktirilmaydi. Hozirgi ${member.activeStudents} ta o‘quvchisi unda qoladi — kerak bo‘lsa «O‘quvchilar» tabida boshqasiga o‘tkazing.`,
    confirmLabel: 'Chiqarish',
  })
  if (ok) remove.mutate(member)
}

function saveCapacity(member: CuratorPoolMemberDto): void {
  const capacity = capacityDraft.get(member.curatorId)
  if (capacity === undefined || capacity === member.capacity) return
  update.mutate({ member, capacity, isAccepting: member.isAccepting })
}
</script>

<template>
  <BaseCard class="mb-4">
    <div class="mb-3 flex flex-wrap items-start justify-between gap-2">
      <div>
        <h3 class="text-sm font-bold text-slate-100">
          Avtomatik biriktirish
        </h3>
        <p class="mt-0.5 max-w-2xl text-xs text-slate-400">
          Yangi Mustaqil o‘quvchi ro‘yxatdagi birinchi bo‘sh kuratorga o‘zi biriktiriladi. Kurator to‘lsa —
          keyingisiga; hammasi to‘lsa o‘quvchi navbatda kutadi va eslatma chiqadi.
        </p>
      </div>
      <BaseButton
        size="sm"
        variant="secondary"
        :loading="run.isPending.value"
        :disabled="members.length === 0"
        @click="run.mutate()"
      >
        Hozir taqsimlash
      </BaseButton>
    </div>

    <div
      v-if="pool !== null"
      class="mb-3 flex flex-wrap gap-2 text-xs"
    >
      <BaseBadge tone="neutral">
        Umumiy sig‘im: {{ pool.totalCapacity }}
      </BaseBadge>
      <BaseBadge :tone="pool.freeSlots === 0 ? 'danger' : 'success'">
        Bo‘sh joy: {{ pool.freeSlots }}
      </BaseBadge>
      <BaseBadge :tone="pool.waitingStudents > 0 ? 'warning' : 'neutral'">
        Navbatda: {{ pool.waitingStudents }}
      </BaseBadge>
    </div>

    <div
      v-if="members.length === 0"
      class="mb-3 rounded-lg border border-dashed border-line p-4 text-center text-sm text-slate-400"
    >
      Hali kurator qo‘shilmagan — o‘quvchilar avtomatik biriktirilmaydi.
    </div>

    <ol
      v-else
      class="mb-3 space-y-2"
    >
      <li
        v-for="(member, index) in members"
        :key="member.curatorId"
        class="rounded-xl border border-line bg-ink-900 p-3"
        :class="{ 'opacity-60': !member.isAccepting || !member.isActive }"
      >
        <div class="flex flex-wrap items-center gap-3">
          <span
            class="flex size-6 shrink-0 items-center justify-center rounded-full bg-ink-800 text-xs font-bold text-slate-300"
            v-text="index + 1"
          />
          <div class="min-w-40 flex-1">
            <p class="font-semibold text-slate-100">
              {{ member.fullName }}
              <span
                class="ml-1 text-xs font-normal text-dim"
                v-text="member.role === 'Assistant' ? 'kurator' : 'ustoz'"
              />
            </p>
            <p class="text-[11px] text-dim">
              {{ formatDateNumeric(member.addedAt) }} dan beri
              <span
                v-if="!member.isActive"
                class="text-rose-400"
              > · profil nofaol</span>
              <span
                v-else-if="!member.isAccepting"
                class="text-amber-400"
              > · to‘xtatilgan</span>
            </p>
          </div>

          <div class="flex min-w-44 items-center gap-2">
            <div class="h-2 w-28 overflow-hidden rounded-full bg-ink-800">
              <div
                class="h-full rounded-full"
                :class="loadTone(member)"
                :style="{ width: `${loadPercent(member)}%` }"
              />
            </div>
            <span
              class="text-xs tabular-nums"
              :class="member.activeStudents > member.capacity ? 'font-bold text-rose-400' : 'text-slate-300'"
              v-text="`${member.activeStudents}/${member.capacity}`"
            />
            <BaseBadge
              v-if="member.activeStudents >= member.capacity"
              tone="danger"
            >
              {{ member.activeStudents > member.capacity ? `${member.capacity}+` : 'To‘ldi' }}
            </BaseBadge>
          </div>

          <div class="flex items-center gap-1.5">
            <input
              type="number"
              class="zn-input w-20 py-1 text-sm"
              :min="1"
              :max="1000"
              :value="capacityDraft.get(member.curatorId) ?? member.capacity"
              aria-label="Sig‘im"
              @input="capacityDraft.set(member.curatorId, Number(($event.target as HTMLInputElement).value))"
              @keydown.enter="saveCapacity(member)"
            >
            <BaseButton
              v-if="capacityDraft.has(member.curatorId) && capacityDraft.get(member.curatorId) !== member.capacity"
              size="sm"
              :loading="update.isPending.value"
              @click="saveCapacity(member)"
            >
              Saqlash
            </BaseButton>
          </div>

          <div class="flex gap-1.5">
            <BaseButton
              size="sm"
              variant="ghost"
              :loading="update.isPending.value"
              @click="update.mutate({ member, capacity: member.capacity, isAccepting: !member.isAccepting })"
            >
              {{ member.isAccepting ? 'To‘xtatish' : 'Qayta yoqish' }}
            </BaseButton>
            <button
              type="button"
              class="rounded-lg p-1.5 text-slate-500 transition-colors hover:bg-ink-800 hover:text-rose-400"
              :aria-label="`${member.fullName} — ro‘yxatdan chiqarish`"
              @click="askRemove(member)"
            >
              <AppIcon
                name="close"
                :size="15"
              />
            </button>
          </div>
        </div>
      </li>
    </ol>

    <div class="flex flex-wrap items-end gap-2 border-t border-line pt-3">
      <label class="min-w-56 flex-1">
        <span class="mb-1 block text-xs font-semibold text-slate-300">Kurator qo‘shish</span>
        <select
          v-model="newCuratorId"
          class="zn-input"
        >
          <option value="">
            Xodimni tanlang
          </option>
          <option
            v-for="c in addable"
            :key="c.id"
            :value="c.id"
          >
            {{ c.fullName }} · {{ c.role === 'Assistant' ? 'kurator' : 'ustoz' }}
          </option>
        </select>
      </label>
      <label>
        <span class="mb-1 block text-xs font-semibold text-slate-300">Sig‘im</span>
        <input
          v-model.number="newCapacity"
          type="number"
          class="zn-input w-24"
          :min="1"
          :max="1000"
        >
      </label>
      <BaseButton
        :loading="add.isPending.value"
        :disabled="newCuratorId === '' || !(newCapacity >= 1 && newCapacity <= 1000)"
        @click="add.mutate()"
      >
        Qo‘shish
      </BaseButton>
    </div>

    <p
      v-if="error !== null"
      class="mt-3 rounded-lg border border-rose-500/25 bg-rose-500/10 px-3 py-2 text-xs text-rose-200"
      role="alert"
      v-text="error"
    />
  </BaseCard>
</template>
