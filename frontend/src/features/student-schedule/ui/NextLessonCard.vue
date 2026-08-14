<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { sessionTitle } from '@/entities/session'
import { formatWeekdayDateTime } from '@/shared/lib/datetime'
import type { LiveSessionDto, SessionTypeName } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'

import { canJoin, sessionState } from '../model/useStudentSchedule'

/**
 * "Keyingi dars" kartochkasi (eski `heroCard()` / `.hero2`).
 *
 * Bosh sahifada IKKITA bo'ladi: ustoz darsi (oltin) va kurator darsi (firuza).
 * Dars jonli bo'lsa kartochka qizarib, tugma qizil pulsatsiyaga o'tadi —
 * eski ilovadagi `.hero2.live` + `.btn.live` bilan bir xil.
 */
const props = defineProps<{
  type: SessionTypeName
  session: LiveSessionDto | null
  now: Date
}>()

const router = useRouter()

const isTeacher = computed(() => props.type === 'Teacher')
const typeLabel = computed(() => (isTeacher.value ? 'Ustoz darsi' : 'Kurator darsi'))

const isLive = computed(
  () => props.session !== null && sessionState(props.session, props.now) === 'live',
)
const joinable = computed(() => props.session !== null && canJoin(props.session, props.now))

/**
 * Kartochka foni: indigo / firuza / (jonli bo'lsa) qizil radial gradient.
 *
 * ★ RANGLAR TOKENGA O'TKAZILDI. Ilgari qiymatlar QOTIB QOLGAN edi
 * (`rgb(245 183 49)` — eski oltin aksent, `rgb(34 211 238)` — firuza,
 * `rgb(239 68 68)` — qizil). Brend indigo bo'lgach oltin kartochka butun
 * ilovadan ajralib qolardi. `color-mix` shaffoflikni TOKENDAN hisoblaydi,
 * ya'ni aksent almashsa kartochka o'z-o'zidan moslashadi.
 *
 * Yorug' temada tintlar PASAYTIRILDI (24%/16% -> 18%/12%): oq sirt ustida
 * bir xil foiz to'yingan rangni ancha "baland" ko'rsatadi va sarlavha
 * yozuvi tint ustida kontrastini yo'qotardi.
 */
const cardStyle = computed(() => {
  if (isLive.value) {
    return {
      borderColor: 'color-mix(in oklab, var(--color-rose-500) 45%, transparent)',
      background:
        'radial-gradient(125% 100% at 100% 0, color-mix(in oklab, var(--color-rose-500) 18%, transparent), transparent 60%), var(--color-ink-900)',
    }
  }
  if (isTeacher.value) {
    return {
      borderColor: 'color-mix(in oklab, var(--color-brand-500) 38%, transparent)',
      background:
        'radial-gradient(125% 100% at 100% 0, color-mix(in oklab, var(--color-brand-500) 12%, transparent), transparent 60%), var(--color-ink-900)',
    }
  }
  return {
    borderColor: 'color-mix(in oklab, var(--color-cyan-500) 38%, transparent)',
    background:
      'radial-gradient(125% 100% at 100% 0, color-mix(in oklab, var(--color-cyan-500) 12%, transparent), transparent 60%), var(--color-ink-900)',
  }
})

/*
  Sarlavha yozuvi (11px extrabold uppercase) — MATN, ya'ni `-400`/`-300`
  darajasi kerak (shkalalar teskari: `style.css` boshidagi izoh).
  To'yingan `cyan-500`/`rose-500` bu yerda 2.5:1 berardi.
*/
const labelColor = computed(() => {
  if (isLive.value) return 'var(--color-rose-400)'
  return isTeacher.value ? 'var(--color-brand-400)' : 'var(--color-cyan-300)'
})

/** Orqaga sanoq: kun / soat / daqiqa / sek (eski `.count.mini`). */
const countdown = computed(() => {
  if (props.session === null) return null
  const diffMs = new Date(props.session.scheduledStart).getTime() - props.now.getTime()
  let seconds = Math.max(0, Math.floor(diffMs / 1000))
  const days = Math.floor(seconds / 86400)
  seconds %= 86400
  const hours = Math.floor(seconds / 3600)
  seconds %= 3600
  const minutes = Math.floor(seconds / 60)
  const rest = seconds % 60
  const pad = (value: number): string => (value < 10 ? `0${value}` : String(value))
  return [
    { value: String(days), label: 'kun' },
    { value: pad(hours), label: 'soat' },
    { value: pad(minutes), label: 'daqiqa' },
    { value: pad(rest), label: 'sek' },
  ]
})

function join(): void {
  if (props.session === null) return
  void router.push({ name: 'live-room', params: { sessionId: String(props.session.id) } })
}
</script>

