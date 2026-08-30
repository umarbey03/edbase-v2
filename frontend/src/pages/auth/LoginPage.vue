<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useTemplateRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { homeRouteFor } from '@/entities/user'
import type { User } from '@/entities/user'
import { useAuthStore } from '@/features/auth/model/auth.store'
import { TelegramAuthScreen } from '@/features/telegram-auth'
import { toUserMessage } from '@/shared/api'
import { COURSE_FACTS } from '@/shared/config/course-facts'
import {
  maskPhoneField,
  PHONE_INPUT_MAXLENGTH,
  phoneDigits,
  stripPhoneFormatting,
} from '@/shared/lib/phone'
import { isTelegramMiniApp } from '@/shared/lib/telegram-web-app'
import type { TelegramLoginStatusName } from '@/shared/types'
import { AppIcon, BaseButton } from '@/shared/ui'
import type { IconName } from '@/shared/ui'

/*
  ══════════════════════════════════════════════════════════════════════════
  ★★ KIRISH — TELEGRAM ORQALI (2026-08-13 / 2026-08-28)

  Email va parol formasi 2026-08-13 da BUTUNLAY olib tashlandi. O'rniga
  telefon + Telegramga keladigan kod qo'yilgan edi.

  ⚠️ 2026-08-28 — BOT ORQALI KIRISH ASOSIY YO'L BO'LDI (loyiha egasining
     qarori). Sabab amaliy va u telefon oqimining ikki kamchiligidan
     kelib chiqadi:

       1) foydalanuvchi raqamini QO'LDA yozadi — eng ko'p xato qilinadigan
          qadam ("+998" bormi? "0" tushdimi? qaysi raqam bilan
          ro'yxatdan o'tgan edim?);
       2) sayt botga HAVOLA BERMASDI — foydalanuvchi uni Telegram
          qidiruvidan O'ZI topishi kerak edi, va aynan shu joyda ko'p
          odam to'xtab qolardi.

     Yangi oqimda saytga HECH NARSA yozilmaydi: bitta tugma botni ochadi,
     bot esa Telegram akkauntning O'ZIDAN kimligini biladi va 6 xonali kod
     yuboradi.

  ══════════════════════════════════════════════════════════════════════════
  🔴 TELEFON OQIMI OLIB TASHLANMADI — U ZAXIRA YO'L

  Uni o'chirish "kirishning yagona yo'li" ni yasab qo'yardi, va u yo'l
  ishlamay qolganda hech kim (xodimlar ham) tizimga kira olmasdi. Real
  holatlar:
    • bot bloklangan yoki foydalanuvchi uni «Stop» qilgan;
    • brauzer yangi oynani to'sgan va havola ochilmagan;
    • Telegram BOSHQA qurilmada (kompyuterda brauzer, telefonda Telegram) —
      bunda deep-link havolasi ochilmaydi, lekin kod baribir keladi.

  Shuning uchun pastda «Telefon raqami bilan kirish» havolasi turadi va u
  ESKI, TEGILMAGAN oqimga olib boradi.
  ══════════════════════════════════════════════════════════════════════════

  🔴 XAVFSIZLIK — INTERFEYS DARAJASIDAGI QAT'IY QOIDALAR:

    (a) "Bunday raqam topilmadi" degan xabar HECH QACHON ko'rsatilmaydi.
        Server bu ma'lumotni ataylab bermaydi (hisob sanashga qarshi), va
        uni bu yerda "o'ylab topish" himoyani bekor qilardi. Raqam
        yuborilgach ekran DOIM kod bosqichiga o'tadi.

    (b) Telefon raqami serverga XOM holda yuboriladi — mijozda hech
        qanday normalizatsiya QILINMAYDI. Normalizatsiya qoidasi
        backendda bitta joyda (`User.NormalizePhone`) va u
        `PhoneNormalized` ustunini to'ldiradigan AYNI metod.

        ⚠️ MAYDON FORMATLANADI, LEKIN QOIDA BUZILMAYDI: `maskPhoneField`
        faqat ko'rinishni yasaydi, `stripPhoneFormatting` esa yuborishdan
        oldin FAQAT bo'shliqlarni oladi.

    (c) KOD BOT OQIMIDA HAM SO'RALADI — bot foydalanuvchini allaqachon
        tanigan bo'lsa ham. Sabab: deep-link havolasini hujumchi O'ZI
        yasab, qurbonga yuborishi mumkin ("kirish uchun shu tugmani
        bosing"). Qurbon `/start` bosgan zahoti sessiya ochilsa, u
        HUJUMCHINING brauzerida ochilardi. Kod esa QURBONNING Telegramiga
        boradi — ya'ni uni saytga kiritadigan odam BRAUZER egasi bo'lishi
        shart. To'liq tahlil: backenddagi `ITelegramLoginService`.
  ══════════════════════════════════════════════════════════════════════════
*/

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()

/**
 * Qaysi eshikdan kirilyapti.
 *
 * ★ BOSHLANG'ICH QIYMAT — `bot`: u asosiy yo'l. Telefon oqimiga
 * foydalanuvchi ATAYLAB o'tadi va o'sha yerda qoladi (avtomatik qaytish
 * yo'q — u odamni o'zi tanlagan yo'ldan urib ketardi).
 */
const mode = ref<'bot' | 'telefon'>('bot')

const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)

const codeInput = useTemplateRef<HTMLInputElement>('codeInput')
const botCodeInput = useTemplateRef<HTMLInputElement>('botCodeInput')

/**
 * TELEGRAM MINI APP REJIMI.
 *
 * Ilova Telegram Mini App ichida ochilgan bo'lsa, o'quvchi hech narsa
 * kiritmasligi kerak — `initData` imzosi kifoya. Shuning uchun forma
 * o'rniga avtomatik kirish ekrani ko'rsatiladi.
 *
 * ★ SHART BIR MARTA hisoblanadi va `ref` da turadi (computed emas): xato
 * holatida foydalanuvchi ATAYLAB formaga o'tishi mumkin, ya'ni qiymat
 * hodisaga qarab o'zgaradi.
 */
const telegramMode = ref(isTelegramMiniApp())

const sessionExpired = computed(() => route.query['sabab'] === 'sessiya-tugadi')

