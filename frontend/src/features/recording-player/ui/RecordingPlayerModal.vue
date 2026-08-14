<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'

import { useAuthStore } from '@/features/auth/model/auth.store'
import { BaseButton, BaseModal, BaseSpinner } from '@/shared/ui'

import { useRecordingLink } from '../model/useRecordingLink'

/**
 * Yozuv pleyeri.
 *
 * ★ ESKI ILOVADAN AYNAN (`academic.html`, 6383–6401-qatorlar): oyna sarlavhasi
 * "Dars yozuvi", ichida `controls playsinline` bilan `<video>`, pastda
 * "⏱️ Tezlik:" va 1.0x / 1.25x / 1.5x / 2.0x tugmalari hamda "Yopish".
 * Tezlik tugmalari o'quvchilar uchun eng ko'p ishlatiladigan imkoniyat edi —
 * dars 80 daqiqa, 1.5x da uni qayta ko'rish real vaqt talab qiladi.
 *
 * ESKISIDAN FARQ: `v.src = j.url` darhol qo'yilardi va manzil eskirsa video
 * jimgina to'xtardi. Bu yerda `error` hodisasi ushlanadi va manzil BIR MARTA
 * qayta so'ralib, ko'rilgan vaqt tiklanadi (`currentTime`) — presigned havola
 * 15 daqiqada eskiraydi, 80 daqiqalik darsni esa hech kim 15 daqiqada
 * ko'rmaydi.
 *
 * ════════════════════════════════════════════════════════════════════════
 *  SUV BELGISI (talab R8) — U NIMA QILADI VA NIMA QILMAYDI
 * ════════════════════════════════════════════════════════════════════════
 *
 * Loyiha egasining talabi: *"videoning ichida studentning telefon raqami
 * har bir student uchun uning o'ziniki videoning ustidan aylanib yurishi
 * kerak, xavfsizlik masalasi uchun"*.
 *
 * ✅ NIMAGA YARAYDI: ekranni yozib olib tarqatishni TIYADI. Yozuvda
 *    tarqatuvchining raqami ko'rinib turadi, ya'ni "kim tarqatgan" degan
 *    savol arzon javob topadi. Amalda to'siq ham SHU — tarqatishlarning
 *    ko'pchiligi o'ylamasdan qilingan qayta yuborish.
 *
 * 🔴 NIMAGA YARAMAYDI — VA BUNI KAM BAHOLAMANG: bu DOM/CSS qatlami, u
 *    videoning O'ZIGA kuymaydi. Yozuv presigned S3 manzili bilan
 *    beriladi (`useRecordingLink` izohi), ya'ni:
 *      • brauzerning "Network" panelidan manzilni nusxalab olish 15
 *        daqiqa ichida TOZA faylni beradi;
 *      • DevTools'da overlay elementini o'chirish bir bosish.
 *    Ya'ni QAT'IY QARORLI odamni bu to'xtatmaydi. To'xtatadigan yagona
 *    yechim — serverda har ko'ruvchi uchun alohida transkod (burn-in), u
 *    esa ombor hajmini KO'RUVCHILAR SONIGA ko'paytiradi. Loyiha egasi
 *    ataylab DOM qatlamini tanladi.
 *
 * Bu izoh ATAYLAB shu yerda: kelajakda kimdir "video himoyalangan" deb
 * hisoblab, yozuvlarga nisbatan yumshoqroq qoida joriy qilmasligi kerak.
 */
const props = defineProps<{
  /** `null` — oyna yopiq. Ochilganda yozuv id'si beriladi. */
  recordingId: number | null
  title: string
}>()

const emit = defineEmits<{ close: [] }>()

const link = useRecordingLink()
const video = ref<HTMLVideoElement | null>(null)

/* ============================================================================
 *  SUV BELGISI (talab R8)
 * ==========================================================================*/

const auth = useAuthStore()

