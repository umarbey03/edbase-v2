<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, shallowRef, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { fetchDevQuickLoginAccounts, homeRouteFor } from '@/entities/user'
import type { User } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { TelegramAuthScreen } from '@/features/telegram-auth'
import { toUserMessage } from '@/shared/api'
import {
  formatPhone,
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  phoneDigits,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { isTelegramMiniApp } from '@/shared/lib/telegram-web-app'
import type { DevQuickLoginAccount, UserRoleName } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'

/*
  ══════════════════════════════════════════════════════════════════════════
  ★★ KIRISH — FAQAT TELEFON ORQALI (2026-08-13, loyiha egasining qarori)

  Email va parol formasi BUTUNLAY olib tashlandi. O'rniga ikki bosqich:

    1) foydalanuvchi telefon raqamini kiritadi;
    2) kod uning TELEGRAM hisobiga keladi va u kodni shu yerga yozadi.

  ★ NIMA UCHUN MINI APP YOLG'IZ YETARLI EMASDI: xodimlar ish stolida,
    oddiy brauzerda ishlaydi; Mini App qobig'i o'quvchi shakliga qurilgan;
    Telegram Login Widget esa kod bazasida umuman yozilmagan. Ya'ni "faqat
    telefon orqali" talabini bajarish uchun brauzerda ishlaydigan oqim
    QURILISHI shart edi.

  🔴 XAVFSIZLIK — INTERFEYS DARAJASIDAGI IKKI QAT'IY QOIDA:

    (a) "Bunday raqam topilmadi" degan xabar HECH QACHON ko'rsatilmaydi.
        Server bu ma'lumotni ataylab bermaydi (hisob sanashga qarshi), va
        uni bu yerda "o'ylab topish" himoyani bekor qilardi. Raqam
        yuborilgach ekran DOIM kod bosqichiga o'tadi.

    (b) Telefon raqami serverga XOM holda yuboriladi — mijozda hech
        qanday normalizatsiya QILINMAYDI. Normalizatsiya qoidasi
        backendda bitta joyda (`User.NormalizePhone`) va u
        `PhoneNormalized` ustunini to'ldiradigan AYNI metod. Bu yerga
        ikkinchi nusxa yozilsa, ikkalasi asta bir-biridan uzoqlashib,
        "raqamim to'g'ri, lekin kod kelmayapti" turkumidagi nosozlik
        berardi.

        ⚠️ 2026-08-15 — MAYDON FORMATLANADI, LEKIN QOIDA BUZILMADI.
        Loyiha egasi raqam hamma joyda `+998 90 123 45 67` ko'rinishida
        bo'lishini so'radi, shuning uchun maydonga maska qo'yildi
        (`maskPhoneField`). Serverga yuborishdan oldin esa
        `stripPhoneFormatting` FAQAT bo'shliqlarni oladi — mamlakat kodi
        qo'shmaydi, raqam kesmaydi, `0` tashlamaydi. Ya'ni yuqoridagi
        (b) qoidasi kuchida: normalizatsiya hamon SERVERDA va bu yerda
        uning nusxasi YO'Q.

        ★ CHET EL RAQAMI HAM ISHLAYDI: maska `+` bilan boshlangan va
        `998` emas raqamga UMUMAN tegmaydi (sabab `phone.ts` da),
        `stripPhoneFormatting` esa uni buzmaydi. Quyidagi
        `canSendPhone` ham ataylab "≥7 raqam" bo'lib qoladi.
  ══════════════════════════════════════════════════════════════════════════
*/

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

/** Oqim bosqichi: raqam kiritish -> kod kiritish. */
const step = ref<'telefon' | 'kod'>('telefon')

const phone = ref('')
const code = ref('')
const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)

/** Qayta yuborishgacha qolgan sekundlar (0 — tugma faol). */
const resendIn = ref(0)
let resendTimer: ReturnType<typeof setInterval> | null = null

const codeInput = useTemplateRef<HTMLInputElement>('codeInput')

