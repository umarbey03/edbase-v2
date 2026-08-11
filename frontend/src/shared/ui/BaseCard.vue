<script setup lang="ts">
/*
  Kartochka: oq sirt, 1px yumshoq chegara + kichik soya, 16px radius,
  20px padding. Telefonda padding kichrayadi (14px) — 390px ekranda
  kontent uchun joy qolsin.

  ★ NEGA CHEGARA HAM, SOYA HAM: yorug' temada chegara (#eceff5) oq
  kartochkada 1.09:1 beradi — u yolg'iz o'zi sirt qirrasini ko'rsatmaydi.
  Yolg'iz soya esa ekran suratlaridagi "aniq qirra" hissini bermaydi va
  ustma-ust turgan kartochkalar bir-biriga oqib ketadi. Ikkisi birga —
  iOS naqshi.
*/
withDefaults(
  defineProps<{
    title?: string
    /** Sarlavha ostidagi izoh. */
    subtitle?: string
    /** Ichki paddingsiz variant — jadval yoki ro'yxat to'g'ridan-to'g'ri joylashsa. */
    flush?: boolean
  }>(),
  { title: '', subtitle: '', flush: false },
)
</script>

<template>
  <section
    class="rounded-2xl border border-line bg-ink-900 shadow-sm"
    :class="flush ? '' : 'p-3.5 sm:p-5'"
  >
    <header
      v-if="title.length > 0 || $slots.actions"
      class="flex flex-wrap items-center justify-between gap-3"
      :class="flush ? 'border-b border-line px-3.5 py-3.5 sm:px-5' : 'mb-3.5'"
    >
      <div class="min-w-0">
        <h2
          v-if="title.length > 0"
          class="truncate text-[15px] font-semibold"
          v-text="title"
        />
        <p
          v-if="subtitle.length > 0"
          class="mt-0.5 text-xs text-slate-400"
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
    <slot />
  </section>
</template>