/*
  CHAP USTUNDAGI BREND PANELI (faqat `lg` dan boshlab ko'rinadi).

  ★ NIMA UCHUN AYNAN SHU UCH GAP: kirish ekraniga ikki xil odam keladi —
    (a) allaqachon o'quvchi, u shunchaki kirmoqchi; (b) landing'dan kelib
    "bu qanaqa tizim?" deb qarab turgan odam. Ikkinchisi uchun panel
    kabinetda NIMA borligini bir qarashda aytadi.

  🔴 BU YERDA SOTUV MATNI YO'Q (narx, guruh hajmi, aksiya). Kirish
     ekrani — ishchi ekran; unga sotuv matnini qo'yish kirmoqchi bo'lgan
     odamni sekinlashtiradi. Sotuv landing'da (`pages/landing`).
*/
const PANEL_POINTS: readonly { icon: IconName, title: string, text: string }[] = [
  {
    icon: 'video',
    title: 'Jonli darslar',
    text: 'Dars saytning o‘zida ochiladi va yozuvi kabinetingizda qoladi.',
  },
  {
    icon: 'clipboard',
    title: 'Vazifa va testlar',
    text: 'Har mavzudan keyingi test, natijalaringiz va davomatingiz.',
  },
  {
    icon: 'telegram',
    title: 'Parolsiz kirish',
    text: 'Eslab qolinadigan parol yo‘q — tasdiq Telegram orqali keladi.',
  },
]

/*
  PANEL PASTIDAGI FAKTLAR CHIZIG'I (2026-08-30).

  ★ MUAMMO: panel `justify-between` bilan tuzilgan — tepada logo, pastda
    "bosh sahifaga" havolasi. Matn bloki kalta bo'lgani uchun ular
    orasida katta bo'sh maydon qolardi va panelning pastki uchdan biri
    quruq yashil bo'lib turardi.

  ┌────────────────────────────────────────────────────────────────────┐
  │ 🔴 NEGA "1 200 O'QUVCHI" KABI RAQAM EMAS                           │
  └────────────────────────────────────────────────────────────────────┘
  Premium kirish sahifalari odatda ijtimoiy dalil ko'rsatadi va eng
  kuchlisi — o'quvchi soni. Lekin uni HOZIR chizib bo'lmaydi:

    • `/api/v1/*` ning hammasi autentifikatsiya talab qiladi, ya'ni
      KIRMAGAN odam turgan sahifa bu sonni serverdan ola olmaydi;
    • qo'lda yozilgan son bir hafta ichida eskiradi va yolg'onga
      aylanadi — kirish sahifasidagi yolg'on esa ishonchni aynan
      ishonch eng kerak bo'lgan joyda buzadi.

  Shuning uchun bu yerda SHARTNOMAVIY faktlar turibdi: ular ham aniq va
  tekshiriladigan, lekin markaz qarori bilan o'zgaradi va o'z-o'zidan
  eskirmaydi. Manba — `shared/config/course-facts.ts`, ya'ni landing
  bilan AYNI qiymatlar (ikki sahifa hech qachon ikki xil raqam
  ko'rsatmaydi).

  ⚠️ HAQIQIY HISOBLAGICH KERAK BO'LSA: backendda anonim endpoint
     ochilishi kerak (masalan `GET /api/v1/public/stats`). O'shanda bu
     ro'yxat so'rov natijasi bilan almashtiriladi.
*/
const PANEL_STATS: readonly { value: string, label: string }[] = [
  { value: COURSE_FACTS.courseDuration, label: 'ATF kursi' },
  { value: COURSE_FACTS.weeklyLessonDays, label: 'haftasiga dars' },
  { value: COURSE_FACTS.groupSize, label: 'kishilik guruh' },
]

/** Telegram orqali kirish tugagach — rolga mos bosh sahifaga. */
async function handleTelegramSuccess(user: User): Promise<void> {
  /*
    `?redirect=` ATAYLAB e'tiborga olinmaydi. Telegram ilovani `/` da,
    `#tgWebAppData=...` fragmenti bilan ochadi va guard uni `redirect`
    query'siga ko'chiradi — ya'ni manzil imzolangan kirish ma'lumotini
    o'z ichiga oladi. Unga qaytib borish uni URL'da yana tarqatardi.
  */
  await router.replace({ name: homeRouteFor(user.role) })
}

/** `?redirect=` dagi ichki manzil (bo'lmasa `null`). */
function redirectTarget(): string | null {
  const raw = route.query['redirect']
  const value = Array.isArray(raw) ? raw[0] : raw
  // Faqat ichki yo'llarga yo'naltiramiz (ochiq redirect zaifligining oldini olish).
  if (typeof value === 'string' && value.startsWith('/') && !value.startsWith('//')) return value
  return null
}

/** Kirish tugagach boriladigan manzil — ikkala oqim uchun YAGONA. */
async function goAfterLogin(user: User): Promise<void> {
  const target = redirectTarget()
  if (target !== null) await router.replace(target)
  else await router.replace({ name: homeRouteFor(user.role) })
}

/* ════════════════════════════════════════════════════════════ BOT OQIMI */

/**
 * Oqim holati SAHIFADA, store'da EMAS.
 *
 * ★ SABAB: chipta AYNI shu ekranga tegishli. Store'ga ko'chirilsa
 * "kirish sahifasidan chiqib ketilganda kim tozalaydi?" degan savol
 * paydo bo'lardi va javobi har doim shu komponent bo'lardi.
 */
const botStep = ref<'boshlash' | 'kutish'>('boshlash')
const botToken = ref('')
const botLink = ref('')
const botStatus = ref<TelegramLoginStatusName>('kutilmoqda')
const botHint = ref('')
const botExpiresIn = ref(0)
const botCode = ref('')

/**
 * Brauzer yangi oynani to'sganmi.
 *
 * ★ BU XATO EMAS, HOLAT: oqim buzilmaydi — sahifada tugma qoladi va
 * foydalanuvchi uni O'ZI bosadi (bu safar bosish "foydalanuvchi
 * so'ragan" deb hisoblanadi va to'silmaydi).
 */
const botPopupBlocked = ref(false)

/**
 * Chipta `sessionStorage` da saqlanadi.
 *
 * ★ NEGA `localStorage` EMAS: chipta 15 daqiqa yashaydi va u FAQAT shu
 * brauzer oynasining kirish urinishiga tegishli. `localStorage` da
 * qolgan chipta boshqa kunda ochilgan yorliqda "kod kutilmoqda" ekranini
 * ko'rsatib, foydalanuvchini chalkashtirardi.
 */