/**
 * TELEGRAM REJIMI.
 *
 * Ilova Telegram Mini App ichida ochilgan bo'lsa, o'quvchi hech narsa
 * kiritmasligi kerak — `initData` imzosi kifoya. Shuning uchun forma
 * o'rniga avtomatik kirish ekrani ko'rsatiladi.
 *
 * ★ SHART BIR MARTA hisoblanadi va `ref` da turadi (computed emas): xato
 * holatida foydalanuvchi ATAYLAB telefon formasiga o'tishi mumkin, ya'ni
 * qiymat hodisaga qarab o'zgaradi. `isTelegramMiniApp()` esa o'zgarmas
 * bo'lgani uchun formadan Telegram ekraniga qaytish yo'li yo'q — bu
 * to'g'ri: o'quvchi noto'g'ri ekranda "qamalib" qolmaydi, ilovani qayta
 * ochsa bo'ldi.
 */
const telegramMode = ref(isTelegramMiniApp())

/** Telegram orqali kirish tugagach — rolga mos bosh sahifaga. */
async function handleTelegramSuccess(user: User): Promise<void> {
  /*
    `?redirect=` ATAYLAB e'tiborga olinmaydi. Telegram ilovani `/` da,
    `#tgWebAppData=...` fragmenti bilan ochadi va guard uni `redirect`
    query'siga ko'chiradi — ya'ni manzil imzolangan kirish ma'lumotini
    o'z ichiga oladi. Unga qaytib borish uni URL'da yana tarqatardi;
    bosh sahifa esa AYNI natijani beradi (fragment `/` ga baribir kerak emas).
  */
  await router.replace({ name: homeRouteFor(user.role) })
}

const sessionExpired = computed(() => route.query['sabab'] === 'sessiya-tugadi')

/**
 * Raqamda kamida shuncha RAQAM bo'lsin.
 *
 * ★ TO'LIQ TEKSHIRUV ATAYLAB YO'Q: qat'iy shakl talabi (masalan
 * `+998 XX XXX XX XX`) chet el raqami bilan ro'yxatdan o'tgan xodimni
 * to'sib qo'yardi, va shakl qoidasi backenddagi normalizatsiya bilan
 * ikkinchi nusxa bo'lib qolardi. Bu yerda faqat "tugmani bekorga
 * bosmang" darajasidagi filtr.
 */
const canSendPhone = computed(
  () => phoneDigits(phone.value).length >= 7 && !isSubmitting.value,
)

/** Kod — AYNAN 6 raqam (server ham shuni yasaydi). */
const canVerify = computed(() => /^\d{6}$/.test(code.value.trim()) && !isSubmitting.value)

