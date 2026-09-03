<script setup lang="ts">
import { onBeforeUnmount, onMounted, useTemplateRef, watch } from 'vue'

import { useModalHost } from '@/shared/lib/useModalHost'
import { AppIcon, BaseButton } from '@/shared/ui'

import { useLevelTest } from '../model/use-level-test'

/*
  ══════════════════════════════════════════════════════════════════════════
  DARAJA ANIQLASH TESTI — OYNA
  ══════════════════════════════════════════════════════════════════════════

  ★ NEGA `BaseModal` EMAS: `BaseModal` ning ichki maydoni har tomondan
    tekis `px-4 py-4` bilan o'ralgan. Bu yerda esa uchta blok chekkadan
    chekkagacha bo'lishi kerak — yuqori panel, progress chizig'i va
    natija ekranidagi daraja nishoni. Ular `BaseModal` ichida faqat
    manfiy margin bilan yasalardi, ya'ni har responsive tuzatish ikki
    joyda takrorlanardi.

  🔴 LEKIN OYNA MEXANIKASI QAYTA YOZILMADI. `useModalHost` AYNAN
     `BaseModal` ishlatadigan mexanizm: SANOQLI skroll qulfi, ESC steki
     va fokus tuzog'i. Nusxa ko'chirilganda ular asta-sekin bir-biridan
     ajralardi va xato faqat bittasida tuzatilardi (o'sha faylning
     izohidagi uchta yashirin xato aynan shundan chiqqan edi).

  ★ TEST MANTIQI BU YERDA EMAS, `model/use-level-test.ts` DA.
    Bu fayl faqat chizadi va hodisalarni uzatadi.
*/

const props = defineProps<{ open: boolean }>()

const emit = defineEmits<{
  close: []
  /**
   * «Shu daraja bilan ariza qoldirish».
   *
   * ★ NEGA HODISA, TO'G'RIDAN-TO'G'RI FORMAGA YOZISH EMAS: forma bu
   * komponentdan bexabar. Hodisa orqali landing sahifa ikkalasini
   * bog'laydi va test forma tuzilishini bilishi shart emas.
   */
  apply: [payload: { course: string, note: string }]
}>()

const panel = useTemplateRef<HTMLElement>('panel')

const test = useLevelTest()

useModalHost({
  open: () => props.open,
  onClose: () => emit('close'),
  panel,
  // Ochilganda birinchi variant fokus oladi — klaviatura bilan
  // yechadigan odam darhol javob bera oladi.
  initialFocusSelector: '.js-modal-autofocus',
})

/*
  Test har OCHILGANDA noldan boshlanadi.

  ★ NEGA SAQLANMAYDI: yarim yechilgan test qayta ochilganda odam
    "men buni allaqachon boshlaganmidim?" degan holatga tushardi.
    Test 3 daqiqalik — uni qaytadan boshlash yo'qotish emas.
*/
watch(
  () => props.open,
  (open) => {
    if (open) test.start()
    else test.stop()
  },
)

/*
  ══════════════════════════════════════════════════════════════════════
   KLAVIATURA BILAN JAVOB BERISH — 1–5 yoki A–E
  ══════════════════════════════════════════════════════════════════════

  ★ NEGA KERAK: 16 ta savolni sichqoncha bilan bosib chiqish sekin.
    Raqam bilan javob berish testni ikki barobar tezlashtiradi.

  ⚠️ TARTIB — EKRANDAGI tartib, savol ma'lumotidagi emas. Variantlar
     har seansda aralashtiriladi (`use-level-test.ts` dagi Fisher–Yates),
     ya'ni «B» tugmasi har safar boshqa javobni bildiradi.

  🔴 ESC VA TAB BU YERDA USHLANMAYDI — ular `useModalHost` dagi UMUMIY
     ishlovchida. Bu yerda takrorlansa, ichma-ich oyna ochilganda ikkala
     qatlam birga yopilardi.
*/
function onKeydown(event: KeyboardEvent): void {
  if (!props.open || test.screen.value !== 'quiz') return
  if (event.altKey || event.ctrlKey || event.metaKey) return

  let slot = -1

  if (/^[1-5]$/.test(event.key)) slot = Number(event.key) - 1

  const letter = 'abcde'.indexOf(event.key.toLowerCase())
  if (letter > -1) slot = letter

  if (slot === -1) return
  if (test.chooseBySlot(slot)) event.preventDefault()
}

