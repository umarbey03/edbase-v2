<script setup lang="ts">
import { computed, ref } from 'vue'

import { formatCountdown } from '@/shared/lib/datetime'
import { AppIcon, BaseAvatar, BaseBadge, BaseButton } from '@/shared/ui'

import type { BreakoutMemberDto, BreakoutStateDto } from '../api/breakout-api'

/**
 * USTOZ PANELI — ichki guruhlar boshqaruvi.
 *
 * Yopiq holatda: taymer tanlab "Guruhlarga bo'lish". Ochiq holatda: har
 * guruh a'zolari, kim gapirayotgani (yashil halqa), "ustozni chaqirish"
 * belgisi, guruhga kirish va o'quvchini boshqa guruhga o'tkazish.
 */
const props = defineProps<{
  state: BreakoutStateDto | null
  busy: boolean
  remainingMs: number | null
  /** Ustoz hozir qaysi xonada: `null` — asosiy. */
  currentRoom: number | null
  speaking: ReadonlyMap<number, ReadonlySet<number>>
  connected: ReadonlyMap<number, ReadonlySet<number>>
  /** Darsdagi o'quvchilar soni — bo'lish tugmasi uchun. */
  studentCount: number
}>()

const emit = defineEmits<{
  start: [durationMinutes: number | null]
  move: [studentId: number, room: number]
  close: []
  visit: [room: number | null]
  dismiss: []
}>()

const DURATIONS: ReadonlyArray<{ value: number | null; label: string }> = [
  { value: null, label: 'Taymersiz' },
  { value: 5, label: '5 daqiqa' },
  { value: 10, label: '10 daqiqa' },
  { value: 15, label: '15 daqiqa' },
  { value: 20, label: '20 daqiqa' },
  { value: 30, label: '30 daqiqa' },
]

const MAX_GROUP_SIZE = 4

const duration = ref<number | null>(10)
const active = computed(() => props.state?.active === true)
const rooms = computed(() => props.state?.rooms ?? [])
const unassigned = computed(() => props.state?.unassigned ?? [])
const countdown = computed(() => (props.remainingMs === null ? null : formatCountdown(props.remainingMs)))
const helpCount = computed(() => rooms.value.filter((room) => room.helpRequested).length)

/** Bo'lish natijasi oldindan: masalan 7 o'quvchi -> "4 + 3". */
const splitPreview = computed(() => {
  const n = props.studentCount
  if (n < 2) return null
  const count = Math.ceil(n / MAX_GROUP_SIZE)
  const sizes = Array.from({ length: count }, (_, i) => Math.floor(n / count) + (i < n % count ? 1 : 0))
  return `${count} ta guruh: ${sizes.join(' + ')}`
})

function isSpeaking(room: number, member: BreakoutMemberDto): boolean {
  return props.speaking.get(room)?.has(member.userId) === true
}

function isConnected(room: number, member: BreakoutMemberDto): boolean {
  return props.connected.get(room)?.has(member.userId) === true
}

function onMove(member: BreakoutMemberDto, event: Event): void {
  const value = Number((event.target as HTMLSelectElement).value)
  if (Number.isNaN(value)) return
  emit('move', member.userId, value)
}

function moveOptions(currentRoom: number): Array<{ value: number; label: string; disabled: boolean }> {
  const options = [{ value: 0, label: 'Asosiy xona', disabled: currentRoom === 0 }]
  for (const room of rooms.value) {
    options.push({
      value: room.number,
      label: `${room.number}-guruh (${room.members.length})`,
      disabled: room.number === currentRoom || room.members.length >= MAX_GROUP_SIZE,
    })
  }
  options.push({ value: rooms.value.length + 1, label: 'Yangi guruh', disabled: false })
  return options
}
</script>