<template>
  <!--
    ★ DESKTOP (≥1024px): kartochka ichki o'lchamlari bir pog'ona kattaroq va
    sichqoncha ostida "ko'tariladi". Ko'tarilish BEJIZ EMAS — kartochka
    ichida yagona harakat tugmasi ("Darsga kirish") bor va bosh sahifada
    kursor tabiiy ravishda avval kartochkaga tushadi: hover unga "bu blok
    tirik" degan javob beradi (loyiha egasi talabi: *"interaktivroq"*).

    ★ Yangi `@keyframes` QO'SHILMADI — bu shunchaki `transition`, ya'ni
    `prefers-reduced-motion` bloki (`style.css`) uni o'z-o'zidan 0.01ms ga
    tushiradi. Tailwind `hover:` ni `@media (hover:hover)` ga o'raydi, ya'ni
    teginishli ekranda "yopishib qolgan" holat bo'lmaydi; ustiga `lg:`
    ham qo'yilgan — telefon yo'li umuman ko'rmaydi.
  -->
  <article
    class="flex flex-col overflow-hidden rounded-[18px] border-[1.5px] p-4 pb-3.5 lg:p-5 lg:transition lg:hover:-translate-y-0.5 lg:hover:shadow-md"
    :style="cardStyle"
  >
    <p
      class="flex items-center gap-1.5 text-[11px] font-extrabold uppercase tracking-[1px] lg:text-xs"
      :style="{ color: labelColor }"
    >
      <span
        v-if="isLive"
        class="size-2 animate-ping-live rounded-full bg-red-500"
        aria-hidden="true"
      />
      <AppIcon
        v-else
        :name="isTeacher ? 'graduation' : 'user-check'"
        :size="15"
      />
      {{ isLive ? `JONLI · ${typeLabel}` : typeLabel }}
    </p>

    <p
      v-if="props.session === null"
      class="mt-3 text-[13px] text-slate-400"
    >
      Rejalashtirilgan dars yo‘q
    </p>

    <template v-else>
      <h3
        class="mb-1 mt-2.5 text-lg font-extrabold leading-tight lg:mt-3 lg:text-xl"
        v-text="sessionTitle(props.session)"
      />
      <p class="text-[12.5px] leading-snug text-slate-400 lg:text-[13.5px]">
        <span v-text="props.session.groupName" /><br>
        <span v-text="formatWeekdayDateTime(props.session.scheduledStart)" />
      </p>

      <!--
        Jonli darsda sanoq ortiqcha — darhol kirish tugmasi chiqadi.

        Katakcha foni ilgari `bg-black/25` edi: qorong'i fonda u "chuqurlik"
        berardi, oq kartochkada esa kulrang dog' bo'lib chiqadi.
        `ink-800` — yorug' temadagi ichki blok rangi.

        🔴 SANOQ DESKTOPDA QAYTA O'LCHANDI. 21px lik raqam 520px lik
        ustunning YARMIGA (≈250px) mo'ljallangan edi; desktopda esa
        kartochka ~560px bo'ladi va o'sha raqam 130px lik katakning
        o'rtasida yo'qolib, sanoq "bo'sh qutilar qatori" bo'lib ko'rinardi.
        Katak `flex-1` bo'lgani uchun uni TORAYTIRIB bo'lmaydi (o'ngda
        tushunarsiz bo'shliq qolardi) — shuning uchun MAZMUN kattalashadi:
        32px raqam + balandroq to'ldirma + kattaroq yorliq.
      -->
      <div
        v-if="!isLive && countdown !== null"
        class="mb-2.5 mt-3 flex gap-1.5 lg:mb-3.5 lg:mt-4 lg:gap-2.5"
      >
        <div
          v-for="cell in countdown"
          :key="cell.label"
          class="flex-1 rounded-[11px] border border-line bg-ink-800 px-1 py-2 text-center lg:rounded-2xl lg:py-3.5"
        >
          <b
            class="block text-[21px] font-extrabold leading-none tabular-nums lg:text-[32px]"
            v-text="cell.value"
          />
          <span
            class="mt-1 block text-[9px] uppercase tracking-wider text-slate-400 lg:mt-1.5 lg:text-[10px]"
            v-text="cell.label"
          />
        </div>
      </div>

      <BaseButton
        class="mt-auto"
        :class="isLive ? 'animate-pulse-btn' : ''"
        :variant="isLive ? 'danger' : 'secondary'"
        size="lg"
        block
        :disabled="!joinable"
        @click="join"
      >
        <!-- Eski ilovada strelka matndan KEYIN turardi: "Darsga kirish ›". -->
        Darsga kirish
        <AppIcon
          v-if="isLive"
          name="chevron-right"
          :size="18"
        />
      </BaseButton>
    </template>
  </article>
</template>