const FLOW_KEY = 'zinnur:tg-login'

function saveFlow(): void {
  try {
    sessionStorage.setItem(
      FLOW_KEY,
      JSON.stringify({ token: botToken.value, link: botLink.value }),
    )
  } catch {
    // Xotira to'lgan yoki saqlash o'chirilgan — oqim baribir ishlaydi,
    // faqat sahifa yangilanganda boshidan boshlanadi.
  }
}

function clearFlow(): void {
  try {
    sessionStorage.removeItem(FLOW_KEY)
  } catch {
    // yuqoridagi bilan bir xil — jimgina o'tamiz.
  }
}

function restoreFlow(): boolean {
  try {
    const raw = sessionStorage.getItem(FLOW_KEY)
    if (raw === null) return false

    const saved = JSON.parse(raw) as { token?: unknown, link?: unknown }
    if (typeof saved.token !== 'string' || typeof saved.link !== 'string') return false

    botToken.value = saved.token
    botLink.value = saved.link
    return true
  } catch {
    return false
  }
}

/*
  ┌──────────────────────────────────────────────────────────────────────┐
  │ BOTNI YANGI OYNADA OCHISH — NEGA IKKI QADAMDA                        │
  └──────────────────────────────────────────────────────────────────────┘
  Havolani SERVERDAN olamiz, ya'ni `window.open` `await` dan KEYIN
  chaqirilardi. Bunday chaqiruvni brauzer "foydalanuvchi so'ramagan" deb
  hisoblaydi va JIMGINA to'sib qo'yadi — hech qanday xato ham ko'rinmaydi.

  Shuning uchun BO'SH oyna aynan bosish paytida ochiladi (ishora hali
  kuchda), manzil esa chipta kelgach qo'yiladi.
*/

/** Bosish paytida ochiladigan bo'sh oyna. To'silgan bo'lsa — `null`. */
function openBlankWindow(): Window | null {
  try {
    return window.open('', '_blank')
  } catch {
    return null
  }
}

/** Bo'sh oynani havolaga yo'naltiradi. Ochilganini bildiradi. */
function sendWindowTo(win: Window | null, link: string): boolean {
  if (win === null || win.closed) return false

  try {
    // Yangi oyna bu sahifaga tegmasin (tabnabbing).
    win.opener = null
    win.location.replace(link)
    return true
  } catch {
    return false
  }
}

/** 1-QADAM: chipta olish va botni ochish. */
async function handleStartBot(): Promise<void> {
  if (isSubmitting.value) return

  const win = openBlankWindow()

  isSubmitting.value = true
  errorMessage.value = null

  try {
    const flow = await auth.startTelegramLogin()

    botToken.value = flow.token
    botLink.value = flow.link
    botExpiresIn.value = flow.expiresInSeconds
    botStatus.value = 'kutilmoqda'
    botHint.value = 'Holat tekshirilmoqda…'
    botCode.value = ''
    botStep.value = 'kutish'

    saveFlow()

    botPopupBlocked.value = !sendWindowTo(win, flow.link)

    startTicker()
  } catch (error) {
    // Bo'sh oyna osilib qolmasin.
    win?.close()

    // 503 (bot sozlanmagan) va 429 — yagona haqiqiy xato holatlari.
    // Serverning matni O'ZI zaxira yo'lni ko'rsatadi ("telefon raqami
    // bilan kiring"), shuning uchun bu yerda qo'shimcha matn yozilmaydi.
    errorMessage.value = toUserMessage(error)
  } finally {
    isSubmitting.value = false
  }
}

/** Botni QO'LDA ochish (to'silgan yoki qayta ochmoqchi bo'lgan holat). */
function openBotManually(): void {
  botPopupBlocked.value = false
  window.open(botLink.value, '_blank', 'noopener')
}

/**
 * Oqimni boshidan boshlash.
 *
 * ★ SAHIFA QAYTA YUKLANMAYDI (`location.reload` YO'Q): SPA'da bu butun
 * ilovani qaytadan ko'tarardi, holat esa shu yerda va uni tozalash
 * yetarli.
 */
function resetBot(): void {
  stopTicker()
  clearFlow()

  botStep.value = 'boshlash'
  botToken.value = ''
  botLink.value = ''
  botStatus.value = 'kutilmoqda'
  botHint.value = ''
  botExpiresIn.value = 0
  botCode.value = ''
  botPopupBlocked.value = false
  errorMessage.value = null
}

/** Kod maydoni ko'rinadigan yagona holat. */
const botCodeReady = computed(() => botStatus.value === 'kod')

/** Oqim davom eta olmaydigan holatlar — bunda faqat "qaytadan" qoladi. */
const botDeadEnd = computed(() => botStatus.value === 'yoq' || botStatus.value === 'nofaol')

const canVerifyBot = computed(
  () => /^\d{6}$/.test(botCode.value) && !isSubmitting.value,
)

/** Qolgan vaqt `12:34` ko'rinishida. */
const botClock = computed(() => {
  const total = Math.max(0, botExpiresIn.value)
  const minutes = Math.floor(total / 60)
  const seconds = total % 60
  return `${minutes}:${seconds.toString().padStart(2, '0')}`
})

/*
  ┌──────────────────────────────────────────────────────────────────────┐
  │ YAGONA TAYMER                                                        │
  └──────────────────────────────────────────────────────────────────────┘
  Ikki vazifa bor: qolgan vaqtni sanash (har sekund) va holatni so'rash
  (har 3 sekund). Ular IKKITA `setInterval` bilan ham qilinardi, lekin
  bitta hisoblagich bilan ular ORASIDA nomuvofiqlik bo'lishi mumkin
  emas — masalan "muddat tugadi, lekin so'rov hali ketyapti" holati.
*/
let ticker: ReturnType<typeof setInterval> | null = null
let tickCount = 0

/** Holat necha sekundda bir so'raladi. */
const POLL_EVERY_SECONDS = 3

function startTicker(): void {
  stopTicker()
  tickCount = 0

  // Birinchi so'rov DARHOL: bot allaqachon javob bergan bo'lishi mumkin
  // (masalan foydalanuvchi sahifani yangilagan bo'lsa).
  void pollBotStatus()

  ticker = setInterval(() => {
    if (botExpiresIn.value > 0) botExpiresIn.value -= 1

    tickCount += 1
    if (tickCount % POLL_EVERY_SECONDS === 0) void pollBotStatus()
  }, 1000)
}