onMounted(() => document.addEventListener('keydown', onKeydown))
onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown))

function onApply(): void {
  const result = test.result.value
  if (result === null) return

  emit('apply', { course: result.courseMatch, note: test.summaryLine.value })
  emit('close')
}
</script>

<template>
  <Teleport to="body">
    <div
      v-if="props.open"
      class="fixed inset-0 z-50 flex justify-center bg-slate-900/45 backdrop-blur-sm sm:items-center sm:p-5"
      role="presentation"
      @click.self="emit('close')"
    >
      <div
        ref="panel"
        class="flex max-h-dvh w-full animate-sheet-up flex-col overflow-hidden bg-ink-900 shadow-lg sm:max-h-[92dvh] sm:max-w-[41rem] sm:animate-fade-up sm:rounded-[1.25rem] sm:border sm:border-line"
        role="dialog"
        aria-modal="true"
        aria-labelledby="level-test-title"
        tabindex="-1"
      >
        <!-- ═══════════════════════════════════════════ YUQORI PANEL ═══ -->
        <header
          class="lt-head flex shrink-0 items-center gap-3 border-b border-line px-4 py-3.5 sm:px-5"
        >
          <img
            src="/logo-64.png"
            alt=""
            width="36"
            height="36"
            class="size-9 shrink-0 rounded-full object-cover ring-1 ring-line"
          >
          <div class="min-w-0 flex-1">
            <h2
              id="level-test-title"
              class="truncate text-[15px] font-semibold text-slate-100"
            >
              Daraja aniqlash testi
            </h2>
            <p class="truncate text-xs text-slate-400">
              Arab tili · ZIN-NUR ONLINE
            </p>
          </div>
          <button
            type="button"
            class="tap-target flex shrink-0 items-center justify-center rounded-xl text-slate-400 transition-colors hover:bg-ink-800 hover:text-slate-100"
            title="Yopish"
            aria-label="Yopish"
            @click="emit('close')"
          >
            <AppIcon
              name="close"
              :size="18"
            />
          </button>
        </header>

        <div class="scrollbar-slim min-h-0 flex-1 overflow-y-auto">
          <!-- ═════════════════════════════════════════ SAVOL EKRANI ═══ -->
          <template v-if="test.screen.value === 'quiz'">
            <div class="px-4 pt-4 sm:px-5">
              <div class="mb-2.5 flex items-center justify-between gap-3">
                <span
                  class="rounded-full bg-green-950 px-3 py-1 text-[11px] font-bold uppercase tracking-wider text-green-100 ring-1 ring-inset ring-green-900"
                  v-text="test.blockLabel.value"
                />
                <span
                  class="text-xs font-semibold tabular-nums text-slate-400"
                  v-text="test.countLabel.value"
                />
              </div>
              <!--
                `role="progressbar"` QO'YILMADI: bu chiziq ADAPTIV testda
                taxminiy joyni ko'rsatadi, aniq foizni emas. Ekran o'qigich
                uchun haqiqiy ma'lumot yuqoridagi «Savol 2 / 4» matnida.
              -->
              <div
                class="h-1.5 overflow-hidden rounded-full bg-ink-800"
                aria-hidden="true"
              >
                <i
                  class="lt-track block h-full rounded-full"
                  :style="{ width: `${test.progressPercent.value}%` }"
                />
              </div>
            </div>

            <div
              v-if="test.current.value !== null"
              class="px-4 py-5 sm:px-5"
            >
              <p
                class="font-display text-xl leading-snug text-slate-50 sm:text-[22px]"
                v-text="test.current.value.question"
              />

              <!--
                ARAB MATNI. `lang` va `dir` SHART: ularsiz brauzer
                harflarni chapdan o'ngga terib qo'yadi va so'z teskari
                o'qiladi. `lang="ar"` esa ekran o'qigichga to'g'ri
                talaffuz ovozini tanlashga imkon beradi.
              -->
              <p
                v-if="test.current.value.arabic !== undefined"
                class="lt-arabic lt-arabic-block mt-4 overflow-x-auto rounded-2xl border border-green-900 px-4 py-3 text-center text-4xl leading-[1.5] text-green-100 sm:text-5xl"
                lang="ar"
                dir="rtl"
                v-text="test.current.value.arabic"
              />

              <div class="mt-5 grid gap-2.5">
                <button
                  v-for="(option, slot) in test.options.value"
                  :key="option.value"
                  type="button"
                  class="flex w-full items-center gap-3 rounded-2xl border px-4 py-3.5 text-left text-[15px] transition-all"
                  :class="[
                    slot === 0 ? 'js-modal-autofocus' : '',
                    option.value === -1
                      ? 'border-dashed border-line-strong text-slate-400 hover:border-slate-500 hover:text-slate-300'
                      : 'border-line-strong text-slate-100 hover:-translate-y-0.5 hover:border-green-500 hover:shadow-sm',
                    test.pickedValue.value === option.value
                      ? '-translate-y-0.5 border-green-400 bg-green-950'
                      : 'bg-ink-900',
                  ]"
                  :disabled="test.isLocked.value"
                  @click="test.choose(option.value)"
                >
                  <span
                    class="grid size-7 shrink-0 place-items-center rounded-lg text-xs font-bold transition-colors"
                    :class="
                      test.pickedValue.value === option.value
                        ? 'bg-green-400 text-white'
                        : 'bg-ink-800 text-slate-500'
                    "
                    v-text="option.key"
                  />
                  <span
                    v-if="option.isArabic"
                    class="lt-arabic min-w-0 flex-1 text-2xl"
                    lang="ar"
                    dir="rtl"
                    v-text="option.label"
                  />
                  <span
                    v-else
                    class="min-w-0 flex-1"
                    v-text="option.label"
                  />
                </button>
              </div>
            </div>

            <div class="flex items-center gap-3 px-4 pb-5 sm:px-5">
              <button
                type="button"
                class="flex items-center gap-1.5 rounded-lg px-3 py-2 text-[13px] font-semibold text-slate-400 transition-colors hover:bg-green-950 hover:text-green-100 disabled:pointer-events-none disabled:opacity-35"
                :disabled="!test.canGoBack.value"
                @click="test.back()"
              >
                <AppIcon
                  name="arrow-left"
                  :size="15"
                />
                Orqaga
              </button>
              <span class="ml-auto text-xs text-slate-500">
                Javobni tanlang
              </span>
            </div>
          </template>

          <!-- ═══════════════════════════════ ISHONCHSIZ NATIJA ═══ -->
          <!--
            🔴 NEGA BU EKRAN BOR: savolni o'qishga ulgurmaydigan tezlikda
               bosilgan javoblardan chiqqan daraja HAQIQIY emas. Uni
               ko'rsatib qo'yish menejerni ham, o'quvchini ham chalg'itadi —
               odam noto'g'ri guruhga tushadi va birinchi haftadayoq
               orqada qoladi. Shuning uchun natija YASHIRILADI, lekin
               majburlanmaydi: «Baribir ko'rsatish» tugmasi qoladi.
          -->
          <div
            v-else-if="test.screen.value === 'warning'"
            class="px-6 py-8 text-center"
          >
            <div
              class="mx-auto grid size-16 place-items-center rounded-full bg-amber-950 text-amber-400 ring-2 ring-amber-800"
            >
              <AppIcon
                name="alert"
                :size="30"
              />
            </div>
            <h3 class="mt-5 font-display text-[22px] text-slate-50">
              Natijani aniqlab boʻlmadi
            </h3>
            <p
              class="mt-4 inline-flex items-center gap-2 rounded-full bg-amber-950 px-4 py-2 text-[13px] font-semibold text-amber-200 ring-1 ring-inset ring-amber-800"
            >
              <AppIcon
                name="clock"
                :size="15"
              />
              Har savolga oʻrtacha
              <b v-text="test.averageSeconds.value" />
              soniya
            </p>
            <p class="mx-auto mt-3 max-w-[44ch] text-sm leading-relaxed text-slate-300">
              Javoblar savolni oʻqishga ulgurmaydigan tezlikda berildi, shuning
              uchun natija haqiqiy darajangizni koʻrsatmaydi. Savollarni oʻqib,
              bilmaganingizni «Bilmayman» deb belgilasangiz — daraja aniq chiqadi.
            </p>
            <BaseButton
              class="mt-6"
              size="lg"
              block
              @click="test.start()"
            >
              Qaytadan yechish
            </BaseButton>
            <button
              type="button"
              class="mt-2 p-2 text-[13px] font-semibold text-slate-500 underline underline-offset-4 transition-colors hover:text-slate-300"
              @click="test.showResultAnyway()"
            >
              Baribir natijani koʻrsatish
            </button>
          </div>

          <!-- ═══════════════════════════════════════ NATIJA EKRANI ═══ -->
          <template v-else-if="test.result.value !== null">
            <div class="lt-badge border-b border-line px-5 py-7 text-center">
              <div
                class="lt-level mx-auto grid size-[74px] place-items-center rounded-full font-display text-[28px] text-white"
                v-text="test.result.value.level"
              />
              <p class="mt-4 text-[11px] font-bold uppercase tracking-[0.13em] text-green-400">
                Sizning darajangiz
              </p>
              <h3
                class="mt-1.5 font-display text-2xl text-slate-50"
                v-text="test.result.value.name"
              />
              <p
                class="mx-auto mt-2.5 max-w-[46ch] text-[15px] leading-relaxed text-slate-300"
                v-text="test.result.value.text"
              />
            </div>

            <div class="m-5 rounded-2xl border border-dashed border-line bg-ink-950 p-4">
              <p class="text-[11px] font-bold uppercase tracking-[0.12em] text-slate-400">
                Tavsiya etilgan yoʻnalish
              </p>
              <p
                class="mt-1.5 font-display text-lg text-green-100"
                v-text="test.result.value.recommendation"
              />
              <p
                class="mt-2 text-sm leading-relaxed text-slate-300"
                v-text="test.result.value.recommendationText"
              />
            </div>

            <div class="grid gap-3 px-5">
              <div
                v-for="row in test.breakdown.value"
                :key="row.id"
                class="grid grid-cols-[1fr_auto] items-center gap-x-3 gap-y-1.5"
                :class="row.tone === 'skip' ? 'opacity-45' : ''"
              >
                <span
                  class="text-[13px] font-semibold text-slate-300"
                  v-text="row.name"
                />
                <span
                  class="text-xs font-bold tabular-nums text-slate-400"
                  v-text="row.reached ? `${row.score} / ${row.total}` : 'oʻtilmadi'"
                />
                <div class="col-span-2 h-2 overflow-hidden rounded-full bg-ink-800">
                  <i
                    class="block h-full rounded-full transition-[width] duration-700"
                    :class="`lt-bar-${row.tone}`"
                    :style="{ width: `${row.percent}%` }"
                  />
                </div>
              </div>
            </div>

            <div class="grid gap-2.5 px-5 pt-6">
              <BaseButton
                size="lg"
                block
                @click="onApply"
              >
                Shu daraja bilan ariza qoldirish
              </BaseButton>
              <BaseButton
                variant="secondary"
                block
                @click="test.start()"
              >
                Testni qayta yechish
              </BaseButton>
            </div>

            <details class="m-5 overflow-hidden rounded-2xl border border-line">
              <summary
                class="flex cursor-pointer items-center gap-2.5 px-4 py-3.5 text-sm font-semibold text-slate-100"
              >
                Javoblaringiz tahlili
                <AppIcon
                  class="ml-auto text-green-400 transition-transform"
                  name="chevron-down"
                  :size="17"
                />
              </summary>
              <div class="grid gap-3 px-4 pb-4">
                <div
                  v-for="item in test.review.value"
                  :key="item.number"
                  class="border-t border-dashed border-line pt-3"
                >
                  <p class="mb-1.5 text-sm font-semibold text-slate-100">
                    {{ item.number }}. {{ item.question }}
                    <span
                      v-if="item.arabic !== undefined"
                      class="lt-arabic"
                      lang="ar"
                      dir="rtl"
                      v-text="item.arabic"
                    />
                  </p>
                  <p
                    class="mb-1 flex items-start gap-2 text-[13px]"
                    :class="item.isCorrect ? 'text-green-400' : 'text-rose-400'"
                  >
                    <AppIcon
                      class="mt-0.5 shrink-0"
                      :name="item.isCorrect ? 'check' : 'close'"
                      :size="15"
                    />
                    <span>Siz: {{ item.given }}</span>
                  </p>
                  <p
                    v-if="!item.isCorrect"
                    class="mb-1 flex items-start gap-2 text-[13px] text-green-400"
                  >
                    <AppIcon
                      class="mt-0.5 shrink-0"
                      name="check"
                      :size="15"
                    />
                    <span>Toʻgʻri: {{ item.expected }}</span>
                  </p>
                  <p
                    class="mt-1.5 rounded-xl bg-ink-950 px-3 py-2 text-[13px] leading-relaxed text-slate-300"
                    v-text="item.explanation"
                  />
                </div>
              </div>
            </details>

            <p class="px-5 pb-5 text-center text-xs leading-relaxed text-slate-400">
              Bu — taxminiy natija. Yakuniy darajangizni menejerlarimiz suhbat
              orqali aniqlaydi va sizga mos guruhni tanlaydi.
            </p>
          </template>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/*
  Tailwind sinflari bilan yozib bo'lmaydigan uchta narsa: gradientlar,
  arab shrifti va `details` ochilganda strelkani burish.
*/