<template>
  <div class="flex h-full min-h-0 flex-col bg-ink-900">
    <div class="flex items-center gap-2 border-b border-line px-4 py-3">
      <AppIcon
        name="grid"
        :size="16"
        class="text-slate-400"
      />
      <h2 class="flex-1 text-sm font-semibold text-slate-100">
        Ichki guruhlar
      </h2>
      <BaseBadge
        v-if="helpCount > 0"
        tone="warning"
        size="xs"
      >
        Chaqiruv: {{ helpCount }}
      </BaseBadge>
      <button
        type="button"
        class="rounded-md p-1 text-slate-400 hover:bg-ink-800 hover:text-slate-200"
        aria-label="Yopish"
        @click="emit('dismiss')"
      >
        <AppIcon
          name="close"
          :size="16"
        />
      </button>
    </div>

    <!-- Yopiq: bo'lish -->
    <div
      v-if="!active"
      class="space-y-4 p-4"
    >
      <p class="text-sm text-slate-400">
        Darsdagi o‘quvchilar 2–4 kishilik guruhlarga tasodifiy bo‘linadi. Keyin ularni
        qo‘lda boshqa guruhga o‘tkazishingiz mumkin.
      </p>
      <label class="block space-y-1.5">
        <span class="text-xs font-medium text-slate-400">Vaqt</span>
        <select
          v-model="duration"
          class="w-full rounded-lg bg-ink-800 px-3 py-2 text-sm text-slate-100 ring-1 ring-inset ring-line"
        >
          <option
            v-for="option in DURATIONS"
            :key="String(option.value)"
            :value="option.value"
          >
            {{ option.label }}
          </option>
        </select>
      </label>
      <p class="text-xs text-slate-500">
        {{ splitPreview ?? 'Bo‘lish uchun darsda kamida 2 o‘quvchi bo‘lishi kerak.' }}
      </p>
      <BaseButton
        block
        :loading="props.busy"
        :disabled="props.studentCount < 2"
        @click="emit('start', duration)"
      >
        Guruhlarga bo‘lish
      </BaseButton>
    </div>

    <!-- Ochiq: boshqaruv -->
    <template v-else>
      <div class="flex items-center gap-2 border-b border-line px-4 py-2.5 text-xs">
        <span class="text-slate-400">Siz:</span>
        <span class="font-medium text-slate-200">
          {{ props.currentRoom === null ? 'Asosiy xona' : `${props.currentRoom}-guruh` }}
        </span>
        <button
          v-if="props.currentRoom !== null"
          type="button"
          class="text-brand-400 hover:underline"
          @click="emit('visit', null)"
        >
          asosiyga qaytish
        </button>
        <span
          v-if="countdown !== null"
          class="ml-auto inline-flex items-center gap-1 tabular-nums text-slate-300"
        >
          <AppIcon
            name="clock"
            :size="13"
          />
          {{ countdown }}
        </span>
      </div>

      <div class="scrollbar-slim min-h-0 flex-1 space-y-3 overflow-y-auto p-3">
        <section
          v-for="room in rooms"
          :key="room.number"
          class="rounded-xl bg-ink-800/60 ring-1 ring-inset"
          :class="room.helpRequested ? 'ring-amber-500/60' : room.number === props.currentRoom ? 'ring-brand-500/60' : 'ring-line'"
        >
          <header class="flex items-center gap-2 px-3 py-2">
            <span class="text-sm font-semibold text-slate-100">{{ room.number }}-guruh</span>
            <span class="text-xs text-slate-500">{{ room.members.length }} o‘quvchi</span>
            <BaseBadge
              v-if="room.helpRequested"
              tone="warning"
              size="xs"
            >
              <AppIcon
                name="hand"
                :size="12"
              />
              Chaqiryapti
            </BaseBadge>
            <BaseBadge
              v-else-if="room.members.length > 0 && room.members.length < 2"
              tone="neutral"
              size="xs"
            >
              1 kishi
            </BaseBadge>
            <BaseButton
              class="ml-auto"
              size="sm"
              :variant="room.number === props.currentRoom ? 'secondary' : 'ghost'"
              :disabled="room.number === props.currentRoom"
              @click="emit('visit', room.number)"
            >
              {{ room.number === props.currentRoom ? 'Shu yerdasiz' : 'Kirish' }}
            </BaseButton>
          </header>
          <ul class="space-y-0.5 px-2 pb-2">
            <li
              v-for="member in room.members"
              :key="member.userId"
              class="flex items-center gap-2 rounded-lg px-1.5 py-1"
            >
              <span
                class="relative inline-flex rounded-full transition-shadow"
                :class="isSpeaking(room.number, member) ? 'ring-2 ring-emerald-400 ring-offset-2 ring-offset-ink-800' : ''"
              >
                <BaseAvatar
                  :name="member.displayName"
                  size="sm"
                />
              </span>
              <span
                class="min-w-0 flex-1 truncate text-sm"
                :class="isConnected(room.number, member) ? 'text-slate-200' : 'text-slate-500'"
                :title="isConnected(room.number, member) ? '' : 'Guruh xonasiga hali ulanmagan'"
              >
                {{ member.displayName }}
              </span>
              <AppIcon
                v-if="isSpeaking(room.number, member)"
                name="mic"
                :size="13"
                class="text-emerald-400"
              />
              <select
                class="max-w-[7.5rem] rounded-md bg-ink-750 px-1.5 py-1 text-xs text-slate-300 ring-1 ring-inset ring-line"
                aria-label="Boshqa guruhga o‘tkazish"
                :disabled="props.busy"
                @change="onMove(member, $event)"
              >
                <option
                  value=""
                  selected
                  disabled
                >
                  O‘tkazish…
                </option>
                <option
                  v-for="option in moveOptions(room.number)"
                  :key="option.value"
                  :value="option.value"
                  :disabled="option.disabled"
                >
                  {{ option.label }}
                </option>
              </select>
            </li>
            <li
              v-if="room.members.length === 0"
              class="px-1.5 py-1 text-xs text-slate-500"
            >
              Bo‘sh guruh
            </li>
          </ul>
        </section>

        <section
          v-if="unassigned.length > 0"
          class="rounded-xl ring-1 ring-inset ring-line"
        >
          <header class="px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-500">
            Asosiy xonada ({{ unassigned.length }})
          </header>
          <ul class="space-y-0.5 px-2 pb-2">
            <li
              v-for="member in unassigned"
              :key="member.userId"
              class="flex items-center gap-2 rounded-lg px-1.5 py-1"
            >
              <BaseAvatar
                :name="member.displayName"
                size="sm"
              />
              <span class="min-w-0 flex-1 truncate text-sm text-slate-200">{{ member.displayName }}</span>
              <select
                class="max-w-[7.5rem] rounded-md bg-ink-750 px-1.5 py-1 text-xs text-slate-300 ring-1 ring-inset ring-line"
                aria-label="Guruhga qo‘shish"
                :disabled="props.busy"
                @change="onMove(member, $event)"
              >
                <option
                  value=""
                  selected
                  disabled
                >
                  Guruhga…
                </option>
                <option
                  v-for="option in moveOptions(0)"
                  :key="option.value"
                  :value="option.value"
                  :disabled="option.disabled"
                >
                  {{ option.label }}
                </option>
              </select>
            </li>
          </ul>
        </section>
      </div>

      <div class="border-t border-line p-3">
        <BaseButton
          block
          variant="danger"
          :loading="props.busy"
          @click="emit('close')"
        >
          Hammani asosiy xonaga qaytarish
        </BaseButton>
      </div>
    </template>
  </div>
</template>
