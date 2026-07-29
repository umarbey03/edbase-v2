<script setup lang="ts">
import { computed } from 'vue'

import { roleLabel, roleTone } from '@/entities/user'
import { BaseAvatar, BaseBadge } from '@/shared/ui'

/**
 * DIQQAT: bu komponent FAQAT oddiy (primitiv) prop'lar qabul qiladi.
 *
 * Sababi — Vue komponentni yangilashdan oldin prop'larni sayoz taqqoslaydi.
 * Primitivlar o'zgarmagani uchun ro'yxat qayta chizilganda ESKI xabarlar
 * umuman yangilanmaydi; faqat yangi qatorlar DOM'ga qo'shiladi.
 * Agar bu yerga `row` obyekti berilganida — har render'da yangi havola bo'lib,
 * 200 ta qator qayta patch qilinardi.
 */
const props = withDefaults(
  defineProps<{
    senderName: string
    body: string
    time: string
    isOwn: boolean
    showHeader: boolean
    role: string
  }>(),
  { role: '' },
)

const roleText = computed(() => (props.role.length > 0 ? roleLabel(props.role) : ''))
const tone = computed(() => roleTone(props.role))
</script>

<template>
  <div
    class="flex gap-2 px-3"
    :class="[props.isOwn ? 'flex-row-reverse' : '', props.showHeader ? 'mt-3' : 'mt-0.5']"
  >
    <div v-if="!props.isOwn" class="w-8 shrink-0">
      <BaseAvatar v-if="props.showHeader" :name="props.senderName" size="sm" />
    </div>

    <div class="flex min-w-0 max-w-[85%] flex-col" :class="props.isOwn ? 'items-end' : 'items-start'">
      <div
        v-if="props.showHeader && !props.isOwn"
        class="mb-1 flex min-w-0 items-center gap-1.5"
      >
        <span class="truncate text-xs font-semibold text-slate-200" v-text="props.senderName" />
        <BaseBadge v-if="roleText.length > 0" :tone="tone" size="xs">{{ roleText }}</BaseBadge>
      </div>

      <div
        class="max-w-full rounded-2xl px-3 py-1.5 text-sm leading-relaxed shadow-sm"
        :class="
          props.isOwn
            ? 'rounded-br-sm bg-brand-600 text-white'
            : 'rounded-bl-sm bg-ink-800 text-slate-100 ring-1 ring-inset ring-line'
        "
      >
        <!-- `v-text` — HTML sifatida hech qachon talqin qilinmaydi (SPEC 9.9: `v-html` taqiqlangan). -->
        <p class="whitespace-pre-wrap break-words" v-text="props.body" />
        <span
          class="mt-0.5 block text-right text-[10px] tabular-nums"
          :class="props.isOwn ? 'text-white/60' : 'text-slate-500'"
          v-text="props.time"
        />
      </div>
    </div>
  </div>
</template>