/**
 * Video ustida aylanib yuradigan matn — KO'RUVCHINING O'ZINIKI.
 *
 * ════════════════════════════════════════════════════════════════════════
 * 🔴 RAQAM MANBAI: `GET /auth/me` (`UserDto.phone`) — VA FAQAT U
 *
 * Bu endpoint tokendagi `sub` dan javob beradi, ya'ni O'Z-O'ZIGA
 * CHEKLANGAN: undan hech qachon boshqa odamning raqami chiqmaydi.
 *
 * Guruh doirasidagi manbalar (`GroupMemberDto`, davomat varag'i qatori,
 * qatnashuvchilar ro'yxati) ham raqamni BILARDI, lekin ular USTOZGA ham
 * ochiq va talab R27 aynan o'sha yo'lni yopdi. Suv belgisini o'shalardan
 * yig'ish yopilgan teshikni qayta ochardi — shuning uchun bu yerda auth
 * store'dan boshqa manba ISHLATILMASIN.
 * ════════════════════════════════════════════════════════════════════════
 *
 * ★ TELEFONSIZ O'QUVCHILAR BOR (ular Telegram'ni ham ulay olmaydi). Ularda
 * ism + foydalanuvchi id'siga tushiladi. O'YLAB TOPILGAN yoki BO'SH raqam
 * chizilmaydi: bo'sh belgi butun himoya ma'nosini yo'qotadi, soxta raqam
 * esa tergovda BEGONA odamni ko'rsatib qo'yishi mumkin.
 */
const watermark = computed<string>(() => {
  const user = auth.user
  if (user === null) return ''

  const phone = user.phone?.trim() ?? ''
  if (phone.length > 0) return phone

  return `${user.fullName} · #${user.id}`
})

/**
 * Eski ilovadagi to'rtta tezlik (`setPlaybackSpeed`). Yorliqlar ham AYNAN
 * o'sha ko'rinishda — `toFixed()` bilan hisoblansa "1.50x" chiqib ketardi.
 */
const SPEEDS: readonly { value: number; label: string }[] = [
  { value: 1, label: '1.0x' },
  { value: 1.25, label: '1.25x' },
  { value: 1.5, label: '1.5x' },
  { value: 2, label: '2.0x' },
]
const speed = ref<number>(1)

/**
 * Manzil eskirgani sababli BIR MARTA qayta urinildimi.
 * Cheksiz halqa bo'lmasligi uchun: agar qayta olingan manzil ham xato bersa,
 * foydalanuvchiga xato ko'rsatiladi.
 */
let retriedAfterError = false

function applySpeed(value: number): void {
  speed.value = value
  const element = video.value
  if (element !== null) element.playbackRate = value
}

/**
 * Videoni to'xtatib, manbani UZADI.
 *
 * ★ `src` ni bo'shatish SHART: `removeAttribute('src')` + `load()` bo'lmasa
 * brauzer oyna yopilgandan keyin ham faylni yuklab olishda davom etadi
 * (1 GB lik yozuvda bu sezilarli trafik). Eski ilova ham aynan shunday
 * qilardi (`closeRecPlayer`).
 */
function detachVideo(): void {
  const element = video.value
  if (element === null) return
  element.pause()
  element.removeAttribute('src')
  element.load()
}

async function open(recordingId: number): Promise<void> {
  retriedAfterError = false
  speed.value = 1
  const url = await link.load(recordingId)
  if (url === null) return

  const element = video.value
  if (element === null) return
  element.src = url
  element.playbackRate = 1
  // Avtomatik ijro brauzer siyosati bilan bloklanishi mumkin — bu xato emas,
  // foydalanuvchi ▶ ni o'zi bosadi.
  void element.play().catch(() => undefined)
}

/**
 * `<video>` xatosi. Eng ehtimolli sabab — presigned manzil eskirgani, shuning
 * uchun avval JIMGINA qayta olinadi va ko'rilgan joy tiklanadi. Ikkinchi xato
 * allaqachon boshqa sabab (ombor yo'q, fayl o'chirilgan) — u ko'rsatiladi.
 */