/** `?redirect=` dagi ichki manzil (bo'lmasa `null`). */
function redirectTarget(): string | null {
  const raw = route.query['redirect']
  const value = Array.isArray(raw) ? raw[0] : raw
  // Faqat ichki yo'llarga yo'naltiramiz (ochiq redirect zaifligining oldini olish).
  if (typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')) return value
  return null
}

function startResendCountdown(seconds: number): void {
  stopResendCountdown()
  resendIn.value = seconds

  resendTimer = setInterval(() => {
    resendIn.value -= 1
    if (resendIn.value <= 0) stopResendCountdown()
  }, 1000)
}

function stopResendCountdown(): void {
  if (resendTimer !== null) {
    clearInterval(resendTimer)
    resendTimer = null
  }
  resendIn.value = 0
}

// Sahifadan chiqilganda taymer qolib ketmasin (xotira oqishi va
// yopilgan komponentga yozish xatosi).
onBeforeUnmount(stopResendCountdown)

/** 1-BOSQICH: kod so'rash. */
async function handleSendCode(): Promise<void> {
  if (!canSendPhone.value) return

  isSubmitting.value = true
  errorMessage.value = null

  try {
    const result = await auth.requestPhoneCode(stripPhoneFormatting(phone.value))

    /*
      🔴 BU YERDA HECH QANDAY SHART YO'Q — javob raqam bazada bor yoki
      yo'qligidan qat'i nazar AYNI. Ekran DOIM kod bosqichiga o'tadi.
      "Raqam topilmadi" shoxini qo'shish serverning butun anti-enumeration
      himoyasini bir qatorda bekor qilardi.
    */
    step.value = 'kod'
    code.value = ''
    startResendCountdown(result.resendAfterSeconds)

    // Fokus kod maydoniga — mobil klaviatura darhol ochilsin.
    await nextTick()
    codeInput.value?.focus()
  } catch (error) {
    // 429 va 503 — yagona haqiqiy xato holatlari (ikkalasi ham raqamga
    // bog'liq emas, ya'ni hech nima oshkor qilmaydi).
    errorMessage.value = toUserMessage(error)
  } finally {
    isSubmitting.value = false
  }
}

/** 2-BOSQICH: kodni tasdiqlash. */
async function handleVerify(): Promise<void> {
  if (!canVerify.value) return

  isSubmitting.value = true
  errorMessage.value = null

  try {
    const user = await auth.verifyPhoneCode(stripPhoneFormatting(phone.value), code.value.trim())
    const target = redirectTarget()

    // Manzil ko'rsatilmagan bo'lsa — ROLGA mos bosh sahifa. Ilgari hamma
    // `/darslar` ga tushardi, shu sababli admin ham o'quvchi ekranini ko'rardi.
    if (target !== null) await router.replace(target)
    else await router.replace({ name: homeRouteFor(user.role) })
  } catch (error) {
    errorMessage.value = toUserMessage(error)
    code.value = ''
    await nextTick()
    codeInput.value?.focus()
  } finally {
    isSubmitting.value = false
  }
}

/** Raqamni tuzatish uchun ortga qaytish. */
function backToPhone(): void {
  step.value = 'telefon'
  code.value = ''
  errorMessage.value = null
  stopResendCountdown()
}

/*
  ══════════════════════════════════════════════════════════════════════════
  ⚠️ SINOV PANELI — ROL BO'YICHA BIR BOSISHDA KIRISH (2026-08-14)
  ══════════════════════════════════════════════════════════════════════════

  Loyiha egasining talabi: *"real telefon va bot bilan sinash qiyin"*.
  Har ekranni beshta rol ko'zi bilan ko'rish uchun beshta HAQIQIY telefon
  va ISHLAYOTGAN bot kerak edi — dev mashinasida bot tokeni soxta, kod esa
  `MessageOutbox` jadvalida qoladi.

  🔴 BU — AUTENTIFIKATSIYANI CHETLAB O'TISH, VA HIMOYA BUTUNLAY SERVERDA:
     oshkor kalit (`Dev__QuickLogin`, standarti `false`) + muhit
     `Production` EMAS + FAQAT namunaviy (demo) hisoblar. Bu yerda ularning
     hech biri TAKRORLANMAYDI: mijozdagi shart — bezak, u DevTools
     ochilgunicha yashaydi.

  ★ INTERFEYSNING YAGONA VAZIFASI — SERVERGA ISHONISH:
      ro'yxat bo'sh (yoki 404) -> panel UMUMAN chizilmaydi.
    Rollar frontendga QATTIQ YOZILMAYDI. Yozilsa, backend darvozasi yopiq
    serverda ham tugmalar ko'rinib turardi — ya'ni interfeys mavjud
    bo'lmagan xususiyatni va'da qilardi.

  ★ HAQIQIY OQIM BIRLAMCHI BO'LIB QOLADI: panel formaning PASTIDA,
    alohida ramkada va ochiq ogohlantirish bilan. U hech qachon telefon
    formasining o'rnini egallamaydi.
*/

/** Serverdan kelgan namunaviy hisoblar. BO'SH — panel yo'q. */
const devAccounts = shallowRef<DevQuickLoginAccount[]>([])

/** Hozir qaysi rol yuklanmoqda (tugmalarni ikki marta bosishdan saqlaydi). */
const devBusyRole = ref<UserRoleName | null>(null)

/*
  So'rov sahifadan chiqilganda bekor qilinadi: javob kechikib kelsa,
  yo'q qilingan komponentga yozishga urinish bo'lardi.
*/
const devAbort = new AbortController()
onBeforeUnmount(() => devAbort.abort())

onMounted(async () => {
  /*
    ★ FAQAT TELEFON FORMASI REJIMIDA. Telegram Mini App ichida bu panel
      ma'nosiz: o'quvchi allaqachon imzolangan `initData` bilan kiradi,
      va u yerda begona tugma ko'rsatish faqat chalg'itardi.
  */
  if (telegramMode.value) return

  // Xato bu yerga YETIB KELMAYDI — `fetchDevQuickLoginAccounts` 404 ni
  // bo'sh ro'yxatga aylantiradi (sabab: `auth-api.ts` izohi).
  devAccounts.value = await fetchDevQuickLoginAccounts({ signal: devAbort.signal })
})

/** ⚠️ SINOV: tanlangan rol nomidan kirish. */
async function handleDevLogin(role: UserRoleName): Promise<void> {
  if (devBusyRole.value !== null) return

  devBusyRole.value = role
  errorMessage.value = null

  try {
    const user = await auth.devQuickLogin(role)

    /*
      🔴 `?redirect=` ATAYLAB E'TIBORGA OLINMAYDI (telefon oqimidan farqli).

      Sabab amaliy: tekshiruvchi rollarni KETMA-KET almashtiradi. Manzilda
      esa oldingi roldan qolgan chuqur havola turadi (masalan `/moliya`) —
      va o'quvchi sifatida kirgan odam darhol 403 ekraniga tushardi.
      Rolga mos bosh sahifa esa har safar ISHLAYDI.
    */
    await router.replace({ name: homeRouteFor(user.role) })
  } catch (error) {
    errorMessage.value = toUserMessage(error)
  } finally {
    devBusyRole.value = null
  }
}
</script>

<template>
  <!-- Telegram Mini App: forma o'rniga avtomatik kirish ekrani. -->
  <TelegramAuthScreen
    v-if="telegramMode"
    @success="handleTelegramSuccess"
    @phone-login="telegramMode = false"
  />

  <div
    v-else
    class="flex min-h-dvh items-center justify-center bg-ink-950 px-4 py-10"
  >
    <!--
      Fon nuri. Ilgari QOTIB QOLGAN yashil edi (`rgba(47,158,65,.18)` — eski
      `--accent: #2f9e41`), ya'ni brend indigo bo'lgach kirish sahifasi
      ilovaning qolgan qismidan boshqa rangda qolardi. Endi tokendan.

      Yorug' fonda shaffoflik PASAYTIRILDI (18%/10% -> 7%/5%) va
      `opacity-60` olib tashlandi: oq sirtda bir xil foiz nurni "bo'yoq
      dog'i" darajasiga chiqaradi.
    -->
    <div
      class="pointer-events-none fixed inset-0"
      aria-hidden="true"
      style="
        background:
          radial-gradient(
              60rem 40rem at 20% -10%,
              color-mix(in oklab, var(--color-brand-500) 7%, transparent),
              transparent 60%
            ),
          radial-gradient(
            40rem 30rem at 90% 110%,
            color-mix(in oklab, var(--color-violet-500) 5%, transparent),
            transparent 60%
          );
      "
    />

    <div class="relative w-full max-w-sm">
      <div class="mb-7 text-center">
        <!--
          R19 — MONOGRAMMA PLITASI boshqa qobiqlar bilan bir xil bo'ldi:
          `text-white` o'rniga `text-on-brand`. Ikkalasi bugun bir xil
          qiymat (`#ffffff`), lekin `text-white` — QOTIB QOLGAN rang:
          aksent ochroq tonga o'tsa plita ustidagi harf o'qilmay qolardi
          va buni faqat kirish sahifasida ko'rish mumkin bo'lardi.
        -->
        <div
          class="mx-auto flex size-12 items-center justify-center rounded-2xl bg-gradient-to-br from-brand-500 to-brand-700 text-lg font-bold text-on-brand shadow-sm"
        >
          Z
        </div>
        <!--
          R19 — brend nomi BITTA rangda. Ilgari "Zin" `text-slate-50` (eng
          to'q matn rangi), "-Nur" esa `text-brand-500` edi — ya'ni bu
          sahifada bo'linish boshqa joylardagidan ham keskinroq ko'rinardi.
          Endi butun so'z aksent tokenida, `AppSidebar` bilan bir xil.

          ★ Matn O'ZGARMADI — faqat rang qatlami birlashtirildi.
        -->
        <h1 class="mt-4 text-2xl font-bold tracking-tight text-brand-500">
          Zin-Nur
        </h1>
        <p class="mt-1 text-sm text-slate-400">
          Jonli darslar platformasi
        </p>
      </div>

      <!--
        `shadow-2xl shadow-black/40` YORUG' temada juda og'ir: oq forma
        ostidagi qora bulut "yopishqoq" ko'rinadi. `shadow-lg` — yumshoq
        soya tokeni (`style.css`).
      -->
      <form
        class="rounded-[1.25rem] bg-ink-900 p-6 shadow-lg ring-1 ring-inset ring-line"
        novalidate
        @submit.prevent="step === 'telefon' ? handleSendCode() : handleVerify()"
      >
        <div
          v-if="sessionExpired"
          class="mb-4 rounded-xl bg-amber-500/10 px-3 py-2 text-xs text-amber-200 ring-1 ring-inset ring-amber-500/25"
        >
          Sessiya muddati tugadi. Iltimos, qaytadan kiring.
        </div>

        <!-- ================================================ 1-BOSQICH: RAQAM -->
        <template v-if="step === 'telefon'">
          <label class="block">
            <span class="mb-1.5 block text-xs font-medium text-slate-400">Telefon raqami</span>
            <div class="relative">
              <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
                <AppIcon
                  name="phone"
                  :size="17"
                />
              </span>
              <!--
                ★ `:value` + `@input`, `v-model` EMAS — sabab
                `maskPhoneField` izohida: `v-model` avval modelni, keyin
                DOM'ni yangilaydi va kursor har bosishda satr oxiriga
                sakrab ketardi.
              -->
              <input
                :value="phone"
                type="tel"
                name="phone"
                inputmode="tel"
                autocomplete="tel"
                required
                :maxlength="PHONE_INPUT_MAXLENGTH"
                placeholder="+998 90 123 45 67"
                class="h-11 w-full rounded-lg bg-ink-950 pl-10 pr-3 text-sm tracking-[0.3px] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
                @input="phone = maskPhoneField($event.target as HTMLInputElement)"
              >
            </div>
          </label>

          <p class="mt-2 text-[12px] leading-relaxed text-slate-500">
            Kirish kodi shu raqamga ulangan <b class="font-medium text-slate-400">Telegram</b>
            hisobingizga yuboriladi.
          </p>

          <p
            v-if="errorMessage !== null"
            class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
            role="alert"
            v-text="errorMessage"
          />

          <BaseButton
            class="mt-6"
            type="submit"
            size="lg"
            block
            :loading="isSubmitting"
            :disabled="!canSendPhone"
          >
            Kod yuborish
          </BaseButton>
        </template>

        <!-- ================================================ 2-BOSQICH: KOD -->
        <template v-else>
          <!--
            Raqam ko'rinib turadi: foydalanuvchi kodni kutayotib "qaysi
            raqamni yozgan edim?" degan savolga tushmasin. Tuzatish uchun
            yonida "o'zgartirish" tugmasi bor — orqaga qaytish uchun
            brauzer tugmasi ishlamaydi (bu bitta sahifa).
          -->
          <div class="mb-4 flex items-center justify-between gap-2 rounded-xl bg-ink-950 px-3 py-2 ring-1 ring-inset ring-line">
            <span
              class="truncate text-xs text-slate-300"
              v-text="phone"
            />
            <button
              type="button"
              class="shrink-0 rounded-lg px-2 py-1 text-[11px] font-medium text-brand-500 transition-colors hover:bg-ink-750"
              @click="backToPhone"
            >
              O‘zgartirish
            </button>
          </div>

          <label class="block">
            <span class="mb-1.5 block text-xs font-medium text-slate-400">Telegramdan kelgan kod</span>
            <div class="relative">
              <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
                <AppIcon
                  name="lock"
                  :size="17"
                />
              </span>
              <!--
                `inputmode="numeric"` + `autocomplete="one-time-code"` —
                mobil brauzer kodni bildirishnomadan taklif qiladi.
                `maxlength` server yasaydigan uzunlik bilan bir xil.
              -->
              <input
                ref="codeInput"
                v-model="code"
                type="text"
                name="code"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="6"
                required
                placeholder="123456"
                class="h-11 w-full rounded-lg bg-ink-950 pl-10 pr-3 text-center text-lg font-semibold tracking-[0.4em] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:tracking-normal placeholder:text-sm placeholder:font-normal placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
              >
            </div>
          </label>

          <p
            v-if="errorMessage !== null"
            class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
            role="alert"
            v-text="errorMessage"
          />

          <BaseButton
            class="mt-6"
            type="submit"
            size="lg"
            block
            :loading="isSubmitting"
            :disabled="!canVerify"
          >
            Kirish
          </BaseButton>

          <!--
            Qayta yuborish — taymer bilan. Server 60 sekundlik oynani
            RAQAM bo'yicha qo'llaydi, ya'ni tugmani erta bosish 429
            beradi. Taymer shu holatni oldindan ko'rsatadi.
          -->
          <BaseButton
            class="mt-2.5"
            type="button"
            variant="ghost"
            size="md"
            block
            :disabled="resendIn > 0 || isSubmitting"
            @click="handleSendCode"
          >
            {{ resendIn > 0 ? `Qayta yuborish (${resendIn})` : 'Kodni qayta yuborish' }}
          </BaseButton>
        </template>
      </form>

      <p class="mt-6 text-center text-xs leading-relaxed text-slate-600">
        Kod kelmadimi? Telegramda botga <b class="font-medium">/start</b> yozib,
        «Raqamni ulashish» tugmasini bosing. Yordam kerak bo'lsa —
        o'quv bo'limiga murojaat qiling.
      </p>

      <!--
        ══════════════════════════════════════════════════════════════════
        ⚠️ SINOV PANELI — `v-if` BUTUN HIMOYANING KO'RINADIGAN QISMI
        ══════════════════════════════════════════════════════════════════

        Ro'yxat serverdan keladi; xususiyat o'chiq bo'lsa u BO'SH bo'ladi
        va bu blok umuman render qilinmaydi (DOM'da hech qanday izi
        qolmaydi). Sabab va qolgan uch darvoza — `<script>` dagi izohda.

        🔴 KO'RINISHI ATAYLAB "NOTO'G'RI": punktir ramka, sariq
           ogohlantirish rangi va bosh harfli sarlavha. Panel ilovaning
           qolgan qismi bilan UYG'UNLASHMASLIGI kerak — u yerda turgani
           darrov ko'zga tashlansin va uni "mahsulot xususiyati" deb
           o'ylash imkoni bo'lmasin.
      -->
      <section
        v-if="devAccounts.length > 0"
        class="mt-8 rounded-[1.25rem] border-2 border-dashed border-amber-500/50 bg-amber-500/5 p-4"
      >
        <h2 class="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-amber-300">
          <AppIcon
            name="alert"
            :size="15"
          />
          Sinov rejimi
        </h2>

        <p class="mt-1.5 text-[11px] leading-relaxed text-amber-200/70">
          Namunaviy hisoblarga telefon kodisiz kirish. Bu panel ishlab
          chiqarish serverida <b class="font-semibold">ko'rinmaydi</b>.
        </p>

        <div class="mt-3 grid gap-1.5">
          <button
            v-for="account in devAccounts"
            :key="account.role"
            type="button"
            :disabled="devBusyRole !== null"
            class="flex items-center justify-between gap-3 rounded-lg bg-ink-950/60 px-3 py-2 text-left ring-1 ring-inset ring-amber-500/25 transition-colors hover:bg-ink-950 disabled:opacity-50"
            @click="handleDevLogin(account.role)"
          >
            <span class="min-w-0">
              <span
                class="block truncate text-xs font-semibold text-amber-100"
                v-text="account.roleLabel"
              />
              <!--
                Ism va raqam — tekshiruvchi «bu kimning ekrani?» degan
                savolga tugmani bosmasdan javob topsin.
              -->
              <span
                class="block truncate text-[11px] text-amber-200/50"
                v-text="`${account.fullName} · ${formatPhone(account.phone) || '—'}`"
              />
            </span>
            <span
              class="shrink-0 text-[11px] font-medium text-amber-300"
              v-text="devBusyRole === account.role ? '…' : 'Kirish'"
            />
          </button>
        </div>
      </section>
    </div>
  </div>
</template>
