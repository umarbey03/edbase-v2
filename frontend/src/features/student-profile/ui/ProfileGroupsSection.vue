<script setup lang="ts">
import { computed } from 'vue'

import { memberStatusLabel, memberStatusTone } from '@/entities/group'
import { formatDate, formatDateWithYear } from '@/shared/lib/datetime'
import type { ProfileGroupDto } from '@/shared/types'
import { AppIcon, BaseBadge, BaseCard } from '@/shared/ui'

/**
 * 3-BO'LIM: GURUHLAR — faol / pauzada / chiqarilgan / ko'chirilgan.
 *
 * Guruh nomiga bosilsa guruh sahifasi ochiladi (`teacher-group` marshruti —
 * u o'quv bo'limiga ham ochiq). Navigatsiya BU YERDA bajarilmaydi: drawer
 * yopilishi va marshrutga o'tish bitta joyda (chaqiruvchi) turishi kerak,
 * aks holda panel yopilmagan holda sahifa ostidan almashib ketardi.
 *
 * ⚠️ "QAYERGA KO'CHIRILGAN" MA'LUMOTI MODELDA SAQLANMAYDI:
 * `GroupMember` ko'chirishda manba a'zolikni `Moved` qiladi, lekin nishonga
 * HAVOLA QOLDIRMAYDI (`movedToGroupId` hozir DOIM `null`). Shu sababli
 * "→ guruh" chipi faqat qiymat KELGANDA chiziladi: bo'lmaganda "Ko'chirilgan"
 * nishoni yetadi. Vaqt bo'yicha taxmin qilish ATAYLAB rad etilgan (paketli
 * ko'chirishda BOSHQA guruhni ko'rsatib chalg'itardi).
 */
const props = defineProps<{ groups: ProfileGroupDto[] }>()

const emit = defineEmits<{ open: [groupId: number] }>()

/**
 * "Chiqqan sana" DA'VO QILINMAYDI.
 *
 * `leftAt` — a'zolik qatorining `updatedAt` qiymati, ya'ni "holat oxirgi
 * marta qachon o'zgargan". Chiqarilgan a'zolik uchun amalda chiqish vaqti,
 * LEKIN pauza yoki tiklash ham shu ustunni yangilaydi. Shuning uchun yorliq
 * "oxirgi o'zgarish" — noto'g'ri sanani "chiqarilgan sana" deb ko'rsatish
 * xodimni ma'muriy tortishuvda yolg'on dalilga olib borardi.
 */
const hasHistoricRow = computed(() => props.groups.some((group) => group.leftAt !== null))
</script>

<template>
  <BaseCard title="Guruhlar">
    <p
      v-if="props.groups.length === 0"
      class="rounded-xl border border-line bg-ink-800 p-3 text-xs leading-relaxed text-slate-400"
    >
      Hech qaysi guruhga qo‘shilmagan.
    </p>

    <ul
      v-else
      class="divide-y divide-line rounded-xl border border-line"
    >
      <li
        v-for="group in props.groups"
        :key="group.groupId"
        class="p-3"
      >
        <div class="flex flex-wrap items-center gap-x-2 gap-y-1.5">
          <!--
            Guruh nomi — TUGMA, `<a>` EMAS: navigatsiya `router.push` bilan
            bajariladi (SPA ichida sahifa qayta yuklanmaydi) va drawer avval
            yopiladi.
          -->
          <button
            type="button"
            class="tap-target inline-flex min-w-0 items-center gap-1.5 rounded-lg text-sm font-medium text-brand-400 transition-colors hover:text-brand-300"
            @click="emit('open', group.groupId)"
          >
            <span
              class="truncate"
              v-text="group.groupName"
            />
            <AppIcon
              name="chevron-right"
              :size="14"
            />
          </button>

          <BaseBadge :tone="memberStatusTone(group.status)">
            {{ memberStatusLabel(group.status) }}
          </BaseBadge>

          <!-- Pauza MUDDATI bo'lsa aytiladi; muddatsiz pauza qo'lda tiklanadi. -->
          <BaseBadge
            v-if="group.status === 'Paused' && group.pausedUntil !== null"
            tone="warning"
          >
            {{ formatDateWithYear(group.pausedUntil) }} gacha
          </BaseBadge>

          <!-- ⚠️ Hozir hech qachon chiqmaydi — sabab `<script>` izohida. -->
          <BaseBadge
            v-if="group.movedToGroupId !== null"
            tone="neutral"
          >
            → {{ group.movedToGroupName ?? `Guruh #${group.movedToGroupId}` }}
          </BaseBadge>
        </div>

        <p class="mt-1 text-xs text-slate-400">
          <span v-if="group.teacherName !== null">
            Ustoz: <span v-text="group.teacherName" /> ·
          </span>
          qo‘shilgan {{ formatDate(group.joinedAt) }}
          <span v-if="group.leftAt !== null">
            · oxirgi o‘zgarish {{ formatDate(group.leftAt) }}
          </span>
        </p>
      </li>
    </ul>

    <p
      v-if="hasHistoricRow"
      class="mt-2 text-[11px] leading-relaxed text-slate-400"
    >
      «Oxirgi o‘zgarish» — a‘zolik yozuvi oxirgi marta qachon o‘zgargani.
      Guruhdan chiqish sanasi alohida saqlanmaydi.
    </p>
  </BaseCard>
</template>