async function handleVideoError(): Promise<void> {
  const recordingId = props.recordingId
  const element = video.value
  if (recordingId === null || element === null) return

  if (retriedAfterError) {
    link.error.value =
      'Videoni ochib bo‘lmadi. Havola eskirgan yoki fayl ombori javob bermayapti.'
    return
  }

  retriedAfterError = true
  const position = element.currentTime
  const url = await link.load(recordingId, true)
  if (url === null) return

  element.src = url
  // Manba almashgach `currentTime` darhol qo'yilmaydi — metama'lumot kutiladi.
  element.addEventListener(
    'loadedmetadata',
    () => {
      element.currentTime = position
      element.playbackRate = speed.value
      void element.play().catch(() => undefined)
    },
    { once: true },
  )
}

watch(
  () => props.recordingId,
  (id) => {
    if (id === null) {
      detachVideo()
      link.reset()
      return
    }
    void open(id)
  },
)

// Sahifa almashsa oyna ochiq holda yo'q qilinishi mumkin — yuklab olishni
// to'xtatamiz, aks holda oqim fonda davom etardi.
onBeforeUnmount(detachVideo)
</script>

<template>
  <BaseModal
    :open="props.recordingId !== null"
    :title="props.title.length > 0 ? props.title : 'Dars yozuvi'"
    wide
    @close="emit('close')"
  >
    <div
      v-if="link.pending.value"
      class="flex h-48 items-center justify-center rounded-xl bg-black"
    >
      <BaseSpinner />
    </div>

    <p
      v-else-if="link.error.value !== null"
      class="rounded-xl border border-rose-500/25 bg-rose-500/10 px-5 py-6 text-center text-sm text-rose-200"
      role="alert"
      v-text="link.error.value"
    />

    <!--
      `v-show` (`v-if` EMAS): element DOM'da qolishi kerak, aks holda
      `video` ref'i `open()` chaqirilgan paytda hali `null` bo'lardi.

      O'ram (`relative`) suv belgisi uchun QO'SHILDI va u `<video>` ning
      qutisiga AYNAN mos tushadi: video `w-full`, balandligi esa mazmundan
      kelib chiqadi. ⚠️ O'ramga `w-fit` QO'YMANG — `w-full` bola bilan birga
      u aylanma bog'liqlik yasaydi va video o'zining tabiiy (~300px)
      kengligiga qisilib qoladi.

      `block` — `<video>` sukut bo'yicha `inline`, ya'ni o'ram ichida
      pastdan bir necha piksel "harf tagi" bo'shlig'i paydo bo'lardi.
    -->
    <div
      v-show="!link.pending.value && link.error.value === null"
      class="relative"
    >
      <video
        ref="video"
        controls
        playsinline
        class="block max-h-[65dvh] w-full rounded-xl bg-black"
        @error="handleVideoError"
      />

      <!--
        ══════════════════════════════════════════════════════════════════
         SUV BELGISI (R8). Cheklovlari yuqoridagi sarlavha izohida.
        ══════════════════════════════════════════════════════════════════

        🔴 `pointer-events-none` — MAJBURIY: o'ram butun videoni qoplaydi,
           ya'ni usiz ▶, tovush va vaqt chizig'i BOSILMAY qolardi.
        ★ `select-none` — matnni belgilab nusxalash ma'nosiz va u faqat
          sichqoncha tanlovini buzardi.
        ★ `aria-hidden` — bu bezak emas, LEKIN ekran o'quvchiga u
          foydasiz: foydalanuvchi o'z raqamini biladi, o'qilishi esa
          videoning tavsifini bosib ketardi.
        ★ Matn `overflow-hidden` ichida: animatsiya chetga chiqqanda
          modal ichida gorizontal skroll paydo bo'lmasin.

        ★ NEGA UCHTA ELEMENT, IKKITA EMAS: harakat `__track` ga qo'yiladi,
          matnga emas. `translate()` ning FOIZI elementning O'Z o'lchamidan
          hisoblanadi — matnda u raqam uzunligiga bog'liq bo'lib qolardi
          ("+998901234567" va "Aziz Karimov · #42" turli masofa yurardi).
          `__track` esa `inset-0` bilan AYNAN video o'lchamida, ya'ni uning
          foizlari kadrning foizlari.
      -->
      <div
        v-if="watermark.length > 0"
        class="zn-watermark pointer-events-none absolute inset-0 select-none overflow-hidden rounded-xl"
        aria-hidden="true"
      >
        <div class="zn-watermark__track">
          <span
            class="zn-watermark__text"
            v-text="watermark"
          />
        </div>
      </div>
    </div>

    <template #footer>
      <div class="flex flex-1 flex-wrap items-center gap-2">
        <span class="text-xs font-semibold text-slate-400">⏱️ Tezlik:</span>
        <BaseButton
          v-for="option in SPEEDS"
          :key="option.value"
          size="sm"
          :variant="speed === option.value ? 'primary' : 'ghost'"
          @click="applySpeed(option.value)"
        >
          {{ option.label }}
        </BaseButton>
      </div>
      <BaseButton
        size="sm"
        variant="secondary"
        @click="emit('close')"
      >
        Yopish
      </BaseButton>
    </template>
  </BaseModal>
