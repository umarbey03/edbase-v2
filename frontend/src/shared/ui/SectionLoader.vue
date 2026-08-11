<script setup lang="ts">
/**
 * SKELETON — blok darajasidagi yuklanish ko'rsatkichi.
 *
 * Talab: *"Har bir tugma bosilganda agar ma'lumot load qilish uchun ma'lum
 * vaqt ketadigan bo'lsa shu vaqt davomida loader chiqishi kerak"* — uch
 * qatlamdan IKKINCHISI:
 *   1. tugma ichida — `BaseButton :loading` / `IconButton :loading`;
 *   2. BLOK ichida — SHU komponent (drawer bo'limi, modal tanasi, tab kontenti);
 *   3. global yupqa chiziq — `RouteProgress`.
 *
 * NEGA SPINNER EMAS: skeleton kontentning KELAJAK SHAKLINI ko'rsatadi, ya'ni
 * ma'lumot kelganda ekran "sakramaydi" — balandlik oldindan egallangan.
 * Markazdagi spinner esa yuklanish tugagach layout'ni siljitardi.
 *
 * ★ JONLI MA'LUMOTNI ALMASHTIRISH UCHUN ISHLATILMAYDI: qayta yuklashda
 * (`refetch`) eski ma'lumot joyida qolib xiralashishi kerak (`DataStatus`
 * dagi `retrying`), aks holda har 30 sekundlik yangilanishda jadval
 * "yo'qolib" ko'rinardi. Skeleton faqat BIRINCHI yuklashda.
 */
type LoaderVariant = 'list' | 'form' | 'card'

const props = withDefaults(
  defineProps<{
    /**
     * `list` — avatar + ikki qatorli ro'yxat (o'quvchilar, darslar);
     * `form` — yorliq + maydon juftliklari (drawer ichidagi tahrirlash);
     * `card` — kartochka to'ri (KPI, statistika).
     */
    variant?: LoaderVariant
    /** Nechta element chizilsin — ro'yxatning taxminiy uzunligiga qarab. */
    rows?: number
    /** Screen reader uchun matn. */
    label?: string
  }>(),
  { variant: 'list', rows: 3, label: 'Yuklanmoqda' },
)
</script>

<template>
  <!--
    `aria-busy` + `role="status"`: screen reader "yuklanmoqda" deb bir marta
    aytadi. Skeletonning O'ZI `aria-hidden` — bo'sh to'rtburchaklarni o'qishdan
    ma'no yo'q.
  -->
  <div
    role="status"
    aria-busy="true"
    :aria-label="props.label"
  >
    <div
      v-if="props.variant === 'list'"
      class="space-y-2.5"
      aria-hidden="true"
    >
      <div
        v-for="index in props.rows"
        :key="index"
        class="flex animate-pulse items-center gap-3 rounded-xl border border-line bg-ink-900 p-3.5 motion-reduce:animate-none"
      >
        <div class="size-10 shrink-0 rounded-full bg-ink-750" />
        <div class="min-w-0 flex-1 space-y-2">
          <div class="h-3 w-2/5 rounded bg-ink-750" />
          <div class="h-2.5 w-3/5 rounded bg-ink-800" />
        </div>
      </div>
    </div>

    <div
      v-else-if="props.variant === 'form'"
      class="space-y-4"
      aria-hidden="true"
    >
      <div
        v-for="index in props.rows"
        :key="index"
        class="animate-pulse space-y-2 motion-reduce:animate-none"
      >
        <div class="h-2.5 w-24 rounded bg-ink-750" />
        <div class="h-11 rounded-lg border border-line bg-ink-850" />
      </div>
    </div>

    <div
      v-else
      class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3"
      aria-hidden="true"
    >
      <div
        v-for="index in props.rows"
        :key="index"
        class="animate-pulse space-y-3 rounded-xl border border-line bg-ink-900 p-4 motion-reduce:animate-none"
      >
        <div class="h-2.5 w-1/2 rounded bg-ink-800" />
        <div class="h-6 w-2/3 rounded bg-ink-750" />
        <div class="h-2.5 w-full rounded bg-ink-800" />
      </div>
    </div>
  </div>
</template>
