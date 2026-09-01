<script setup lang="ts">
import { useBreakpoint } from '@/shared/lib/useBreakpoint'
import { AppIcon, BaseSpinner } from '@/shared/ui'

const props = withDefaults(
  defineProps<{
    isMicOn: boolean
    isCameraOn: boolean
    isScreenSharing: boolean
    /** Ekran ulashish faqat host uchun (SPEC: `roomAdmin` grant'i hostda). */
    canShareScreen: boolean
    handRaised: boolean
    /**
     * Qo'l ko'tarish FAQAT o'quvchida (R1, 2026-08-13 talabi: "livechatda
     * teacher is not needed to raise hand").
     *
     * ★ EKRAN ULASHISHNING KO'ZGUSI: u hostda, bu esa o'quvchida — ya'ni
     * panel kengligi rolga qarab O'ZGARMAYDI (pastdagi o'rov izohiga qarang).
     * 🔴 Bu FAQAT ko'rinish qatlami: hub'da ham rad etish bor
     * (`LiveClassHub.RaiseHand`), aks holda eski klient yoki `curl` bilan
     * ustozning qo'li HAMMANING "Qo'l ko'targanlar" ro'yxatiga tushardi.
     */
    canRaiseHand: boolean
    /**
     * HAR BIR TUGMA UCHUN ALOHIDA kutish holati.
     *
     * NIMA UCHUN ALOHIDA: ilgari bitta umumiy `isBusy` bor edi va u BARCHA
     * tugmalarni birdan `disabled` qilardi. Foydalanuvchi mikrofonni bosganda
     * kamera tugmasi ham o'chib qolardi — bu "tugmalar ishlamayapti" degan
     * taassurot berardi. Endi faqat bosilgan tugma kutish ko'rsatkichini oladi.
     */
    micPending?: boolean
    cameraPending?: boolean
    screenPending?: boolean
    disabled?: boolean
    /** Mobil rejimda chat tugmasi ustidagi o'qilmagan xabarlar soni. */
    unreadCount?: number
    /**
     * Hozir to'liq ekran rejimidami — ikonka shunga qarab almashadi.
     *
     * ★ HOLAT OTA KOMPONENTDA: to'liq ekranga o'tadigan element bu panel
     * emas, uni O'RAB TURGAN blok (video + panel birga). Bundan tashqari
     * brauzer to'liq ekrandan `Esc` bilan ham chiqadi — ya'ni haqiqat
     * manbai `document.fullscreenElement`, tugmaning o'zi emas.
     */
    isFullscreen?: boolean
    /**
     * Brauzer to'liq ekranni umuman qo'llab-quvvatlaydimi.
     *
     * 🔴 iOS Safari'da `Element.requestFullscreen` YO'Q (faqat `<video>`
     * uchun `webkitEnterFullscreen`). U yerda tugma chizilmaydi —
     * ishlamaydigan tugmani ko'rsatish yolg'on va'da bo'lardi.
     */
    canFullscreen?: boolean
  }>(),
  {
    micPending: false,
    cameraPending: false,
    screenPending: false,
    disabled: false,
    unreadCount: 0,
    isFullscreen: false,
    canFullscreen: false,
  },
)

const emit = defineEmits<{
  'toggle-mic': []
  'toggle-camera': []
  'toggle-screen': []
  'toggle-hand': []
  'toggle-chat': []
  'toggle-fullscreen': []
  leave: []
}>()

/*
  Yotiq telefonda panel video USTIDA suzadi (ota komponentda) — u yerda har
  piksel qimmat, shuning uchun ichki bo'shliq qisqaradi. TUGMA O'LCHAMI
  O'ZGARMAYDI (pastdagi izohga qarang).
*/
const { isShortLandscape } = useBreakpoint()