function stopTicker(): void {
  if (ticker !== null) {
    clearInterval(ticker)
    ticker = null
  }
}

// Sahifadan chiqilganda taymer qolib ketmasin (xotira oqishi va yopilgan
// komponentga yozish xatosi).
onBeforeUnmount(stopTicker)

async function pollBotStatus(): Promise<void> {
  if (botToken.value.length === 0) return

  try {
    const state = await auth.telegramLoginStatus(botToken.value)

    botStatus.value = state.status
    botHint.value = state.hint
    botExpiresIn.value = state.expiresInSeconds

    if (state.status === 'kod') {
      // Kod keldi — endi kuzatadigan narsa qolmadi.
      stopTicker()

      await nextTick()
      botCodeInput.value?.focus()
      return
    }

    if (state.status === 'yoq' || state.status === 'nofaol') {
      // Boshi berk ko'cha: chipta o'lgan yoki profil faol emas.
      stopTicker()
      clearFlow()
    }
  } catch {
    // Tarmoq uzilishi — keyingi urinishda. Xato KO'RSATILMAYDI: bu
    // so'rov fonda ketadi va uning har uzilishi uchun ekranga qizil
    // yozuv chiqarish foydalanuvchini bekorga qo'rqitardi.
  }
}

/** 3-QADAM: kodni tasdiqlash. */
async function handleVerifyBot(): Promise<void> {
  if (!canVerifyBot.value) return

  isSubmitting.value = true
  errorMessage.value = null

  try {
    const user = await auth.verifyTelegramLogin(botToken.value, botCode.value)
    clearFlow()
    await goAfterLogin(user)
  } catch (error) {
    errorMessage.value = toUserMessage(error)
    botCode.value = ''
    await nextTick()
    botCodeInput.value?.focus()
  } finally {
    isSubmitting.value = false
  }
}

/**
 * Kod maydoni: faqat raqam qoladi (nusxa-joylashda ham), 6 ta bo'lgach
 * O'ZI yuboriladi.
 *
 * ★ AVTOMATIK YUBORISH — qulaylik emas, XATOGA QARSHI CHORA:
 * foydalanuvchi kodni Telegramdan ko'chiradi va tugmani izlab o'tirishi
 * kerak emas. Har ortiqcha qadam noto'g'ri kod kiritish ehtimolini
 * oshiradi, urinishlar esa 5 ta.
 */
function onBotCodeInput(event: Event): void {
  const input = event.target as HTMLInputElement
  const digits = input.value.replace(/\D/g, '').slice(0, 6)

  botCode.value = digits
  // Maydondagi qiymat modeldan farq qilsa (masalan harf yozildi) —
  // DOM'ni ham tuzatamiz, aks holda ekranda harf qolib ketardi.
  if (input.value !== digits) input.value = digits

  if (digits.length === 6) void handleVerifyBot()
}

/* ════════════════════════════════════════════════════════ TELEFON OQIMI */

/** Zaxira oqim bosqichi: raqam kiritish -> kod kiritish. */
const step = ref<'telefon' | 'kod'>('telefon')

const phone = ref('')
const code = ref('')

/** Qayta yuborishgacha qolgan sekundlar (0 — tugma faol). */
const resendIn = ref(0)
let resendTimer: ReturnType<typeof setInterval> | null = null

/**
 * Raqamda kamida shuncha RAQAM bo'lsin.
 *
 * ★ TO'LIQ TEKSHIRUV ATAYLAB YO'Q: qat'iy shakl talabi (masalan
 * `+998 XX XXX XX XX`) chet el raqami bilan ro'yxatdan o'tgan xodimni
 * to'sib qo'yardi, va shakl qoidasi backenddagi normalizatsiya bilan
 * ikkinchi nusxa bo'lib qolardi.
 */
const canSendPhone = computed(
  () => phoneDigits(phone.value).length >= 7 && !isSubmitting.value,
)

/** Kod — AYNAN 6 raqam (server ham shuni yasaydi). */
const canVerify = computed(() => /^\d{6}$/.test(code.value.trim()) && !isSubmitting.value)

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
    await goAfterLogin(user)
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

/** Eshikni almashtirish — har ikki yo'nalishda holat tozalanadi. */
function switchTo(next: 'bot' | 'telefon'): void {
  errorMessage.value = null

  if (next === 'telefon') {
    // Bot oqimi to'xtatiladi, lekin chipta O'CHIRILMAYDI: foydalanuvchi
    // fikridan qaytib qaytib kelsa, kutayotgan oqimi joyida bo'lsin.
    stopTicker()
  } else if (botToken.value.length > 0 && botStep.value === 'kutish') {
    startTicker()
  }

  mode.value = next
}

/*
  Sahifa yangilanganda oqim davom etsin: chipta 15 daqiqa yashaydi va
  foydalanuvchi shu vaqt ichida sahifani yangilashi (yoki botdan qaytib
  kelishi) juda ehtimoliy holat.
*/
onMounted(() => {
  if (telegramMode.value) return
  if (!restoreFlow()) return

  botStep.value = 'kutish'
  botHint.value = 'Holat tekshirilmoqda…'
  startTicker()
})
</script>

