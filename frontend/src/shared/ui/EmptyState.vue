<script setup lang="ts">
import AppIcon from './AppIcon.vue'
import type { IconName } from './icon-names'

withDefaults(
  defineProps<{
    icon?: IconName
    title: string
    text?: string
  }>(),
  { icon: 'calendar', text: '' },
)
</script>

<template>
  <div class="rounded-xl border border-line bg-ink-900 px-5 py-10 text-center">
    <div
      class="mx-auto flex size-12 items-center justify-center rounded-xl bg-ink-800 text-slate-500"
    >
      <AppIcon
        :name="icon"
        :size="22"
      />
    </div>
    <p
      class="mt-3.5 text-sm font-medium text-slate-200"
      v-text="title"
    />
    <p
      v-if="text.length > 0"
      class="mt-1 text-xs text-slate-500"
      v-text="text"
    />
    <div
      v-if="$slots.default"
      class="mt-4 flex justify-center"
    >
      <slot />
    </div>
  </div>
</template>
