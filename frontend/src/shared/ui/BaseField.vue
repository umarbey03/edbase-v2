<script setup lang="ts">
/*
  Eski dizayndagi `label` (12px, muted) + maydon juftligi. Alohida komponent
  bo'lgani uchun forma sahifalarida label uslubi takrorlanmaydi.
*/
withDefaults(defineProps<{ label: string; hint?: string; error?: string | null }>(), {
  hint: '',
  error: null,
})
</script>

<template>
  <label class="block">
    <span
      class="mb-1.5 block text-xs font-medium text-slate-400"
      v-text="label"
    />
    <slot />
    <span
      v-if="hint.length > 0 && error === null"
      class="mt-1 block text-[11px] text-dim"
      v-text="hint"
    />
    <span
      v-if="error !== null"
      class="mt-1 block text-[11px] text-rose-400"
      v-text="error"
    />
  </label>
</template>
