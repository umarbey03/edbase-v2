<script setup lang="ts">
import { computed } from 'vue'

/*
  Nishon (badge) — ekran suratlaridagi PASTEL ko'rinish: rangning 12%
  tinti fon, o'sha rangning TO'Q soyasi matn, pill radius, 11px medium.

  ★ ILGARI QATTIQ TO'LDIRISH EDI (`bg-green-400`, `bg-rose-400`...):
  qorong'i fonda yorqin nishon o'rinli edi, yorug' fonda esa u "chaqnab"
  turadi va jadvaldagi 20 ta nishon sahifani rangli chiziqlarga aylantiradi.
  Pastel variant iyerarxiyani saqlaydi: nishon ko'rinadi, lekin matnni
  bosib ketmaydi.

  ★ MATN `-200`, `-600` EMAS (reja hujjatida `-600` yozilgan): bu faylda
  shkalalar TESKARI (`style.css` boshidagi izoh) — 600 "bosilgan holat"
  darajasi, matn darajasi esa 200/400. 12% tint ustida `-200` 6.5…8.5:1
  beradi.

  Roli (`teacher`/`assistant`/`student`) — `entities/user/roleTone()` dan
  keladi va eski ilovadagi rang biriktirishini saqlaydi: ustoz brend,
  yordamchi/kurator firuza, o'quvchi neytral.
*/
type BadgeTone =
  | 'neutral'
  | 'accent'
  | 'success'
  | 'teacher'
  | 'assistant'
  | 'student'
  | 'live'
  | 'warning'
  | 'danger'
type BadgeSize = 'xs' | 'sm'

const props = withDefaults(
  defineProps<{
    tone?: BadgeTone
    size?: BadgeSize
    dot?: boolean
  }>(),
  { tone: 'neutral', size: 'xs', dot: false },
)

const TONES: Record<BadgeTone, string> = {
  neutral: 'bg-ink-800 text-slate-400',
  accent: 'bg-brand-500/12 text-brand-300',
  success: 'bg-green-500/12 text-green-200',
  teacher: 'bg-brand-500/12 text-brand-300',
  assistant: 'bg-sky-500/12 text-sky-200',
  student: 'bg-ink-800 text-slate-300',
  live: 'bg-rose-500/12 text-rose-200',
  warning: 'bg-amber-500/12 text-amber-200',
  danger: 'bg-rose-500/12 text-rose-200',
}

/*
  Nuqta — nishon matnidan bir daraja TO'YINGANROQ (`-500`), chunki u 6px
  grafik element: matn darajasidagi (`-200`) to'q rang pastel fonda "kir
  dog'" bo'lib ko'rinardi va "jonli" belgisi o'chib ketardi.

  ★ KONTRAST: nuqta o'z pastel foni ustida 2.1…5.0:1 beradi, ya'ni ba'zi
  ohanglarda (sariq, moviy) WCAG 1.4.11 ning 3:1 chizig'idan past.
  Bu ATAYLAB va qoidaga zid emas: nuqta `aria-hidden="true"` va DOIM o'z
  matni yonida turadi ("Jonli", "Yordamchi"), ya'ni ma'lumotni YETKAZUVCHI
  element emas — 1.4.11 esa faqat kontentni tushunish uchun ZARUR grafikaga
  taalluqli. To'yingan sariqni 3:1 ga majburlash uni jigarrang dog'ga
  aylantirardi (kontrast auditi skriptida ham shu izoh bor).
*/
const DOT_TONES: Record<BadgeTone, string> = {
  neutral: 'bg-slate-500',
  accent: 'bg-brand-500',
  success: 'bg-green-500',
  teacher: 'bg-brand-500',
  assistant: 'bg-sky-500',
  student: 'bg-slate-500',
  live: 'bg-rose-500',
  warning: 'bg-amber-500',
  danger: 'bg-rose-500',
}

const SIZES: Record<BadgeSize, string> = {
  xs: 'px-2 py-0.5 text-[11px] gap-1',
  sm: 'px-2.5 py-1 text-xs gap-1.5',
}

/*
  `font-medium`, `font-semibold` EMAS: pastel fonda yarim qalin 11px matn
  "qalinlashib" ketadi va nishon sarlavhadan ko'proq e'tibor tortadi.
*/
const classes = computed(() => [
  'inline-flex shrink-0 items-center rounded-full font-medium leading-tight',
  TONES[props.tone],
  SIZES[props.size],
])
</script>

<template>
  <span :class="classes">
    <span
      v-if="props.dot"
      class="size-1.5 shrink-0 rounded-full"
      :class="DOT_TONES[props.tone]"
      aria-hidden="true"
    />
    <slot />
  </span>
</template>
