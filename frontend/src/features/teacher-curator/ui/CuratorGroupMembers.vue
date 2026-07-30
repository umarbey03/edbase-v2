<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query'
import { computed } from 'vue'

import { fetchGroupMembers, memberStatusLabel, memberStatusTone } from '@/entities/group'
import { toUserMessage } from '@/shared/api'
import { AppIcon, BaseBadge, DataStatus } from '@/shared/ui'

/**
 * Guruh o'quvchilari — SO'RALGANDA yuklanadi (qator ochilganda).
 *
 * NEGA TALAB BO'YICHA: kuratorda 10+ guruh bo'lishi mumkin va hammasining
 * ro'yxatini oldindan tortish 10+ so'rov degani. Eski ilova ham `viewSubs()`
 * da shu naqshni ishlatardi.
 */
const props = defineProps<{ groupId: number }>()

const membersQuery = useQuery({
  queryKey: ['group', props.groupId, 'members'],
  queryFn: ({ signal }) => fetchGroupMembers(props.groupId, { signal }),
})

const members = computed(() => membersQuery.data.value ?? [])

const errorMessage = computed(() =>
  membersQuery.error.value !== null ? toUserMessage(membersQuery.error.value) : null,
)
</script>

<template>
  <DataStatus
    :pending="membersQuery.isPending.value"
    :error="errorMessage"
    :empty="members.length === 0"
    :retrying="membersQuery.isFetching.value"
    :skeleton-rows="2"
    empty-icon="users"
    empty-title="Guruhda o‘quvchi yo‘q"
    @retry="membersQuery.refetch()"
  >
    <ul class="space-y-1.5">
      <li
        v-for="member in members"
        :key="member.id"
        class="flex flex-wrap items-center gap-2 rounded-lg border border-line bg-ink-950 px-3 py-2"
      >
        <span
          class="min-w-0 flex-1 truncate text-[13px] font-medium text-slate-100"
          v-text="member.fullName ?? `#${member.studentId}`"
        />
        <BaseBadge :tone="memberStatusTone(member.status)">
          {{ memberStatusLabel(member.status) }}
        </BaseBadge>
        <!--
          Qo'ng'iroq — eski kuratorlik jadvalidagi yashil `i-phone` tugmasi.
          Telefon raqami bo'lmasa tugma UMUMAN chizilmaydi: eski ilovada ham
          shunday edi (`x.phone ? iBtn(...) : ''`).
        -->
        <a
          v-if="member.phone !== null && member.phone.length > 0"
          class="tap-target inline-flex items-center justify-center gap-1.5 rounded-[9px] border border-transparent bg-green-500/15 px-2.5 text-xs font-semibold text-green-400 transition-colors hover:border-green-500"
          :href="`tel:${member.phone}`"
          :title="`Qo‘ng‘iroq: ${member.phone}`"
        >
          <AppIcon
            name="phone"
            :size="14"
          />
          <span v-text="member.phone" />
        </a>
        <span
          v-else
          class="text-[11px] text-dim"
        >Telefon kiritilmagan</span>
      </li>
    </ul>
  </DataStatus>
</template>