/*
  `active:scale-90` — bosishning DARHOL sezilishi uchun. Holat o'zgarishi
  optimistik bo'lsa ham, barmoq/sichqoncha tekkan zahoti vizual javob bo'lishi
  kerak: foydalanuvchi shikoyati aynan "bosilishi bilinmayapti" edi.

  🔴 `size-11` (44px) — WCAG 2.5.5 ning eng kichik teginish nishoni. Panel tor
  telefonga sig'masa ham bu qiymat KICHRAYTIRILMAYDI: sig'dirish o'rov
  (`flex-wrap`) bilan hal qilinadi, tugmani kichraytirish bilan emas.
*/
const BASE =
  'relative inline-flex size-11 shrink-0 items-center justify-center rounded-full transition-[background-color,transform] duration-150 active:scale-90 disabled:cursor-not-allowed disabled:opacity-40'

/** Yoqilgan/o'chirilgan holat uchun uslub — takrorlanmasligi uchun bitta funksiya. */
function toneOf(active: boolean, activeTone = 'bg-ink-750 text-slate-100 hover:bg-ink-700'): string {
  return active ? activeTone : 'bg-rose-500/20 text-rose-300 hover:bg-rose-500/30'
}
</script>

<template>
  <!--
    O'ROVLI PANEL.

    Hisob: 5 ta tugma × 44px + oraliqlar + ajratgich + ichki bo'shliq ≈ 300px.
    320px lik telefonda (Galaxy Fold tashqi ekrani, eski SE) bu panel
    gorizontal ravishda TOSHIB ketardi — "chiqish" tugmasi ekrandan tashqarida
    qolardi.

    ★ ROLLAR ENDI BIR XIL KENGLIKDA: ekran ulashish faqat hostda, qo'l
    ko'tarish faqat o'quvchida (R1) — ikkalasi bir-birining o'rnini oladi.
    Ilgari ustozda tugma bittaga ko'p edi va panel birinchi bo'lib AYNAN
    ustozda qoqilardi; endi bunday nomutanosiblik yo'q.

    ★ YECHIM O'ROV, KICHRAYTIRISH EMAS: `flex-wrap` + `max-w-full` bilan panel
    tor ekranda ikki qatorga bo'linadi. 44px chegara buzilmaydi va tartib
    saqlanadi — tugmalar o'sha ketma-ketlikda, faqat qatordan qatorga o'tadi.
    `max-w-full` SHART: onasi `flex` konteyner, usiz bola o'z kontenti
    kengligida qolib o'ralmasdi.
    ★ Oraliq telefonda 6px, `sm` dan yuqorida 8px — bu bir qator sig'ish
    ehtimolini oshiradi, lekin tugmalarga tegmaydi.
  -->
  <div
    class="flex max-w-full flex-wrap items-center justify-center gap-x-1.5 gap-y-2 rounded-2xl bg-ink-900/90 ring-1 ring-inset ring-line backdrop-blur sm:gap-x-2"
    :class="isShortLandscape ? 'px-2 py-1' : 'px-2 py-2 sm:px-3'"
  >
    <button
      type="button"
      :class="[BASE, toneOf(props.isMicOn)]"
      :disabled="props.disabled || props.micPending"
      :aria-pressed="props.isMicOn"
      :aria-busy="props.micPending"
      :title="props.isMicOn ? 'Mikrofonni o‘chirish' : 'Mikrofonni yoqish'"
      @click="emit('toggle-mic')"
    >
      <AppIcon :name="props.isMicOn ? 'mic' : 'mic-off'" />
      <!-- Kutish halqasi: qurilma ruxsati bir necha sekund davom etishi mumkin. -->
      <span
        v-if="props.micPending"
        class="absolute inset-0 flex items-center justify-center rounded-full bg-ink-950/55"
      >
        <BaseSpinner size="sm" />
      </span>
      <span class="sr-only">Mikrofon</span>
    </button>

    <button
      type="button"
      :class="[BASE, toneOf(props.isCameraOn)]"
      :disabled="props.disabled || props.cameraPending"
      :aria-pressed="props.isCameraOn"
      :aria-busy="props.cameraPending"
      :title="props.isCameraOn ? 'Kamerani o‘chirish' : 'Kamerani yoqish'"
      @click="emit('toggle-camera')"
    >
      <AppIcon :name="props.isCameraOn ? 'camera' : 'camera-off'" />
      <span
        v-if="props.cameraPending"
        class="absolute inset-0 flex items-center justify-center rounded-full bg-ink-950/55"
      >
        <BaseSpinner size="sm" />
      </span>
      <span class="sr-only">Kamera</span>
    </button>

    <button
      v-if="props.canShareScreen"
      type="button"
      :class="[
        BASE,
        props.isScreenSharing
          ? 'bg-brand-600 text-white hover:bg-brand-500'
          : 'bg-ink-750 text-slate-100 hover:bg-ink-700',
      ]"
      :disabled="props.disabled || props.screenPending"
      :aria-pressed="props.isScreenSharing"
      :aria-busy="props.screenPending"
      :title="props.isScreenSharing ? 'Ekran ulashishni to‘xtatish' : 'Ekranni ulashish'"
      @click="emit('toggle-screen')"
    >
      <AppIcon name="screen-share" />
      <span
        v-if="props.screenPending"
        class="absolute inset-0 flex items-center justify-center rounded-full bg-ink-950/55"
      >
        <BaseSpinner size="sm" />
      </span>
      <span class="sr-only">Ekranni ulashish</span>
    </button>

    <button
      v-if="props.canRaiseHand"
      type="button"
      :class="[
        BASE,
        props.handRaised
          ? 'bg-amber-500 text-ink-950 hover:bg-amber-400'
          : 'bg-ink-750 text-slate-100 hover:bg-ink-700',
      ]"
      :disabled="props.disabled"
      :aria-pressed="props.handRaised"
      :title="props.handRaised ? 'Qo‘lni tushirish' : 'Qo‘l ko‘tarish'"
      @click="emit('toggle-hand')"
    >
      <AppIcon name="hand" />
      <span class="sr-only">Qo‘l ko‘tarish</span>
    </button>

    <!-- Mobil: chatni ochish -->
    <button
      type="button"
      :class="[BASE, 'bg-ink-750 text-slate-100 hover:bg-ink-700 lg:hidden']"
      title="Suhbat"
      @click="emit('toggle-chat')"
    >
      <AppIcon name="chat" />
      <span
        v-if="props.unreadCount > 0"
        class="absolute -right-0.5 -top-0.5 flex min-w-4 items-center justify-center rounded-full bg-brand-500 px-1 text-[10px] font-bold text-white"
        v-text="props.unreadCount > 99 ? '99+' : String(props.unreadCount)"
      />
      <span class="sr-only">Suhbat</span>
    </button>

    <!--
      TO'LIQ EKRAN (2026-09-01).

      ★ NEGA AYNAN SHU PANELDA, sahnaning burchagida emas: loyiha egasi
      talabi — "o'quvchi ustozning ekranini to'liq ekranda ko'ra olishi
      kerak" — va o'quvchi to'liq ekranda ham mikrofonini yoqa olishi
      kerak. Shuning uchun to'liq ekranga video BILAN BIRGA shu panel
      ham kiradi (ota komponentdagi `<main>` o'raladi), ya'ni tugmaning
      o'zi ham ko'rinib turadi va qaytish yo'li yo'qolmaydi.

      ★ Ajratgichdan OLDIN turadi: o'ngdagi qizil "chiqish" tugmasi
      alohida guruh bo'lib qolsin — u boshqa og'irlikdagi amal.
    -->
    <button
      v-if="props.canFullscreen"
      type="button"
      :class="[BASE, 'bg-ink-750 text-slate-100 hover:bg-ink-700']"
      :title="props.isFullscreen ? 'To‘liq ekrandan chiqish' : 'To‘liq ekran'"
      @click="emit('toggle-fullscreen')"
    >
      <AppIcon :name="props.isFullscreen ? 'minimize' : 'maximize'" />
      <span
        class="sr-only"
        v-text="props.isFullscreen ? 'To‘liq ekrandan chiqish' : 'To‘liq ekran'"
      />
    </button>

    <div
      class="mx-1 h-6 w-px bg-line"
      aria-hidden="true"
    />

    <button
      type="button"
      :class="[BASE, 'bg-rose-600 text-white hover:bg-rose-500']"
      title="Darsdan chiqish"
      @click="emit('leave')"
    >
      <AppIcon name="leave" />
      <span class="sr-only">Chiqish</span>
    </button>
  </div>
</template>