<template>
  <!-- Telegram Mini App: forma o'rniga avtomatik kirish ekrani. -->
  <TelegramAuthScreen
    v-if="telegramMode"
    @success="handleTelegramSuccess"
    @phone-login="telegramMode = false"
  />

  <!--
    ══════════════════════════════════════════════════════════════════════
     2026-08-30 — EKRAN IKKI USTUNGA BO'LINDI (loyiha egasining talabi)
    ══════════════════════════════════════════════════════════════════════

    Ilgari bu ekran QAT'IY MARKAZLASHGAN edi: qorong'i fon ustida bitta
    `max-w-sm` karta. Telefonda bu to'g'ri ko'rinardi, lekin kompyuterda
    ekranning katta qismi bo'sh qolardi va sahifa "hali qurib
    bo'linmagan" taassurot berardi — landing esa yonida to'liq
    bezatilgan holda turardi.

    Endi `lg` dan boshlab CHAP ustunda brend paneli (landing hero bilan
    AYNI to'q yashil sirt), o'ngda esa o'sha karta turadi.

    🔴 KARTANING ICHIGA TEGILMADI — ATAYLAB. Bo'linish faqat SHU
       o'ramda: `lg` gacha element oddiy `flex` markazlashgan qobiq
       bo'lib qoladi, ya'ni telefonda va Telegram Mini App ichida
       ko'rinish O'ZGARMAGAN. Shu sababli oqim mantig'ining birorta
       qatori ham qayta yozilmadi.

    ┌──────────────────────────────────────────────────────────────────┐
    │ 🔴 `lg:items-stretch` — MAJBURIY, BEZAK EMAS                     │
    └──────────────────────────────────────────────────────────────────┘
    `items-center` telefon uchun kerak (karta ekran o'rtasida tursin),
    lekin u `align-items` xossasi va u GRID rejimida ham kuchda qoladi.
    Natijada `lg` da setka kataklari kontent balandligicha qolib,
    yashil panel ekranning o'rtasida "suzib" turardi — tepasida va
    pastida katta bo'sh maydon bilan.

    ⚠️ Bu 2026-08-30 da AYNAN shunday xato bo'lib chiqdi va skrinshotdan
       topildi. `lg:items-stretch` uni bekor qiladi: panel to'liq
       balandlikni oladi.

    ┌──────────────────────────────────────────────────────────────────┐
    │ USTUN NISBATI VA FORMA KENGLIGI — BIRGA HAL QILINADI             │
    └──────────────────────────────────────────────────────────────────┘
    ⚠️ 2026-08-30 — IKKINCHI TUZATISH. Avval `1.05fr 1fr` va forma
       `max-w-sm` (384px) edi. Natijada chap panel EKRANNING YARMINI
       to'liq bo'yardi, o'ngdagi forma esa o'z ustunining atigi 56%
       ini egallab, o'rtada suzib turardi.

       Ko'z og'irlikni chapda ko'rar edi va KIRISH FORMASI — sahifaning
       butun vazifasi — ikkinchi darajali narsaga o'xshab qolardi.

    Yechim ikki tomonlama: ustunlar TENGLASHTIRILDI va forma
    `max-w-md` (448px) gacha kengaytirildi. Endi o'ng ustunning ~66%
    i to'lgan va ikki tomon og'irligi teng.

    🔴 FORMA KENGLIGI `max-w-md` DAN OSHMASIN: bitta ustunli forma
       448px dan kengaygach, yorliq bilan maydon orasidagi masofa
       ko'z uchun uzayadi va o'qish qiyinlashadi.
  -->
  <div
    v-else
    class="relative flex min-h-dvh items-center justify-center bg-ink-950 px-4 py-10 lg:grid lg:grid-cols-2 lg:items-stretch lg:p-0"
  >
    <!--
      Fon nuri. Yorug' fonda shaffoflik PASAYTIRILGAN (7%/5%): oq sirtda
      bir xil foiz nurni "bo'yoq dog'i" darajasiga chiqaradi.
    -->
    <!--
      ⚠️ 2026-08-30 — FONGA CHUQURLIK QO'SHILDI.

      Yorug' temada karta ham oq, sahifa foni ham deyarli oq edi:
      ikkalasini bir-biridan AJRATADIGAN yagona narsa soya bo'lib
      qolgandi. Natijada karta "qog'ozga chizilgan to'rtburchak"dek
      ko'rinardi.

      Endi uch qatlam: ikkita mavjud burchak nuri (kuchaytirildi) va
      YANGI, kartaning orqasidagi yumshoq yashil halqa. Halqa aynan
      formaning markaziga to'g'ri keladi (`72% 50%` — o'ng ustunning
      o'rtasi), ya'ni karta yorug' dog' ustida "ko'tarilib" turadi.

      🔴 FOIZLAR ATAYLAB KICHIK (5–11%): bu QATLAM, naqsh emas. Qiymat
         kattalashsa yorug' fonda darhol "bo'yoq dog'i" bo'lib ko'rinadi
         va matn kontrasti pasayadi.
    -->
    <div
      class="pointer-events-none fixed inset-0"
      aria-hidden="true"
      style="
        background:
          radial-gradient(
              38rem 30rem at 72% 50%,
              color-mix(in oklab, var(--color-brand-vivid) 11%, transparent),
              transparent 70%
            ),
          radial-gradient(
              60rem 40rem at 20% -10%,
              color-mix(in oklab, var(--color-brand-500) 9%, transparent),
              transparent 60%
            ),
          radial-gradient(
            40rem 30rem at 90% 110%,
            color-mix(in oklab, var(--color-violet-500) 6%, transparent),
            transparent 60%
          );
      "
    />

    <!--
      ═══════════════════════════════════════════ BREND PANELI (chap) ═══
      `surface-brand` — landing hero bilan AYNI to'q yashil sirt (qoidalar
      `style.css` da). Ikki ekran ketma-ket ko'rinadi: landing'dan
      «Kirish» bosgan odam bir xil rangdagi sahifaga tushadi va oqim
      uzilmaydi.

      🔴 `hidden lg:flex` — telefonda BUTUNLAY chizilmaydi. Kichik ekranda
         u formani pastga surib yuborardi, holbuki kirish ekranida
         BIRINCHI ko'rinishi kerak bo'lgan narsa — forma.
    -->
    <aside
      class="surface-brand relative hidden overflow-hidden bg-ink-950 p-10 lg:flex lg:flex-col lg:justify-between xl:p-14"
    >
      <!--
        SETKALI NAQSH — landing hero'dagi bilan AYNI qatlam.

        ★ NEGA KERAK: ikkala ekran ham brend sirtida va ketma-ket
          ko'rinadi (landing -> «Kirish»). Hero'da tekstura bor, bu
          yerda yo'q edi — panel yassi "bo'yalgan maydon" bo'lib turardi
          va ikkalasi bir brendning ikki sahifasidek o'qilmasdi.

        Tuzilishi va niqob nima uchun alohida o'ramda ekani —
        `style.css` dagi `.hero-grid-mask` izohida.
      -->
      <div class="hero-grid-mask">
        <div class="hero-grid text-slate-50" />
      </div>

      <!-- Fon nuri — landing hero'dagi bilan bir xil naqsh. -->
      <div
        class="pointer-events-none absolute inset-0"
        aria-hidden="true"
        style="
          background:
            radial-gradient(
                50rem 34rem at 10% -15%,
                color-mix(in oklab, var(--color-brand-500) 18%, transparent),
                transparent 62%
              ),
            radial-gradient(
              34rem 26rem at 95% 105%,
              color-mix(in oklab, var(--color-brand-500) 12%, transparent),
              transparent 60%
            );
        "
      />

      <RouterLink
        class="relative inline-flex items-center gap-2.5 self-start"
        to="/"
      >
        <img
          class="size-10 rounded-full"
          src="/logo-64.png"
          alt="ZIN-NUR ONLINE logosi"
          width="40"
          height="40"
        >
        <!--
          SO'Z BELGISI — ikki qator, ikkalasi bir xil. Balandlik logo
          bilan AYNAN teng: logo `size-10` = 40px, ya'ni har qator 20px.
          Qoidaning to'liq izohi shu faylning pastroq blokida.
        -->
        <span class="flex h-10 flex-col justify-center">
          <span class="font-display text-[18px] font-semibold leading-[20px] tracking-tight text-brand-500">
            ZIN-NUR
          </span>
          <span class="font-display text-[18px] font-semibold leading-[20px] tracking-tight text-brand-500">
            ONLINE
          </span>
        </span>
      </RouterLink>

      <div class="relative max-w-md">
        <h2
          class="font-display text-[2rem] font-semibold leading-[1.12] tracking-[-0.01em] text-slate-50 xl:text-[2.6rem]"
        >
          Kabinetingizga
          <span class="text-brand-500">xush kelibsiz</span>
        </h2>

        <ul class="mt-9 space-y-5">
          <li
            v-for="point in PANEL_POINTS"
            :key="point.title"
            class="flex gap-3.5"
          >
            <span
              class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-xl bg-brand-500/12 text-brand-500"
            >
              <AppIcon
                :name="point.icon"
                :size="17"
              />
            </span>
            <div>
              <p class="text-sm font-bold text-slate-100">
                {{ point.title }}
              </p>
              <p class="mt-1 text-sm leading-relaxed text-slate-300">
                {{ point.text }}
              </p>
            </div>
          </li>
        </ul>
      </div>

      <div class="relative">
        <!--
          FAKTLAR CHIZIG'I — panelning pastki bo'sh maydonini to'ldiradi
          va bir vaqtning o'zida "bu qanaqa kurs?" degan savolga javob
          beradi. Qiymatlar landing bilan AYNI manbadan; nega o'quvchi
          SONI emasligi `PANEL_STATS` izohida.
        -->
        <dl class="flex flex-wrap gap-x-10 gap-y-5 border-t border-line pt-7">
          <div
            v-for="stat in PANEL_STATS"
            :key="stat.label"
          >
            <dt class="font-display text-2xl font-semibold tracking-tight text-slate-50">
              {{ stat.value }}
            </dt>
            <dd class="mt-0.5 text-xs text-slate-400">
              {{ stat.label }}
            </dd>
          </div>
        </dl>

        <RouterLink
          class="mt-8 inline-flex items-center gap-1.5 text-sm font-medium text-slate-300 transition-colors hover:text-brand-500"
          to="/"
        >
          <AppIcon
            name="arrow-left"
            :size="15"
          />
          Bosh sahifaga qaytish
        </RouterLink>
      </div>
    </aside>

    <!--
      ═══════════════════════════════════════════════ FORMA (o'ng) ═══
      🔴 `lg:place-self-center` — `lg` da bu element setka katagi bo'lib
         qoladi va O'ZINI markazlashtiradi. Shu sabab ichidagi kartaga
         (va butun oqim mantig'iga) tegilmadi: markazlash o'ramdan
         setka katagiga ko'chdi, xolos.
    -->
    <div class="relative w-full max-w-md lg:my-auto lg:justify-self-center lg:px-8">
      <!--
        TELEFONDAGI SARLAVHA. `lg:hidden` — kompyuterda brend chap
        panelda turadi va uni ikkinchi marta takrorlash kartani
        pastga surardi.
      -->
      <div class="mb-7 text-center lg:hidden">
        <!--
          HAQIQIY BREND LOGOSI (2026-08-29).

          Ilgari bu yerda gradientli plita ichida "Z" harfi turardi —
          qo'lda yasalgan monogramma. Markazning o'z logosi bo'lgani
          holda uni ko'rsatmaslik uchun sabab yo'q edi.

          ★ `size-14` (plita `size-12` edi): logo doira shaklida va
            ichida tasvir bor, shuning uchun ayni o'lchamda kichikroq
            ko'rinardi.
        -->
        <img
          class="mx-auto size-14 rounded-full"
          src="/logo-64.png"
          alt="ZIN-NUR ONLINE logosi"
          width="56"
          height="56"
        >
        <!--
          SO'Z BELGISI — IKKI QATOR, LOGODAGIDEK (2026-08-29).

          🔴 IKKALA QATOR HAM BIR XIL: rang, shrift, o'lcham va vazn —
          loyiha egasining aniq talabi. Ilgari "ONLINE" kichikroq va kul
          rangda edi, u RAD ETILDI.

          Bu yerda `items-center`: ekran markazlashgan tuzilishda va
          logo ham markazda turadi (landing'da esa chapga tekislangan,
          chunki u gorizontal panelda).

          🔴 BALANDLIK LOGO BILAN AYNAN TENG (loyiha egasining talabi):
          logo `size-14` = 56px, shuning uchun har qator `leading-[28px]`
          va 28 + 28 = 56. `h-14` — brauzer yaxlitlasa ham siljimasin.

          ★ Bu ekranda logo matnning TEPASIDA turadi, yonida emas, ya'ni
            tenglik tekislash uchun emas — nisbat uchun: so'z belgisi
            logo bilan bir xil "og'irlikda" ko'rinadi.

          ★ Qat'iy `leading` XAVFSIZ: nom butunlay katta harflardan
            iborat, pastga tushuvchi harf (`g`, `y`, `p`) yo'q.
        -->
        <h1 class="mt-4 flex h-14 flex-col items-center justify-center">
          <span class="font-display text-[26px] font-semibold leading-[28px] tracking-tight text-brand-500">
            ZIN-NUR
          </span>
          <span class="font-display text-[26px] font-semibold leading-[28px] tracking-tight text-brand-500">
            ONLINE
          </span>
        </h1>
        <p class="mt-1.5 text-sm text-slate-400">
          Arab tili akademiyasi
        </p>
      </div>

      <!--
        KOMPYUTERDAGI SARLAVHA — logo O'RNIGA, u yonidagi panelda.
        Chapga tekislangan: karta ham chapga tekislangan matndan iborat,
        markazlashgan sarlavha uning ustida "boshqa ekrandan kelgandek"
        turardi.
      -->
      <div class="mb-6 hidden lg:block">
        <h1 class="font-display text-4xl font-semibold tracking-tight text-slate-100">
          Tizimga kirish
        </h1>
        <p class="mt-2.5 text-base text-slate-400">
          Parol kerak emas — kirish Telegram orqali tasdiqlanadi.
        </p>
      </div>

      <form
        class="rounded-2xl bg-ink-900 p-7 shadow-xl ring-1 ring-inset ring-line sm:p-8"
        novalidate
        @submit.prevent="
          mode === 'bot'
            ? handleVerifyBot()
            : (step === 'telefon' ? handleSendCode() : handleVerify())
        "
      >
        <div
          v-if="sessionExpired"
          class="mb-4 rounded-xl bg-amber-500/10 px-3 py-2 text-xs text-amber-200 ring-1 ring-inset ring-amber-500/25"
        >
          Sessiya muddati tugadi. Iltimos, qaytadan kiring.
        </div>

        <!-- ══════════════════════════════════════════ BOT OQIMI ══════ -->
        <template v-if="mode === 'bot'">
          <!--
            BOSQICHLAR CHIZIG'I. Ikki qadam: botni ochish -> kod.

            ★ NEGA KERAK: bu oqimda foydalanuvchi SAYTDAN CHIQIB KETADI
            (Telegramga) va qaytib kelganda "men qayerda edim?" degan
            savolga javob kerak bo'ladi.
          -->
          <ol
            class="mb-6 flex items-center gap-2.5 text-xs"
            aria-label="Bosqichlar"
          >
            <li class="flex flex-1 items-center gap-2">
              <span
                class="flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-bold"
                :class="botStep === 'kutish'
                  ? 'border-transparent bg-brand-500 text-on-brand'
                  : 'border-brand-500 text-brand-500'"
              >
                <AppIcon
                  v-if="botStep === 'kutish'"
                  name="check"
                  :size="13"
                />
                <template v-else>1</template>
              </span>
              <span
                class="whitespace-nowrap"
                :class="botStep === 'boshlash' ? 'font-semibold text-slate-200' : 'text-slate-500'"
              >Telegram</span>
              <span class="h-px flex-1 bg-line" />
            </li>
            <li class="flex items-center gap-2">
              <span
                class="flex size-7 shrink-0 items-center justify-center rounded-full border text-xs font-bold"
                :class="botStep === 'kutish'
                  ? 'border-brand-500 text-brand-500'
                  : 'border-line text-slate-600'"
              >2</span>
              <span
                class="whitespace-nowrap"
                :class="botStep === 'kutish' ? 'font-semibold text-slate-200' : 'text-slate-600'"
              >Kod</span>
            </li>
          </ol>

          <!-- ─────────────────────────────── 1-QADAM: BOTNI OCHISH -->
          <template v-if="botStep === 'boshlash'">
            <!--
              ⚠️ 2026-08-30 — MATN QISQARTIRILDI. Ilgari bu yerda uch
              gap turardi va ularning ikkitasi pastdagi izoh bilan bir
              narsani ("yozish shart emas", "parol yo'q") takrorlardi.
              Kirish ekranida o'qiladigan matn qancha kam bo'lsa,
              tugmagacha shuncha tez boriladi.
            -->
            <p class="text-sm leading-relaxed text-slate-400">
              <b class="font-medium text-slate-200">Telegram boti</b> sizni
              taniydi va 6 xonali kod yuboradi — hech narsa yozish shart emas.
            </p>

            <p
              v-if="errorMessage !== null"
              class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
              role="alert"
              v-text="errorMessage"
            />

            <BaseButton
              class="mt-5"
              type="button"
              size="lg"
              block
              :loading="isSubmitting"
              @click="handleStartBot"
            >
              <!--
                🔴 `telegram` — HAQIQIY BREND BELGISI (2026-08-30).
                   Ilgari `send` ("qog'oz samolyot") turardi: u Telegram
                   logosiga O'XSHAYDI, lekin u emas. Tugma odamni
                   Telegramga olib chiqadi, ya'ni belgi aynan o'sha
                   ilovani ko'rsatishi kerak.
              -->
              <AppIcon
                name="telegram"
                :size="17"
              />
              Telegram orqali kirish
            </BaseButton>

            <p class="mt-3 text-center text-[12px] leading-relaxed text-slate-500">
              Parol yo'q — kirish har safar Telegram orqali tasdiqlanadi.
            </p>
          </template>

          <!-- ────────────────── 2-QADAM: BOTDAN KOD KUTISH / KIRITISH -->
          <template v-else>
            <!-- Boshi berk ko'cha: chipta o'lgan yoki profil faol emas. -->
            <template v-if="botDeadEnd">
              <div class="flex gap-2.5 rounded-xl bg-amber-500/10 px-3 py-2.5 text-xs leading-relaxed text-amber-200 ring-1 ring-inset ring-amber-500/25">
                <AppIcon
                  class="mt-0.5 shrink-0"
                  name="alert"
                  :size="15"
                />
                <span v-text="botHint" />
              </div>

              <BaseButton
                class="mt-5"
                type="button"
                size="lg"
                block
                @click="resetBot"
              >
                Qaytadan boshlash
              </BaseButton>
            </template>

            <template v-else>
              <p class="text-sm leading-relaxed text-slate-400">
                <template v-if="botStatus === 'raqam-kerak'">
                  Botda <b class="font-medium text-slate-200">«📱 Raqamni ulashish»</b>
                  tugmasini bosing — kod shundan keyin keladi.
                </template>
                <template v-else>
                  Telegram boti ochildi. U yerda
                  <b class="font-medium text-slate-200">«Ishga tushirish»</b> (Start)
                  tugmasini bosing — kod shu chatga keladi.
                </template>
              </p>

              <!--
                Brauzer oynani to'sgan holat. Bu XATO EMAS: havola
                o'sha-o'sha, faqat uni foydalanuvchi o'zi bosishi kerak.
              -->
              <div
                v-if="botPopupBlocked"
                class="mt-4 flex gap-2.5 rounded-xl bg-amber-500/10 px-3 py-2.5 text-xs leading-relaxed text-amber-200 ring-1 ring-inset ring-amber-500/25"
              >
                <AppIcon
                  class="mt-0.5 shrink-0"
                  name="alert"
                  :size="15"
                />
                <span>Brauzer yangi oynani to'sdi. Quyidagi tugmani o'zingiz bosing.</span>
              </div>

              <BaseButton
                class="mt-5"
                type="button"
                size="lg"
                block
                @click="openBotManually"
              >
                <AppIcon
                  name="telegram"
                  :size="17"
                />
                Telegram botni ochish
              </BaseButton>

              <p class="mt-2.5 flex items-center justify-center gap-1.5 text-center text-[12px] text-slate-500">
                <AppIcon
                  name="clock"
                  :size="13"
                />
                <span v-if="botCodeReady">Kod keldi. Havola {{ botClock }} amal qiladi.</span>
                <span v-else>{{ botHint }} · {{ botClock }}</span>
              </p>

              <!-- Kod maydoni FAQAT kod kelgach ochiladi. -->
              <div
                v-if="botCodeReady"
                class="mt-5 border-t border-line pt-5"
              >
                <label class="block">
                  <span class="mb-2 block text-xs font-semibold uppercase tracking-[0.6px] text-slate-500">Botdan kelgan kod</span>
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
                    -->
                    <input
                      ref="botCodeInput"
                      :value="botCode"
                      type="text"
                      name="code"
                      inputmode="numeric"
                      autocomplete="one-time-code"
                      maxlength="6"
                      required
                      placeholder="123456"
                      class="h-13 w-full rounded-xl bg-ink-950 pl-10 pr-3 text-center text-xl font-semibold tracking-[0.5em] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:tracking-normal placeholder:text-sm placeholder:font-normal placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
                      @input="onBotCodeInput"
                    >
                  </div>
                </label>

                <BaseButton
                  class="mt-4"
                  type="submit"
                  size="lg"
                  block
                  :loading="isSubmitting"
                  :disabled="!canVerifyBot"
                >
                  Kirish
                </BaseButton>
              </div>

              <p
                v-if="errorMessage !== null"
                class="mt-4 rounded-xl bg-rose-500/10 px-3 py-2 text-xs text-rose-200 ring-1 ring-inset ring-rose-500/25"
                role="alert"
                v-text="errorMessage"
              />

              <button
                type="button"
                class="mt-5 w-full text-center text-[12px] text-slate-500 underline underline-offset-2 transition-colors hover:text-slate-300"
                @click="resetBot"
              >
                Boshidan boshlash
              </button>
            </template>
          </template>

          <!--
            ZAXIRA YO'L. Havola HAR DOIM ko'rinadi (xato bo'lganda emas):
            bot ishlamayotganini foydalanuvchi ko'pincha xatodan emas,
            "hech narsa kelmayapti" holatidan biladi.
          -->
          <div class="mt-6 border-t border-line pt-4 text-center">
            <button
              type="button"
              class="text-xs font-medium text-brand-500 transition-colors hover:text-brand-600"
              @click="switchTo('telefon')"
            >
              Telefon raqami bilan kirish
            </button>
          </div>
        </template>

        <!-- ══════════════════════════════════════ TELEFON OQIMI ══════ -->
        <template v-else>
          <!-- ─────────────────────────────────── 1-BOSQICH: RAQAM -->
          <template v-if="step === 'telefon'">
            <label class="block">
              <span class="mb-2 block text-xs font-semibold uppercase tracking-[0.6px] text-slate-500">Telefon raqami</span>
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
                  class="h-12 w-full rounded-xl bg-ink-950 pl-10 pr-3 text-base tracking-[0.3px] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
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

          <!-- ───────────────────────────────────── 2-BOSQICH: KOD -->
          <template v-else>
            <!--
              Raqam ko'rinib turadi: foydalanuvchi kodni kutayotib "qaysi
              raqamni yozgan edim?" degan savolga tushmasin.
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
              <span class="mb-2 block text-xs font-semibold uppercase tracking-[0.6px] text-slate-500">Telegramdan kelgan kod</span>
              <div class="relative">
                <span class="pointer-events-none absolute inset-y-0 left-3 flex items-center text-slate-500">
                  <AppIcon
                    name="lock"
                    :size="17"
                  />
                </span>
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
                  class="h-13 w-full rounded-xl bg-ink-950 pl-10 pr-3 text-center text-xl font-semibold tracking-[0.5em] text-slate-100 ring-1 ring-inset ring-line-strong transition-colors placeholder:tracking-normal placeholder:text-sm placeholder:font-normal placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-brand-500"
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

          <div class="mt-6 border-t border-line pt-4 text-center">
            <button
              type="button"
              class="text-xs font-medium text-brand-500 transition-colors hover:text-brand-600"
              @click="switchTo('bot')"
            >
              Telegram bot orqali kirish
            </button>
          </div>
        </template>
      </form>

      <!--
        ⚠️ 2026-08-30 — "O'QUV BO'LIMI" O'RNIGA "MENEJERLARIMIZ"
        (loyiha egasining talabi). Ayni almashtirish ariza formasi va
        landing matnida ham qilingan.
      -->
      <p class="mt-6 text-center text-xs leading-relaxed text-slate-600">
        Hisobingiz yo'qmi? Hisoblarni menejerlarimiz ochadi —
        <RouterLink
          class="font-medium text-slate-500 underline underline-offset-2 transition-colors hover:text-slate-400"
          to="/#ariza"
        >
          ariza qoldiring
        </RouterLink>
        yoki markazga murojaat qiling.
      </p>

      <!--
        TELEFONDA BOSH SAHIFAGA QAYTISH. Kompyuterda bu havola chap
        panelning pastida turadi (`lg:hidden` shuning uchun).

        🔴 ILGARI QAYTISH YO'LI UMUMAN YO'Q EDI: landing'dan «Kirish»
           bosgan odam bu ekranda qamalib qolardi va faqat brauzerning
           «orqaga» tugmasi qutqarardi.
      -->
      <div class="mt-6 text-center lg:hidden">
        <RouterLink
          class="inline-flex items-center gap-1.5 text-xs font-medium text-slate-500 transition-colors hover:text-slate-300"
          to="/"
        >
          <AppIcon
            name="arrow-left"
            :size="13"
          />
          Bosh sahifaga qaytish
        </RouterLink>
      </div>
    </div>
  </div>
</template>
