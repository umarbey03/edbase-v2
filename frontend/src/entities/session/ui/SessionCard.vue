<script setup lang="ts">
import { computed } from 'vue'

import { formatDateTime, formatTime } from '@/shared/lib/datetime'
import { AppIcon, BaseBadge, BaseButton } from '@/shared/ui'

import type { LiveSession } from '../model/types'
import {
  isJoinable,
  sessionStatusLabel,
  sessionStatusTone,
  sessionTitle,
  sessionTypeLabel,
} from '../model/types'

const props = defineProps<{
  session: LiveSession
}>()

const emit = defineEmits<{ join: [sessionId: number] }>()

const title = computed(() => sessionTitle(props.session))
const joinable = computed(() => isJoinable(props.session))
const statusTone = computed(() => sessionStatusTone(props.session.status))
const timeRange = computed(
  () => `${formatDateTime(props.session.scheduledStart)} – ${formatTime(props.session.scheduledEnd)}`,
)
</script>

<template>
  <!--
    Telefonda ustun (tugma pastda, to'liq kenglikda), `sm` dan boshlab qator.
    Shu almashinuv 390px ekranda tugma va sarlavha bir-birini siqib
    qo'yishining oldini oladi.
  -->
  <article
    class="flex flex-col gap-3 rounded-xl border border-line bg-ink-900 p-3.5 transition-colors hover:border-line-strong sm:flex-row sm:items-center sm:p-4"
  >
    <div class="min-w-0 flex-1">
      <div class="flex flex-wrap items-center gap-2">
        <h3
          class="min-w-0 truncate text-sm font-semibold text-slate-100"
          v-text="title"
        />
        <BaseBadge
          :tone="statusTone"
          :dot="props.session.status === 'Live'"
        >
          {{ sessionStatusLabel(props.session.status) }}
        </BaseBadge>
      </div>

      <!--
        ★ "Siz olib borasiz" YORLIG'I OLIB TASHLANDI (loyiha egasi,
        2026-08-15): u FAQAT `isHost` chaqiruvchiga nisbatan haqiqat edi,
        boshqa hech kim uchun hech narsa demasdi. Ustoz/kurator o'z
        darsini ko'rganda `isHost` DOIM `true` (ScopeByRole ularni faqat
        O'Z guruhlariga cheklaydi), ya'ni yorliq har doim ko'rinar va
        ma'lumot bermas edi. O'quv bo'limi esa (`isHost` doim `false`)
        buni umuman ko'rmasdi — aynan ular "kim olib boradi?" savolini
        beradigan auditoriya. Xodim ISMI endi HAMMAGA bir xil ko'rinadi.
      -->
      <div class="mt-1.5 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400">
        <span class="inline-flex min-w-0 items-center gap-1.5">
          <AppIcon
            name="users"
            :size="13"
          />
          <span
            class="truncate"
            v-text="props.session.groupName"
          />
        </span>
        <span
          v-if="props.session.hostName !== null"
          class="inline-flex min-w-0 items-center gap-1.5"
        >
          <AppIcon
            name="user"
            :size="13"
          />
          <span
            class="truncate"
            v-text="props.session.hostName"
          />
        </span>
        <span class="inline-flex items-center gap-1.5">
          <AppIcon
            name="calendar"
            :size="13"
          />
          <span
            class="tabular-nums"
            v-text="timeRange"
          />
        </span>
        <span
          class="text-dim"
          v-text="sessionTypeLabel(props.session.type)"
        />
      </div>
    </div>

    <BaseButton
      class="w-full shrink-0 sm:w-auto"
      :variant="props.session.status === 'Live' ? 'primary' : 'secondary'"
      :disabled="!joinable"
      @click="emit('join', props.session.id)"
    >
      <template #icon>
        <AppIcon
          name="play"
          :size="14"
        />
      </template>
      Darsga kirish
    </BaseButton>
  </article>
</template>
