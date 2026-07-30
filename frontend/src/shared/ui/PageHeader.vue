<script setup lang="ts">
/*
  Eski dizayndagi `h1` (22px/700) + `.sub` (13px, muted) juftligi.
  Telefonda sarlavha 18px — 390px kengliqda ikki qatorga bo'linmasin.
*/
withDefaults(defineProps<{ title: string; subtitle?: string }>(), { subtitle: '' })
</script>

<template>
  <header class="mb-5 flex flex-wrap items-end justify-between gap-3">
    <div class="min-w-0">
      <h1
        class="text-lg font-bold tracking-tight sm:text-[22px]"
        v-text="title"
      />
      <p
        v-if="subtitle.length > 0"
        class="mt-1 text-[13px] text-slate-400"
        v-text="subtitle"
      />
    </div>
    <div
      v-if="$slots.actions"
      class="flex shrink-0 flex-wrap items-center gap-2"
    >
      <slot name="actions" />
    </div>
  </header>
</template>