</template>

<style scoped>
/*
  ══════════════════════════════════════════════════════════════════════════
   SUV BELGISI — HARAKAT VA KO'RINISH
  ══════════════════════════════════════════════════════════════════════════

  ★ NIMA UCHUN `transform`, `top/left` EMAS: `transform` kompozitor
    qatlamida ishlaydi va sahifani qayta joylashtirishga (layout) majbur
    qilmaydi. `top/left` bilan animatsiya qilinsa 80 daqiqalik video
    davomida brauzer uzluksiz reflow qilardi — pastroq telefonlarda bu
    videoning o'zini sekinlashtiradi.

  ★ FOIZLAR `__track` NING (= kadrning) o'lchamidan hisoblanadi — sabab
    shablondagi izohda.

  ★ VERTIKAL YO'L 8%..60% BILAN CHEGARALANGAN: pastdagi ~15% da video
    boshqaruvlari turadi. `pointer-events-none` tufayli ular BOSILADI, lekin
    matn ular ustidan o'tsa vaqt chizig'i o'qilmay qolardi.
    Gorizontal yo'l 55% da to'xtaydi: matn eng uzun holatida ham
    (ism + id) o'ng chetdan qirqilib qolmasin.

  ★ 44 SONIYA — ataylab SEKIN: tez harakat diqqatni tortadi va darsni
    ko'rishga xalaqit beradi; sekin siljish esa ekran yozuvida baribir
    qoladi (maqsad shu).

  ★ `alternate`: qaytish nuqtasida sakrash bo'lmaydi, ya'ni 0% va 100%
    holatlari bir xil bo'lishi shart emas.
*/
.zn-watermark__track {
  position: absolute;
  inset: 0;
  animation: zn-watermark-drift 44s ease-in-out infinite alternate both;
}

.zn-watermark__text {
  display: inline-block;
  white-space: nowrap;
  font-size: clamp(11px, 1.6vw, 15px);
  font-weight: 700;
  letter-spacing: 0.08em;
  /*
    Oq matn + qora soya: video kadri ham oq, ham qora bo'lishi mumkin.
    Soyasiz belgi oq slaydda BUTUNLAY yo'qolardi.
  */
  color: rgb(255 255 255 / 42%);
  text-shadow: 0 1px 3px rgb(0 0 0 / 55%);
}

@keyframes zn-watermark-drift {
  0% {
    transform: translate(4%, 8%);
  }
  25% {
    transform: translate(48%, 22%);
  }
  50% {
    transform: translate(14%, 48%);
  }
  75% {
    transform: translate(55%, 34%);
  }
  100% {
    transform: translate(30%, 60%);
  }
}

/*
  HARAKATNI KAMAYTIRISH (WCAG 2.3.3).

  `style.css` dagi global qoida cheksiz animatsiyalarni allaqachon
  to'xtatadi (`animation-iteration-count: 1`), LEKIN u yerda `!important`
  bilan `animation-duration: 0.01ms` qo'yiladi — ya'ni element `both` fill
  tufayli 100% kadrida QOTIB qoladi. Bu ishlaydi, lekin bog'liqlik yashirin.

  Shuning uchun bu yerda holat OSHKORA belgilanadi: harakat yo'q, joy esa
  boshqaruvlardan uzoq — chap yuqori chorak.
*/
@media (prefers-reduced-motion: reduce) {
  .zn-watermark__track {
    animation: none;
    transform: translate(30%, 20%);
  }
}
</style>
