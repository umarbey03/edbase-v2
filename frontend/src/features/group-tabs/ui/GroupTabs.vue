<script setup lang="ts">
import { AppIcon } from '@/shared/ui'

import type { GroupTabDef, GroupTabKey } from '../model/tabs'

/**
 * Eski `.tabs` paneli: yumaloq tugmalar, gorizontal skroll (telefonda
 * sakkiztasi bir qatorga sig'maydi), faol tabda accent fon va chegara.
 */
const props = defineProps<{
  tabs: readonly GroupTabDef[]
  modelValue: GroupTabKey
}>()

const emit = defineEmits<{ 'update:modelValue': [value: GroupTabKey] }>()
</script>

<template>
  <div
    class="scroll-x-safe scrollbar-none mb-5 -mx-4 border-b border-line px-4 pb-2.5 sm:mx-0 sm:px-0"
  >
    <div
      class="flex gap-2"
      role="tablist"
    >
      <button
        v-for="tab in props.tabs"
        :key="tab.key"
        type="button"
        role="tab"
        class="inline-flex min-h-11 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-[20px] border px-[15px] text-[13px] transition-colors"
        :class="
          tab.key === props.modelValue
            ? 'border-brand-500 bg-brand-500/14 font-semibold text-brand-500'
            : 'border-line bg-ink-900 font-medium text-slate-400 hover:border-line-strong hover:bg-ink-800 hover:text-slate-100'
        "
        :aria-selected="tab.key === props.modelValue"
        @click="emit('update:modelValue', tab.key)"
      >
        <AppIcon
          :name="tab.icon"
          :size="15"
        />
        {{ tab.label }}
      </button>
    </div>
  </div>
</template>