.lt-head {
  background: linear-gradient(120deg, var(--color-green-950), transparent 70%);
}

.lt-track {
  background: linear-gradient(90deg, var(--color-green-800), var(--color-green-400));
  transition: width 0.5s cubic-bezier(0.16, 1, 0.3, 1);
}

.lt-badge {
  background: linear-gradient(160deg, var(--color-green-950), transparent 80%);
}

.lt-level {
  background: linear-gradient(150deg, var(--color-green-500), var(--color-green-100));
  box-shadow: 0 16px 34px -12px rgb(6 118 71 / 0.55);
}

/* Natija ustunchalari — ball darajasiga qarab rang. */
.lt-bar-good {
  background: linear-gradient(90deg, var(--color-green-800), var(--color-green-400));
}

.lt-bar-mid {
  background: linear-gradient(90deg, var(--color-amber-800), var(--color-amber-500));
}

.lt-bar-low {
  background: linear-gradient(90deg, var(--color-rose-800), var(--color-rose-500));
}

.lt-bar-skip {
  background: var(--color-line-strong);
}

/*
  Arab matni. `--font-arabic` — Amiri (`style.css` dagi `@theme`).

  `unicode-bidi: isolate` — arabcha parcha o'zbekcha jumla ICHIDA
  turganda (tahlil ro'yxatida) qo'shni tinish belgilarini o'ziga tortib
  ketmasin.
*/
.lt-arabic {
  font-family: var(--font-arabic);
  direction: rtl;
  unicode-bidi: isolate;
}

/* Savol ustidagi ramkali blok — faqat shunda fon bo'ladi. */
.lt-arabic-block {
  background: linear-gradient(150deg, var(--color-ink-950), var(--color-green-950));
}

details[open] summary :deep(svg) {
  transform: rotate(180deg);
}
</style>
